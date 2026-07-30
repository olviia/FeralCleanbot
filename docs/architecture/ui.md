# UI

**Assembly:** `Features.UI` (+ `Core/Events` channels) · **Namespace:** `Features.UI` · **Source:** `Features/UI/**`, `Core/Events/**`

Channels and mount points, popups and modals, the menu shell and pause, and the
quest views. `Features.UI` references only `Core`, TMP and Localization.

---

## Seam selection

| Seam | Use when |
|---|---|
| **SO event channel** (`UIElementDisplayRequestSO`) | Crossing a boundary — a runtime-spawned object talking to a scene object, or two prefabs that never meet at author time. Both ends reference an asset. |
| **`UnityEvent`** (`Button.onClick`, `UIHoldToConfirm.onCompleted`) | Inside one prefab, where both ends exist on disk together. Bindings are stored by method-name string. |
| **Core registry slot** (`QuestInfo`, `GlyphInput`) | A view pulls state and subscription order is unknown. |

1. A prefab cannot serialize a reference to a scene object.
2. Dragging a prefab's own root into a `UnityEvent` argument from the **hierarchy**
   serializes an internal reference, and `Instantiate` remaps internal references to
   the copies — the argument arrives as the instance. Dragging the same prefab from
   the **Project** window serializes an asset reference, which is not remapped.

---

## Channels & mount

| Type | File | Description |
|---|---|---|
| `UIPromptDisplayRequestSO` | `Core/Events/UIPromptDisplayRequestSO.cs` | `Show(string text, Intent)` / `Hide()`. |
| `UIPromptPositionRequestSO` | `Core/Events/UIPromptPositionRequestSO.cs` | `SetPosition(Vector3)`. |
| `UIElementDisplayRequestSO` | `Core/Events/UIElementDisplayRequestSO.cs` | Generic widget channel: `RaiseShow(GameObject)` / `RaiseHide(GameObject)`. |
| `UIPrompt` | `UIPrompt.cs` | Listens to a prompt channel, sets the label, resolves the glyph for the `Intent` from `GlyphInput`, refreshes on `DeviceChanged`, fades a `CanvasGroup`. |
| `UIPromptPosition` | `UIPromptPosition.cs` | Places the prompt at `WorldToScreenPoint(hitPoint + offset)` each `LateUpdate`. |
| `UIMountPoint` | `UIMountPoint.cs` | Sits on the canvas. `Show(prefab)` → `Instantiate(prefab, container)`, keyed into `active`. `Hide(key)` → destroy. |

### Contracts

1. A mounted popup prefab has no `Canvas` of its own; its root is a plain
   `RectTransform` parented into the scene's canvas by `UIMountPoint.container`.
   `CanvasScaler` is per root canvas, and each root Screen-Space-Overlay canvas is
   its own rebuild and draw batch.
2. `UIMountPoint` is the only type that destroys a mounted instance. A popup that
   destroys itself leaves a stale `active` entry, and `Mount`'s `ContainsKey` guard
   then refuses to show that prefab again.
3. `active` maps prefab → instance. `Unmount(key)` first removes by prefab, then
   falls back to a reverse scan over `active.Values` to resolve an instance. Callers
   know one address or the other: `UIPopupRequest` knows the prefab, a popup's own
   close control knows the instance.
4. The reverse scan finds first and mutates after; it breaks out of the loop before
   removing.
5. Double-hide is a no-op. Both lookups miss and nothing happens.

---

## Popups

```
QuestRuntime spawns at world root ─► TutorialPopupRequest
                                       [UIPopupRequest] prefab + channel
                                         OnEnable → RaiseShow(prefab)
                                             ▼
                                    UIMountPoint (canvas, same channel asset)
                                         Instantiate(prefab, container)
                                             ▼
                                     TutorialPopup instance
                                       ├ UIPopup          Enter/Exit(Menu)
                                       ├ UIHoldToConfirm  onCompleted ─┐
                                       ├ btn_close        onClick ─────┤
                                       └ InputGlyphText ×2             ▼
                                              UIElementDisplayRequestSO.RaiseHide(root)
                                                → mount point destroys instance
                                                → UIPopup.OnDisable → Exit(Menu)
```

| Type | File | Description |
|---|---|---|
| `UIPopupRequest` | `UIPopupRequest.cs` | Lives on a world-root prefab, typically a quest `setupPrefab`. `OnEnable`→`RaiseShow(prefab)`, `OnDisable`→`RaiseHide(prefab)`. Holds no context or content knowledge. |
| `UIPopup` | `UIPopup.cs` | On the popup prefab root. `OnEnable`→`InputRouter.Enter(Menu)`, `OnDisable`→`Exit(Menu)`. |
| `UIHoldToConfirm` | `UIHoldToConfirm.cs` | Subscribes `MenuInput.ConfirmDown`/`Up`. Accumulates `Time.unscaledDeltaTime` into `elapsed`, writes `fill.fillAmount = elapsed / holdSeconds`, fires `UnityEvent onCompleted` at the threshold. `ResetHold` on release and on enable. |

