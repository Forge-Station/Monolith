using System.Collections.Immutable;
using System.Linq;
using Content.Shared._WF.RMCCore.Dialog;
using Content.Shared._WF.Xenonids.Announce;
using Content.Shared._WF.Xenonids.Construction;
using Content.Shared._WF.Xenonids.Hive;
using Content.Shared.Coordinates;
using Content.Shared.Examine;
using Content.Shared.GameTicking;
using Content.Shared.Mind;
using Content.Shared.Mobs.Systems;
using Content.Shared.Players.PlayTimeTracking;
using Content.Shared.Popups;
using Content.Shared.Roles;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Spawners;
using Robust.Shared.Timing;

namespace Content.Shared._WF.Xenonids.ManageHive.Boons;

public sealed partial class HiveBoonSystem : EntitySystem
{
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private IComponentFactory _compFactory = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private DialogSystem _dialog = default!;
    [Dependency] private SharedGameTicker _gameTicker = default!;
    [Dependency] private SharedXenoHiveSystem _hive = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private ISharedPlaytimeManager _playtime = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private ISerializationManager _serialization = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedXenoAnnounceSystem _xenoAnnounce = default!;

    private static readonly EntProtoId<HiveBoonDefinitionComponent> KingBoonId = "RMCHiveBoonKing";
    private static readonly EntProtoId<HiveKingCocoonComponent> KingCocoonId = "RMCHiveCocoonKing";

    public ImmutableArray<(EntityPrototype Prototype, HiveBoonDefinitionComponent Component)> Boons
    {
        get;
        private set;
    } = ImmutableArray<(EntityPrototype Prototype, HiveBoonDefinitionComponent Component)>.Empty;

    private TimeSpan _royalResinEvery;
    private TimeSpan _kingVoteCandidateTimeRequired;
    private TimeSpan _kingFirstWarningTime;
    private TimeSpan _kingVoteStartTime;
    private TimeSpan _kingVoteAskCandidatesTime;
    private TimeSpan _kingVoteStartHatchingTime;

    private EntityQuery<ExcludedFromKingVoteComponent> _excludedFromKingVoteQuery;

    private readonly HashSet<ProtoId<PlayTimeTrackerPrototype>> _xenoJobs = new();

    public override void Initialize()
    {
        _excludedFromKingVoteQuery = GetEntityQuery<ExcludedFromKingVoteComponent>();

        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
        SubscribeLocalEvent<HiveBoonActivateLarvaSurgeEvent>(OnActivateLarvaSurge);
        SubscribeLocalEvent<HiveBoonActivateKingEvent>(OnActivateKing);

        SubscribeLocalEvent<XenoComponent, HiveKingVoteDialogEvent>(OnKingVote);

        SubscribeLocalEvent<HivePylonComponent, ExaminedEvent>(OnPylonExamined);
        SubscribeLocalEvent<HivePylonComponent, EntityTerminatingEvent>(OnPylonTerminating);

        SubscribeLocalEvent<HiveKingCocoonComponent, EntityTerminatingEvent>(OnCocoonTerminating);

        Subs.CVar(_config,
            WFXenoQueenCVars.RMCRoyalResinEveryMinutes,
            v => _royalResinEvery = TimeSpan.FromMinutes(v),
            true);

        Subs.CVar(_config,
            WFXenoQueenCVars.RMCKingVoteCandidateTimeRequirementHours,
            v => _kingVoteCandidateTimeRequired = TimeSpan.FromHours(v),
            true);

        Subs.CVar(_config,
            WFXenoQueenCVars.RMCKingHatchingFirstWarningMinutes,
            v => _kingFirstWarningTime = TimeSpan.FromMinutes(v),
            true);

        Subs.CVar(_config,
            WFXenoQueenCVars.RMCKingVoteStartTimeSeconds,
            v => _kingVoteStartTime = TimeSpan.FromSeconds(v),
            true);

        Subs.CVar(_config,
            WFXenoQueenCVars.RMCKingVoteAskCandidatesTimeSeconds,
            v => _kingVoteAskCandidatesTime = TimeSpan.FromSeconds(v),
            true);

        Subs.CVar(_config,
            WFXenoQueenCVars.RMCKingVoteStartHatchingTimeSeconds,
            v => _kingVoteStartHatchingTime = TimeSpan.FromSeconds(v),
            true);

        ReloadPrototypes();
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs ev)
    {
        if (ev.WasModified<EntityPrototype>())
            ReloadPrototypes();
    }

