# HaywireCleaner — Architecture

Reference documentation for the code under `Assets/Scripts`. One file per system.

---

## Documentation format

**This is the required format for all files in this folder.** Use it for new
systems and keep existing files in it.

### Rules

1. **Document what exists, not what is planned.** No roadmaps, no "not yet built",
   no TODO lists, no status labels. If it is not in the code, it is not in here.
2. **State facts, not reasons.** No "why we chose", no rejected alternatives, no
   history of past bugs. Rationale belongs in `../architecture-foundations.md` and
   the per-feature design docs; task lists belong in `../todo.md`.
3. **Contracts are stated, not argued.** "X must Y" — one sentence, no justification.
4. **No comparisons to other engines, games, or literature.**
5. **Every claim must be checkable against the code.** If a rename would make a
   sentence wrong, that sentence must be short enough to be worth fixing.
6. **Link by section anchor** when another file owns a subject; never restate it.

### Structure

````markdown
# System name

**Assembly:** `X` · **Namespaces:** `A`, `B` · **Source:** `path/**`

One or two sentences stating what the system does.

## Types
| Type | File | Description |

## Contracts
Numbered. Each is a rule that must hold.

## Sequences
Ordered runtime flows, as code blocks.

## Authoring
What must be configured in the editor for the system to work.
````

Omit any section that has no content. `Types` and `Contracts` are near-mandatory;
`Sequences` and `Authoring` appear only where behaviour is order-dependent or
requires inspector setup.

---

## Index

| File | System |
|---|---|
| [`core.md`](core.md) | Fact spine · save container · session intent · scene flow · possession & modules |
| [`quests.md`](quests.md) | Quest state machine · progression writers · quest read-model |
| [`cutscenes.md`](cutscenes.md) | Data-driven Timeline playback |
| [`input.md`](input.md) | Context stack · execution transport · glyph display |
| [`interaction.md`](interaction.md) | Focus sensor · interactables · docking |
| [`modules.md`](modules.md) | Module ownership facts · loadout reconciler |
| [`ui.md`](ui.md) | Channels · popups · menu shell · quest views |
| [`cleaning.md`](cleaning.md) | Paint canvas · dust shells · surface identity & mask persistence |
| [`editor-tooling.md`](editor-tooling.md) | Fact-key registry · input action-key drawer |

---

## Assemblies

| Assembly | References | Source |
|---|---|---|
| `Core` | `Unity.Localization` | `Core/**` |
| `App` | `Core`, `Features.Modules` | `App/**` |
| `Features.Input` | `Core`, `Unity.InputSystem`, `Unity.TextMeshPro` | `Features/Input/**` |
| `Features.Modules` | `Core`, `Unity.Localization`, `Unity.InputSystem` | `Features/Modules/**` |
| `Cutscenes` | `Core`, `Unity.Timeline`, `Unity.TextMeshPro` | `Features/Cutscenes/**` |
| `Features.Interactables` | `Core`, `Unity.Localization` | `Features/Interactables/**` |
| `Features.UI` | `Core`, `Unity.TextMeshPro`, `Unity.Localization` | `Features/UI/**` |
| `Features.Quests` | `Core`, `Unity.Localization` | `Features/Quests/**` |
| `Features.Title` | `Core`, `Unity.Localization` | `Features/Title/**` |
| `Assembly-CSharp-Editor` (predefined) | auto-references all asmdefs and packages | `Editor/**` |
| `Assembly-CSharp` (predefined) | — | `Prototypes/**`, `FpvSlimPrototype/**` |

`Features.UI` does not list `UnityEngine.UI`; `Image` and `Button` resolve through
`Unity.TextMeshPro`, whose types derive from `MaskableGraphic`.

`Prototypes/**` and `FpvSlimPrototype/**` are self-contained spikes in their own
namespaces, referencing neither `Core` nor `Features`. They are not part of any
system documented here.

## Dependency rules

```
App ──────────────────────┐
  │                       ▼
  │   Features.*(Input, Modules, Cutscenes, Interactables, UI, Quests, Title)
  │                       │
  └───────────► Core ◄────┘
```

1. A Feature assembly references `Core` and Unity packages only.
2. A Feature assembly never references another Feature assembly.
3. `Core` references no assembly above itself.
4. `App` may reference `Core` and any Feature. It is the only assembly that may.
5. Nothing references `App`. A Feature needing to invoke App code is wired through
   scene data (`UnityEvent`) or an SO event channel.
6. `Core` never references `UnityEngine.InputSystem`.
7. `SceneManager` is called only in `Core/SceneControls/SceneLoader.cs`.

## Runtime sequence: new game to first module grant

```
GameFlow.Begin(session, NewGame)
  WorldState.NewSave()
  SceneStateMachine.ChangeSceneTo(Gameplay)
GameplayBootstrap.Start → newGameStarted.RaiseAction()
CutsceneDirector.Play(intro)
  InputRouter.Enter(Cutscene); Timeline runs
  playable.stopped → WorldState.SetFlag(CutsceneFinished("intro"))
                     eventRaiseOnFinish?.RaiseAction()
                     InputRouter.Exit(Cutscene)
WorldState.FactChanged
QuestRuntime → startConditions met → SetCounter(QuestStage(id), 1)
  ReconcileSetup instantiates stage 1 setupPrefabs:
    AxisInputDwell ×2, UIPopupRequest
UIPopupRequest.OnEnable → RaiseShow(prefab)
  UIMountPoint → Instantiate(prefab, container)
  UIPopup.OnEnable → InputRouter.Enter(Menu) → GamePauseInMenu sets timeScale 0
UIHoldToConfirm.onCompleted or btn_close → RaiseHide(root)
  UIMountPoint destroys instance → UIPopup.OnDisable → Exit(Menu) → timeScale 1
AxisInputDwell → SetFlag(TutorialPlayerMoved / TutorialPlayerRotated), self-destructs
WorldState.FactChanged → QuestRuntime → stage 2
stage 2 popup completes → FactSetterSO.Write → module.InteractModule.owned = true
WorldState.FactChanged → ModuleLoadout instantiates InteractionModule under ActorHost
  → Actor.RegisterModule → doors show prompts
```

Every step is a fact write, an event raise, or a context push. No step is a direct
call between systems.