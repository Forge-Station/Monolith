using System.Numerics;
using Content.Server.Engineering.Components;
using Content.Shared._Forge.Turrets;
using Content.Shared.Construction.Components;
using Content.Shared.Examine;
using Content.Shared.Humanoid;
using Content.Shared.Mind;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Prototypes;
using Content.Shared.NPC.Systems;
using Content.Shared.Popups;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._Forge.Turrets;

/// <summary>
/// Grid-local command server for faction turrets. Friend and foe lists override faction IFF.
/// Turrets without <see cref="TurretCommandLinkComponent"/> (PvE) are not affected.
/// </summary>
public sealed class TurretCommandSystem : EntitySystem
{
    private const float MinTurretSpacing = 4f;
    private const float SpacingCheckInterval = 30f;
    private const float AmmoUiInterval = 30f;

    private static readonly ProtoId<NpcFactionPrototype>[] PeacefulSpaceFactions =
    [
        "Mouse",
        "Cat",
        "PetsNT",
        "Passive",
        "MD",
        "ContrabandDetection",
    ];

    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly NpcFactionSystem _factions = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    private float _spacingTimer;
    private float _ammoUiTimer;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TurretCommandLinkComponent, ComponentStartup>(OnTurretStartup);
        SubscribeLocalEvent<TurretCommandLinkComponent, ComponentShutdown>(OnTurretShutdown);
        SubscribeLocalEvent<TurretCommandLinkComponent, EntParentChangedMessage>(OnTurretParentChanged);
        SubscribeLocalEvent<TurretCommandLinkComponent, ExaminedEvent>(OnTurretExamined);
        SubscribeLocalEvent<TurretCommandLinkComponent, AnchorAttemptEvent>(OnAnchorAttempt);
        SubscribeLocalEvent<TurretCommandLinkComponent, EntInsertedIntoContainerMessage>(OnMagazineChanged);
        SubscribeLocalEvent<TurretCommandLinkComponent, EntRemovedFromContainerMessage>(OnMagazineChanged);
        SubscribeLocalEvent<SpawnAfterInteractComponent, SpawnAfterInteractAttemptEvent>(OnDeployAttempt);

        SubscribeLocalEvent<TurretCommandServerComponent, ComponentStartup>(OnServerStartup);
        SubscribeLocalEvent<TurretCommandServerComponent, ComponentShutdown>(OnServerShutdown);
        SubscribeLocalEvent<TurretCommandServerComponent, EntParentChangedMessage>(OnServerParentChanged);