### Contracts

1. A context push is owned by an object whose lifetime equals the context's
   duration. `Enter`/`Exit(Menu)` lives on `UIPopup` (the instance), not on
   `UIPopupRequest` (which outlives it).
2. Anything a modal draws uses `Time.unscaledDeltaTime`. `Enter(Menu)` sets
   `timeScale` to 0, so `deltaTime` is zero. `Update` and the EventSystem still tick.
3. Hold duration is `holdSeconds` on `UIHoldToConfirm`, not a `Hold` interaction in
   the action asset ([`input.md`](input.md) contract 5).
4. `UIHoldToConfirm` guards `onCompleted` with a `fired` flag; `elapsed` stays past
   the threshold until release.
5. The reset method is named `ResetHold`. `Reset` is a Unity magic method invoked
   by the editor when the component is added, and would run with `fill` unassigned.
6. Both close paths pass the popup **root** to `RaiseHide`, which arrives as the
   instance.

---

## Menu shell & pause

| Type | File | Description |
|---|---|---|
| `UIMenuController` | `UIMenu/UIMenuController.cs` | On a persistent object, never on the panel it toggles. Subscribes `MenuInput.ToggleMenu`. `OnToggle`: close reads its own `state`; open requires `ActiveContext == Gameplay`. `Open`/`Close` flip `state`, `SetActive` the panel, and `InputRouter.Enter`/`Exit(Menu)`. `Start` forces the closed baseline. |
| `GamePauseInMenu` | `UIMenu/GamePauseInMenu.cs` | The only writer of `Time.timeScale`. On `InputRouter.ContextChangedTo`: `Menu` → 0, otherwise 1. `OnDisable` restores 1. |

### Contracts

1. `state` is the source of truth for open/closed, not `panel.activeSelf`.
2. A router transition is emitted only when one occurred. `Start` sets the closed
   baseline without calling `Exit(Menu)`.
3. Nothing other than `GamePauseInMenu` writes `Time.timeScale`, and there is
   exactly one instance. A second instance would restore `timeScale = 1` in its own
   `OnDisable`.
4. One owner holds the `Menu` context at a time, enforced by `UIMenuController`'s
   `ActiveContext == Gameplay` open-gate. A sub-panel that layers over a menu is a
   panel inside the owner, not a second context push; the owner routes Back/Esc for
   its children.
5. `lastTabIndex` exists and is unused. There is one tab.

---

## Quest views

Three projections of `QuestInfo`. No view references `Features.Quests` or
`WorldState`. The projection they rely on is in
[`quests.md`](quests.md#read-model).

| Type | File | Description |
|---|---|---|
| `UIQuestListEntry` | `UIMenu/UIQuestListEntry.cs` | Row. `Bind(snapshot)`: title, plus `description` = `StageStory[^1]`; recolours and re-styles the title when `Status == Completed`. Holds `id`; a `Button` calls `Click()` → `event Action<string> Clicked`. |
| `UIQuestTab` | `UIMenu/UIQuestTab.cs` | List view and selection presenter. On `QuestInfo.Changed` and on enable, `Rebuild` destroys old rows, spawns one entry per snapshot into `activeGroup` or `completedGroup` by `Status`, and forwards each row's `Clicked` to `Select`. Owns `currentId`; `Select(id)` calls `detailPanel.Show(Get(id))`. |
| `UIQuestDetailPanel` | `UIMenu/UIQuestDetailPanel.cs` | `Show(snapshot)`: title plus one TMP rich-text block — active stage line, its objectives (completed ones struck and tinted with `completedColor`), then prior stages struck, newest first. A completed quest strikes every line. |
| `HUDQuest` | `HUDQuest.cs` | Always-mounted. On `QuestInfo.Changed` and on enable, `Refresh` reads `Get(TrackedId)`; if null sets `CanvasGroup.alpha = 0`, otherwise paints `title` and one `objectives` block and sets `alpha = 1`. |

### Contracts

1. `Rebuild` re-runs the detail panel's `Show` via `Select(currentId)`, so an
   objective completing while the panel is open repaints the detail as well as the
   list.
2. Selection is owned by `UIQuestTab`. Rows raise `Clicked(id)` only.
3. `Get(id)` returns `null` for an unknown id. `Select` guards and calls
   `ShowEmpty()`. `Rebuild` re-defaults `currentId` when it stops resolving:
   `TrackedId`, then the first snapshot, then none.
4. Active and Completed are visual states of one prefab, branched on
   `QuestStatus` — not separate prefabs.
5. `HUDQuest` hides by `alpha = 0`, never `SetActive(false)`, so the subscription
   stays alive.
6. `HUDQuest` and `UIQuestDetailPanel` share the strike contract (`completedColor`
   plus `<s>`) and both build TMP rich text with a `StringBuilder`.
