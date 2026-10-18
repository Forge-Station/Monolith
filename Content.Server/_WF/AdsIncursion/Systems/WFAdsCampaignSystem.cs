using System.Linq;
using System.Numerics;
using Content.Server._NF.GameTicking.Events;
using Content.Server._WF.AdsIncursion.Components;
using Content.Server._WF.Encounters;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.NpcCrew;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server._WF.SectorControl;
using Content.Server._WF.SectorControl.Systems;
using Content.Server._WF.ShipShields;
using Content.Server.Chat.Systems;
using Content.Server.GameTicking;
using Content.Server.Radio.EntitySystems;
using Content.Shared._WF.AdsIncursion;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.SectorControl;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Mobs.Systems;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.AdsIncursion.Systems;

/// <summary>What keeps the ADS incursion from acting, in the order it is checked.</summary>
public enum WFAdsIdle : byte
{
    None,
    CampaignOff,
    EncountersOff,
    SectorControlOff,
    NotInRound,
    NoCampaign,
}

/// <summary>The ADS incursion: nodes that hold sector cells, gain sentinels, sweep and interdict, challenge other factions' cells and raise a threat tier.</summary>
public sealed partial class WFAdsCampaignSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private RadioSystem _radio = default!;
    [Dependency] private WFEncounterSystem _encounters = default!;
    [Dependency] private WFEncounterSchedulerSystem _scheduler = default!;
    [Dependency] private WFEncounterRewardSystem _rewards = default!;
    [Dependency] private WFSectorTerritorySystem _territory = default!;
    [Dependency] private WFSectorActivitySystem _activity = default!;
    [Dependency] private WFSectorSyncSystem _sync = default!;
    [Dependency] private WFSectorNoticeSystem _notices = default!;
    [Dependency] private WFCrewObjectiveSystem _objectives = default!;
    [Dependency] private WFCrewShipStatusSystem _status = default!;

    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);

    /// <summary>How soon a founding that found no place is tried again.</summary>
    private static readonly TimeSpan FoundRetry = TimeSpan.FromMinutes(1);

    private bool _enabled;
    private bool _encountersEnabled;
    private bool _campaignDirty = true;
    private WFAdsCampaignPrototype? _campaign;
    private string? _missingFor;
    private TimeSpan _nextTick;
    private TimeSpan _lastTick;
    private TimeSpan _nextFoundTry;
    private TimeSpan? _stationsAt;

    /// <summary>The faction whose legend line is up, so it can be taken down.</summary>
    private ProtoId<WFSectorFactionPrototype>? _legend;

    /// <summary>Stops founding, spread, sweeps, sentinels, interdictions and challenges and holds every clock until resumed; nodes still break and pay.</summary>
    public new bool Paused;

    /// <summary>The campaign of the storyteller's preset, or null if it has none.</summary>
    public WFAdsCampaignPrototype? Campaign
    {
        get
        {
            if (!_campaignDirty)
                return _campaign;

            _campaignDirty = false;
            _campaign = null;
            if (_scheduler.Preset is not { } preset)
                return null;

            foreach (var campaign in _prototypes.EnumeratePrototypes<WFAdsCampaignPrototype>())
            {
                if (campaign.Preset != preset.ID)
                    continue;

                _campaign = campaign;
                break;
            }

            if (_campaign == null && _missingFor != preset.ID)
            {
                _missingFor = preset.ID;
                Log.Info($"Encounter preset {preset.ID} has no ADS campaign; none runs.");
            }

            return _campaign;
        }
    }

    /// <summary>Whether the campaign acts: every switch on, the round in progress and a campaign for the preset.</summary>
    public bool Running => Idle() == WFAdsIdle.None;

    /// <summary>The first thing that keeps the campaign from acting, or none while it runs.</summary>
    public WFAdsIdle Idle()
    {
        if (!_enabled && !_ticker.IsGameRuleActive<WFAdsCampaignRuleComponent>())
            return WFAdsIdle.CampaignOff;
        if (!_encountersEnabled)
            return WFAdsIdle.EncountersOff;
        if (!_territory.Enabled)
            return WFAdsIdle.SectorControlOff;
        if (_ticker.RunLevel != GameRunLevel.InRound)
            return WFAdsIdle.NotInRound;

        return Campaign == null ? WFAdsIdle.NoCampaign : WFAdsIdle.None;
    }

    public override void Initialize()
    {
        base.Initialize();
        Subs.CVar(_config, AdsIncursionCVars.Enabled, value => _enabled = value, true);
        Subs.CVar(_config, EncountersCVars.Enabled, value => _encountersEnabled = value, true);
        Subs.CVar(_config, EncountersCVars.Preset, _ => _campaignDirty = true, true);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
        SubscribeLocalEvent<StationsGeneratedEvent>(OnStationsGenerated);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundCleanup);
        SubscribeLocalEvent<WFEncounterResolvedEvent>(OnResolved);
        SubscribeLocalEvent<WFSectorClaimAttemptEvent>(OnClaimAttempt);
        SubscribeLocalEvent<WFSectorResetEvent>(OnSectorReset);
        SubscribeLocalEvent<WFSectorRetakeAttemptEvent>(OnRetakeAttempt);
        SubscribeLocalEvent<WFSectorCellRetakenEvent>(OnRetaken);
        SubscribeLocalEvent<WFSectorCellClearingEvent>(OnClearing);
        SubscribeLocalEvent<WFSectorContestResolvedEvent>(OnContestResolved);
        SubscribeLocalEvent<RoundEndTextAppendEvent>(OnRoundEndText);
        SubscribeLocalEvent<WFCrewHullHitEvent>(OnHullHit);
        SubscribeLocalEvent<WFShipShieldAttackedEvent>(OnShieldHit);
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<WFAdsCampaignPrototype>())
            _campaignDirty = true;
    }

    private void OnStationsGenerated(StationsGeneratedEvent args)
    {
        _stationsAt = _timing.CurTime;
    }

    private void OnRoundCleanup(RoundRestartCleanupEvent args)
    {
        _stationsAt = null;
        _nextTick = TimeSpan.Zero;
        _lastTick = TimeSpan.Zero;
        _nextFoundTry = TimeSpan.Zero;
        _nextDreadnoughtTry = TimeSpan.Zero;
        _legend = null;
        _clearingPending.Clear();
        _campaignDirty = true;
        Paused = false;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        if (_clearingPending.Count > 0)
            AnnounceClearing();

        if (now < _nextTick)
            return;

        var elapsed = _lastTick == TimeSpan.Zero ? 1f : Math.Clamp((float) (now - _lastTick).TotalSeconds, 0f, 5f);
        var since = _lastTick == TimeSpan.Zero ? TimeSpan.Zero : now - _lastTick;
        _lastTick = now;
        _nextTick = now + TickInterval;
        if (_ticker.RunLevel != GameRunLevel.InRound)
            return;

        // Nodes already standing still break and pay with the campaign switched off; a held campaign's clocks are pushed on.
        var running = Running;
        var held = !running || Held;
        var current = State(running);
        if (held)
            HoldClocks(since, current);

        if (current is not { } state)
        {
            ClearLegend();
            return;
        }

        var ships = Crewed();
        if (CampaignFor(state) is { } reporting)
            ReportPresence(reporting);

        if (!held)
            HoldDisputed(TimeSpan.FromSeconds(elapsed));

        TendNodes(state, ships, elapsed);
        TendDeployments(state, !held);
        TendSentinels();
        TendSweeps(state);
        TendInterdicts(state);
        TendChallenges(state, !held);
        TendPickets();
        TendDreadnoughts();
        if (!running || Campaign is not { } campaign)
        {
            ClearLegend();
            return;
        }

        // A finished campaign founds, launches and sends nothing more; what stands fights on until broken.
        if (!held && !state.Finished)
        {
            TickFounding(campaign, state);
            TickSentinels(campaign, state);
            TickSpread(campaign, state);
            TickSweeps(campaign, state);
            TickInterdicts(campaign, state, ships);
            TickChallenges(campaign, state);
            TickDreadnought(campaign, state);
        }

        Recompute();
    }

    /// <summary>The campaign state on the sector map, made there if asked and a campaign is in force.</summary>
    public WFAdsCampaignComponent? State(bool create = false)
    {
        var map = _territory.SectorMap;
        if (map == MapId.Nullspace || !_map.TryGetMap(map, out var mapUid) || mapUid is not { } uid)
            return null;

        var campaign = Campaign;
        if (TryComp<WFAdsCampaignComponent>(uid, out var state))
        {
            if (campaign != null)
                state.Campaign = campaign.ID;
            return state;
        }

        if (!create || campaign == null)
            return null;

        state = EnsureComp<WFAdsCampaignComponent>(uid);
        state.Campaign = campaign.ID;
        return state;
    }

    /// <summary>The campaign in force, or else the one the round's state was made for.</summary>
    private WFAdsCampaignPrototype? CampaignFor(WFAdsCampaignComponent? state)
    {
        if (Campaign is { } campaign)
            return campaign;

        return state != null && _prototypes.TryIndex(state.Campaign, out var held) ? held : null;
    }

    /// <summary>The live campaign encounters of one kind on the sector map.</summary>
    private List<Entity<T, WFEncounterComponent>> Live<T>() where T : IComponent
    {
        var map = _territory.SectorMap;
        var found = new List<Entity<T, WFEncounterComponent>>();
        var query = EntityQueryEnumerator<T, WFEncounterComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out var encounter, out var xform))
        {
            if (encounter.Resolution == null && xform.MapID == map && !TerminatingOrDeleted(uid))
                found.Add((uid, comp, encounter));
        }

        return found;
    }

    /// <summary>The nodes standing on the sector map.</summary>
    public List<Entity<WFAdsNodeComponent, WFEncounterComponent>> Nodes()
    {
        return Live<WFAdsNodeComponent>();
    }

    /// <summary>The sentinels out on the sector map, inbound or joined.</summary>
    public List<Entity<WFAdsSentinelComponent, WFEncounterComponent>> Sentinels()
    {
        return Live<WFAdsSentinelComponent>();
    }

    /// <summary>The sweeps out on the sector map.</summary>
    public List<Entity<WFAdsSweepComponent, WFEncounterComponent>> Sweeps()
    {
        return Live<WFAdsSweepComponent>();
    }

    /// <summary>The deployments under way on the sector map.</summary>
    public List<Entity<WFAdsDeploymentComponent, WFEncounterComponent>> Deployments()
    {
        return Live<WFAdsDeploymentComponent>();
    }

    /// <summary>The interdictors out on a challenge.</summary>
    public List<Entity<WFAdsInterdictorComponent, WFEncounterComponent>> Interdictors()
    {
        return Live<WFAdsInterdictorComponent>();
    }

    /// <summary>The pickets holding cells taken by challenge.</summary>
    public List<Entity<WFAdsPicketComponent, WFEncounterComponent>> Pickets()
    {
        return Live<WFAdsPicketComponent>();
    }

    /// <summary>Where a campaign encounter is: its ship of that key, or its origin once the ship is gone.</summary>
    private Vector2 Position(WFEncounterComponent encounter, string key)
    {
        return encounter.Ships.TryGetValue(key, out var ship) && !TerminatingOrDeleted(ship.Grid)
            ? _transform.GetWorldPosition(ship.Grid)
            : encounter.Origin.Position;
    }

    /// <summary>The lead hull of an encounter, if it still has one.</summary>
    private EntityUid? LeadGrid(EntityUid encounter, string lead)
    {
        return TryComp<WFEncounterComponent>(encounter, out var comp) && comp.Ships.TryGetValue(lead, out var ship)
            && !TerminatingOrDeleted(ship.Grid)
            ? ship.Grid
            : null;
    }

    /// <summary>A value of a by-tier list, the last entry serving higher tiers.</summary>
    private static T ByTier<T>(List<T> values, int tier, T fallback)
    {
        return values.Count == 0 ? fallback : values[Math.Clamp(tier, 0, values.Count - 1)];
    }

    /// <summary>The players connected, for the caps.</summary>
    private int Players()
    {
        return _playerManager.PlayerCount;
    }

    /// <summary>A clear point at a random distance from a centre, or false after a few tries.</summary>
    private bool TryClearPoint(MapId map, Vector2 centre, float min, float max, float clearance, out Vector2 point)
    {
        for (var i = 0; i < 8; i++)
        {
            point = centre + _random.NextAngle().ToVec() * _random.NextFloat(min, max);
            if (_scheduler.IsClearSpace(map, point, clearance, 0f))
                return true;
        }

        point = default;
        return false;
    }

    /// <summary>Picks one entry, each as likely as its weight; null if none has any.</summary>
    private T? PickWeighted<T>(List<(T Item, float Weight)> entries) where T : struct
    {
        var total = 0f;
        foreach (var (_, weight) in entries)
        {
            total += MathF.Max(0f, weight);
        }

        if (total <= 0f)
            return null;

        var roll = _random.NextFloat(total);
        foreach (var (item, weight) in entries)
        {
            roll -= MathF.Max(0f, weight);
            if (roll <= 0f)
                return item;
        }

        return entries[^1].Item;
    }

    /// <summary>The crewed ships on the sector map the campaign deals with; those docked at a station are left out.</summary>
    private HashSet<EntityUid> Crewed()
    {
        var map = _territory.SectorMap;
        var ships = CampaignFor(State()) is { } campaign ? _activity.CrewedShips(map, campaign.Faction) : _activity.CrewedShips(map);
        ships.RemoveWhere(_encounters.DockedAtStation);
        return ships;
    }

    /// <summary>Whether a station's bounds are centred in a cell: the ADS never claims, contests or challenges one.</summary>
    public bool HasStation(MapId map, WFSectorCell cell)
    {
        return _territory.StationsIn(map, cell).Count > 0;
    }

    /// <summary>The Asset Preservation Directive: vetoes every ADS claim, contest and challenge on a cell with a station in it.</summary>
    private void OnClaimAttempt(ref WFSectorClaimAttemptEvent args)
    {
        if (args.Cancelled || CampaignFor(State()) is not { } campaign || args.Faction != campaign.Faction)
            return;

        if (HasStation(args.Map, args.Cell))
            args.Cancelled = true;
    }

    /// <summary>Spawns an encounter for the campaign.</summary>
    private bool SpawnEncounter(WFEncounterPrototype prototype, MapCoordinates at, out EntityUid uid, bool offBudget = false)
    {
        return _encounters.TrySpawn(prototype, at, out uid, offBudget: offBudget);
    }

    private void OnResolved(ref WFEncounterResolvedEvent args)
    {
        if (TryComp<WFAdsNodeComponent>(args.Encounter, out var node))
            NodeResolved((args.Encounter, node), args.Resolution);
        else if (TryComp<WFAdsSentinelComponent>(args.Encounter, out var sentinel))
            SentinelResolved((args.Encounter, sentinel), args.Resolution);
        else if (TryComp<WFAdsSweepComponent>(args.Encounter, out var sweep))
            SweepResolved((args.Encounter, sweep), args.Resolution);
        else if (TryComp<WFAdsDeploymentComponent>(args.Encounter, out var deployment))
            DeploymentResolved((args.Encounter, deployment), args.Resolution);
        else if (TryComp<WFAdsInterdictorComponent>(args.Encounter, out var interdictor))
            InterdictorResolved((args.Encounter, interdictor), args.Resolution);
        else if (TryComp<WFAdsPicketComponent>(args.Encounter, out var picket))
            PicketResolved((args.Encounter, picket), args.Resolution);
        else if (TryComp<WFAdsDreadnoughtComponent>(args.Encounter, out var dreadnought))
            DreadnoughtResolved((args.Encounter, dreadnought), args.Resolution);
        else
            StrongholdResolved(args.Encounter, args.Resolution);
    }

    private void OnRoundEndText(RoundEndTextAppendEvent args)
    {
        if (State() is not { Founded: > 0 } state)
            return;

        args.AddLine(Loc.GetString("wf-ads-round-end",
            ("peak", state.PeakCells),
            ("founded", state.Founded),
            ("broken", state.Broken),
            ("sweeps", state.SweepsDestroyed),
            ("deployments", state.DeploymentsStopped),
            ("retaken", state.Retaken)));
        if (state.Challenges > 0)
        {
            args.AddLine(Loc.GetString("wf-ads-round-end-challenges",
                ("challenged", state.Challenges),
                ("won", state.ChallengesWon),
                ("lost", state.ChallengesLost)));
        }

        if (state.Dreadnoughts > 0)
        {
            args.AddLine(Loc.GetString("wf-ads-round-end-dreadnought",
                ("activated", state.Dreadnoughts),
                ("destroyed", state.DreadnoughtsDestroyed),
                ("strongholds", state.StrongholdsBroken)));
        }

        if (state.Finished)
            args.AddLine(Loc.GetString("wf-ads-round-end-finished"));
        else if (state.Defeats > 0)
            args.AddLine(Loc.GetString("wf-ads-round-end-dormant", ("count", state.Defeats)));
    }

    /// <summary>Whether an encounter is an ADS node, sentinel, sweep, deployment, interdictor, picket or dreadnought.</summary>
    private bool IsCampaignEncounter(EntityUid uid)
    {
        return HasComp<WFAdsNodeComponent>(uid) || HasComp<WFAdsSentinelComponent>(uid) || HasComp<WFAdsSweepComponent>(uid)
            || HasComp<WFAdsDeploymentComponent>(uid) || HasComp<WFAdsInterdictorComponent>(uid) || HasComp<WFAdsPicketComponent>(uid)
            || HasComp<WFAdsDreadnoughtComponent>(uid);
    }

    /// <summary>Ends every campaign encounter, frees the faction's cells, lifts a finished campaign and starts the regroup clock. Returns how many encounters ended.</summary>
    public int ClearCampaign()
    {
        var ended = new List<EntityUid>();
        var query = EntityQueryEnumerator<WFEncounterComponent>();
        while (query.MoveNext(out var uid, out var encounter))
        {
            if (encounter.Resolution == null && IsCampaignEncounter(uid))
                ended.Add(uid);
        }

        foreach (var uid in ended)
        {
            _encounters.End(uid);
        }

        var state = State();
        if (CampaignFor(state) is { } campaign)
            _territory.Clear(_territory.SectorMap, campaign.Faction);

        if (state != null)
        {
            state.Finished = false;
            state.NoNodeSince = state.Founded > 0 ? _timing.CurTime : null;
            state.NextSpread = null;
            state.NextChallenge = null;
        }

        Recompute();
        return ended.Count;
    }

    /// <summary>Where the living, non-ghost players on a map are.</summary>
    private List<Vector2> PlayerPositions(MapId map)
    {
        var positions = new List<Vector2>();
        var players = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (players.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapID == map && !HasComp<GhostComponent>(uid) && !_mobs.IsDead(uid))
                positions.Add(_transform.GetWorldPosition(xform));
        }

        return positions;
    }

    /// <summary>Whether a cell is held or beside held space.</summary>
    private static bool InOrBeside(HashSet<WFSectorCell> held, WFSectorCell cell)
    {
        if (held.Contains(cell))
            return true;

        for (var direction = 0; direction < 6; direction++)
        {
            if (held.Contains(cell.Neighbour(direction)))
                return true;
        }

        return false;
    }

    /// <summary>The held cells of the faction as a set.</summary>
    private HashSet<WFSectorCell> HeldSet(WFAdsCampaignPrototype campaign)
    {
        return _territory.Held(_territory.SectorMap, campaign.Faction).ToHashSet();
    }
}