    private void OnActivateLarvaSurge(HiveBoonActivateLarvaSurgeEvent ev)
    {
        _hive.ChangeBurrowedLarva(ev.Hive, 5);
        _xenoAnnounce.AnnounceSameHiveDefaultSound(ev.Boon, "The Queen has awakened 5 extra burrowed larva to join the hive!");
    }

    private void OnActivateKing(HiveBoonActivateKingEvent ev)
    {
        if (ev.Core is not { } core)
            return;

        var cocoon = SpawnAtPosition(KingCocoonId, core.ToCoordinates());
        _hive.SetHive(cocoon, ev.Hive);

        var areaName = GetLocationName(core);
        _xenoAnnounce.AnnounceSameHiveDefaultSound(core, Loc.GetString("rmc-boon-king-announcement-xenos", ("area", areaName)));
    }

    private void OnKingVote(Entity<XenoComponent> ent, ref HiveKingVoteDialogEvent args)
    {
        if (GetEntity(args.Cocoon) is not { Valid: true } cocoon ||
            GetEntity(args.Voted) is not { Valid: true } votedId)
        {
            return;
        }

        GetKingVotingData(ent.Owner, cocoon, out _, out var canVote);
        if (!canVote)
            return;

        GetKingVotingData(votedId, cocoon, out var canBeKing, out _);
        if (!canBeKing)
            return;

        if (!TryComp(votedId, out ActorComponent? actor))
            return;

        var vote = EnsureVote(cocoon);
        var votedUserId = actor.PlayerSession.UserId;
        vote.Comp.Votes[votedUserId] = vote.Comp.Votes.GetValueOrDefault(votedUserId) + 1;
        Dirty(vote);
    }

    private string GetLocationName(EntityUid uid)
    {
        // WOLFGATE: areas are cut; the grid's name stands in for the location
        if (Transform(uid).GridUid is { } grid)
            return Name(grid);

        return Loc.GetString("wf-xeno-announce-unknown-location");
    }

    private void OnPylonExamined(Entity<HivePylonComponent> ent, ref ExaminedEvent args)
    {
        using (args.PushGroup(nameof(HivePylonComponent)))
        {
            var msg = $"[color=cyan]This will grant the hive 1 royal resin every {(int)_royalResinEvery.TotalMinutes} minutes, allowing the Queen to obtain buffs![/color]";
            args.PushMarkup(msg);
        }
    }

    private void OnPylonTerminating(Entity<HivePylonComponent> ent, ref EntityTerminatingEvent args)
    {
        if (!TryComp(ent, out TransformComponent? xform) ||
            TerminatingOrDeleted(xform.MapUid) ||
            HasComp<HiveConstructionSuppressAnnouncementsComponent>(ent))
        {
            return;
        }

        var area = GetLocationName(ent);
        _xenoAnnounce.AnnounceSameHiveDefaultSound(ent.Owner, $"We have lost our control of the tall's communication relay at {area}.");
    }

    private void OnCocoonTerminating(Entity<HiveKingCocoonComponent> ent, ref EntityTerminatingEvent args)
    {
        if (!TryComp(ent, out TransformComponent? xform) ||
            TerminatingOrDeleted(xform.MapUid) ||
            HasComp<HiveConstructionSuppressAnnouncementsComponent>(ent))
        {
            return;
        }

        _xenoAnnounce.AnnounceSameHiveDefaultSound(ent.Owner, Loc.GetString("rmc-boon-king-announcement-stopped-xeno"));
    }

    private void StartKingVote(Entity<HiveKingCocoonComponent> cocoon)
    {
        _xenoJobs.Clear();
        foreach (var prototype in _prototype.EnumeratePrototypes<PlayTimeTrackerPrototype>())
        {
            // WOLFGATE: no IsXeno flag on trackers here; RMC's xeno trackers are the CMJobXeno ones
            if (prototype.ID.StartsWith("CMJobXeno", StringComparison.Ordinal))
                _xenoJobs.Add(prototype.ID);
        }

        var options = new List<DialogOption>();
        var canVoteList = new List<EntityUid>();
        var netCocoon = GetNetEntity(cocoon);
        var xenosQuery = EntityQueryEnumerator<ActorComponent, XenoComponent>();
        while (xenosQuery.MoveNext(out var uid, out var actor, out _))
        {
            GetKingVotingData((uid, actor), cocoon, out var canBeKing, out var canVote);

            if (canBeKing)
                options.Add(new DialogOption(Name(uid), new HiveKingVoteDialogEvent(netCocoon, GetNetEntity(uid))));

            if (canVote)
                canVoteList.Add(uid);
        }

        foreach (var uid in canVoteList)
        {
            _dialog.OpenOptions(uid, "Choose a sister", options, "Vote for a sister you wish to become the King.");
        }

        EnsureVote(cocoon);
    }

