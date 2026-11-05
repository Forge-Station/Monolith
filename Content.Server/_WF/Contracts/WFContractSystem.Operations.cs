using System.Linq;
using Content.Shared._WF.Contracts;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Verbs;
using Content.Shared.Xenoarchaeology.Artifact;
using Content.Shared.Xenoarchaeology.Artifact.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Random;

namespace Content.Server._WF.Contracts;

/// <summary>Mail runs, sweeps, clearing, repairs, downloads, inspections, salvage operations, departures and chains.</summary>
public sealed partial class WFContractSystem
{
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private ItemSlotsSystem _itemSlots = default!;
    [Dependency] private SharedXenoArtifactSystem _artifacts = default!;
    [Dependency] private Content.Shared.Mobs.Systems.MobStateSystem _mobState = default!;

    /// <summary>Metres between derelicts of a twin survey.</summary>
    private const float TwinSpread = 160f;

    /// <summary>Least distance between two points of a sweep.</summary>
    private const float SweepSpacing = 800f;

    /// <summary>How long a follow-up offer waits for its crew.</summary>
    private static readonly TimeSpan FollowUpLifetime = TimeSpan.FromMinutes(15);

    /// <summary>Derelicts finished with (repaired, towed in) and when they go, once no one is aboard.</summary>
    private readonly Dictionary<EntityUid, TimeSpan> _departures = new();

    private void InitializeOperations()
    {
        SubscribeLocalEvent<WFContractInspectableComponent, GetVerbsEvent<AlternativeVerb>>(OnInspectVerbs);
    }

    /// <summary>Rolls one of the second set of objectives; null if the objective is not one of them.</summary>
    private bool? TryRollOperation(WFContract contract, WFContractPrototype template, Dictionary<string, EntityUid> homes,
        EntityUid board, string boardTag)
    {
        switch (template.Objective)
        {
            case WFContractMailRun mail:
            {
                var stations = homes.Values.Distinct().Where(s => s != board && OpenTo(s, boardTag, homes)).ToList();
                var count = _random.Next(mail.Stops.X, Math.Max(mail.Stops.X, mail.Stops.Y) + 1);
                if (stations.Count < mail.Stops.X)
                    return false;

                _random.Shuffle(stations);
                contract.Stops = stations.Take(count).ToList();
                contract.Count = contract.Stops.Count;
                var at = board;
                foreach (var stop in contract.Stops)
                {
                    contract.Distance += Distance(at, stop);
                    at = stop;
                }

                contract.Reward += mail.PerStop * contract.Count + DistancePay(mail.PerKilometre, contract.Distance);
                return true;
            }
            case WFContractSweep sweep:
                contract.Count = _random.Next(sweep.Points.X, Math.Max(sweep.Points.X, sweep.Points.Y) + 1);
                contract.Distance = (sweep.MinDistance + sweep.MaxDistance) / 2f;
                contract.Reward += sweep.PerPoint * contract.Count;
                return true;
            case WFContractClear clear:
                return RollWreck(contract, clear.Wrecks, clear.MinDistance, clear.MaxDistance);
            case WFContractRepair repair:
                return RollWreck(contract, repair.Wrecks, repair.MinDistance, repair.MaxDistance);
            case WFContractDownload download:
                return RollWreck(contract, download.Wrecks, download.MinDistance, download.MaxDistance);
            case WFContractInspect inspect:
                return RollWreck(contract, inspect.Wrecks, inspect.MinDistance, inspect.MaxDistance);
            case WFContractOperation operation:
                if (!operation.Anomalies.Any(id => _proto.HasIndex(id)) || !RollWreck(contract, operation.Wrecks,
                        operation.MinDistance, operation.MaxDistance))
                    return false;

                contract.Destination = contract.Home;
                contract.Count = _random.Next(operation.Count.X, Math.Max(operation.Count.X, operation.Count.Y) + 1);
                return true;
        }

        return null;
    }

    private bool RollWreck(WFContract contract, List<Robust.Shared.Utility.ResPath> wrecks, float min, float max)
    {
        if (wrecks.Count == 0)
            return false;

        contract.Wreck = _random.Pick(wrecks);
        contract.Distance = (min + max) / 2f;
        return true;
    }

