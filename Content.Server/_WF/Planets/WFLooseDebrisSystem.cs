using Content.Server._Mono.GridClaimer;
using Content.Server._NF.Worldgen.Components.Debris;
using Content.Server.Worldgen.Components.Debris;
using Content.Shared._NF.Shipyard.Components;
using Robust.Server.Physics;

namespace Content.Server._WF.Planets;

/// <summary>
/// Tells asteroids and worldgen wrecks, and the pieces they break into, from ships. Planets leave debris alone: the
/// gravity well doesn't pull it and orbit doesn't decay it, so rocks never pile into planet transit.
/// </summary>
public sealed class WFLooseDebrisSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PostGridSplitEvent>(OnPostSplit);
    }

    private void OnPostSplit(ref PostGridSplitEvent args)
    {
        if (IsDebris(args.OldGrid))
            EnsureComp<WFLooseDebrisComponent>(args.Grid);
    }

    /// <summary>Whether a grid is unclaimed debris. A claimed or deeded one is somebody's ship.</summary>
    public bool IsDebris(EntityUid grid)
    {
        // SpaceDebris stays put; OwnedDebris comes off when a rock drifts out of its world chunk's map.
        return (HasComp<SpaceDebrisComponent>(grid) || HasComp<OwnedDebrisComponent>(grid) || HasComp<WFLooseDebrisComponent>(grid))
               && !HasComp<ShuttleDeedComponent>(grid)
               && !(TryComp<ClaimableGridComponent>(grid, out var claimable) && claimable.Claimed);
    }
}
