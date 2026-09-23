using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Robust.Shared.Physics.Events;

namespace Content.Shared._Forge.Xenomorphs;

/// <summary>
/// Resin doors open when a xenomorph bumps them and close again after it passes.
/// </summary>
public sealed class ForgeXenoResinDoorSystem : EntitySystem
{
    [Dependency] private readonly SharedDoorSystem _doors = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ForgeXenoResinDoorComponent, StartCollideEvent>(OnCollide);
        SubscribeLocalEvent<ForgeXenoResinDoorComponent, EndCollideEvent>(OnEndCollide);
    }

    private void OnCollide(Entity<ForgeXenoResinDoorComponent> ent, ref StartCollideEvent args)
    {
        if (!HasComp<ForgeXenoComponent>(args.OtherEntity))
            return;

        if (!TryComp<DoorComponent>(ent, out var door))
            return;

        if (door.State is DoorState.Closed or DoorState.Closing)
            _doors.StartOpening(ent, door);
    }

    private void OnEndCollide(Entity<ForgeXenoResinDoorComponent> ent, ref EndCollideEvent args)
    {
        if (!HasComp<ForgeXenoComponent>(args.OtherEntity))
            return;

        if (!TryComp<DoorComponent>(ent, out var door))
            return;

        if (door.State is DoorState.Open or DoorState.Opening)
            _doors.StartClosing(ent, door);
    }
}