    private void GetKingVotingData(Entity<ActorComponent?> xeno, EntityUid cocoon, out bool canBeKing, out bool canVote)
    {
        canBeKing = false;
        canVote = false;
        if (!Resolve(xeno, ref xeno.Comp, false))
            return;

        if (_mobState.IsDead(xeno))
            return;

        // TODO RMC14 if banished do not count
        if (!_hive.FromSameHive(xeno.Owner, cocoon))
            return;

        if (_excludedFromKingVoteQuery.TryComp(xeno, out var excluded))
        {
            canBeKing = excluded.CanBeKing;
            canVote = excluded.CanVote;
            return;
        }

        canVote = true;
        IReadOnlyDictionary<string, TimeSpan> playTimes;
        try
        {
            playTimes = _playtime.GetPlayTimes(xeno.Comp.PlayerSession);
        }
        catch
        {
            return;
        }

        var totalTime = TimeSpan.Zero;
        foreach (var (jobId, jobTime) in playTimes)
        {
            if (!_xenoJobs.Contains(jobId))
                continue;

            totalTime += jobTime;
        }

        if (totalTime < _kingVoteCandidateTimeRequired)
            return;

        canBeKing = true;
    }

    public Entity<HiveKingVoteComponent> EnsureVote(EntityUid xeno)
    {
        var query = EntityQueryEnumerator<HiveKingVoteComponent>();
        while (query.MoveNext(out var voteId, out var voteComp))
        {
            if (!_hive.FromSameHive(voteId, xeno))
                continue;

            return (voteId, voteComp);
        }

        var vote = Spawn();
        var comp = EnsureComp<HiveKingVoteComponent>(vote);
        _hive.SetSameHive(xeno, vote);
        return (vote, comp);
    }

    public void TryActivateBoon(Entity<ManageHiveComponent> manage, EntProtoId<HiveBoonDefinitionComponent> boon)
    {
        if (!_prototype.TryIndex(boon, out var boonProto) ||
            !boonProto.TryGetComponent(out HiveBoonDefinitionComponent? boonComp, _compFactory))
        {
            return;
        }

        if (_hive.GetHive(manage.Owner) is not { } hive)
            return;

        var boons = EnsureBoons(hive);
        if (boons.Comp.RoyalResin < boonComp.Cost)
        {
            var msg = Loc.GetString("rmc-boon-not-enough-royal-resin",
                ("cost", boonComp.Cost),
                ("current", boons.Comp.RoyalResin));
            _popup.PopupCursor(msg, manage, PopupType.MediumCaution);
            return;
        }

        var pylons = 0;
        var pylonQuery = EntityQueryEnumerator<HivePylonComponent>();
        while (pylonQuery.MoveNext(out var uid, out _))
        {
            if (_hive.FromSameHive(uid, manage.Owner))
                pylons++;
        }

        if (pylons < boonComp.Pylons)
        {
            var msg = Loc.GetString("rmc-boon-not-enough-pylons",
                ("cost", boonComp.Pylons),
                ("current", pylons));
            _popup.PopupCursor(msg, manage, PopupType.MediumCaution);
            return;
        }

        if (!TryGetUnlockAt(boons, boonProto.ID, out var unlockAt))
            return;

        if (_gameTicker.RoundDuration() < unlockAt)
        {
            var msg = Loc.GetString("rmc-boon-not-enough-time");
            _popup.PopupCursor(msg, manage, PopupType.MediumCaution);
            return;
        }

        if (boonComp.NoLivingKing)
        {
            var kingQuery = EntityQueryEnumerator<HiveBoonKingComponent>();
            while (kingQuery.MoveNext(out var uid, out _))
            {
                if (!_mobState.IsDead(uid) &&
                    _hive.FromSameHive(manage.Owner, uid))
                {
                    var msg = Loc.GetString("rmc-boon-only-one-king");
                    _popup.PopupCursor(msg, manage, PopupType.MediumCaution);
                    return;
                }
            }
        }

        if (boonComp.RequiresCore && _hive.GetHiveCore(hive) == null)
        {
            var msg = Loc.GetString("rmc-boon-requires-core");
            _popup.PopupCursor(msg, manage, PopupType.MediumCaution);
            return;
        }

        var time = _timing.CurTime;
        if (boons.Comp.UsedAt.TryGetValue(boonProto.ID, out var usedAt))
        {
            var cooldownLeft = usedAt + boonComp.Cooldown - time;
            if (cooldownLeft > TimeSpan.Zero)
            {
                var msg = Loc.GetString("rmc-boon-on-cooldown",
                    ("boon", boonProto.Name),
                    ("minutes", (int) cooldownLeft.TotalMinutes));
                _popup.PopupCursor(msg, manage, PopupType.MediumCaution);
                return;
            }
        }

        if (boonComp.DuplicateId is { } duplicateId &&
            boons.Comp.Active.TryGetValue(duplicateId, out var activeBoonId) &&
            !TerminatingOrDeleted(activeBoonId))
        {
            var msg = Loc.GetString("rmc-boon-duplicate-active", ("boon", Name(activeBoonId)));
            _popup.PopupCursor(msg, manage, PopupType.MediumCaution);
            return;
        }

        if (!boonComp.Reusable && boons.Comp.UsedAt.ContainsKey(boonProto.ID))
        {
            var msg = Loc.GetString("rmc-boon-not-reusable", ("boon", boonProto.Name));
            _popup.PopupCursor(msg, manage, PopupType.MediumCaution);
            return;
        }

        if (boonComp.Event == null)
            return;

        var ev = (HiveBoonEvent?) _serialization.CreateCopy((object) boonComp.Event, notNullableOverride: true);
        if (ev == null)
            return;

        boons.Comp.UsedAt[boonProto.ID] = time;
        boons.Comp.RoyalResin = Math.Max(0, boons.Comp.RoyalResin - boonComp.Cost);

        var boonEnt = Spawn(boonProto.ID, MapCoordinates.Nullspace);
        _hive.SetSameHive(manage.Owner, boonEnt);

        EnsureComp<TimedDespawnComponent>(boonEnt).Lifetime = (float) boonComp.Duration.TotalSeconds;

        boons.Comp.Active[boonProto.ID] = boonEnt;
        Dirty(boons, boons.Comp);

        ev.Boon = boonEnt;
        ev.Hive = hive;
        ev.Core = _hive.GetHiveCore(hive);
        RaiseLocalEvent((object) ev);
    }

