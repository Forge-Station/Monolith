using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.Planets.Parachute;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.Vehicles.Falling;

/// <summary>Temporarily gives parachuted vehicles the existing canopy-controlled z-level physics.</summary>
public sealed class WFVehicleParachuteSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<VehicleFallComponent, WFParachuteAttachAttemptEvent>(OnAttachAttempt);
        SubscribeLocalEvent<WFParachutedComponent, ComponentStartup>(OnAttached);
        SubscribeLocalEvent<WFParachutedComponent, ComponentRemove>(OnRemoved);
    }

    private void OnAttachAttempt(Entity<VehicleFallComponent> ent, ref WFParachuteAttachAttemptEvent args)
    {
        args.Allowed |= ent.Comp.Enabled;
    }

    private void OnAttached(Entity<WFParachutedComponent> ent, ref ComponentStartup args)
    {
        if (!HasComp<VehicleFallComponent>(ent.Owner) || HasComp<CEZPhysicsComponent>(ent.Owner))
            return;

        EnsureComp<WFVehicleParachutePhysicsComponent>(ent.Owner);
        var physics = EnsureComp<CEZPhysicsComponent>(ent.Owner);
        physics.AutoStep = false;
        Dirty(ent.Owner, physics);
    }

    private void OnRemoved(Entity<WFParachutedComponent> ent, ref ComponentRemove args)
    {
        if (!HasComp<WFVehicleParachutePhysicsComponent>(ent.Owner) || TerminatingOrDeleted(ent.Owner))
            return;

        RemCompDeferred<CEZPhysicsComponent>(ent.Owner);
        RemCompDeferred<WFVehicleParachutePhysicsComponent>(ent.Owner);
    }
}

/// <summary>Marks z-physics added only for a vehicle's parachute, so removal restores its ordinary falling path.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WFVehicleParachutePhysicsComponent : Component;
