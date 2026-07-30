# Modules (abilities)

**Assembly:** `Features.Modules` · **Namespace:** `Features.Modules` · **Source:** `Features/Modules/**`

Which modules the actor owns, and how instances get spawned from that record.
The dispatch half — `Actor`, `IModule`, `Command`, `TagSet` — is in
[`core.md`](core.md#7-possession--module-input).

Ownership is the flag `module.{id}.owned`. There is no owned-module list in
`SaveData`.

## Types

| Type | File | Description |
|---|---|---|
| `ModuleDefinitionSO` | `ModuleDefinitionSO.cs` | `id` (save-file identity), `LocalizedString displayName`, `GameObject prefab` carrying an `IModule`. Menu `Cleanbot/Modules/Definition`. |
| `ModuleCatalogSO` | `ModuleCatalogSO.cs` | `List<ModuleDefinitionSO> modules`. Menu `Cleanbot/Modules/Catalog`. |
| `ModuleLoadout` | `ModuleLoadout.cs` | `MonoBehaviour` on the `ActorHost`. Subscribes `WorldState.FactChanged` and self-primes in `OnEnable`. Walks the catalog and drives spawned instances to match the owned flags. Raises `ModuleInstalled(def)` for fresh grants only. |
| `ModuleKeySource` | `Editor/FactKeyRegistry/ModuleKeySource.cs` | Yields `FactKeys.ModuleOwned(id)` for every `ModuleDefinitionSO` asset ([`editor-tooling.md`](editor-tooling.md#fact-key-registry)). |

## Sequences

### Grant

```
UIHoldToConfirm.onCompleted (quest stage 2 popup)
  FactSetterSO.Write → module.InteractModule.owned = true
    WorldState.FactChanged
      ModuleLoadout walks the catalog
        Instantiate(prefab, moduleRoot, false)
          InteractionModule.Awake   GetComponentInParent<ActorHost>()
          InteractionModule.OnEnable Actor.RegisterModule(this)
```

### Restore

`FactChanged(null)` on load runs the same reconcile path. No restore-specific code
exists.

## Contracts

1. Ownership is recorded in exactly one place: `module.{id}.owned`.
2. `Instantiate(prefab, moduleRoot, false)` uses the parent overload. Modules
   resolve their owner with `GetComponentInParent<ActorHost>()` in `Awake`, which
   runs inside `Instantiate`; instantiating first and reparenting after leaves the
   host null. `false` preserves the prefab's authored local transform.
3. Uninstall requires no cooperation from the module. `Destroy` triggers
   `OnDisable`, which calls `Actor.RemoveModule(this)`.
4. `ModuleLoadout` treats the first reconcile and any `FactChanged(null)` as a
   restore and raises `ModuleInstalled` only otherwise.
5. Install presentation is authored on the definition, not on the loadout. An empty
   field means no presentation.
6. `id` is embedded in the save schema via `module.{id}.owned` and is frozen once a
   build has been played. The asset may be renamed.
7. Every fact change walks the whole catalog.

## Authoring

Create the `ModuleDefinitionSO`, add it to the catalog assigned on
`ModuleLoadout`, and author the granting `FactSetterSO` with its key picked from
the dropdown.
