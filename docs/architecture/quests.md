# Quests

**Assembly:** `Features.Quests` (+ read-model contracts in `Core`) · **Namespaces:** `Features.Quests`, `Core.Quests` · **Source:** `Features/Quests/**`, `Core/Quests/**`

Quest progress is a fact. `quest.{id}.stage` is a counter and the source of truth:
`0` inactive, `1..stages.Length` active on that stage, `> stages.Length` completed.
The runtime rebuilds from the counter and stores nothing else.

The three views over this system live in [`ui.md`](ui.md#quest-views).

---

## Types

| Type | File | Description |
|---|---|---|
| `QuestDefinitionSO` | `QuestDefinitionSO.cs` | `id`, `LocalizedString title`, `Stage[] stages`, `FactCondition[] startConditions`. Menu `Cleanbot/Quests/Definition`. |
| `Stage` | `QuestDefinitionSO.cs` | `LocalizedString journalEntry`, `Objective[] objective`, `GameObject[] setupPrefabs`. |
| `Objective` | `QuestDefinitionSO.cs` | `LocalizedString description`, `FactCondition condition`. |
| `QuestCatalogSO` | `QuestCatalogSO.cs` | `List<QuestDefinitionSO> quests`. Menu `Cleanbot/Quests/Catalog`. |
| `QuestRuntime` | `QuestRuntime.cs` | `MonoBehaviour`. Subscribes `WorldState.FactChanged`; `OnEnable` performs a catch-up scan. Holds `setupStage` (quest → built stage) and `setupInstances` (quest → spawned prefabs). |
| `FactSetterSO` | `Progression/FactSetterSO.cs` | SO wrapping one `FactCondition` applied as a write. `Write()` maps `FlagIsTrue`→`SetFlag(true)`, `FlagIsFalse`→`SetFlag(false)`, `CounterAtLeast`→`SetCounter(value)`. Invoked from `UnityEvent`s only. Menu `Cleanbot/Quests/Fact Setter`. |
| `DwellTracker` | `Progression/DwellTracker.cs` | Abstract `MonoBehaviour`. Accumulates `Time.deltaTime` while `Intensity() > deadzone`; after `requiredSeconds` writes `SetFlag(FactKey, true)` and destroys its GameObject. |
| `AxisInputDwell` | `Progression/AxisInputDwell.cs` | `DwellTracker` subclass. Serialized `axis` (`Vertical`/`Horizontal`); subscribes `ModuleInput.OnIntent`, caches the latest `Move` value, maps the axis component to `TutorialPlayerMoved` / `TutorialPlayerRotated`. |
| `FactCondition` | `FactCondition.cs` | Documented with the spine: [`core.md`](core.md#1-fact-spine). |
| `testquest` | `testquest.cs` | Dev probe. Logs the tracked quest's objectives on every `QuestInfo.Changed`. |

## Sequences

### Per-quest evaluation

```
read quest.{id}.stage
  stage == 0            → if startConditions.Length > 0 and all met: SetCounter(stage, 1)
  stage >  stages.Length → if not quest.{id}.completed: Teardown, SetFlag(completed, true)
  otherwise             → ReconcileSetup(quest, stage)
                          if all stages[stage-1] objectives IsMet: AddToCounter(stage, 1)

ReconcileSetup: if built stage != counter
                  Teardown previous instances
                  Instantiate each setupPrefabs entry
                  record built stage
```

### Pass loop

`Evaluate` writes facts and `FactChanged` is synchronous, so `OnFactChanged`
flattens recursion into iteration:

```
needsAnotherPass = true
if (reconciling) return
reconciling = true
try     while (needsAnotherPass) { needsAnotherPass = false; sweep catalog
                                   if (++passes >= 32) { LogError; break } }
finally reconciling = false
```

## Contracts

1. Quest progress is stored only as `quest.{id}.stage` and `quest.{id}.completed`.
   Triggers are not remembered.
2. A quest chain is expressed as a `FactCondition` on another quest's stage fact.
3. Re-entry during reconciliation sets a flag for another pass; it never starts a
   nested sweep. Stack depth is constant.
4. Reconciliation terminates on a full sweep that writes nothing.
5. The pass cap is 32. Exceeding it means two quests toggle each other's
   conditions. `finally` resets `reconciling` on any exit path.
6. `ReconcileSetup` calls `Instantiate(prefab)` with no parent, so the instance
   lands at the active scene's root.
7. A stage must not spawn a UI widget directly; a canvas-less `RectTransform` at
   world root renders nothing. A stage spawns a requester
   ([`ui.md`](ui.md#popups)) instead.
8. `Teardown` destroys stage instances when the stage advances. Anything that must
   outlive the quest is a fact, not a spawned object.
9. `startConditions.Length` must be greater than zero for a quest to ever start.

## Authoring

Create the `QuestDefinitionSO`, add it to the catalog assigned on `QuestRuntime`,
and author every `FactCondition` key from the dropdown
([`editor-tooling.md`](editor-tooling.md#fact-key-registry)). `title`,
`journalEntry` and each objective `description` are `LocalizedString`s and require
entries in the string table for every locale.

---

## Read-model

Reads `quest.{id}.stage` and produces immutable snapshots for the UI. The UI
reaches quest state through `Core` and never references `Features.Quests`.

| Type | File | Description |
|---|---|---|
| `IQuestInfoSource` | `Core/Quests/IQuestInfoSource.cs` | `Snapshots()`, `Get(id)`, `TrackedId`, `SetTracked(id)`, `Changed`. Pure C#; no `UnityEngine`, no quest types. |
| `QuestSnapshot`, `ObjectiveLine`, `QuestStatus` | `Core/Quests/QuestSnapshot.cs` | Immutable. `Id`, `Title`, `Status` (`Active`/`Completed`), `StageStory[]`, `Objectives[]` of `(Text, Completed)`. Resolved `string`s only. |
| `QuestInfo` | `Core/Quests/QuestInfo.cs` | Static registry slot. Holds one `IQuestInfoSource`, re-exposes `Changed`, forwards reads null-safe. |
| `QuestInfoPasser` | `Features/Quests/QuestInfoPasser.cs` | `MonoBehaviour, IQuestInfoSource`. Builds snapshots from catalog + `WorldState`, resolves `LocalizedString` to `string`, holds the tracked pin. `OnEnable` subscribes `WorldState.FactChanged` and `LocalizationSettings.SelectedLocaleChanged`, and registers into `QuestInfo`; `OnDisable` reverses. |

### Contracts

1. `Build` returns `null` for stage `0`; `Snapshots()` filters those out.
2. `StageStory` contains the journal line of every reached stage `0..reached-1`.
   Its last entry is the active stage's line.
3. `Objectives` contains only the current stage's objectives, each `Completed`
   evaluated live. A completed quest carries all stage lines and an empty
   `Objectives`.
4. Status is derived from the stage counter with the same thresholds
   `QuestRuntime` uses.
5. No `LocalizedString` crosses into `Core.Quests`. `Core/Quests/**` is
   Localization-free.
6. `TrackedId` resolves on every read: the explicit pin if still active, otherwise
   the first active quest, otherwise `null`. It is not persisted.
7. `Changed` fires on every `FactChanged` and every locale change. Views re-pull.
8. Subscription order is irrelevant; `QuestInfo` is a facade with a stable event.
