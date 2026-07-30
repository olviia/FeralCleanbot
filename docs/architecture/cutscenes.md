# Cutscenes

**Assembly:** `Cutscenes` · **Namespace:** `Features.Cutscenes` · **Source:** `Features/Cutscenes/**`

Each cutscene declares its own trigger event. `CutsceneDirector` subscribes to all
of them and plays on raise. Playback is Timeline inside an instantiated prefab.

## Types

| Type | File | Description |
|---|---|---|
| `CutsceneDefinitionSO` | `CutsceneDefinitionSO.cs` | `id`, `cutscenePrefab` (carries a `PlayableDirector`), `eventTrigger` (`VoidEventSO`), `eventRaiseOnFinish` (`VoidEventSO`), `replayable`, `isTriggerForQuest`. Derived: `WritesFinishedFact => !replayable \|\| isTriggerForQuest`. |
| `CutsceneCatalogSO` | `CutsceneCatalogSO.cs` | `List<CutsceneDefinitionSO> cutscenes`, serialized onto the director. |
| `CutsceneDirector` | `CutsceneDirector.cs` | `MonoBehaviour`. Binds trigger handlers on enable, unbinds on disable, plays and tears down cutscenes. |
| `CutsceneTextScrambleReveal` | `CutsceneTextScrambleReveal.cs` | `ITimeControl` on a Timeline clip. Scrambles then settles TMP text char-by-char, driven by clip time. |

## Sequences

### Subscription — `OnEnable`

```
foreach def in catalog.cutscenes
    skip if def.eventTrigger == null
    skip if !def.replayable && WorldState.GetFlag(CutsceneFinished(def.id))
    bind () => Play(def) to def.eventTrigger.Raised, record the binding
```

`OnDisable` unbinds every recorded handler and clears the list.

### Playback — `Play(def)`

```
if activeCutscenes++ == 0        InputRouter.Enter(Cutscene)
instance = Instantiate(def.cutscenePrefab)
playable = instance.GetComponent<PlayableDirector>()
bind playable.Stop to CutsceneInput.SkipCutscene
playable.stopped:
    unbind the skip handler
    if def.WritesFinishedFact   WorldState.SetFlag(CutsceneFinished(def.id), true)
    def.eventRaiseOnFinish?.RaiseAction()
    Destroy(instance)
    if --activeCutscenes == 0    InputRouter.Exit(Cutscene)
playable.Play()
```

## Contracts

1. Adding a cutscene requires no code change.
2. Each cutscene prefab is self-contained and carries its own `PlayableDirector`
   and Timeline bindings. An unbound `ActivationTrack` fails silently.
3. The play-once gate is evaluated at subscription time in `OnEnable`. A cutscene
   that is both `replayable` and `isTriggerForQuest` writes its finished-fact but
   is not blocked from replaying.
4. The director owns the `Cutscene` input context for the duration of playback.
   Skip is a `Cinematic`-map action ([`input.md`](input.md)).
5. `cutscene.{id}.finished` is the hand-off to quests. The director does not
   reference the quest system.

## Authoring

Create the definition, assign `cutscenePrefab` and `eventTrigger`, add it to the
catalog assigned on `CutsceneDirector`, and ensure something raises the trigger
asset. To chain, assign `eventRaiseOnFinish` to the next cutscene's `eventTrigger`.
