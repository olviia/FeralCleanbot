# Core

**Assembly:** `Core` (+ `App`) · **Namespaces:** `Core.*`, `Bootstrap` · **Source:** `Core/**`, `App/**`

The fact spine and save container, session entry, scene flow, and possession/module
dispatch. Input contexts live in `Core/Input` and are documented in
[`input.md`](input.md); quest read-model contracts live in `Core/Quests` and are
documented in [`quests.md`](quests.md#read-model).

---

## Connection idioms

Three shapes account for nearly every cross-system connection in the project.

| Shape | Examples | Definition |
|---|---|---|
| **Static channel** | `ModuleInput`, `CutsceneInput`, `MenuInput` | Static class holding `event`s plus `RaiseX()` methods. One class per **input map**, never per action. |
| **Static registry slot** | `GlyphInput`, `QuestInfo` | Static facade holding one implementation, registered at runtime, re-exposing a stable event and forwarding reads null-safe. Subscribers may attach before the implementation exists. |
| **SO event channel** | `Core/Events/*RequestSO.cs` | `ScriptableObject` asset carrying `event` + `Raise`. Both a prefab on disk and a runtime-spawned object can reference the same asset. Seam-choice rule: [`ui.md`](ui.md#seam-selection). |

`InputRouter` is a broadcast bus: any system pushes a context, any system reacts.

### `Core/Events` channels

| Channel | Payload | Raised by |
|---|---|---|
| `VoidEventSO` | `RaiseAction()` | Cutscene triggers and chains, `GameplayBootstrap.newGameStarted`. Public, void, no args — assignable directly to `Button.onClick`. |
| `UIPromptDisplayRequestSO` | `Show(string, Intent)` / `Hide()` | Interactables on focus |
| `UIPromptPositionRequestSO` | `SetPosition(Vector3)` | Interactables |
| `UIElementDisplayRequestSO` | `Show/Hide(GameObject)` | `UIPopupRequest`, popup close controls |

---

## 1. Fact spine

All persistent game state is flags and counters in one static store. Systems write
facts and react to `WorldState.FactChanged`; they do not call each other.

### Types

| Type | File | Description |
|---|---|---|
| `WorldState` | `Core/SaveSystem/WorldState.cs` | Static mediator over the one in-memory `SaveData`. `GetFlag/SetFlag`, `GetCounter/SetCounter/AddToCounter`, `TryGetPosition/SetPosition`, `GetBlob/SetBlob`. Flag and counter setters fire `FactChanged(key)`. Owns the save container: `Save`, `Load`, `NewSave`, `SaveExists`. `Load`/`NewSave` fire `FactChanged(null)`. `Load()` returns `bool` and swallows a missing or unreadable file. |
| `SaveData` | `Core/SaveSystem/SaveData.cs` | `internal` container; the on-disk wire format. Dictionaries by value shape: `flags:bool`, `counters:int`, `reactions:float`, `names:string`, `positions:SaveVec3`, `attributeValues:float`, `blobs:byte[]` (`[JsonIgnore]`). Plus `skillId`, character fields, `saveVersion`, `inGameTimeSeconds`, `savedAt`. Declares `const int CurrentVersion`. All collections initialised at declaration. |
| `SaveVec3` | `Core/SaveSystem/SaveData.cs` | `internal struct {float x,y,z}` + `ToVector3()`. `Vector3` does not cross into `SaveData`. |
| `BlobFile` | `Core/SaveSystem/BlobFile.cs` | `internal static`. Binary sidecar layout: magic `"CBLB"`, version, count, then per entry `key`, `rawLength`, `packedLength`, deflated bytes. `Write(path, blobs, version)`, `Read(path, expectedVersion)`. |
| `FactKeys` | `Core/SaveSystem/FactKeys.cs` | Every key string in the project. Builders: `CutsceneFinished(id)`, `QuestStage(id)`, `QuestCompleted(id)`, `ModuleOwned(id)`, `SurfaceMask(id)`. Consts: `TutorialPlayerMoved`, `TutorialPlayerRotated`, `TutorialDoorOpened`. Carries `[FactKeySource]`. |
| `FactCondition` | `Features/Quests/FactCondition.cs` | Serializable `{factKey, FactTest test, int value}` + `IsMet()`. `FactTest` = `FlagIsTrue` / `FlagIsFalse` / `CounterAtLeast`. Lives in `Features.Quests`. |

### Fact traffic

| Direction | Participants |
|---|---|
| Writers | `CutsceneDirector` (`CutsceneFinished`), `DwellTracker` (`Tutorial*`), `QuestRuntime` (`QuestStage`, `QuestCompleted`), `FactSetterSO.Write` (inspector-wired), `GameFlow.Begin` (`NewSave`/`Load`), `TestButton` (`Save`), `SaveCoordinator` (`Save`), `CleanableSurface.CaptureMask` (`SetBlob`) |
| Readers | `FactCondition.IsMet`, `CutsceneDirector` (play-once gate), `ModuleLoadout` (`ModuleOwned`), `CleanableSurface.RestoreMask` (`GetBlob`) |
| `FactChanged` subscribers | `QuestRuntime`, `QuestInfoPasser`, `ModuleLoadout` |

`FactSetterSO` writers are persistent `UnityEvent` calls stored in prefab and scene
YAML; text search will not find them.

### Contracts

1. `SaveData` is `internal`. Nothing outside `Core/SaveSystem/` may reference it.
2. Key strings are never hand-typed. Reader and writer call the same `FactKeys`
   method. `FactKeys` is append-only.
3. Ids embedded in keys (`module.{id}.owned`, `quest.{id}.stage`,
   `surface.{id}.mask`) are part of the save schema. An asset may be renamed; its
   `id` may not, once a build has been played.
4. `FactChanged` is synchronous. A setter called inside a handler re-enters all
   handlers before returning. A consumer that can cause fact writes must not be
   re-entrant; see [`quests.md`](quests.md#pass-loop).
5. The store is never null. `WorldState.Data` lazily creates an empty `SaveData`.
   One-shot side effects triggered before a load cannot be un-fired, so state is
   prepared before the gameplay scene loads (§5).
6. `SetPosition` does not raise `FactChanged`.
7. `SetBlob` does not raise `FactChanged`. A blob is read once, by the object that
   owns it, at enable.
8. A new `WorldState` accessor pair is added when a feature first needs it, not
   in advance.
9. File I/O occurs only in `WorldState.Save`/`Load` and `BlobFile`. No other type in
   the project touches `File` or `Path`.
10. `SaveData.blobs` is excluded from JSON but stored inside `SaveData`, so
    `NewSave()` clears it and `Load()` replaces it wholesale.
11. `SaveData.CurrentVersion` is the only format-version authority. `Load()` compares
    it against the file's `saveVersion` and passes the file's version to
    `BlobFile.Read`, which throws on disagreement.

### Save container

```
{persistentDataPath}/save/
  state.json      JSON: flags, counters, positions, metadata
  state.json.old  previous generation
  blobs.bin       BlobFile: deflated opaque byte arrays
  blobs.bin.old   previous generation
```

12. Blobs are written first and `state.json` last. `SaveExists` tests `state.json`,
    so an interrupted write leaves the previous save intact.
13. `Commit(path)` uses `File.Replace(tmp, final, final + ".old")` when the target
    exists and `File.Move` when it does not.
14. An unreferenced blob is ignored on load; a missing blob leaves its owner at its
    default state.

---

## 5. Session intent & new-game flow

Entry intent is carried as data across the scene load.

| Concern | Type |
|---|---|
| Intent — the player chose Continue | `GameSession` |
| State preparation — the save must exist | `GameFlow.Begin` |
| Execution — play the awakening | `GameplayBootstrap` |

### Types

| Type | File | Description |
|---|---|---|
| `GameSession` | `Core/SceneControls/GameSession.cs` | SO. `EntryMode {None, NewGame, Continue}`; `Request(mode)`, `Consume()` (read-and-clear), `OnEnable` resets to `None`. Menu `Cleanbot/App/GameSession`. |
| `GameFlow` | `Core/SceneControls/GameFlow.cs` | Static. `Begin(session, mode)`: prepare `WorldState` (`Load()` for Continue, `NewSave()` for NewGame) → `session.Request(mode)` → `ChangeSceneTo(Gameplay)`. A failed `Load()` rewrites `mode` to `NewGame` before intent is recorded. |
| `GameplayBootstrap` | `App/GameplayBootstrap.cs` | `MonoBehaviour` in Gameplay. `Start`: `switch(session.Consume())` → NewGame raises `newGameStarted`; Continue and None do nothing. |
| `SaveCoordinator` | `App/SaveCoordinator.cs` | `MonoBehaviour` in Gameplay. `Save()` captures every registered cleanable surface ([`cleaning.md`](cleaning.md#save)) then calls `WorldState.Save()`. |
| `StartNewGameButton` | `Features/Title/StartNewGameButton.cs` | `GameFlow.Begin(session, NewGame)`. |
| `LoadGameButton` | `Features/Title/LoadGameButton.cs` | `GameFlow.Begin(session, Continue)`. `OnEnable` sets `button.interactable = WorldState.SaveExists`. |
| `TestButton` | `Features/Title/TestButton.cs` | Dev harness: `OnSwitchLocale`, `OnSaveTestButton` → `WorldState.Save()`. |

### Contracts

1. `WorldState` is prepared before the scene transition is requested.
   `SceneLoader` loads additively and asynchronously, and Unity guarantees no
   ordering between objects in the loaded scene.
2. `newGameStarted` is raised in `GameplayBootstrap.Start`. Listeners subscribe in
   `OnEnable`. Unity runs all `OnEnable` before any `Start`.
3. `EntryMode.None` performs no state preparation. Entering the Gameplay scene
   directly does not load a save.
4. The `GameSession` asset referenced by the Title buttons and by
   `GameplayBootstrap` must be the same asset. The `VoidEventSO` on
   `GameplayBootstrap.newGameStarted` and on the intro definition's `eventTrigger`
   must be the same asset.

---

## 6. Scene flow

| Type | File | Description |
|---|---|---|
| `SceneStateMachine` | `Core/SceneControls/SceneStateMachine.cs` | Static. `GameScene {Title, Gameplay}`, `CurrentGameScene`, `ChangeSceneTo(next)`, `event OnGameSceneChanged(from, to)` — fires when a transition is requested. |
| `SceneLoader` | `Core/SceneControls/SceneLoader.cs` | Static. `Initialize(map)`, `LoadScene(from, to)` loads `to` additively then unloads `from`, then fires `OnSceneLoaded(to)`. |
| `Bootstrap` | `App/Bootstrap.cs` | `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]`: sets the `GameScene`→scene-name map and wires `SceneStateMachine.OnGameSceneChanged` to `SceneLoader.LoadScene`. |

### Contracts

1. Features request transitions only through `ChangeSceneTo`.
2. Code needing objects from a just-loaded scene subscribes to
   `SceneLoader.OnSceneLoaded`, not `OnGameSceneChanged`.
3. Parentless `Instantiate` places the object in the **active** scene. Gameplay is
   loaded additively.

---

## 7. Possession & module input

`Actor` is plain C#, not a `MonoBehaviour`. Only the possessed actor is subscribed
to input.

| Type | File | Description |
|---|---|---|
| `IPosessable` | `Core/Player/IPosessable.cs` | `OnPosessed` / `OnUnposessed`. |
| `Posession` | `Core/Player/Posession.cs` | Static. `Register` / `Unregister`, `Posess(next)` (unpossesses current first), `Available`. |
| `Actor` | `Core/Player/Actor.cs` | `IPosessable`. Owns `Tags`, `Focus`, module list. `OnPosessed` subscribes `Send` to `ModuleInput.OnIntent`. `Send` (input payload) and `Dispatch` (world payload, carries a `Transform`) build a `Command` and forward it to modules whose `ReactsTo` contains the intent. `GetModule<T>()`. |
| `ActorHost` | `Core/Player/ActorHost.cs` | `MonoBehaviour`. Registers its `Actor` on enable, unregisters on disable. |
| `IModule` | `Core/Player/IModule.cs` | `ReactsTo`, `Tag BlockedBy => Tag.None`, `Handle(owner, cmd)`. |
| `Command` | `Core/Player/Command.cs` | `readonly struct`: `Intent WhatToDo`, `Vector2 ExtraInfo`, `Transform Position`. Two constructors. |
| `Intent` | `Core/Player/Intent.cs` | `Move`, `Interact`, `Charge`, `StopCharge`. |
| `TagSet` / `Tag` | `Core/Player/TagSet.cs` | `[Flags] Tag {None, Interacting, Charging, Busy}`. `TagSet` is ref-counted: `Add`, `Remove`, `HasAny`, `HasAll`, `Added`, `Removed`. |
| `WalkModule` | `Features/Modules/WalkModule.cs` | `MonoBehaviour, IModule`. Reacts to `Move`: rigidbody rotate (`ExtraInfo.x`) and drive (`ExtraInfo.y`) in `FixedUpdate`. Blocked by `Interacting` and `Charging`. |

### Contracts

1. Modules never subscribe to `ModuleInput`. Only the possessed `Actor` does.
2. A module resolves its owner with `GetComponentInParent<ActorHost>()` in `Awake`,
   never an inspector reference.
3. `ModuleInput.RaiseX` is invoked only from `InputReader.cs`.
