using Content.Server._WF.SectorControl.Systems;
using Content.Shared._WF.DroneRings;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.DroneRings;

/// <summary>Keeps drones from spawning in sector cells a faction holds or has just freed.</summary>
public sealed class WFDroneHeldSpaceSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private WFSectorTerritorySystem _territory = default!;

    private HashSet<string>? _sources;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFDebrisPlacingEvent>(OnPlacing);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnReloaded);
    }

    private void OnReloaded(PrototypesReloadedEventArgs args)
    {
        _sources = null;
    }

    private void OnPlacing(ref WFDebrisPlacingEvent args)
    {
        if (Sources().Contains(args.Proto) && _territory.IsHeldOrClearing(args.At))
            args.Cancelled = true;
    }

    /// <summary>The debris-table entries that spawn drones.</summary>
    private HashSet<string> Sources()
    {
        if (_sources != null)
            return _sources;

        _sources = new HashSet<string>();
        foreach (var list in _prototypes.EnumeratePrototypes<WFDroneSourcesPrototype>())
        {
            foreach (var spawner in list.Spawners)
            {
                _sources.Add(spawner);
            }
        }

        return _sources;
    }
}