        SubscribeLocalEvent<TurretCommandConsoleComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<TurretCommandConsoleComponent, TurretCommandToggleTurretMessage>(OnToggleTurret);
        SubscribeLocalEvent<TurretCommandConsoleComponent, TurretCommandAddNameMessage>(OnAddName);
        SubscribeLocalEvent<TurretCommandConsoleComponent, TurretCommandClearPersonMessage>(OnClearPerson);
        SubscribeLocalEvent<TurretCommandConsoleComponent, TurretCommandRefreshMessage>(OnRefresh);
        SubscribeLocalEvent<TurretCommandConsoleComponent, TurretCommandSetDoctrineMessage>(OnSetDoctrine);
        SubscribeLocalEvent<TurretCommandConsoleComponent, TurretCommandSetTurretDoctrineMessage>(OnSetTurretDoctrine);
        SubscribeLocalEvent<TurretCommandConsoleComponent, TurretCommandSetMagazineLockMessage>(OnSetMagazineLock);
    }

    /// <summary>
    /// Stood-down turrets and people on the friendly list are not valid targets.
    /// </summary>
    public bool BlocksTarget(EntityUid turret, EntityUid target)
    {
        if (!TryComp<TurretCommandLinkComponent>(turret, out var link) || !link.Managed)
            return false;

        if (!link.Enabled || link.Jammed)
            return true;

        if (link.Server is not { } server || !TryComp<TurretCommandServerComponent>(server, out var comp))
            return false;

        if (IsAggressiveMob(turret, target))
            return false;

        if (link.Doctrine == TurretDoctrine.Hostile)
            return IsListed(server, target, friendly: true);

        return true;
    }

    /// <summary>
    /// Hostile doctrine shoots everyone except the friendly list.
    /// Neutral and friendly doctrines only force aggressive mobs.
    /// </summary>
    public bool ForcesTarget(EntityUid turret, EntityUid target)
    {
        if (!TryComp<TurretCommandLinkComponent>(turret, out var link) || !link.Managed || !link.Enabled || link.Jammed)
            return false;

        if (link.Server is not { } server || !HasComp<TurretCommandServerComponent>(server))
            return false;

        if (IsListed(server, target, friendly: true))
            return false;

        if (IsAggressiveMob(turret, target))
            return true;

        return link.Doctrine == TurretDoctrine.Hostile;
    }

    /// <summary>
    /// Adds explicitly hostile people who are not faction-hostile, so the turret can acquire them.
    /// </summary>
    public void AddForcedTargets(EntityUid turret, float range, HashSet<EntityUid> into)
    {
        if (TryComp<TurretCommandLinkComponent>(turret, out var link) && link.Managed && (!link.Enabled || link.Jammed))
            return;

        var map = _transform.GetMapCoordinates(turret);
        foreach (var ent in _lookup.GetEntitiesInRange<MobStateComponent>(map, range))
        {
            if (ent.Owner == turret)
                continue;

            // Space fauna is off the faction lists turrets actually use, so pull it in directly.
            if (IsSpaceFauna(ent.Owner) || ForcesTarget(turret, ent.Owner))
                into.Add(ent.Owner);
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _ammoUiTimer += frameTime;
        if (_ammoUiTimer >= AmmoUiInterval)
        {
            _ammoUiTimer = 0f;
            RefreshOpenConsoles();
        }

        _spacingTimer += frameTime;
        if (_spacingTimer < SpacingCheckInterval)
            return;

        _spacingTimer = 0f;
        RefreshSpacing();
    }

    private void OnTurretStartup(Entity<TurretCommandLinkComponent> ent, ref ComponentStartup args)
    {
        Bind(ent);
        if (ent.Comp.Managed)
            RefreshSpacing();
    }

    private void OnTurretShutdown(Entity<TurretCommandLinkComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.Managed)
            RefreshSpacing();
    }

    private void OnTurretParentChanged(Entity<TurretCommandLinkComponent> ent, ref EntParentChangedMessage args)
    {
        Bind(ent);
        if (ent.Comp.Managed)
            RefreshSpacing();
    }

    private void OnAnchorAttempt(Entity<TurretCommandLinkComponent> ent, ref AnchorAttemptEvent args)
    {
        if (!ent.Comp.Managed || !HasManagedTurretNearby(Transform(ent).Coordinates, ent))
            return;

        args.Cancel();
        _popup.PopupEntity(Loc.GetString("turret-command-too-close"), args.User, args.User);
    }

    private void OnDeployAttempt(Entity<SpawnAfterInteractComponent> ent, ref SpawnAfterInteractAttemptEvent args)
    {
        if (string.IsNullOrEmpty(ent.Comp.Prototype)
            || !_proto.TryIndex<EntityPrototype>(ent.Comp.Prototype, out var proto)
            || !proto.TryGetComponent<TurretCommandLinkComponent>(out var link, EntityManager.ComponentFactory)
            || !link.Managed)
            return;

        if (!HasManagedTurretNearby(args.Coordinates, null))
            return;

        args.Cancel();
        _popup.PopupEntity(Loc.GetString("turret-command-too-close"), args.User, args.User);
    }

    private void OnTurretExamined(Entity<TurretCommandLinkComponent> ent, ref ExaminedEvent args)
    {
        if (!ent.Comp.Managed)
            return;

        var text = ent.Comp.Jammed
            ? Loc.GetString("turret-command-examine-jammed")
            : ent.Comp.Server != null
                ? Loc.GetString(ent.Comp.Enabled ? "turret-command-examine-linked" : "turret-command-examine-stood-down")
                : Loc.GetString("turret-command-examine-unlinked");

        args.PushMarkup(text);
        if (ent.Comp.MagazineLocked)
            args.PushMarkup(Loc.GetString("turret-command-examine-magazine-locked"));
    }

    private void OnMagazineChanged(Entity<TurretCommandLinkComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID == "gun_magazine")
            RefreshOpenConsoles();
    }

    private void OnMagazineChanged(Entity<TurretCommandLinkComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID == "gun_magazine")
            RefreshOpenConsoles();
    }

    private void OnServerStartup(Entity<TurretCommandServerComponent> ent, ref ComponentStartup args)
    {
        RebindGrid(Transform(ent).GridUid);
    }

    private void OnServerShutdown(Entity<TurretCommandServerComponent> ent, ref ComponentShutdown args)
    {
        var query = AllEntityQuery<TurretCommandLinkComponent>();
        while (query.MoveNext(out _, out var link))
        {
            if (link.Server == ent.Owner)
                link.Server = null;
        }
    }

    private void OnServerParentChanged(Entity<TurretCommandServerComponent> ent, ref EntParentChangedMessage args)
    {
        var query = AllEntityQuery<TurretCommandLinkComponent>();
        while (query.MoveNext(out var uid, out var link))
        {
            if (link.Server == ent.Owner)
                Bind((uid, link));
        }

        RebindGrid(Transform(ent).GridUid);
    }

    private void OnUiOpened(Entity<TurretCommandConsoleComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateUi(ent);
    }

    private void OnToggleTurret(Entity<TurretCommandConsoleComponent> ent, ref TurretCommandToggleTurretMessage args)
    {
        if (FindServer(ent) is not { } server)
            return;

        if (!TryOwnTurret(server, args.Turret, out var turret, out var link))
            return;

        link.Enabled = args.Enabled;
        Bind((turret, link));
        UpdateUi(ent);
    }

    private void OnSetMagazineLock(Entity<TurretCommandConsoleComponent> ent, ref TurretCommandSetMagazineLockMessage args)
    {
        if (FindServer(ent) is not { } server)
            return;

        if (!TryOwnTurret(server, args.Turret, out var turret, out var link))
            return;

        link.MagazineLocked = args.Locked;
        Dirty(turret, link);
        UpdateUi(ent);
    }

    private void OnAddName(Entity<TurretCommandConsoleComponent> ent, ref TurretCommandAddNameMessage args)
    {
        if (FindServer(ent) is not { } server || !TryComp<TurretCommandServerComponent>(server, out var comp))
            return;

        var name = args.Name.Trim();
        if (name.Length == 0 || name.Length > 64 || args.Side == TurretIffSide.Clear)
            return;

        var key = NameKey(name);
        comp.Entries.RemoveAll(entry => NameKey(entry.Name) == key);
        comp.Entries.Add(new TurretIffEntry
        {
            Name = name,
            Friendly = args.Side == TurretIffSide.Friendly,
        });

        UpdateUi(ent);
    }

    private void OnClearPerson(Entity<TurretCommandConsoleComponent> ent, ref TurretCommandClearPersonMessage args)
    {
        if (FindServer(ent) is not { } server || !TryComp<TurretCommandServerComponent>(server, out var comp))
            return;

        var key = args.Key;
        if (string.IsNullOrEmpty(key))
            return;

        comp.Entries.RemoveAll(entry => NameKey(entry.Name) == key);
        UpdateUi(ent);
    }

    private void OnSetDoctrine(Entity<TurretCommandConsoleComponent> ent, ref TurretCommandSetDoctrineMessage args)
    {
        if (FindServer(ent) is not { } server || !TryComp<TurretCommandServerComponent>(server, out var comp))
            return;

        comp.Doctrine = args.Doctrine;
        SetDoctrine(server, comp, args.Doctrine);
        UpdateUi(ent);
    }

    private void OnSetTurretDoctrine(Entity<TurretCommandConsoleComponent> ent, ref TurretCommandSetTurretDoctrineMessage args)
    {
        if (FindServer(ent) is not { } server)
            return;

        if (!TryOwnTurret(server, args.Turret, out var turret, out var link))
            return;

        link.Doctrine = args.Doctrine;
        Bind((turret, link));
        UpdateUi(ent);
    }

    private void OnRefresh(Entity<TurretCommandConsoleComponent> ent, ref TurretCommandRefreshMessage args)
    {
        if (FindServer(ent) is { } server)
        {
            RebindGrid(Transform(server).GridUid);
            if (TryComp<TurretCommandServerComponent>(server, out var comp))
                ScanPlayers(server, comp);
        }

        UpdateUi(ent);
    }

    private void SetDoctrine(EntityUid server, TurretCommandServerComponent comp, TurretDoctrine doctrine)
    {
        var grid = Transform(server).GridUid;
        var query = EntityQueryEnumerator<TurretCommandLinkComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var link, out var xform))
        {
            if (!OwnsTurret(server, comp, uid, link, xform))
                continue;

            link.Doctrine = doctrine;
        }
    }

    private bool TryOwnTurret(EntityUid server, NetEntity netTurret, out EntityUid turret, out TurretCommandLinkComponent link)
    {
        turret = GetEntity(netTurret);
        if (!TryComp<TurretCommandLinkComponent>(turret, out var found) || !found.Managed)
        {
            link = found!;
            return false;
        }

        link = found;
        return link.Server == server || FindServer(turret) == server;
    }

    /// <summary>
    /// Records character names on the server grid. Called only from the refresh button.
    /// </summary>
    private void ScanPlayers(EntityUid server, TurretCommandServerComponent comp)
    {
        comp.Detected.Clear();
        comp.PlayersScanned = true;

        var grid = Transform(server).GridUid;
        if (grid == null)
            return;

        var query = EntityQueryEnumerator<HumanoidAppearanceComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid != grid || !_mind.TryGetMind(uid, out _, out var mind))
                continue;

            var name = string.IsNullOrWhiteSpace(mind.CharacterName) ? Name(uid) : mind.CharacterName;
            if (string.IsNullOrWhiteSpace(name))
                continue;

            name = name.Trim();
            if (comp.Detected.Exists(existing => NamesEqual(existing, name)))
                continue;

            comp.Detected.Add(name);
        }

        comp.Detected.Sort(StringComparer.OrdinalIgnoreCase);
    }

    private void UpdateUi(EntityUid console)
    {
        var state = new TurretCommandConsoleState();
        if (FindServer(console) is not { } server || !TryComp<TurretCommandServerComponent>(server, out var comp))
        {
            state.HasServer = false;
            _ui.SetUiState(console, TurretCommandUiKey.Key, state);
            return;
        }

        state.HasServer = true;
        state.Doctrine = comp.Doctrine;
        state.Accent = comp.Accent;
        var grid = Transform(server).GridUid;
        if (grid is { } gridUid)
        {
            state.HasGrid = true;
            state.Grid = GetNetEntity(gridUid);
        }

        var turrets = EntityQueryEnumerator<TurretCommandLinkComponent, TransformComponent, MetaDataComponent>();
        while (turrets.MoveNext(out var uid, out var link, out var xform, out _))
        {
            if (!OwnsTurret(server, comp, uid, link, xform))
                continue;

            var position = Vector2.Zero;
            if (grid is { } gridId)
            {
                var mapCoords = _transform.GetMapCoordinates(uid, xform);
                position = _transform.ToCoordinates(gridId, mapCoords).Position;
            }

            var ammo = new GetAmmoCountEvent();
            RaiseLocalEvent(uid, ref ammo);

            state.Turrets.Add(new TurretCommandTurretEntry
            {
                Entity = GetNetEntity(uid),
                Name = Name(uid),
                Enabled = link.Enabled,
                Jammed = link.Jammed,
                MagazineLocked = link.MagazineLocked,
                Ammo = ammo.Count,
                AmmoCapacity = ammo.Capacity,
                Doctrine = link.Doctrine,
                Position = position,
            });
        }

        state.Turrets.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));

        state.PlayersScanned = comp.PlayersScanned;
        foreach (var detected in comp.Detected)
        {
            state.Present.Add(new TurretCommandPersonEntry
            {
                Key = NameKey(detected),
                Name = detected,
            });
        }

        foreach (var entry in comp.Entries)
        {
            var row = new TurretCommandPersonEntry
            {
                Key = NameKey(entry.Name),
                Name = entry.Name,
            };

            if (entry.Friendly)
                state.Friendly.Add(row);
            else
                state.Hostile.Add(row);
        }

        _ui.SetUiState(console, TurretCommandUiKey.Key, state);
    }

    private void RefreshOpenConsoles()
    {
        var consoles = EntityQueryEnumerator<TurretCommandConsoleComponent>();
        while (consoles.MoveNext(out var uid, out _))
        {
            if (_ui.IsUiOpen(uid, TurretCommandUiKey.Key))
                UpdateUi(uid);
        }
    }

    private void Bind(Entity<TurretCommandLinkComponent> turret)
    {
        if (!turret.Comp.Managed)
        {
            turret.Comp.Server = null;
            return;
        }

        turret.Comp.Server = FindServer(turret);
    }

    private void RebindGrid(EntityUid? grid)
    {
        if (grid == null)
            return;

        var query = EntityQueryEnumerator<TurretCommandLinkComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var link, out var xform))
        {
            if (xform.GridUid != grid)
                continue;

            Bind((uid, link));
        }
    }

    private EntityUid? FindServer(EntityUid ent)
    {
        if (TryComp<TurretCommandServerComponent>(ent, out var self))
        {
            if (!TryComp<TurretCommandLinkComponent>(ent, out var selfLink) || FactionEquals(self.Faction, selfLink.Faction))
                return ent;
        }

        var bindAny = TryComp<TurretCommandLinkComponent>(ent, out var link) && link.BindAny;
        var wanted = link?.Faction ?? string.Empty;
        var xform = Transform(ent);
        if (xform.GridUid is not { } grid)
            return null;

        EntityUid? best = null;
        var bestDist = float.MaxValue;
        var origin = _transform.GetWorldPosition(ent);
        var query = EntityQueryEnumerator<TurretCommandServerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var serverComp, out var serverXform))
        {
            if (serverXform.GridUid != grid)
                continue;

            if (!bindAny && !FactionEquals(serverComp.Faction, wanted))
                continue;

            var dist = (_transform.GetWorldPosition(uid) - origin).LengthSquared();
            if (dist >= bestDist)
                continue;

            bestDist = dist;
            best = uid;
        }

        return best;
    }

    private bool IsListed(EntityUid? server, EntityUid target, bool friendly)
    {
        if (server is not { } serverUid || !TryComp<TurretCommandServerComponent>(serverUid, out var comp))
            return false;

        foreach (var entry in comp.Entries)
        {
            if (entry.Friendly != friendly)
                continue;

            if (EntryMatches(entry, target))
                return true;
        }

        return false;
    }

    private bool IsPerson(EntityUid uid)
    {
        return HasComp<HumanoidAppearanceComponent>(uid) || HasComp<ActorComponent>(uid);
    }

    private bool IsAggressiveMob(EntityUid turret, EntityUid target)
    {
        if (IsPerson(target))
            return false;

        if (IsSpaceFauna(target))
            return true;

        return _factions.IsHostileFactionMember(turret, target);
    }

    /// <summary>
    /// Carp, xenos, bears and the rest of the space-mob family. Pets that only share the body stay ignored.
    /// </summary>
    private bool IsSpaceFauna(EntityUid uid)
    {
        if (IsPerson(uid))
            return false;

        var protoId = MetaData(uid).EntityPrototype?.ID;
        if (protoId == null || !_proto.TryIndex<EntityPrototype>(protoId, out var proto))
            return false;

        if (!InheritsPrototype(proto, "SimpleSpaceMobBase"))
            return false;

        return !_factions.IsMemberOfAny(uid, PeacefulSpaceFactions);
    }

    private bool InheritsPrototype(EntityPrototype proto, string parentId)
    {
        if (proto.Parents == null)
            return false;

        foreach (var parent in proto.Parents)
        {
            if (parent == parentId)
                return true;

            if (_proto.TryIndex<EntityPrototype>(parent, out var parentProto) && InheritsPrototype(parentProto, parentId))
                return true;
        }

        return false;
    }

    private bool OwnsTurret(EntityUid server, TurretCommandServerComponent comp, EntityUid turret, TurretCommandLinkComponent link, TransformComponent xform)
    {
        if (!link.Managed || xform.GridUid != Transform(server).GridUid)
            return false;

        if (!link.BindAny && !FactionEquals(link.Faction, comp.Faction))
            return false;

        return link.Server == server || FindServer(turret) == server;
    }

    private bool HasManagedTurretNearby(EntityCoordinates coords, EntityUid? ignore)
    {
        var map = _transform.ToMapCoordinates(coords);
        foreach (var other in _lookup.GetEntitiesInRange<TurretCommandLinkComponent>(map, MinTurretSpacing))
        {
            if (other.Owner == ignore || !other.Comp.Managed)
                continue;

            if ((_transform.GetWorldPosition(other.Owner) - map.Position).Length() < MinTurretSpacing)
                return true;
        }

        return false;
    }

    private void RefreshSpacing()
    {
        var turrets = new List<(EntityUid Uid, MapCoordinates Map, TurretCommandLinkComponent Link, EntityUid? Grid)>();
        var query = EntityQueryEnumerator<TurretCommandLinkComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var link, out var xform))
        {
            if (!link.Managed || MetaData(uid).EntityLifeStage >= EntityLifeStage.Terminating)
                continue;

            turrets.Add((uid, _transform.GetMapCoordinates(uid, xform), link, xform.GridUid));
        }

        var jammed = new HashSet<EntityUid>();
        for (var i = 0; i < turrets.Count; i++)
        {
            for (var j = i + 1; j < turrets.Count; j++)
            {
                if (turrets[i].Map.MapId != turrets[j].Map.MapId)
                    continue;

                if ((turrets[i].Map.Position - turrets[j].Map.Position).Length() >= MinTurretSpacing)
                    continue;

                jammed.Add(turrets[i].Uid);
                jammed.Add(turrets[j].Uid);
            }
        }

        var dirtyGrids = new HashSet<EntityUid>();
        foreach (var turret in turrets)
        {
            var should = jammed.Contains(turret.Uid);
            if (turret.Link.Jammed == should)
                continue;

            turret.Link.Jammed = should;
            if (turret.Grid is { } grid)
                dirtyGrids.Add(grid);
        }

        if (dirtyGrids.Count == 0)
            return;

        var consoles = EntityQueryEnumerator<TurretCommandConsoleComponent, TransformComponent>();
        while (consoles.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid is { } grid && dirtyGrids.Contains(grid))
                UpdateUi(uid);
        }
    }

    private static bool FactionEquals(string left, string right)
    {
        return string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private bool EntryMatches(TurretIffEntry entry, EntityUid target)
    {
        if (string.IsNullOrWhiteSpace(entry.Name))
            return false;

        if (NamesEqual(entry.Name, Name(target)))
            return true;

        return _mind.TryGetMind(target, out _, out var mind)
               && mind.CharacterName is { } character
               && NamesEqual(entry.Name, character);
    }

    private static bool NamesEqual(string listed, string actual)
    {
        return string.Equals(listed.Trim(), actual.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static string NameKey(string name)
    {
        return "n:" + name.Trim().ToLowerInvariant();
    }
}