    private bool TryGetUnlockAt(Entity<HiveBoonsComponent> boons, EntProtoId<HiveBoonDefinitionComponent> boonId, out TimeSpan unlockAt)
    {
        if (!_prototype.TryIndex(boonId, out var boon) ||
            !boon.TryGetComponent(out HiveBoonDefinitionComponent? boonComp, _compFactory))
        {
            unlockAt = TimeSpan.Zero;
            return false;
        }

        if (boons.Comp.UnlockAt.TryGetValue(boonId, out unlockAt))
            return true;

        unlockAt = boonComp.UnlockAt;
        if (boonComp.UnlockAtRandomAdd != TimeSpan.Zero)
            unlockAt += _random.Next(TimeSpan.Zero, boonComp.UnlockAtRandomAdd);

        boons.Comp.UnlockAt[boonId] = unlockAt;
        Dirty(boons);
        return true;
    }

    public Entity<HiveBoonsComponent> EnsureBoons(Entity<HiveComponent> hive)
    {
        var boons = EnsureComp<HiveBoonsComponent>(hive);
        return (hive, boons);
    }

    private void ReloadPrototypes()
    {
        var boons = ImmutableArray.CreateBuilder<(EntityPrototype Prototype, HiveBoonDefinitionComponent Component)>();
        foreach (var prototype in _prototype.EnumeratePrototypes<EntityPrototype>())
        {
            if (!prototype.TryGetComponent(out HiveBoonDefinitionComponent? comp, _compFactory))
                continue;

            boons.Add((prototype, comp));
        }

        boons.Sort((a, b) => string.Compare(a.Prototype.Name, b.Prototype.Name, StringComparison.InvariantCultureIgnoreCase));
        Boons = boons.ToImmutable();
    }

    private void GainResin()
    {
        try
        {
            var time = _timing.CurTime;
            var pylons = EntityQueryEnumerator<HivePylonComponent>();
            while (pylons.MoveNext(out var uid, out var pylon))
            {
                if (pylon.NextRoyalResin >= time)
                    continue;

                pylon.NextRoyalResin = time + _royalResinEvery;
                Dirty(uid, pylon);

                if (_hive.GetHive(uid) is not { } hive)
                    continue;

                var boons = EnsureBoons(hive);
                boons.Comp.RoyalResin = Math.Clamp(boons.Comp.RoyalResin + 1, 0, boons.Comp.RoyalResinMax);
            }
        }
        catch (Exception e)
        {
            Log.Error($"Error gaining royal resin:\n{e}");
        }
    }

