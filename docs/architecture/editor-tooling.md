# Editor tooling

**Assembly:** `Assembly-CSharp-Editor` (predefined) · **Namespaces:** `Tools.FactKeyRegistry`, `Tools` · **Source:** `Editor/**`

Everything here compiles into the predefined editor assembly, which auto-references
every asmdef and every package. Editor tooling may use `Unity.InputSystem` without
affecting the runtime quarantine ([`input.md`](input.md) contract 2).

---

## Fact-key registry

An authored `FactCondition` picks its key from a hierarchical dropdown of every
valid key in the project.

| Type | File | Description |
|---|---|---|
| `IFactKeySource` | `FactKeyRegistry/IFactKeySource.cs` | `IEnumerable<string> GetFactKeys()`. |
| `ConstKeySource` | `FactKeyRegistry/ConstKeySource.cs` | Reflects the public static const strings of every `[FactKeySource]`-marked class. |
| `CutsceneKeySource` | `FactKeyRegistry/CutsceneKeySource.cs` | Enumerates opted-in `CutsceneDefinitionSO` assets, yields `FactKeys.CutsceneFinished(id)`. |
| `QuestKeySource` | `FactKeyRegistry/QuestKeySource.cs` | Enumerates `QuestDefinitionSO` assets, yields `FactKeys.QuestCompleted(id)` and `QuestStage(id)`. |
| `ModuleKeySource` | `FactKeyRegistry/ModuleKeySource.cs` | Enumerates `ModuleDefinitionSO` assets, yields `FactKeys.ModuleOwned(id)`. |
| `FactKeyRegistry` | `FactKeyRegistry/FactKeyRegistry.cs` | Static. `Collect()` discovers every `IFactKeySource` implementor via `TypeCache.GetTypesDerivedFrom`, instantiates each, concatenates and de-dupes. |
| `FactKeyDropdown`, `FactKeyDropdownItem` | `FactKeyRegistry/FactKeyDropdownItem.cs` | `AdvancedDropdown` splitting keys on `.` into a tree; the leaf carries the full key. |
| `FactConditionDrawer` | `FactKeyRegistry/FactConditionDrawer.cs` | `[CustomPropertyDrawer(typeof(FactCondition))]`. Renders the key dropdown, `test`, and `value` (only when `CounterAtLeast`). |

### Contracts

1. The same `FactKeys` method computes a key at author time and at runtime.
2. Adding a derived key family requires one `FactKeys` method and, if the key is
   asset-derived, one `IFactKeySource` implementation. Discovery is by convention.
3. A type implementing `IFactKeySource` without a parameterless constructor is
   skipped with a warning.

---

## Input action-key drawer

| Type | File | Description |
|---|---|---|
| `InputGlyphDrawer` | `InputGlyphDrawer.cs` | `[CustomPropertyDrawer(typeof(InputActionKeyAttribute))]`. Enum-style `EditorGUI.Popup` listing `(none)` plus every `"Map/Action"` read from the single `InputActionAsset`. |
| `SceneSwitchOverlay` | `SceneSwitchOverlay.cs` | Scene-view overlay for jumping between scenes. |
| `FontToSprite` | `FontToSprite.cs` | Asset utility. |

### Contracts

1. `BeginChangeCheck` guards the write-back. A stale or renamed key is not in
   `_options`, so `Array.IndexOf` returns `-1` and the popup displays `(none)`; the
   value is written back only if the user changes it.
2. `BeginProperty` and `EndProperty` must be balanced on every repaint, not only on
   frames where the value changed. `actionKeys` is an array, so an unbalanced pair
   pushes once per element per repaint. Unity does not throw on imbalance.
3. `_options ??= BuildOptions()` caches for the drawer's lifetime. A newly added
   action appears after a domain reload or reselect. `BuildOptions` performs an
   `AssetDatabase.FindAssets` and a full asset load.