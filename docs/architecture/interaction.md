# Interaction & docking

**Assemblies:** `Core`, `Features.Modules`, `Features.Interactables` · **Source:** `Core/Interaction/**`, `Features/Modules/InteractionModule.cs`, `Features/Modules/ChargingModule.cs`, `Features/Interactables/**`

A sensor decides what is focused; the `Interact` intent decides when to act on it.
The module machinery is in [`core.md`](core.md#7-possession--module-input).

## Types

| Type | File | Description |
|---|---|---|
| `IInteractable` | `Core/Interaction/IInteractable.cs` | `CanInteract(actor)`, `OnFocus(hitPoint)`, `OnUnfocus()`, `Interact(actor)`. |
| `InteractionFocus` | `Core/Player/InteractionFocus.cs` | Held by `Actor`. `Current`, `Set`, `Clear`, with focus and unfocus callbacks. |
| `InteractionModule` | `Features/Modules/InteractionModule.cs` | `MonoBehaviour, IModule`. Sensor and executor. `Update` performs a `SphereCast` from the camera and sets `Actor.Focus`; `Handle(Interact)` calls `Focus.Current.Interact(owner)`. Blocked by `Interacting` and `Charging`. |
| `IDock` | `Core/Interaction/IDock.cs` | `Dock`, `UnDock`, `Docked`. |
| `IChargeable` | `Core/Player/IChargeable.cs` | `StartDocking(dock)`. |
| `ChargingModule` | `Features/Modules/ChargingModule.cs` | `IModule, IChargeable`. `StartDocking` docks the rigidbody and subscribes `Docked` to swap the `Interacting` tag for `Charging`. Pressing interact while charging undocks and clears the tag. |
| `ChargingStation` | `Features/Interactables/ChargingStation.cs` | `IInteractable, IDock`. `Interact` calls `actor.GetModule<IChargeable>().StartDocking(this)` and adds `Interacting`. `Dock` shows a stop-prompt, raises a static dock camera's depth, coroutine-lerps the body to `dockAnchor`, then fires `Docked`. |
| `SlidingDoors` | `Features/Interactables/SlidingDoors.cs` | `IInteractable`. Toggles an `Animator` bool, shows and hides a prompt ([`ui.md`](ui.md#channels--mount)). `OnMotionFinished` (animation event) clears `isBusy`. |

## Contracts

1. The world commands the actor through the same module dispatch used by input.
   `Actor.Dispatch` exists for this; `ChargingStation.Interact` currently calls
   `StartDocking` directly.
2. Mutual exclusion between `Interacting` and `Charging` is what prevents walking
   while docked. Modules decline through `BlockedBy`; there is no state machine.
3. `Intent.StopCharge` has no handler. `ChargingModule` reacts to `Interact`.

## Authoring

`SlidingDoors` requires an animation event calling `OnMotionFinished` at the end of
both open and close clips, and a `UIPromptDisplayRequestSO` asset shared with the
scene's `UIPrompt`. `ChargingStation` requires a `dockAnchor` transform and a
dedicated camera.
