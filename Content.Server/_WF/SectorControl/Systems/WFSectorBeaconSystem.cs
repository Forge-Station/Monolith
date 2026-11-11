using Content.Server._WF.NpcCrew.Systems;
using Content.Server._WF.SectorControl.Components;
using Content.Server.Administration.Managers;
using Content.Server.GameTicking;
using Content.Shared._Mono.Company;
using Content.Shared._WF.SectorControl;
using Content.Shared.Damage;
using Content.Shared.Examine;
using Content.Shared.GameTicking;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Player;
using Content.Shared.Popups;
using Content.Shared.Projectiles;
using Content.Shared.Verbs;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.SectorControl.Systems;

/// <summary>
/// Player, admin and NPC beacons as claim sources. One anchored beacon per cell. Destroying a player beacon frees
/// the cell, so its colour leaves the sector map. Admin and NPC beacons take no damage, and their cells stay theirs.
/// </summary>
public sealed class WFSectorBeaconSystem : EntitySystem
{
    [Dependency] private IAdminManager _admin = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private ServerGameTicker _ticker = default!;
    [Dependency] private SharedPointLightSystem _lights = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFCrewSystem _crew = default!;
    [Dependency] private WFSectorSyncSystem _sync = default!;
    [Dependency] private WFSectorTerritorySystem _territory = default!;

    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(1);

    public const string ReasonOccupied = "wf-sector-beacon-occupied";
    public const string ReasonHeld = "wf-sector-beacon-held";
    public const string ReasonNotInSpace = "wf-sector-beacon-not-in-space";
    public const string ReasonNoCompany = "wf-sector-beacon-no-company";
    public const string ReasonNoFaction = "wf-sector-beacon-no-faction";
    public const string ReasonRefused = "wf-sector-beacon-refused";
    public const string ReasonAdminOnly = "wf-sector-beacon-admin-only";
    public const string ReasonAlreadyOn = "wf-sector-beacon-already-on";