    public override void Update(float frameTime)
    {
        if (_net.IsClient)
            return;

        AnnounceKingUnlock();
        GainResin();

        var cocoonQuery = EntityQueryEnumerator<HiveKingCocoonComponent>();
        while (cocoonQuery.MoveNext(out var cocoonId, out var cocoonComp))
        {
            var pylons = 0;
            var pylonQuery = EntityQueryEnumerator<HivePylonComponent>();
            while (pylonQuery.MoveNext(out var pylonId, out _))
            {
                if (!_hive.FromSameHive(cocoonId, pylonId))
                    continue;

                pylons++;
            }

            if (pylons >= cocoonComp.RequiredPylons)
            {
                if (cocoonComp.LastPylons < cocoonComp.RequiredPylons)
                {
                    _xenoAnnounce.AnnounceSameHiveDefaultSound(cocoonId, Loc.GetString("rmc-boon-king-announcement-resumed-xeno"));
                }

                cocoonComp.LastPylons = pylons;
            }
            else
            {
                if (cocoonComp.LastPylons >= cocoonComp.RequiredPylons)
                {
                    _xenoAnnounce.AnnounceSameHiveDefaultSound(cocoonId, Loc.GetString("rmc-boon-king-announcement-paused-xeno"));
                }

                cocoonComp.LastPylons = pylons;
                return;
            }

            cocoonComp.TimeLeft -= TimeSpan.FromSeconds(frameTime);

            if (cocoonComp.TimeLeft <= _kingFirstWarningTime &&
                !cocoonComp.FirstWarning)
            {
                cocoonComp.FirstWarning = true;
                Dirty(cocoonId, cocoonComp);

                _xenoAnnounce.AnnounceSameHiveDefaultSound(cocoonId, Loc.GetString("rmc-boon-king-announcement-minutes-xeno", ("minutes", (int) cocoonComp.TimeLeft.TotalMinutes + 1)));
            }

            if (cocoonComp.TimeLeft > _kingVoteStartTime)
                continue;

            if (!cocoonComp.VoteStarted)
            {
                cocoonComp.VoteStarted = true;
                Dirty(cocoonId, cocoonComp);
                StartKingVote((cocoonId, cocoonComp));
            }

            if (cocoonComp.TimeLeft > _kingVoteStartHatchingTime)
                continue;

            if (!cocoonComp.FinalWarning)
            {
                cocoonComp.FinalWarning = true;
                Dirty(cocoonId, cocoonComp);

                _xenoAnnounce.AnnounceSameHiveDefaultSound(cocoonId,
                    Loc.GetString("rmc-boon-king-announcement-seconds-xeno",
                        ("seconds", (int) cocoonComp.TimeLeft.TotalSeconds + 1)));
            }

            if (cocoonComp.TimeLeft > TimeSpan.Zero)
                continue;

            var vote = EnsureVote(cocoonId);
            var votes = new List<(NetUserId Id, int Votes)>();
            foreach (var (user, userVotes) in vote.Comp.Votes)
            {
                votes.Add((user, userVotes));
            }

            votes = votes.OrderByDescending(a => a.Votes).ToList();
            var king = SpawnAtPosition(cocoonComp.Spawn, cocoonId.ToCoordinates());
            _hive.SetSameHive(cocoonId, king);
            EnsureComp<HiveConstructionSuppressAnnouncementsComponent>(cocoonId);
            QueueDel(cocoonId);

            foreach (var (user, _) in votes)
            {
                _mind.ControlMob(user, king);
                if (TryComp(king, out ActorComponent? actor) && actor.PlayerSession.UserId == user)
                {
                    QueueDel(vote);

                    _xenoAnnounce.AnnounceSameHiveDefaultSound(cocoonId, Loc.GetString("rmc-boon-king-announcement-hatch-xeno"));
                    return;
                }
            }
        }
    }

    private void AnnounceKingUnlock()
    {
        try
        {
            if (_net.IsClient)
                return;

            var query = EntityQueryEnumerator<HiveComponent>();
            while (query.MoveNext(out var uid, out var hive))
            {
                var boons = EnsureBoons((uid, hive));
                if (boons.Comp.KingAnnounced)
                    continue;

                if (!TryGetUnlockAt(boons, KingBoonId, out var unlockAt) ||
                    _gameTicker.RoundDuration() < unlockAt)
                {
                    continue;
                }

                boons.Comp.KingAnnounced = true;
                Dirty(boons);

                // WOLFGATE: bioscan is cut, so the unlock announcement uses the default hive sound
                _xenoAnnounce.AnnounceToHive(default, uid, "The hive is now ready to begin hatching His Grace, the King, if we gain control of both tall hivemind towers.");
            }
        }
        catch (Exception e)
        {
            Log.Error($"Error announcing king unlock:\n{e}");
        }
    }
}
