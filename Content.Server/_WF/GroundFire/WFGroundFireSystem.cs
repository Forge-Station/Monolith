using Content.Server._WF.Planets.Atmosphere;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._ES.TileFires;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._WF.GroundFire;

/// <summary>
/// Keeps a tile fire on planet ground to a small patch that burns out. Upstream tile fires spread to every flammable
/// tile for as long as there is air, and a planet never runs out of it.
/// </summary>
public sealed class WFGroundFireSystem : EntitySystem
{
    [Dependency] private AtmosphereSystem _atmos = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedMapSystem _map = default!;

    /// <summary>Most tile fires one fire and all it starts may make, itself included.</summary>
    public const int MaxFires = 12;

    /// <summary>How long a fire on planet ground burns before it is put out for good.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(100);

    public override void Initialize()
    {
        SubscribeLocalEvent<WFGroundFireComponent, EntityTerminatingEvent>(OnTerminating);
    }

    /// <summary>Whether a fire may start another one. Fires off planet ground are never limited.</summary>
    public bool CanSpread(Entity<ESTileFireComponent> fire)
    {
        if (Transform(fire).GridUid is not { } grid || !HasComp<WFTerrainAtmosphereComponent>(grid))
            return true;

        if (!TryComp(fire, out WFGroundFireComponent? ground))
        {
            ground = AddComp<WFGroundFireComponent>(fire);
            ground.Budget = MaxFires - 1;
            Limit(fire.Comp, _timing.CurTime + Lifetime);
        }

        return ground.Budget > 0;
    }

    /// <summary>Hands a share of the parent's budget to a fire it just started.</summary>
    public void Spread(Entity<ESTileFireComponent> parent, EntityUid child)
    {
        if (!TryComp(parent, out WFGroundFireComponent? ground) || ground.Budget <= 0)
            return;

        var share = (ground.Budget - 1) / 2;
        ground.Budget -= 1 + share;
        AddComp<WFGroundFireComponent>(child).Budget = share;

        // The whole line goes out together.
        if (TryComp(child, out ESTileFireComponent? fire))
            Limit(fire, parent.Comp.SmolderTime);
    }

    private static void Limit(ESTileFireComponent fire, TimeSpan end)
    {
        if (end < fire.SmolderTime)
            fire.SmolderTime = end;

        fire.SmolderDeleteFireChance = 1f;
    }

    private void OnTerminating(Entity<WFGroundFireComponent> ent, ref EntityTerminatingEvent args)
    {
        var xform = Transform(ent);

        if (xform.GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var mapGrid))
            return;

        var tile = _map.TileIndicesFor(grid, mapGrid, xform.Coordinates);

        for (var x = -1; x <= 1; x++)
        for (var y = -1; y <= 1; y++)
        {
            _atmos.WfRestoreBurntAir(grid, tile + new Vector2i(x, y));
        }
    }
}
