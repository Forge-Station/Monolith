using Content.Shared.Actions;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Robust.Shared.Network;

namespace Content.Shared._Forge.Botany.PlantGrab;

public sealed class SharedPlantGrabSystem : EntitySystem
{
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly INetManager _net = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PlantGrabbedComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<PlantGrabbedComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<PlantGrabbedComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
        SubscribeLocalEvent<PlantGrabbedComponent, PlantGrabBreakFreeEvent>(OnBreakFree);
    }

    private void OnStartup(Entity<PlantGrabbedComponent> ent, ref ComponentStartup args)
    {
        if (_net.IsServer)
            _actions.AddAction(ent.Owner, ref ent.Comp.BreakFreeActionEntity, ent.Comp.BreakFreeAction);

        _movement.RefreshMovementSpeedModifiers(ent.Owner);
    }

    private void OnShutdown(Entity<PlantGrabbedComponent> ent, ref ComponentShutdown args)
    {
        if (_net.IsServer && ent.Comp.BreakFreeActionEntity != null)
            _actions.RemoveAction(ent.Owner, ent.Comp.BreakFreeActionEntity);

        _movement.RefreshMovementSpeedModifiers(ent.Owner);
    }

    private void OnRefreshSpeed(Entity<PlantGrabbedComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        args.ModifySpeed(ent.Comp.WalkModifier, ent.Comp.SprintModifier);
    }

    private void OnBreakFree(Entity<PlantGrabbedComponent> ent, ref PlantGrabBreakFreeEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        if (!_net.IsServer)
            return;

        _popup.PopupEntity(Loc.GetString("plant-grab-break-free"), ent.Owner, ent.Owner);
        RemComp<PlantGrabbedComponent>(ent.Owner);
    }
}