    private TimeSpan _nextCheck;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFSectorBeaconComponent, ActivateInWorldEvent>(OnActivate);
        SubscribeLocalEvent<WFSectorBeaconComponent, GetVerbsEvent<ActivationVerb>>(OnVerbs);
        SubscribeLocalEvent<WFSectorBeaconComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<WFSectorBeaconComponent, BeforeDamageChangedEvent>(OnDamage);
        SubscribeLocalEvent<WFSectorBeaconComponent, EntityTerminatingEvent>(OnTerminating);
        SubscribeLocalEvent<WFSectorBeaconComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WFSectorRetakeAttemptEvent>(OnRetake);
    }

    private void OnMapInit(Entity<WFSectorBeaconComponent> ent, ref MapInitEvent args)
    {
        ConsiderAuto(ent);
    }

    private void OnVerbs(Entity<WFSectorBeaconComponent> ent, ref GetVerbsEvent<ActivationVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || ent.Comp.Active)
            return;

        var user = args.User;
        args.Verbs.Add(new ActivationVerb
        {
            Text = Loc.GetString("wf-sector-beacon-verb-activate"),
            Act = () => Activate(ent, user),
        });
    }

    private void OnActivate(Entity<WFSectorBeaconComponent> ent, ref ActivateInWorldEvent args)
    {
        if (!args.Complex)
            return;

        args.Handled = true;
        Activate(ent, args.User);
    }

    private void Activate(Entity<WFSectorBeaconComponent> beacon, EntityUid user)
    {
        if (!TryActivate(beacon, user, out var reason))
        {
            _popup.PopupEntity(Loc.GetString(reason), beacon, user);
            return;
        }

        var callsign = beacon.Comp.Cell is { } cell ? WFSectorHex.Callsign(cell) : "";
        var company = beacon.Comp.Company is { } id ? CompanyName(id) : "";
        _popup.PopupEntity(Loc.GetString("wf-sector-beacon-activated", ("callsign", callsign), ("company", company)), beacon, user);
    }

    /// <summary>Claims the beacon's cell for the activator's company, or for the company set on an admin beacon.</summary>
    public bool TryActivate(EntityUid uid, EntityUid? user, out string reason)
    {
        return TryActivate((uid, Comp<WFSectorBeaconComponent>(uid)), user, out reason);
    }

    /// <inheritdoc cref="TryActivate(EntityUid, EntityUid?, out string)"/>
    public bool TryActivate(Entity<WFSectorBeaconComponent> beacon, EntityUid? user, out string reason)
    {
        reason = ReasonRefused;
        if (beacon.Comp.Active)
        {
            reason = ReasonAlreadyOn;
            return false;
        }

        if (beacon.Comp.Immortal && user is { } adminUser && !_admin.IsAdmin(adminUser))
        {
            reason = ReasonAdminOnly;
            return false;
        }

        var xform = Transform(beacon);
        if (!xform.Anchored || xform.GridUid == null || xform.MapID != _territory.SectorMap || !_territory.Enabled)
        {
            reason = ReasonNotInSpace;
            return false;
        }

        if (!ResolveCompany(beacon.Comp, user, out var company))
        {
            reason = ReasonNoCompany;
            return false;
        }

        var faction = FactionFor(company);
        if (faction == null)
        {
            reason = ReasonNoFaction;
            return false;
        }

        var cell = _territory.CellOf(new MapCoordinates(_transform.GetWorldPosition(beacon), xform.MapID));
        if (Occupied(xform.MapID, cell, beacon.Owner))
        {
            reason = ReasonOccupied;
            return false;
        }

        var owner = _territory.Owner(xform.MapID, cell);
        if (owner != null && owner != faction.ID)
        {
            reason = ReasonHeld;
            return false;
        }

        if (!_territory.TryClaim(xform.MapID, cell, faction.ID, beacon.Owner))
        {
            reason = ReasonRefused;
            return false;
        }

        beacon.Comp.Active = true;
        beacon.Comp.Cell = cell;
        beacon.Comp.Company = company;
        beacon.Comp.Faction = faction.ID;
        var name = CompanyName(company);
        _meta.SetEntityName(beacon, Loc.GetString("wf-sector-beacon-named", ("company", name)));
        _lights.SetColor(beacon, faction.Color);
        _lights.SetEnabled(beacon, true);
        RefreshLegend(faction.ID);
        return true;
    }

    private void OnExamined(Entity<WFSectorBeaconComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        if (ent.Comp.Active && ent.Comp.Cell is { } cell && ent.Comp.Company is { } company)
            args.PushText(Loc.GetString("wf-sector-beacon-examine-on", ("callsign", WFSectorHex.Callsign(cell)), ("company", CompanyName(company))));
        else
            args.PushText(Loc.GetString("wf-sector-beacon-examine-off"));
    }

    /// <summary>Player beacons take damage only from a non-admin player. Admin and NPC beacons take none.</summary>
    private void OnDamage(Entity<WFSectorBeaconComponent> ent, ref BeforeDamageChangedEvent args)
    {
        if (args.Cancelled || !args.Damage.AnyPositive())
            return;

        if (ent.Comp.Immortal || !PlayerDealt(args.Origin))
            args.Cancelled = true;
    }

    private void OnTerminating(Entity<WFSectorBeaconComponent> ent, ref EntityTerminatingEvent args)
    {
        if (!ent.Comp.Active)
            return;

        var faction = ent.Comp.Faction;
        ent.Comp.Active = false;
        ent.Comp.Cell = null;
        _territory.Release(ent.Owner);
        if (faction is { } id)
            RefreshLegend(id);
    }

    /// <summary>A living beacon keeps its cell. Parking a ship on it does not free the colour.</summary>
    private void OnRetake(ref WFSectorRetakeAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        var query = EntityQueryEnumerator<WFSectorBeaconComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out var xform))
        {
            if (comp.Active && comp.Cell == args.Cell && xform.MapID == args.Map && !TerminatingOrDeleted(uid))
            {
                args.Cancelled = true;
                return;
            }
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _nextCheck)
            return;

        _nextCheck = _timing.CurTime + CheckInterval;
        var pending = new List<Entity<WFSectorBeaconComponent>>();
        var query = EntityQueryEnumerator<WFSectorBeaconComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            pending.Add((uid, comp));
        }

        foreach (var beacon in pending)
        {
            if (TerminatingOrDeleted(beacon))
                continue;

            if (!beacon.Comp.Active)
            {
                ConsiderAuto(beacon);
                continue;
            }

            if (LeftItsCell(beacon))
                Deactivate(beacon);
        }
    }

    /// <summary>Drops the claim. The cell's colour goes with the last source.</summary>
    public void Deactivate(Entity<WFSectorBeaconComponent> beacon)
    {
        if (!beacon.Comp.Active)
            return;

        var faction = beacon.Comp.Faction;
        beacon.Comp.Active = false;
        beacon.Comp.Cell = null;
        _meta.SetEntityName(beacon, Loc.GetString("wf-sector-beacon-name"));
        _lights.SetEnabled(beacon, false);
        _territory.Release(beacon.Owner);
        if (faction is { } id)
            RefreshLegend(id);
    }

    private void ConsiderAuto(Entity<WFSectorBeaconComponent> beacon)
    {
        if (!beacon.Comp.AutoActivate || beacon.Comp.AutoTried || _ticker.RunLevel != GameRunLevel.InRound)
            return;

        beacon.Comp.AutoTried = true;
        TryActivate(beacon, null, out _);
    }

    private bool LeftItsCell(Entity<WFSectorBeaconComponent> beacon)
    {
        if (beacon.Comp.Cell is not { } cell || beacon.Comp.Faction is not { } faction)
            return true;

        var xform = Transform(beacon);
        if (!xform.Anchored || xform.MapID != _territory.SectorMap)
            return true;

        var here = _territory.CellOf(new MapCoordinates(_transform.GetWorldPosition(beacon), xform.MapID));
        return here != cell || _territory.Owner(xform.MapID, cell) != faction;
    }

    private bool Occupied(MapId map, WFSectorCell cell, EntityUid except)
    {
        var query = EntityQueryEnumerator<WFSectorBeaconComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (uid == except || !xform.Anchored || xform.GridUid == null || xform.MapID != map)
                continue;

            if (_territory.CellOf(new MapCoordinates(_transform.GetWorldPosition(uid), map)) == cell)
                return true;
        }

        return false;
    }

    private bool ResolveCompany(WFSectorBeaconComponent beacon, EntityUid? user, out ProtoId<CompanyPrototype> company)
    {
        if (KnownCompany(beacon.Company, out company))
            return true;

        if (user is { } uid && TryComp<CompanyComponent>(uid, out var member) && KnownCompany(member.CompanyName, out company))
            return true;

        company = default;
        return false;
    }

    private bool KnownCompany(ProtoId<CompanyPrototype>? id, out ProtoId<CompanyPrototype> company)
    {
        company = default;
        if (id is not { } value || value == "None" || !_prototypes.HasIndex(value))
            return false;

        company = value;
        return true;
    }

    private WFSectorFactionPrototype? FactionFor(ProtoId<CompanyPrototype> company)
    {
        foreach (var faction in _prototypes.EnumeratePrototypes<WFSectorFactionPrototype>())
        {
            if (faction.PlayerBeacons && faction.Company is { } id && id == company)
                return faction;
        }

        return null;
    }

    private string CompanyName(ProtoId<CompanyPrototype> company)
    {
        return _prototypes.TryIndex(company, out CompanyPrototype? proto) ? Loc.GetString(proto.Name) : company.Id;
    }

    /// <summary>
    /// True when a non-admin player dealt the hit. A carried gun resolves to its holder. A mounted gun counts when a
    /// living non-admin player is aboard its grid, so a player ship can break a beacon and an NPC ship cannot.
    /// </summary>
    private bool PlayerDealt(EntityUid? origin)
    {
        return origin is { } source && !TerminatingOrDeleted(source) && FindPlayer(source, 0) != null;
    }

    private EntityUid? FindPlayer(EntityUid source, int depth)
    {
        if (depth > 4 || TerminatingOrDeleted(source))
            return null;

        if (TryComp<ProjectileComponent>(source, out var projectile)
            && projectile.Shooter is { } shooter
            && shooter != source
            && FindPlayer(shooter, depth + 1) is { } fromShot)
            return fromShot;

        var wielder = _crew.Wielder(source);
        if (HasComp<ActorComponent>(wielder))
            return _admin.IsAdmin(wielder) ? null : wielder;

        var grid = Transform(wielder).GridUid ?? Transform(source).GridUid;
        if (grid == null)
            return null;

        var players = EntityQueryEnumerator<ActorComponent, TransformComponent, MobStateComponent>();
        while (players.MoveNext(out var uid, out _, out var xform, out var mob))
        {
            if (xform.GridUid != grid || _admin.IsAdmin(uid) || !_mobs.IsAlive(uid, mob))
                continue;

            return uid;
        }

        return null;
    }

    private void RefreshLegend(ProtoId<WFSectorFactionPrototype> faction)
    {
        if (!_prototypes.TryIndex(faction, out var proto) || !proto.PlayerBeacons)
        {
            _sync.SetStatus(faction, null);
            return;
        }

        var count = 0;
        var query = EntityQueryEnumerator<WFSectorBeaconComponent>();
        while (query.MoveNext(out _, out var comp))
        {
            if (comp.Active && comp.Faction == faction)
                count++;
        }

        _sync.SetStatus(faction, count == 0
            ? null
            : Loc.GetString("wf-sector-beacon-legend", ("faction", Loc.GetString(proto.Name)), ("count", count)));
    }
}