    /// <summary>Puts one of the second set of objectives in the world; null if the objective is not one of them.</summary>
    private bool? TryStartOperation(WFContract contract, WFContractObjective objective, EntityUid board)
    {
        switch (objective)
        {
            case WFContractMailRun mail:
            {
                if (BoardDrop(board) is not { } coords)
                    return false;

                foreach (var stop in contract.Stops)
                {
                    var parcel = Spawn(mail.Package, coords);
                    Track(contract, parcel);
                    contract.ParcelStops[parcel] = stop;
                    _meta.SetEntityName(parcel, Loc.GetString("wf-contract-package-name", ("name", Name(parcel)),
                        ("destination", StationName(stop))));
                }

                return true;
            }
            case WFContractSweep sweep:
            {
                for (var attempt = 0; attempt < contract.Count * 6 && contract.Points.Count < contract.Count; attempt++)
                {
                    if (PickSite(contract.Home, sweep.MinDistance, sweep.MaxDistance, 1000f) is not { } point
                        || contract.Points.Any(p => (p.Position - point.Position).Length() < SweepSpacing))
                        continue;

                    contract.Points.Add(point);
                }

                if (contract.Points.Count < contract.Count)
                {
                    contract.Points.Clear();
                    return false;
                }

                contract.Site = contract.Points[0];
                return true;
            }
            case WFContractClear clear:
            {
                if (LoadWreck(contract, clear.MinDistance, clear.MaxDistance, clear.StationClearance) is not { } grid)
                    return false;

                var count = _random.Next(clear.Targets.Count.X, Math.Max(clear.Targets.Count.X, clear.Targets.Count.Y) + 1);
                for (var i = 0; i < count && FreeTile(grid) is { } tile; i++)
                {
                    contract.Targets.Add(Spawn(clear.Targets.Entity, tile));
                }

                if (contract.Targets.Count == 0)
                {
                    DropWreck(contract);
                    return false;
                }

                contract.Count = contract.Targets.Count;
                Infest(grid, clear.Infestation);
                return true;
            }
            case WFContractRepair repair:
                return StartDevice(contract, repair.Device, repair.MinDistance, repair.MaxDistance, repair.StationClearance,
                    repair.Infestation, device =>
                    {
                        if (_proto.TryIndex<DamageTypePrototype>("Blunt", out var blunt))
                            _damageable.TryChangeDamage(device, new DamageSpecifier(blunt, 80), ignoreResistances: true);
                    });
            case WFContractDownload download:
                return StartDevice(contract, download.Terminal, download.MinDistance, download.MaxDistance,
                    download.StationClearance, new List<WFContractSpawn>(), null);
            case WFContractInspect inspect:
                return StartDevice(contract, inspect.Cargo, inspect.MinDistance, inspect.MaxDistance, inspect.StationClearance,
                    inspect.Crew, cargo => EnsureComp<WFContractInspectableComponent>(cargo).Contract = contract.Id);
            case WFContractOperation operation:
            {
                if (LoadWreck(contract, operation.MinDistance, operation.MaxDistance, operation.StationClearance) is not { } grid
                    || FreeTile(grid) is not { } itemTile)
                {
                    DropWreck(contract);
                    return false;
                }

                Track(contract, Spawn(operation.Item, itemTile));
                PlaceAnomalies(contract, new List<Entity<MapGridComponent>> { grid }, operation.Anomalies);
                if (contract.Anomalies.Count < contract.Count)
                {
                    DropWreck(contract);
                    return false;
                }

                Infest(grid, operation.Infestation);
                return true;
            }
        }

        return null;
    }

    /// <summary>Loads a wreck with one device on it, recorded as the contract's target.</summary>
    private bool StartDevice(WFContract contract, Robust.Shared.Prototypes.EntProtoId device, float min, float max, float clearance,
        List<WFContractSpawn> infestation, Action<EntityUid>? setup)
    {
        if (LoadWreck(contract, min, max, clearance) is not { } grid || FreeTile(grid) is not { } tile)
        {
            DropWreck(contract);
            return false;
        }

        var placed = Spawn(device, tile);
        contract.Targets.Add(placed);
        setup?.Invoke(placed);
        Infest(grid, infestation);
        return true;
    }

