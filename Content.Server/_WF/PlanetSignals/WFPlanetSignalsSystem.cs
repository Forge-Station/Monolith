using Content.Shared._WF.PlanetCities;
using System.Linq;
using System.Numerics;
using Content.Server._WF.PlanetCities;
using Content.Server._WF.PlanetPois;
using Content.Server._WF.Planets;
using Content.Shared._WF.Planets;
using Content.Shared._WF.PlanetSignals;
using Content.Shared._WF.Vehicles.Core;
using Content.Shared.Implants.Components;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Shuttles.Components;
using Content.Shared.Station;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.PlanetSignals;

/// <summary>
/// Keeps each sector body's list of the vessels on its world's layers, the sites on its ground and the people on it:
/// counted, and named only where a medical tracking implant gives them away.
/// </summary>
public sealed class WFPlanetSignalsSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedStationSystem _station = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    /// <summary>Grids smaller than this many square metres are debris, not vessels.</summary>
    private const float MinVesselArea = 6f;

    /// <summary>The implant that names whoever carries it on a world's signal list.</summary>
    private static readonly EntProtoId MedicalTracker = "MedicalTrackingImplant";

    private TimeSpan _next;
    private readonly Dictionary<EntityUid, int> _people = new();
    private readonly Dictionary<EntityUid, (EntityUid Body, WFPlanetSignalLayer Layer)> _layers = new();
    private readonly Dictionary<EntityUid, List<WFPlanetSignal>> _found = new();

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _next)
            return;

        _next = _timing.CurTime + Interval;
        _layers.Clear();
        _found.Clear();
        _people.Clear();

        var networks = EntityQueryEnumerator<WFPlanetNetworkComponent>();
        while (networks.MoveNext(out var network, out var comp))
        {
            if (comp.Planet is not { } body || !HasComp<WFSectorPlanetComponent>(body))
                continue;

            var signals = new List<WFPlanetSignal>();
            _found[body] = signals;
            foreach (var layer in comp.Layers.Append(comp.OrbitMap).Append(comp.GroundMap))
            {
                _layers[layer] = (body, layer == comp.OrbitMap ? WFPlanetSignalLayer.Orbit
                    : layer == comp.GroundMap ? WFPlanetSignalLayer.Ground
                    : WFPlanetSignalLayer.Air);
            }

            foreach (var lower in comp.LowerLayers)
            {
                _layers[lower] = (body, WFPlanetSignalLayer.Underground);
            }

            AddSites(comp.GroundMap, signals);
        }

        AddPeople();

        var grids = EntityQueryEnumerator<MapGridComponent, TransformComponent>();
        while (grids.MoveNext(out var grid, out var gridComp, out var xform))
        {
            if (xform.MapUid is not { } map || map == grid || !_layers.TryGetValue(map, out var at)
                || gridComp.LocalAABB.Width * gridComp.LocalAABB.Height < MinVesselArea
                || HasComp<WFPlanetCityGridComponent>(grid))
                continue;

            var iff = CompOrNull<IFFComponent>(grid);
            if (iff != null && (iff.Flags & IFFFlags.Hide) != 0)
                continue;

            var name = iff != null && (iff.Flags & IFFFlags.HideLabel) != 0 ? string.Empty : Name(grid);
            var centre = Vector2.Transform(gridComp.LocalAABB.Center, _transform.GetWorldMatrix(xform));
            _found[at.Body].Add(new WFPlanetSignal(WFPlanetSignalKind.Vessel, name, centre.Floored(), GetNetEntity(map), at.Layer, null));
        }

        foreach (var (body, signals) in _found)
        {
            // A steady order, so an unchanged world is never resent.
            signals.Sort((a, b) => a.Kind != b.Kind ? a.Kind.CompareTo(b.Kind)
                : a.Layer != b.Layer ? a.Layer.CompareTo(b.Layer)
                : string.CompareOrdinal(a.Name, b.Name) is var name and not 0 ? name
                : a.Position.X != b.Position.X ? a.Position.X.CompareTo(b.Position.X)
                : a.Position.Y.CompareTo(b.Position.Y));

            var comp = EnsureComp<WFPlanetSignalsComponent>(body);
            var people = _people.GetValueOrDefault(body);
            if (comp.Signals.SequenceEqual(signals) && comp.People == people)
                continue;

            comp.Signals = signals;
            comp.People = people;
            Dirty(body, comp);
        }

        // A world whose network is gone shows nothing.
        var stale = EntityQueryEnumerator<WFPlanetSignalsComponent>();
        while (stale.MoveNext(out var body, out var comp))
        {
            if (_found.ContainsKey(body) || comp.Signals.Count == 0 && comp.People == 0)
                continue;

            comp.Signals = new List<WFPlanetSignal>();
            comp.People = 0;
            Dirty(body, comp);
        }
    }

    /// <summary>
    /// Counts the player characters on each world, living or dead, aboard a vessel or inside a vehicle on it too, and
    /// lists by name those with a medical tracking implant.
    /// </summary>
    private void AddPeople()
    {
        var query = EntityQueryEnumerator<MindContainerComponent, MobStateComponent, TransformComponent>();
        while (query.MoveNext(out var mob, out var container, out var mobState, out var xform))
        {
            // A player character, whether or not its player is still with it.
            if (container.Mind is not { } mindId || !TryComp<MindComponent>(mindId, out var mind)
                || mind.UserId == null && mind.OriginalOwnerUserId == null)
                continue;

            // Someone inside a vehicle is wherever the vehicle is.
            var where = xform;
            if (xform.MapUid is { } inside && !_layers.ContainsKey(inside)
                && TryComp<VehicleInteriorOccupantComponent>(mob, out var occupant)
                && TryComp<TransformComponent>(occupant.Vehicle, out var vehicleXform))
                where = vehicleXform;

            if (where.MapUid is not { } map || !_layers.TryGetValue(map, out var at))
                continue;

            _people[at.Body] = _people.GetValueOrDefault(at.Body) + 1;
            if (!HasMedicalTracker(mob))
                continue;

            var condition = mobState.CurrentState switch
            {
                MobState.Critical => WFPlanetSignalCondition.Critical,
                MobState.Dead => WFPlanetSignalCondition.Dead,
                _ => WFPlanetSignalCondition.Alive,
            };
            _found[at.Body].Add(new WFPlanetSignal(WFPlanetSignalKind.Person, Name(mob), _transform.GetWorldPosition(where).Floored(),
                GetNetEntity(map), at.Layer, null, condition));
        }
    }

    private bool HasMedicalTracker(EntityUid mob)
    {
        if (!TryComp<ImplantedComponent>(mob, out var implanted))
            return false;

        foreach (var implant in implanted.ImplantContainer.ContainedEntities)
        {
            if (MetaData(implant).EntityPrototype?.ID == MedicalTracker.Id)
                return true;
        }

        return false;
    }

    /// <summary>Every site on a ground, named once identified, with the grid of any ship running an expedition there.</summary>
    private void AddSites(EntityUid ground, List<WFPlanetSignal> signals)
    {
        if (!TryComp<WFPlanetPoiComponent>(ground, out var record))
            return;

        var map = GetNetEntity(ground);
        foreach (var site in record.Sites)
        {
            if (!TryComp<WFPoiSignalComponent>(site.Signal, out var signal))
                continue;

            NetEntity? expedition = null;
            if (site.ExpeditionClaim is { } station && _station.GetLargestGrid(station) is { } ship)
                expedition = GetNetEntity(ship);

            signals.Add(new WFPlanetSignal(WFPlanetSignalKind.Site, signal.Revealed ? site.Name.Id : string.Empty,
                signal.Origin, map, WFPlanetSignalLayer.Ground, expedition));
        }
    }
}
