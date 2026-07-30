# Input

**Assemblies:** `Core`, `Features.Input`, `Features.UI` · **Namespaces:** `Core.Input`, `Features.Input`, `Features.UI` · **Source:** `Core/Input/**`, `Features/Input/**`, `Features/UI/TextDisplay/**`

One adapter, `InputReader`, turns the action asset into a single `Intent→InputAction`
map and fans it out three ways.

| Concern | Mechanism |
|---|---|
| **Context** — which map is active | `InputRouter` context stack; `InputReader.Apply` enables the matching action map |
| **Execution** — push | `ModuleInput.RaiseX` broadcasts `OnIntent(Intent, Vector2)`; the possessed `Actor` receives it ([`core.md`](core.md#7-possession--module-input)) |
| **Display** — pull | `InputGlyphProvider` registers into `GlyphInput`; UI asks by action-key string `"Map/Action"` for a `Glyph` |

## Types

| Type | File | Description |
|---|---|---|
| `InputReader` | `Features/Input/InputReader.cs` | Builds the `Intent→InputAction` map from the `Player` map. Pumps `Move` each frame; `Interact` on `performed`; `Skip`→`CutsceneInput.RaiseSkip`; `ToggleMenu`→`MenuInput.RaiseToggleMenu`; `Confirm` on `started`/`canceled`→`MenuInput.RaiseConfirmDown`/`Up`. Constructs and registers the glyph provider. Enables and disables maps per context. |
| `InputContext`, `InputRouter` | `Core/Input/InputContext.cs`, `InputRouter.cs` | Enum `{Gameplay, Cutscene, Menu}` and a static context stack. `Enter`, `Exit`, `ContextChangedTo`, `ActiveContext` (`Gameplay` when empty). `Exit` is `List.Remove(value)`. |
| `ModuleInput` | `Core/Input/ModuleInput.cs` | Player-map channel: `OnIntent`, `RaiseMove`, `RaiseInteract`, `RaiseStopCharging`. |
| `CutsceneInput` | `Core/Input/CutsceneInput.cs` | Cinematic-map channel: `SkipCutscene`, `RaiseSkip`. |
| `MenuInput` | `Core/Input/MenuInput.cs` | UI-map channel: `ToggleMenu`, `ConfirmDown`, `ConfirmUp` and their raisers. |
| `GlyphInput` | `Core/Input/GlyphInput.cs` | Static registry slot holding one `IInputGlyphProvider`. `Register`, `Glyphs`. |
| `IInputGlyphProvider`, `Glyph` | `Core/Input/*` | `GetGlyph(string actionKey)`, `KeyFor(Intent)`, `DeviceChanged`. `Glyph {label, sprite}`; `sprite` is unwired. |
| `InputGlyphProvider` | `Features/Input/InputGlyphProvider.cs` | Plain C#, `IDisposable`. Resolves an action key to a `Glyph` for the active control scheme, caches by key, tracks the device via `InputSystem.onActionChange`, clears the cache and raises `DeviceChanged` on a scheme switch. |
| `InputGlyphText` | `Features/UI/TextDisplay/InputGlyphText.cs` | `MonoBehaviour` over a `LocalizeStringEvent`. Holds `string[] actionKeys`; on enable and on `DeviceChanged`, resolves each key to a glyph label, pushes them as the localized string's `Arguments` (`{0}`, `{1}`, …) and calls `RefreshString()`. |
| `InputActionKeyAttribute` | `Features/UI/TextDisplay/InputActionKey.cs` | `PropertyAttribute` marker. `UnityEngine`-only; its drawer is editor-only ([`editor-tooling.md`](editor-tooling.md#input-action-key-drawer)). |

## Contracts

1. The `Intent→InputAction` map exists only in `InputReader`.
2. `Core` never references `UnityEngine.InputSystem`. The package is confined to
   `Features.Input` and editor-only tooling.
3. UI pulls glyphs only from `GlyphInput`, which is null until `InputReader.Awake`
   registers. Callers null-guard and query at show time.
4. One static channel per input map, never per action. `MenuInput` carries
   `ToggleMenu` and `ConfirmDown`/`ConfirmUp` because both are UI-map actions.
5. `Confirm` is a plain Button action with no `Hold` interaction. `InputReader`
   reports both edges; hold duration lives in the widget that draws it
   ([`ui.md`](ui.md#popups)). `started` fires only on a transition, so a button
   already held when a widget mounts produces nothing until release and re-press.
6. `DisplayFor` accepts either a plain in-scheme leaf binding or a composite whose
   next part binding is in scheme, then renders the composite by index.
   `PrimaryKeys` keeps the first token of each `/`-separated part.
   Scheme-filtered `GetBindingDisplayString` on a composite header returns blank.
7. The `UI` map is always enabled. `Apply` disables only `player` and `cinematic`.
   A UI action must not be bound to a key the Player map also uses.
8. `InputRouter` is a stack and `Exit` removes by value, so the list behaves as a
   counter when two owners push the same context.
9. Adding a verb requires an `Intent`, a binding, one line in `InputReader`, and a
   `Raise` call. The display side needs no change.
10. `InputGlyphText` substitutes through Localization `Arguments`, never string
    concatenation, and references no InputSystem type.

## Authoring

`InputGlyphText.actionKeys` entries are picked from the action-key dropdown. The
indices map to `{0}`, `{1}`, … in the localized string, so translations may reorder
them freely.