    /// <summary>Spreads a survey's anomalies over its derelicts in turn, each started at a safe severity.</summary>
    private void PlaceAnomalies(WFContract contract, List<Entity<MapGridComponent>> grids, List<Robust.Shared.Prototypes.EntProtoId> pool)
    {
        var anomalies = pool.Where(id => _proto.HasIndex(id)).ToList();
        for (var i = 0; i < contract.Count; i++)
        {
            if (FreeTile(grids[i % grids.Count]) is not { } tile)
                break;

            var anomaly = Spawn(_random.Pick(anomalies), tile);
            if (TryComp<Content.Shared.Anomaly.Components.AnomalyComponent>(anomaly, out var comp) && comp.Severity > SurveyStartSeverity)
                _anomalies.ChangeAnomalySeverity(anomaly, SurveyStartSeverity - comp.Severity, comp);
            contract.Anomalies.Add(anomaly);
        }
    }

    /// <summary>Loads a survey's extra derelicts beside its first.</summary>
    private List<Entity<MapGridComponent>> LoadTwins(WFContract contract, Entity<MapGridComponent> first, int derelicts)
    {
        var grids = new List<Entity<MapGridComponent>> { first };
        if (contract.Site is not { } site || contract.Wreck is not { } wreck)
            return grids;

        for (var i = 1; i < derelicts; i++)
        {
            var offset = _random.NextAngle().ToVec() * TwinSpread;
            if (!_mapLoader.TryLoadGrid(site.MapId, wreck, out var twin, offset: site.Position + offset, rot: _random.NextAngle()))
                continue;

            _meta.SetEntityName(twin.Value.Owner, Loc.GetString("wf-contract-wreck-name", ("id", contract.Id)));
            contract.ExtraWrecks.Add(twin.Value.Owner);
            grids.Add(twin.Value);
        }

        return grids;
    }

