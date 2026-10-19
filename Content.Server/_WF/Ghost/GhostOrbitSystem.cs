using System.Numerics;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Server.Dragon;
using Content.Server.Nuke;
using Content.Server.Roles.Jobs;
using Content.Server.Tesla.Components;
using Content.Server.Warps;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WF.Ghost;
using Content.Shared._WF.Xenonids;
using Content.Shared._WF.Xenonids.Evolution;
using Content.Shared._WF.Xenonids.Hive;
using Content.Shared.Anomaly.Components;
using Content.Shared.Chat;
using Content.Shared.Damage;
using Content.Shared.Database;
using Content.Shared.Follower;
using Content.Shared.Follower.Components;
using Content.Shared.Ghost;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Nuke;
using Content.Shared.Roles;
using Content.Shared.Singularity.Components;
using Content.Shared.StatusIcon;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Ghost;

/// <summary>
/// Builds the categorised target list for the ghost orbit menu and handles orbit requests.
/// </summary>
public sealed class GhostOrbitSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IAdminManager _admin = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private FollowerSystem _follower = default!;
    [Dependency] private JobSystem _jobs = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private WFEncounterMarkerSystem _markers = default!;
    [Dependency] private MobThresholdSystem _thresholds = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private SharedRoleSystem _roles = default!;
    [Dependency] private SharedXenoHiveSystem _hives = default!;
    [Dependency] private TransformSystem _transform = default!;

    private EntityQuery<GhostComponent> _ghostQuery;

    public override void Initialize()
    {
        base.Initialize();

        _ghostQuery = GetEntityQuery<GhostComponent>();

        SubscribeNetworkEvent<GhostOrbitRequestEvent>(OnRequest);
        SubscribeNetworkEvent<GhostOrbitWarpEvent>(OnWarp);
    }

    private void OnRequest(GhostOrbitRequestEvent msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { Valid: true } viewer || !_ghostQuery.HasComp(viewer))
            return;

        var isAdmin = _admin.IsAdmin(viewer);
        var targets = BuildTargets(viewer, isAdmin);

        var encounters = new List<GhostOrbitEncounterShip>();
        AddEncounters(encounters, isAdmin);

        EntityManager.System<Content.Server._WF.NpcCrew.Systems.WFCrewUiDiagnosticsSystem>().Reply("ghost", args.SenderSession, targets.Count);
        RaiseNetworkEvent(new GhostOrbitTargetsEvent(targets, encounters, _hives.GetBurrowedLarva()), args.SenderSession.Channel);
    }

    /// <summary>
    /// Everything the viewer can orbit except encounter ships.
    /// </summary>
    public List<GhostOrbitTarget> BuildTargets(EntityUid viewer, bool isAdmin)
    {
        var targets = new List<GhostOrbitTarget>();
        var seen = new HashSet<EntityUid> { viewer };

        AddInterests(targets, seen, isAdmin);
        AddMobs(targets, seen, isAdmin);
        AddSessions(targets, seen, isAdmin);
        AddWarpPoints(targets, seen, isAdmin);
        return targets;
    }

    private void OnWarp(GhostOrbitWarpEvent msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { Valid: true } ghost || !_ghostQuery.HasComp(ghost))
            return;

        var target = GetEntity(msg.Target);
        if (!Exists(target) || target == ghost)
            return;

        var isAdmin = _admin.IsAdmin(ghost);
        TryComp<WarpPointComponent>(target, out var warp);
        if (!isAdmin && (warp is { AdminOnly: true } || (_ghostQuery.HasComp(target) && _admin.IsAdmin(target))))
        {
            _adminLog.Add(LogType.Action, LogImpact.Medium, $"{ToPrettyString(ghost):player} tried to orbit admin-only {ToPrettyString(target)}");
            return;
        }

        _adminLog.Add(LogType.GhostWarp, $"{ToPrettyString(ghost)} ghost warped to {ToPrettyString(target)}");

        if (warp is not { Follow: false })
        {
            _follower.StartFollowingEntity(ghost, target);

            // A grid's origin can lie off its hull; sit the ghost in the middle of it.
            if (TryComp<MapGridComponent>(target, out var grid) && Transform(ghost).ParentUid == target)
                _transform.SetLocalPosition(ghost, grid.LocalAABB.Center);
            return;
        }

        var xform = Transform(ghost);
        _transform.SetCoordinates(ghost, xform, Transform(target).Coordinates);
        _transform.AttachToGridOrMap(ghost, xform);
        if (TryComp<PhysicsComponent>(ghost, out var physics))
            _physics.SetLinearVelocity(ghost, Vector2.Zero, body: physics);
    }

    /// <summary>
    /// Radio chat is sent without a source so listeners can't unmask voices. Ghosts get a copy that names the
    /// speaker, which gives them an orbit link in chat.
    /// </summary>
    public ChatMessage AddRadioSource(EntityUid listener, ChatMessage msg, EntityUid source)
    {
        if (!_ghostQuery.HasComp(listener) || !Exists(source))
            return msg;

        return new ChatMessage(msg.Channel, msg.Message, msg.WrappedMessage, GetNetEntity(source), msg.SenderKey,
            msg.HideChat, msg.MessageColorOverride, msg.AudioPath, msg.AudioVolume);
    }

    private void AddInterests(List<GhostOrbitTarget> targets, HashSet<EntityUid> seen, bool isAdmin)
    {
        AddInterest<GhostInterestComponent>(targets, seen, isAdmin, null);
        AddInterest<SingularityComponent>(targets, seen, isAdmin, "wf-ghost-orbit-interest-singularity");
        AddInterest<TeslaEnergyBallComponent>(targets, seen, isAdmin, "wf-ghost-orbit-interest-tesla");
        AddInterest<NukeComponent>(targets, seen, isAdmin, "wf-ghost-orbit-interest-nuke");
        AddInterest<NukeDiskComponent>(targets, seen, isAdmin, "wf-ghost-orbit-interest-disk");
        AddInterest<DragonRiftComponent>(targets, seen, isAdmin, "wf-ghost-orbit-interest-rift");
        AddInterest<AnomalyComponent>(targets, seen, isAdmin, "wf-ghost-orbit-interest-anomaly");
    }

    private void AddInterest<T>(List<GhostOrbitTarget> targets, HashSet<EntityUid> seen, bool isAdmin, LocId? kind)
        where T : IComponent
    {
        var query = AllEntityQuery<T, TransformComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out _, out var xform, out var meta))
        {
            if (!Listable(uid, xform, meta) || !seen.Add(uid))
                continue;

            var label = CompOrNull<GhostInterestComponent>(uid)?.Label ?? kind;
            targets.Add(new GhostOrbitTarget
            {
                Entity = GetNetEntity(uid),
                Name = meta.EntityName,
                Category = GhostOrbitCategory.Interest,
                Detail = label != null ? Loc.GetString(label) : meta.EntityPrototype?.Name,
                Prototype = meta.EntityPrototype?.ID,
                Followers = CountFollowers(uid, isAdmin),
            });
        }
    }

    /// <summary>
    /// Every mob, sorted by who is driving it: players, xenos, SSD players, dead players and NPCs.
    /// </summary>
    private void AddMobs(List<GhostOrbitTarget> targets, HashSet<EntityUid> seen, bool isAdmin)
    {
        var query = EntityQueryEnumerator<MobStateComponent, TransformComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out var mobState, out var xform, out var meta))
        {
            if (_ghostQuery.HasComp(uid) || !Listable(uid, xform, meta) || seen.Contains(uid))
                continue;

            var mindId = CompOrNull<MindContainerComponent>(uid)?.Mind;
            var mind = CompOrNull<MindComponent>(mindId);
            var playerOwned = mind?.UserId != null;
            var hasClient = HasComp<ActorComponent>(uid);
            var dead = _mobState.IsDead(uid, mobState);

            // Mindless corpses are clutter.
            if (dead && !playerOwned && !hasClient)
                continue;

            // Player-driven xenos get a section of their own; mindless ones stay with the NPCs.
            TryComp<XenoComponent>(uid, out var xeno);
            var isXeno = xeno != null && playerOwned;

            GhostOrbitCategory category;
            if (dead)
                category = GhostOrbitCategory.Dead;
            else if (isXeno)
                category = GhostOrbitCategory.Xenonid;
            else if (isAdmin && _roles.MindIsAntagonist(mindId))
                category = GhostOrbitCategory.Antagonist;
            else if (!hasClient)
                category = playerOwned ? GhostOrbitCategory.Disconnected : GhostOrbitCategory.Npc;
            else if (_mobState.IsCritical(uid, mobState))
                category = GhostOrbitCategory.Critical;
            else
                category = GhostOrbitCategory.Alive;

            string? detail = null;
            ProtoId<JobIconPrototype>? icon = null;
            var order = 0;
            if (!dead && xeno != null && playerOwned && _proto.TryIndex(xeno.Role, out var caste))
            {
                detail = caste.LocalizedName;
                icon = caste.Icon;

                // Queen and king first, then the highest tier down, with larvae and other tier 0 last.
                var tier = xeno.Tier;
                order = HasComp<XenoEvolutionGranterComponent>(uid) ? 0 : tier > 0 ? 4 - Math.Min(tier, 3) : 4;
            }
            else if (_jobs.MindTryGetJob(mindId, out var job))
            {
                detail = job.LocalizedName;
                icon = job.Icon;
            }

            seen.Add(uid);
            targets.Add(new GhostOrbitTarget
            {
                Entity = GetNetEntity(uid),
                Name = meta.EntityName,
                Category = category,
                Detail = detail,
                Prototype = meta.EntityPrototype?.ID,
                JobIcon = icon,
                Damage = dead ? 1f : GetDamage(uid),
                Followers = CountFollowers(uid, isAdmin),
                Disconnected = playerOwned && !hasClient && !dead,
                Order = order,
            });
        }
    }

    /// <summary>
    /// Ghosts, plus players attached to something that is not a mob.
    /// </summary>
    private void AddSessions(List<GhostOrbitTarget> targets, HashSet<EntityUid> seen, bool isAdmin)
    {
        foreach (var session in _player.Sessions)
        {
            if (session.AttachedEntity is not { Valid: true } attached || seen.Contains(attached))
                continue;

            var meta = MetaData(attached);
            if (!Listable(attached, Transform(attached), meta))
                continue;

            var isGhost = _ghostQuery.HasComp(attached);
            var adminGhost = isGhost && _admin.IsAdmin(session);
            if (adminGhost && !isAdmin)
                continue;

            seen.Add(attached);
            targets.Add(new GhostOrbitTarget
            {
                Entity = GetNetEntity(attached),
                Name = meta.EntityName,
                Category = isGhost ? GhostOrbitCategory.Ghost : GhostOrbitCategory.Alive,
                Detail = adminGhost ? Loc.GetString("wf-ghost-orbit-admin-ghost") : null,
                Prototype = meta.EntityPrototype?.ID,
                Followers = CountFollowers(attached, isAdmin),
                AdminOnly = adminGhost,
            });
        }
    }

    /// <summary>
    /// One entry per ship of every unresolved encounter, hidden ones included, for the Encounters tab.
    /// </summary>
    private void AddEncounters(List<GhostOrbitEncounterShip> ships, bool isAdmin)
    {
        var query = EntityQueryEnumerator<WFEncounterComponent>();
        while (query.MoveNext(out var uid, out var encounter))
        {
            if (encounter.Resolution != null)
                continue;

            var id = GetNetEntity(uid);
            foreach (var (key, ship) in encounter.Ships)
            {
                if (TerminatingOrDeleted(ship.Grid))
                    continue;

                var meta = MetaData(ship.Grid);
                if (!Listable(ship.Grid, Transform(ship.Grid), meta))
                    continue;

                ships.Add(new GhostOrbitEncounterShip
                {
                    Grid = GetNetEntity(ship.Grid),
                    Ship = meta.EntityName.Length > 0 ? meta.EntityName : key,
                    Encounter = encounter.Name,
                    EncounterId = id,
                    Side = ship.Side,
                    Category = encounter.Category,
                    Color = _markers.ShipColor(encounter, ship),
                    Hidden = encounter.Hidden,
                    Followers = CountFollowers(ship.Grid, isAdmin),
                });
            }
        }
    }

    private void AddWarpPoints(List<GhostOrbitTarget> targets, HashSet<EntityUid> seen, bool isAdmin)
    {
        var query = AllEntityQuery<WarpPointComponent, TransformComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out var warp, out var xform, out var meta))
        {
            if ((warp.AdminOnly && !isAdmin) || !Listable(uid, xform, meta) || !seen.Add(uid))
                continue;

            var deed = CompOrNull<ShuttleDeedComponent>(xform.GridUid);
            targets.Add(new GhostOrbitTarget
            {
                Entity = GetNetEntity(uid),
                Name = warp.Location ?? meta.EntityName,
                Category = deed != null ? GhostOrbitCategory.Ship : GhostOrbitCategory.Location,
                Detail = deed?.ShuttleOwner is { } owner ? Loc.GetString("wf-ghost-orbit-ship-owner", ("owner", owner)) : null,
                Followers = CountFollowers(uid, isAdmin),
                AdminOnly = warp.AdminOnly,
            });
        }
    }

    /// <summary>
    /// Skips nullspace and paused maps (cryo storage, shipyard previews).
    /// </summary>
    private static bool Listable(EntityUid uid, TransformComponent xform, MetaDataComponent meta)
    {
        return xform.MapID != MapId.Nullspace && !meta.EntityPaused && meta.EntityLifeStage < EntityLifeStage.Terminating;
    }

    private float? GetDamage(EntityUid uid)
    {
        if (!TryComp<DamageableComponent>(uid, out var damageable)
            || !_thresholds.TryGetDeadThreshold(uid, out var threshold)
            || threshold.Value <= 0)
            return null;

        return Math.Clamp(damageable.TotalDamage.Float() / threshold.Value.Float(), 0f, 1f);
    }

    private int CountFollowers(EntityUid uid, bool isAdmin)
    {
        if (!TryComp<FollowedComponent>(uid, out var followed))
            return 0;

        var count = 0;
        foreach (var follower in followed.Following)
        {
            if (_ghostQuery.HasComp(follower) && (isAdmin || !_admin.IsAdmin(follower)))
                count++;
        }

        return count;
    }
}
