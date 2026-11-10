using Content.Shared.Camera;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.Vehicles.Hardpoint;

/// <summary>Widens a gunner's view by the scale the weapons system gave them.</summary>
public sealed partial class VehicleGunnerViewSystem : EntitySystem
{
    [Dependency] private SharedContentEyeSystem _eye = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<VehicleGunnerViewUserComponent, GetEyePvsScaleEvent>(OnGetEyePvsScale);
        SubscribeLocalEvent<VehicleGunnerViewUserComponent, AfterAutoHandleStateEvent>(OnHandleState);
        SubscribeLocalEvent<VehicleGunnerViewUserComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<VehicleGunnerViewUserComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnGetEyePvsScale(Entity<VehicleGunnerViewUserComponent> ent, ref GetEyePvsScaleEvent args)
    {
        // A view on its way out still answers events until the deferred removal lands, and its own shutdown and its remover refresh the scale in that window.
        if (ent.Comp.LifeStage >= ComponentLifeStage.Stopping)
            return;

        args.Scale += ent.Comp.PvsScale + ent.Comp.CursorPvsIncrease;
    }

    private void OnHandleState(Entity<VehicleGunnerViewUserComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        RefreshPvsScale(ent.Owner);
    }

    private void OnStartup(Entity<VehicleGunnerViewUserComponent> ent, ref ComponentStartup args)
    {
        RefreshPvsScale(ent.Owner);
    }

    private void OnShutdown(Entity<VehicleGunnerViewUserComponent> ent, ref ComponentShutdown args)
    {
        RefreshPvsScale(ent.Owner);
    }

    private void RefreshPvsScale(EntityUid uid)
    {
        if (!HasComp<EyeComponent>(uid) || !HasComp<ContentEyeComponent>(uid))
            return;

        _eye.UpdatePvsScale(uid);
    }
}
