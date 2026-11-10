using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;

namespace Content.Client._WF.Vehicles.Visuals;

/// <summary>Asks what an eye should stay level with when it is not the grid its owner stands on.</summary>
[ByRefEvent]
public record struct EyeWatchedGridEvent(EntityUid? Relative = null);

/// <summary>Keeps the view level with the grid under a vehicle watched from inside its interior.</summary>
public sealed partial class VehicleEyeRotationSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<EyeComponent, EyeWatchedGridEvent>(OnWatchedGrid);
    }

    private void OnWatchedGrid(Entity<EyeComponent> ent, ref EyeWatchedGridEvent args)
    {
        if (ent.Comp.Target is not { } target || !TryComp(target, out TransformComponent? targetXform))
            return;

        // An eye on its owner's own map already follows the owner's grid.
        if (targetXform.MapID == Transform(ent).MapID)
            return;

        args.Relative = targetXform.GridUid ?? targetXform.MapUid;
    }
}