    /// <summary>Checks one of the second set of objectives; false if the objective is not one of them.</summary>
    private bool CheckOperation(WFContract contract, WFContractObjective objective)
    {
        switch (objective)
        {
            case WFContractMailRun:
                foreach (var (parcel, stop) in contract.ParcelStops)
                {
                    if (TerminatingOrDeleted(parcel) || !TryComp<WFContractItemComponent>(parcel, out var comp) || comp.Delivered
                        || !OnStation(parcel, stop))
                        continue;

                    comp.Delivered = true;
                    contract.Delivered++;
                    QueueDel(parcel);
                    _dirty = true;
                    if (contract.Delivered < contract.Count && contract.User is { } user)
                        Tell(new WFContractHolder(user, contract.Character), Loc.GetString("wf-contract-package-delivered",
                            ("title", Title(contract)), ("done", contract.Delivered), ("count", contract.Count)));
                }

                if (contract.Delivered >= contract.Count)
                    Complete(contract);
                return true;
            case WFContractSweep sweep:
            {
                var bodies = CrewBodies(contract);
                for (var i = 0; i < contract.Points.Count; i++)
                {
                    var point = contract.Points[i];
                    if (contract.Logged.Contains(i) || !bodies.Any(b => Near(b, point, sweep.Range)))
                        continue;

                    contract.Logged.Add(i);
                    _dirty = true;
                    if (contract.Logged.Count < contract.Points.Count && contract.User is { } user)
                        Tell(new WFContractHolder(user, contract.Character), Loc.GetString("wf-contract-point-logged",
                            ("title", Title(contract)), ("done", contract.Logged.Count), ("count", contract.Points.Count)));
                }

                if (contract.Logged.Count >= contract.Points.Count)
                    Complete(contract);
                return true;
            }
            case WFContractClear:
                if (contract.Targets.All(t => TerminatingOrDeleted(t)))
                    Complete(contract);
                return true;
            case WFContractRepair repair:
            {
                if (contract.Targets.FirstOrDefault() is not { Valid: true } device || TerminatingOrDeleted(device))
                {
                    Fail(contract, "wf-contract-reason-device-lost");
                    return true;
                }

                if (Repaired(device))
                {
                    if (contract.WreckGrid is { } wreck)
                        _departures[wreck] = _timing.CurTime + repair.Departure;
                    Complete(contract);
                }

                return true;
            }
            case WFContractDownload download:
            {
                if (contract.Targets.FirstOrDefault() is not { Valid: true } terminal || TerminatingOrDeleted(terminal))
                {
                    Fail(contract, "wf-contract-reason-device-lost");
                    return true;
                }

                var at = _transform.GetMapCoordinates(terminal);
                if (!CrewBodies(contract).Any(b => Near(b, at, download.Range)))
                    return true;

                var now = _timing.CurTime;
                if (!contract.Started)
                {
                    contract.Started = true;
                    contract.NextWave = now;
                    if (contract.User is { } user)
                        Tell(new WFContractHolder(user, contract.Character), Loc.GetString("wf-contract-download-started",
                            ("title", Title(contract))));
                }

                contract.Progress += CheckInterval;
                _dirty = true;
                if (now >= contract.NextWave && contract.WreckGrid is { } wreck && TryComp<MapGridComponent>(wreck, out var grid))
                {
                    contract.NextWave = now + download.WaveInterval;
                    Infest((wreck, grid), download.Waves);
                }

                if (contract.Progress >= download.Duration)
                    Complete(contract);
                return true;
            }
            case WFContractInspect:
                if (contract.Targets.FirstOrDefault() is not { Valid: true } cargo || TerminatingOrDeleted(cargo))
                    Fail(contract, "wf-contract-reason-device-lost");
                return true;
            case WFContractOperation operation:
            {
                UpdateSurvey(contract);
                if (!contract.Recovered && contract.Items.FirstOrDefault() is { Valid: true } item && !TerminatingOrDeleted(item)
                    && OnStation(item, contract.Destination) && TryComp<WFContractItemComponent>(item, out var itemComp))
                {
                    itemComp.Delivered = true;
                    contract.Recovered = true;
                    QueueDel(item);
                    _dirty = true;
                }

                if (contract.WreckGrid is not { } hull || TerminatingOrDeleted(hull))
                {
                    if (!contract.Towed)
                    {
                        Fail(contract, "wf-contract-reason-hull-lost");
                        return true;
                    }
                }
                else if (!contract.Towed && TowGap(contract) is { } gap && gap <= operation.Range)
                {
                    contract.Towed = true;
                    _dirty = true;
                }

                if (contract.Anomalies.Any(a => !contract.Scanned.Contains(a) && TerminatingOrDeleted(a)))
                    Fail(contract, "wf-contract-reason-anomaly-collapsed");
                else if (contract.Recovered && contract.Towed && contract.Scanned.Count >= contract.Count)
                {
                    if (contract.WreckGrid is { } towed)
                        _departures[towed] = _timing.CurTime;
                    Complete(contract);
                }
                return true;
            }
        }

        return false;
    }

    /// <summary>The next unlogged point of a sweep.</summary>
    private MapCoordinates? NextPoint(WFContract contract)
    {
        for (var i = 0; i < contract.Points.Count; i++)
        {
            if (!contract.Logged.Contains(i))
                return contract.Points[i];
        }

        return null;
    }

