using System.Linq;
using System.Numerics;
using Content.Server._Mono.ShipRepair;
using Content.Server._NF.Bank;
using Content.Server._NF.SectorServices;
using Content.Server._NF.Shipyard.Systems;
using Content.Server._WF.ShipAccess;
using Content.Server.GameTicking;
using Content.Server.Hands.Systems;
using Content.Server.Preferences.Managers;
using Content.Server.Shuttles.Components;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Shared._Mono.Ships.Components;
using Content.Shared._Mono.Shipyard;
using Content.Shared._NF.Shipyard;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.RoundSetup;
using Content.Shared._WF.ShipAccess;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.GameTicking;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Content.Shared.Preferences;
using Content.Shared.Salvage.Expeditions;
using Content.Shared.Station;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WF.RoundSetup;

/// <summary>Prepares civilian and faction ship starts and charges their host once through the existing bank.</summary>
public sealed partial class WFShipScenarioSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IServerPreferencesManager _preferences = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private BankSystem _bank = default!;
    [Dependency] private SectorServiceSystem _sectorServices = default!;
    [Dependency] private MapLoaderSystem _loader = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private StationSystem _stations = default!;
    [Dependency] private ShipyardSystem _shipyard = default!;
    [Dependency] private SharedIdCardSystem _cards = default!;
    [Dependency] private HandsSystem _hands = default!;
    [Dependency] private ShipRepairSystem _repair = default!;
    [Dependency] private WFShipAccessServerSystem _access = default!;
    [Dependency] private WFSafeSpawnSystem _safeSpawn = default!;

    private readonly Dictionary<int, Scenario> _scenarios = new();

    /// <summary>Holds the original purchase receipt until launch has finished or been refunded.</summary>
    private sealed class Scenario(EntityUid hull, EntityUid station, string vessel, WFShipStartLocation location)
    {
        public readonly EntityUid Hull = hull;
        public readonly EntityUid Station = station;
        public readonly string Vessel = vessel;
        public readonly WFShipStartLocation Location = location;
        public WFCrewFaction Faction;
        public readonly HashSet<EntityUid> Grids = new() { hull };
        public ICommonSession? Buyer;
        public string? PurchaseJob;
        public readonly List<PendingRefund> Receipts = new();
        public int Paid;
        public bool Used;
    }

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GridSplitEvent>(OnSplit);
        SubscribeLocalEvent<WFShipScenarioComponent, EntityTerminatingEvent>(OnHullTerminating);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRestart);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnFactionPrototypesReloaded);
    }

    /// <summary>Lists civilian designs or the selected faction tier catalog at the authoritative shipyard price.</summary>
    public WFRoundSetupVessel[] GetVessels(WFCrewFaction faction = WFCrewFaction.None, string? job = null)
    {
        var catalog = FactionVessels(faction);
        return _prototypes.EnumeratePrototypes<VesselPrototype>()
            .Where(v => faction == WFCrewFaction.None ? IsEligible(v, job) : IsFactionVessel(v, faction, catalog))
            .OrderBy(v => v.Price).ThenBy(v => v.Name)
            .Select(v => new WFRoundSetupVessel(v.ID, v.Name, v.Description, v.Price)).ToArray();
    }

    /// <summary>Requires a paid design in the civilian or selected faction catalog, including forged requests.</summary>
    public bool IsVesselAvailable(string? id, WFCrewFaction faction = WFCrewFaction.None, string? job = null) => id != null && Enum.IsDefined(faction) &&
        _prototypes.TryIndex<VesselPrototype>(id, out var vessel) &&
        (faction == WFCrewFaction.None ? IsEligible(vessel, job) : IsFactionVessel(vessel, faction, FactionVessels(faction)));

    /// <summary>Returns only the recipient’s selected character’s spendable balance.</summary>
    public int? GetBalance(ICommonSession host) =>
        _preferences.GetPreferencesOrNull(host.UserId)?.SelectedCharacter is HumanoidCharacterProfile profile &&
        !profile.TraitPreferences.Contains("Ironman") ? profile.BankBalance : null;

    /// <summary>Whether the host's selected character can pay for the design, checked before any grid loads.</summary>
    public bool CanAfford(string? vesselId, ICommonSession host, out string reason)
    {
        reason = "wf-round-setup-ship-invalid";
        if (vesselId == null || !_prototypes.TryIndex<VesselPrototype>(vesselId, out var vessel))
            return false;
        reason = "wf-round-setup-ship-bank-unavailable";
        if (GetBalance(host) is not { } balance)
            return false;
        reason = "wf-round-setup-ship-insufficient";
        return balance >= vessel.Price;
    }

    /// <summary>Loads a selected ship on a clear part of the sector before any money is withdrawn.</summary>
    /// <param name="licensed">A company hangar launch, whose design the company licensed; it skips the civilian catalogue check.</param>
    public bool TryPrepare(int lobbyId, string? vesselId, out EntityUid station, out string reason,
        WFShipStartLocation location = WFShipStartLocation.RandomSpace, WFCrewFaction faction = WFCrewFaction.None, string? job = null,
        bool licensed = false)
    {
        station = EntityUid.Invalid;
        if (HasAssignedShip(lobbyId))
        {
            reason = "wf-round-setup-ship-gone";
            return TryGetAssignedSpawn(lobbyId, out station, out _);
        }
        reason = "wf-round-setup-ship-invalid";
        if (!WFRoundSetupPolicy.IsShipStartAllowed(location, faction))
            return false;
        if (_scenarios.TryGetValue(lobbyId, out var existing))
        {
            if (existing.Vessel == vesselId && existing.Location == location && existing.Faction == faction && !TerminatingOrDeleted(existing.Station) && TryGetSpawn(lobbyId, out _))
            {
                if (!existing.Used && !licensed && !IsVesselAvailable(vesselId, faction, job))
                    return false;
                if (!existing.Used)
                    existing.PurchaseJob = job;
                station = existing.Station;
                return true;
            }
            if (existing.Used || existing.Paid > 0)
            {
                reason = "wf-round-setup-ship-gone";
                return false;
            }
            Rollback(lobbyId);
        }
        if (vesselId == null || !licensed && !IsVesselAvailable(vesselId, faction, job) ||
            !_prototypes.TryIndex<VesselPrototype>(vesselId, out var prototype) || prototype.Abstract)
            return false;
        var mapId = _ticker.DefaultMap;
        if (!_map.TryGetMap(mapId, out var mapEntity))
        {
            reason = "wf-round-setup-ship-no-sector";
            return false;
        }
        var vessel = _prototypes.Index<VesselPrototype>(vesselId!);
        reason = "wf-round-setup-ship-load-failed";
        if (!_loader.TryLoadGrid(mapId, vessel.ShuttlePath, out var loaded, offset: _random.NextVector2(1500f, 6000f)))
            return false;
        var hull = loaded.Value.Owner;
        EntityManager.AddComponents(hull, vessel.AddComponents);
        if (!HasComp<ShuttleComponent>(hull) || !TryFindFloor(hull, out _))
        {
            Del(hull);
            return false;
        }
        var positioned = location == WFShipStartLocation.RandomSpace
            ? FindClearPosition(hull, loaded.Value.Comp, mapEntity.Value, mapId)
            : TryStartAtStation(hull, loaded.Value.Comp, mapEntity.Value, mapId, faction);
        if (!positioned)
        {
            reason = location == WFShipStartLocation.RandomSpace ? reason : "wf-round-setup-ship-no-station-space";
            Del(hull);
            return false;
        }
        station = _stations.InitializeNewStation(new StationConfig
        {
            StationPrototype = "WFShipScenarioStation",
            StationComponentOverrides = new ComponentRegistry(),
        }, new[] { hull }, VesselStationName(vessel));
        EnsureComp<WFShipScenarioStationComponent>(station).LobbyId = lobbyId;
        AddVesselExpeditions(station, vessel);
        EnsureComp<WFShipScenarioComponent>(hull).LobbyId = lobbyId;
        _scenarios.Add(lobbyId, new Scenario(hull, station, vessel.ID, location) { Faction = faction, PurchaseJob = job });
        return true;
    }

    /// <summary>The numbered name a shipyard purchase of the vessel gets, such as "HT Sagittarius EXP14-123".</summary>
    private string VesselStationName(VesselPrototype vessel)
    {
        if (_prototypes.TryIndex<GameMapPrototype>(vessel.ID, out var gameMap) &&
            gameMap.Stations.TryGetValue(vessel.ID, out var config) &&
            config.StationComponentOverrides?.TryGetValue(Factory.GetComponentName<StationNameSetupComponent>(), out var entry) == true &&
            entry.Component is StationNameSetupComponent setup)
        {
            return setup.NameGenerator?.FormatName(setup.StationNameTemplate) ?? setup.StationNameTemplate;
        }

        return vessel.Name;
    }

    /// <summary>Gives a private scenario station the expedition offers the vessel's own station would have.</summary>
    public void AddVesselExpeditions(EntityUid station, VesselPrototype vessel)
    {
        if (_prototypes.TryIndex<GameMapPrototype>(vessel.ID, out var gameMap) &&
            gameMap.Stations.TryGetValue(vessel.ID, out var config) &&
            _prototypes.TryIndex(config.StationPrototype, out var stationProto) &&
            stationProto.Components.ContainsKey(Factory.GetComponentName<SalvageExpeditionDataComponent>()))
        {
            EnsureComp<SalvageExpeditionDataComponent>(station);
        }
    }

    /// <summary>Records a company hangar launch as paid by the host, so the deed can be handed out without any refund owed.</summary>
    public void MarkCompanyPaid(int lobbyId, ICommonSession host)
    {
        if (!_scenarios.TryGetValue(lobbyId, out var scenario) || scenario.Paid > 0)
            return;
        scenario.Paid = _prototypes.Index<VesselPrototype>(scenario.Vessel).Price;
        scenario.Buyer = host;
    }

    /// <summary>Gives the successfully spawned buyer a deed and registers normal ship ownership.</summary>
    public bool CompletePurchase(int lobbyId, ICommonSession host, string shipName = "")
    {
        if (!_scenarios.TryGetValue(lobbyId, out var scenario) || scenario.Paid <= 0 || scenario.Buyer?.UserId != host.UserId)
            return false;
        if (scenario.Used)
            return true;
        if (!HasSectorServices() || host.AttachedEntity is not { } body || !scenario.Grids.Contains(Transform(body).GridUid ?? EntityUid.Invalid) ||
            TerminatingOrDeleted(scenario.Hull) || TerminatingOrDeleted(scenario.Station))
            return false;
        EntityUid card;
        if (_cards.TryFindIdCard(body, out var found))
            card = found.Owner;
        else
        {
            card = Spawn("IDCardStandard", Transform(body).Coordinates);
            _cards.TryChangeFullName(card, Name(body));
            _hands.TryPickupAnyHand(body, card);
        }
        var vessel = _prototypes.Index<VesselPrototype>(scenario.Vessel);
        // Crew purchases bypass the normal loader that initializes the sale service.
        _shipyard.SetupShipyardIfNeeded();
        if (!_shipyard.TryAssignDeed(scenario.Hull, card, host, vessel, string.IsNullOrWhiteSpace(shipName) ? Name(scenario.Station) : shipName, existingStation: scenario.Station))
            return false;
        scenario.Used = true;
        EnsureComp<ShipActivityComponent>(scenario.Hull);
        _repair.GenerateRepairData(scenario.Hull);
        return true;
    }

    /// <summary>Links the purchased ship to live crew membership without creating permanent guest entries.</summary>
    public void NotifyArrival(int lobbyId, ICommonSession session)
    {
        if (session.AttachedEntity is not { } body)
            return;
        foreach (var grid in GetActiveGrids(lobbyId))
        {
            if (TryComp<WFShipAccessComponent>(grid, out var access) && access.CrewId == null &&
                _access.TryLinkCrew((grid, access), body))
                _access.SetMode((grid, access), WFShipAccessMode.Crew);
        }
    }

    /// <summary>Returns deck coordinates only on this lobby's surviving ship sections.</summary>
    public bool TryGetSpawn(int lobbyId, out EntityCoordinates coordinates)
    {
        coordinates = default;
        if (HasAssignedShip(lobbyId))
            return TryGetAssignedSpawn(lobbyId, out _, out coordinates);
        if (!_scenarios.TryGetValue(lobbyId, out var scenario) || TerminatingOrDeleted(scenario.Station))
            return false;
        foreach (var grid in scenario.Grids)
        {
            if (!TerminatingOrDeleted(grid) && TryFindFloor(grid, out coordinates))
                return true;
        }
        return false;
    }

    /// <summary>Refunds the original character from fresh preferences, then deletes an unused preparation.</summary>
    public void Rollback(int lobbyId)
    {
        if (!_scenarios.TryGetValue(lobbyId, out var scenario) || scenario.Used)
            return;
        RefundPayments(scenario);
        _scenarios.Remove(lobbyId);
        foreach (var grid in scenario.Grids)
        {
            if (!TerminatingOrDeleted(grid))
                Del(grid);
        }
        if (!TerminatingOrDeleted(scenario.Station))
            Del(scenario.Station);
    }

    /// <summary>Closing a used crew leaves its purchased ship and passengers intact.</summary>
    public void RemoveScenario(int lobbyId)
    {
        _assignedShips.Remove(lobbyId);
        if (_scenarios.TryGetValue(lobbyId, out var scenario) && scenario.Used)
            _scenarios.Remove(lobbyId);
        else
            Rollback(lobbyId);
    }

    /// <summary>Requires the sector registry before accepting payment or issuing a deed.</summary>
    private bool HasSectorServices()
    {
        var service = _sectorServices.GetServiceEntity();
        return service.Valid && !TerminatingOrDeleted(service);
    }

    /// <summary>Moves a newly loaded hull away from existing grids before admitting its crew.</summary>
    private bool FindClearPosition(EntityUid hull, MapGridComponent grid, EntityUid map, MapId mapId,
        Vector2 center = default, float minimumRadius = 1500f, float maximumRadius = 6000f)
    {
        var nearby = new List<Entity<MapGridComponent>>();
        for (var attempt = 0; attempt < 32; attempt++)
        {
            var position = center + _random.NextVector2(minimumRadius, maximumRadius);
            _transform.SetCoordinates(hull, new EntityCoordinates(map, position));
            var bounds = _transform.GetWorldMatrix(hull).TransformBox(grid.LocalAABB).Enlarged(100f);
            nearby.Clear();
            _map.FindGridsIntersecting(mapId, bounds, ref nearby, approx: true, includeMap: false);
            if (nearby.All(other => other.Owner == hull))
                return true;
        }
        return false;
    }

    /// <summary>Picks a breathable interior deck tile, never exterior plating or machinery.</summary>
    private bool TryFindFloor(EntityUid hull, out EntityCoordinates coordinates) =>
        _safeSpawn.TryFindSafeFloor(hull, out coordinates);

    /// <summary>Keeps late arrivals confined to fragments of the purchased hull.</summary>
    private void OnSplit(ref GridSplitEvent args)
    {
        if (!TryComp<WFShipScenarioComponent>(args.Grid, out var original))
            return;
        HashSet<EntityUid>? grids = null;
        if (_assignedShips.TryGetValue(original.LobbyId, out var assigned) && assigned.Grids.Contains(args.Grid))
            grids = assigned.Grids;
        else if (_scenarios.TryGetValue(original.LobbyId, out var scenario) && scenario.Grids.Contains(args.Grid))
            grids = scenario.Grids;
        if (grids == null)
            return;
        foreach (var grid in args.NewGrids)
        {
            EnsureComp<WFShipScenarioComponent>(grid).LobbyId = original.LobbyId;
            grids.Add(grid);
        }
    }

    /// <summary>Revokes surviving cards before their networked deed can reference a deleted scenario hull.</summary>
    private void OnHullTerminating(Entity<WFShipScenarioComponent> ent, ref EntityTerminatingEvent args)
    {
        var owners = new List<EntityUid>();
        var deeds = AllEntityQuery<ShuttleDeedComponent>();
        while (deeds.MoveNext(out var owner, out var deed))
        {
            if (deed.ShuttleUid == ent.Owner && !TerminatingOrDeleted(owner))
                owners.Add(owner);
        }
        foreach (var owner in owners)
            RemComp<ShuttleDeedComponent>(owner);
    }
    private void OnRestart(RoundRestartCleanupEvent args)
    {
        foreach (var id in _scenarios.Keys.Concat(_assignedShips.Keys).Distinct().ToArray())
            RemoveScenario(id);
    }
}