    /// <summary>How the board and app show one of the second set of objectives.</summary>
    private void ViewOperation(WFContract contract, WFContractObjective objective, WFContractView view, bool active)
    {
        var home = StationName(contract.Home);
        var km = Km(contract.Distance);
        switch (objective)
        {
            case WFContractMailRun:
            {
                view.Site = WFContractSite.Station;
                view.Location = Loc.GetString("wf-contract-location-mail", ("count", contract.Count), ("km", km));
                var delivered = contract.ParcelStops.Where(p => TerminatingOrDeleted(p.Key)
                                                                || CompOrNull<WFContractItemComponent>(p.Key)?.Delivered == true)
                    .Select(p => p.Value).ToHashSet();
                foreach (var stop in contract.Stops)
                {
                    view.Objectives.Add(new WFContractObjectiveView(Loc.GetString("wf-contract-objective-mail",
                        ("destination", StationName(stop))), active && delivered.Contains(stop)));
                }

                return;
            }
            case WFContractSweep:
            {
                view.Site = WFContractSite.Space;
                view.Location = Loc.GetString("wf-contract-location-sweep", ("home", home), ("count", contract.Count), ("km", km),
                    ("active", active));
                if (active && NextPoint(contract) is { } next)
                    view.Coordinates = next.Position;
                if (!active)
                {
                    view.Objectives.Add(new WFContractObjectiveView(Loc.GetString("wf-contract-objective-sweep-count",
                        ("count", contract.Count)), false));
                    return;
                }

                for (var i = 0; i < contract.Points.Count; i++)
                {
                    var p = contract.Points[i].Position;
                    view.Objectives.Add(new WFContractObjectiveView(Loc.GetString("wf-contract-objective-sweep",
                        ("x", (int) p.X), ("y", (int) p.Y)), contract.Logged.Contains(i)));
                }

                return;
            }
            case WFContractClear:
                view.Site = WFContractSite.Space;
                view.Location = Loc.GetString("wf-contract-location-derelict", ("home", home), ("km", km), ("active", active));
                view.Objectives.Add(new WFContractObjectiveView(Loc.GetString("wf-contract-objective-clear",
                    ("done", contract.Targets.Count(t => TerminatingOrDeleted(t))), ("count", contract.Count)), false));
                break;
            case WFContractRepair:
            {
                view.Site = WFContractSite.Space;
                view.Location = Loc.GetString("wf-contract-location-derelict", ("home", home), ("km", km), ("active", active));
                var device = contract.Targets.FirstOrDefault();
                var whole = active && device.Valid && TryComp<DamageableComponent>(device, out var damage) && damage.TotalDamage <= 0;
                var powered = active && device.Valid && !TerminatingOrDeleted(device) && _itemSlots.GetItemOrNull(device, "cell_slot") != null;
                view.Objectives.Add(new WFContractObjectiveView(Loc.GetString("wf-contract-objective-repair-weld"), whole));
                view.Objectives.Add(new WFContractObjectiveView(Loc.GetString("wf-contract-objective-repair-cell"), powered));
                break;
            }
            case WFContractDownload download:
                view.Site = WFContractSite.Space;
                view.Location = Loc.GetString("wf-contract-location-derelict", ("home", home), ("km", km), ("active", active));
                view.Objectives.Add(new WFContractObjectiveView(Loc.GetString("wf-contract-objective-download",
                    ("percent", (int) Math.Min(100, contract.Progress / download.Duration * 100)),
                    ("minutes", (int) Math.Ceiling(download.Duration.TotalMinutes))), false));
                break;
            case WFContractInspect:
                view.Site = WFContractSite.Space;
                view.Location = Loc.GetString("wf-contract-location-derelict", ("home", home), ("km", km), ("active", active));
                view.Objectives.Add(new WFContractObjectiveView(Loc.GetString("wf-contract-objective-inspect"), false));
                break;
            case WFContractOperation operation:
                view.Site = WFContractSite.Space;
                view.Location = Loc.GetString("wf-contract-location-derelict", ("home", home), ("km", km), ("active", active));
                view.Objectives.Add(new WFContractObjectiveView(Loc.GetString("wf-contract-objective-survey",
                    ("done", contract.Scanned.Count), ("count", contract.Count)), active && contract.Scanned.Count >= contract.Count));
                view.Objectives.Add(new WFContractObjectiveView(Loc.GetString("wf-contract-objective-return",
                    ("item", _proto.Index(operation.Item).Name), ("destination", home)), contract.Recovered));
                view.Objectives.Add(new WFContractObjectiveView(Loc.GetString("wf-contract-objective-tow",
                    ("destination", home), ("range", (int) operation.Range), ("gap", -1)), contract.Towed));
                break;
        }

        if (active && contract.WreckGrid is { } wreck && !TerminatingOrDeleted(wreck))
            view.Coordinates = _transform.GetWorldPosition(wreck);
        else if (active && contract.Site is { } site)
            view.Coordinates = site.Position;
    }

    /// <summary>Whether a broken device is whole again with a power cell fitted.</summary>
    private bool Repaired(EntityUid device)
    {
        return TryComp<DamageableComponent>(device, out var damage) && damage.TotalDamage <= 0
               && _itemSlots.GetItemOrNull(device, "cell_slot") != null;
    }

    /// <summary>The living bodies of the holder and their crewmates in the round.</summary>
    private List<EntityUid> CrewBodies(WFContract contract)
    {
        if (contract.User is not { } user)
            return new List<EntityUid>();

        var bodies = _crews.CrewInRound(user).Select(m => m.Body).ToList();
        if (MobOf(new WFContractHolder(user, contract.Character)) is { } holder && !bodies.Contains(holder))
            bodies.Add(holder);
        return bodies.Where(b => _mobState.IsAlive(b)).ToList();
    }

    private bool Near(EntityUid body, MapCoordinates point, float range)
    {
        if (TerminatingOrDeleted(body))
            return false;

        var at = _transform.GetMapCoordinates(body);
        return at.MapId == point.MapId && (at.Position - point.Position).Length() <= range;
    }

    /// <summary>The verb that completes an inspection, for the holder's crew only.</summary>
    private void OnInspectVerbs(Entity<WFContractInspectableComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !_contracts.TryGetValue(ent.Comp.Contract, out var contract)
            || contract.Status != WFContractStatus.Active || contract.User is not { } holder
            || !TryComp<ActorComponent>(args.User, out var actor) || !_crews.CrewMembers(holder).Contains(actor.PlayerSession.UserId))
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("wf-contract-inspect-verb"),
            Act = () =>
            {
                _popup.PopupEntity(Loc.GetString("wf-contract-inspect-done"), ent, user);
                Complete(contract);
            },
        });
    }

    /// <summary>Repaired derelicts jump away once their time is up and no one is aboard.</summary>
    private void UpdateDepartures(TimeSpan now)
    {
        foreach (var (grid, when) in _departures.ToList())
        {
            if (TerminatingOrDeleted(grid))
            {
                _departures.Remove(grid);
                continue;
            }

            if (now < when || Occupied(grid))
                continue;

            _departures.Remove(grid);
            QueueDel(grid);
        }
    }

    /// <summary>Whether any player is standing on a grid.</summary>
    private bool Occupied(EntityUid grid)
    {
        var players = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (players.MoveNext(out _, out _, out var xform))
        {
            if (xform.GridUid == grid)
                return true;
        }

        return false;
    }

    /// <summary>Pay added for a recovered artifact by its unlocked nodes.</summary>
    private int ArtifactBonus(WFContract contract)
    {
        if (_proto.Index(contract.Prototype).Objective is not WFContractRecovery { PerUnlockedNode: > 0 } recovery
            || contract.Items.FirstOrDefault() is not { Valid: true } item || !TryComp<XenoArtifactComponent>(item, out var artifact))
            return 0;

        return recovery.PerUnlockedNode * _artifacts.GetAllNodes((item, artifact)).Count(n => !n.Comp.Locked);
    }

    /// <summary>Posts the next link of a chain for the crew that finished the last one.</summary>
    private void PostFollowUp(WFContractHolder holder, WFContractPrototype finished)
    {
        if (finished.FollowUp is not { } next || !_proto.TryIndex(next, out var template) || BoardStation() is not { } board
            || Build(template, ContractorHomes(), board, _timing.CurTime) is not { } offer)
            return;

        offer.ReservedFor = holder.User;
        offer.Deadline = _timing.CurTime + FollowUpLifetime;
        _contracts[offer.Id] = offer;
        _dirty = true;
        Tell(holder, Loc.GetString("wf-contract-follow-up", ("title", Title(offer)), ("minutes", (int) FollowUpLifetime.TotalMinutes)));
    }

    /// <summary>Whether a viewer may see an offer: anyone, unless it is held for another crew.</summary>
    private bool OfferedTo(WFContract contract, Robust.Shared.Network.NetUserId? viewer)
    {
        return contract.ReservedFor is not { } reserved || viewer is { } user && _crews.CrewMembers(reserved).Contains(user);
    }
}
