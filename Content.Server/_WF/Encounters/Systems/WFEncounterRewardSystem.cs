using System.Linq;
using Content.Server._NF.Bank;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.NpcCrew;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server._WF.ShipShields;
using Content.Server.Administration.Logs;
using Content.Server.Chat.Managers;
using Content.Server.KillTracking;
using Content.Server.Stack;
using Content.Shared._Mono.Company;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Encounters;
using Content.Shared.Database;
using Content.Shared.Ghost;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs.Systems;
using Content.Shared.Radio;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Encounters.Systems;

/// <summary>
/// Rewards players who take a side in an encounter. Whose side you are on is decided by who you shoot: ship
/// weapon hits on one side's ships count as help for the others. A ship that has helped a side and not fired on
/// it becomes that side's ally for the encounter. When one side is the only one left, everyone who helped it
/// shares its reward equally, paid into their bank accounts, with faction credits on top for its own company.
/// </summary>
public sealed partial class WFEncounterRewardSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private WFEncounterSystem _encounters = default!;
    [Dependency] private WFCrewEscortSystem _escorts = default!;
    [Dependency] private WFCrewShipStatusSystem _status = default!;
    [Dependency] private WFCrewAlertSystem _alerts = default!;
    [Dependency] private BankSystem _bank = default!;
    [Dependency] private Content.Server._WF.Companies.WFCompanySystem _companies = default!;
    [Dependency] private StackSystem _stack = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private Content.Server.Shuttles.Systems.DockingSystem _docking = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <summary>Hits on a side's ships before a player ship counts as fighting it.</summary>
    public const int MinimumHits = 5;

    /// <summary>How near a stranded ship a player must be when it gets under way to share its rescue reward.</summary>
    public const float RescueRange = 150f;

    private static readonly ProtoId<RadioChannelPrototype> ThanksChannel = "Common";
    private static readonly EntProtoId CashEntity = "SpaceCash";
    private static readonly TimeSpan CapWindow = TimeSpan.FromHours(1);
    private readonly Dictionary<NetUserId, (TimeSpan Since, int Paid)> _paid = new();

    public override void Initialize()
    {
        base.Initialize();
        // After the crews have judged the hit: a stray shot from a ship's own side is no help to its enemies.
        SubscribeLocalEvent<WFCrewHullHitEvent>(OnHullHit, after: [typeof(WFCrewAlertSystem)]);
        SubscribeLocalEvent<WFShipShieldAttackedEvent>(OnShieldHit, after: [typeof(WFCrewAlertSystem)]);
        SubscribeLocalEvent<WFEncounterResolvedEvent>(OnResolved);
        SubscribeLocalEvent<WFCrewFriendlyFireQueryEvent>(OnFriendlyFireQuery);
        SubscribeLocalEvent<KillReportedEvent>(OnKillReported);
    }

    /// <summary>Counts a crewman downed by a player, or with a player's assist, toward that player's share of the encounter.</summary>
    private void OnKillReported(ref KillReportedEvent args)
    {
        if (args.Suicide || !TryComp<WFCrewComponent>(args.Entity, out var member) || member.Group.Length == 0)
            return;

        // A laser names the gun and not its holder, so a kill can come with no player behind it; the crewman's last
        // player attacker takes it then.
        var primary = args.Primary is KillEnvironmentSource ? LastPlayerAttacker(member) ?? args.Primary : args.Primary;
        WFEncounterComponent? found = null;
        var query = EntityQueryEnumerator<WFEncounterComponent>();
        while (query.MoveNext(out _, out var encounter))
        {
            if (encounter.Ships.Values.FirstOrDefault(ship => ship.Group == member.Group) is not { } ship)
                continue;

            found = encounter;
            if (primary is KillPlayerSource || args.Assist is KillPlayerSource)
                ship.LastFought = _timing.CurTime;
            if (encounter.Resolution != null)
                break;

            RecordKill(encounter, primary, ship.Side);
            if (args.Assist is { } assist && assist != primary)
                RecordKill(encounter, assist, ship.Side);
            break;
        }

        if (!EnsureComp<WFCrewKillPaidComponent>(args.Entity, out _))
        {
            PayKillBounty(args.Entity, primary, args.Assist);
            DropCash(args.Entity, found);
        }
    }

    /// <summary>The player who struck a crewman last, if one did in the last little while.</summary>
    private KillSource? LastPlayerAttacker(WFCrewComponent member)
    {
        EntityUid? last = null;
        var at = TimeSpan.MinValue;
        foreach (var (attacker, time) in member.Struck)
        {
            if (time > at && HasComp<ActorComponent>(attacker))
                (last, at) = (attacker, time);
        }

        return CompOrNull<ActorComponent>(last)?.PlayerSession.UserId is { } user ? new KillPlayerSource(user) : null;
    }

    /// <summary>
    /// Drops a downed crewman's thalers at his feet, scaled by the threat tier his encounter spawned at, or by the
    /// sector's threat when he flies for none.
    /// </summary>
    private void DropCash(EntityUid victim, WFEncounterComponent? encounter)
    {
        if (CompOrNull<CompanyComponent>(victim)?.CompanyName.Id is not { } company
            || !_prototypes.TryIndex<WFStandingPrototype>(company, out var standing) || standing.CashDrop <= 0)
            return;

        var tier = encounter?.Tier ?? encounter?.Threat;
        if (tier == null)
        {
            var ev = new WFEncounterThreatTierEvent(null);
            RaiseLocalEvent(ref ev);
            tier = ev.Tier;
        }

        var steps = Math.Clamp(tier ?? 0, 0, WFEncounterThreat.MaxTier) + 1;
        var amount = (int) (standing.CashDrop * steps * _random.NextFloat(0.75f, 1.25f));
        if (amount > 0)
            _stack.SpawnMultiple(CashEntity, amount, Transform(victim).Coordinates);
    }

    /// <summary>
    /// Pays a player on the spot for killing a crewman of a company with a kill bounty, and half that to one who assisted,
    /// whether the crewman's encounter is still running or not: boarders left aboard a player's ship pay too.
    /// </summary>
    private void PayKillBounty(EntityUid victim, KillSource primary, KillSource? assist)
    {
        if (CompOrNull<CompanyComponent>(victim)?.CompanyName.Id is not { } company
            || !_prototypes.TryIndex<WFStandingPrototype>(company, out var standing) || standing.KillBounty <= 0)
            return;

        var name = _prototypes.TryIndex<CompanyPrototype>(company, out var companyPrototype) ? companyPrototype.Name : company;
        PayKill(primary, standing.KillBounty, name, victim);
        if (assist != null && !SamePlayer(assist, primary))
            PayKill(assist, standing.KillBounty / 2, name, victim);
    }

    private static bool SamePlayer(KillSource first, KillSource second)
    {
        return first is KillPlayerSource a && second is KillPlayerSource b && a.PlayerId == b.PlayerId;
    }

    private void PayKill(KillSource source, int amount, string company, EntityUid victim)
    {
        if (source is not KillPlayerSource player || !_playerManager.TryGetSessionById(player.PlayerId, out var session)
            || session.AttachedEntity is not { } mob)
            return;

        var paid = Capped(session.UserId, amount, _config.GetCVar(EncountersCVars.PayoutHourlyCap));
        if (paid <= 0)
            return;

        // A deposit that fails, with no bank account or deposits off, pays nothing and keeps the allowance.
        var cut = _companies.CutFor(session, paid);
        if (!_bank.TryBankDeposit(mob, paid - cut, false))
        {
            Refund(session.UserId, paid);
            return;
        }
        _companies.PayCut(session, cut, "bounty");

        _chat.DispatchServerMessage(session, Loc.GetString("wf-encounter-kill-bounty-paid", ("amount", paid), ("company", company)));
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{session.Name} was paid {paid} thalers for killing {ToPrettyString(victim):entity} of {company}");
    }

    private static void RecordKill(WFEncounterComponent encounter, KillSource source, string side)
    {
        if (source is not KillPlayerSource player)
            return;

        if (!encounter.Kills.TryGetValue(player.PlayerId, out var bySide))
            encounter.Kills[player.PlayerId] = bySide = new Dictionary<string, int>();
        bySide[side] = bySide.GetValueOrDefault(side) + 1;
    }

    private void OnHullHit(ref WFCrewHullHitEvent args)
    {
        Damaged(args.Grid);
        // Judged before it is counted, as the crews judged it, so the same hit is never weighed twice.
        RecordHit(args.Grid, args.AttackerGrid);
        Track(args.Grid, args.AttackerGrid);
    }

    private void OnShieldHit(ref WFShipShieldAttackedEvent args)
    {
        // A handheld shot at a shield is no ship weapon hit.
        if (!_alerts.IsShipWeapon(args.Weapon))
            return;

        Damaged(args.Grid);
        RecordHit(args.Grid, args.AttackerGrid);
        Track(args.Grid, args.AttackerGrid);
    }

    /// <summary>An encounter ship hit by any ship weapon, an NPC's included, stays real: it would come back whole from simulation.</summary>
    private void Damaged(EntityUid victim)
    {
        if (TryComp<WFEncounterGridComponent>(victim, out var marker) && TryComp<WFEncounterComponent>(marker.Encounter, out var encounter)
            && encounter.Ships.TryGetValue(marker.Key, out var ship))
            ship.Touched = true;
    }

    /// <summary>How long a vessel's hits count toward whom it is fighting.</summary>
    private static readonly TimeSpan FireWindow = TimeSpan.FromSeconds(30);

    /// <summary>How many times over its hits on a side a vessel must have hit that side's enemies to be its friend.</summary>
    private const int FriendlyFireRatio = 2;

    /// <summary>Counts a ship weapon hit on an encounter ship by any vessel outside the encounter, excused or not.</summary>
    private void Track(EntityUid victim, EntityUid attacker)
    {
        if (!TryComp<WFEncounterGridComponent>(victim, out var marker) || HasComp<WFEncounterGridComponent>(attacker)
            || !TryComp<WFEncounterComponent>(marker.Encounter, out var encounter)
            || !encounter.Ships.TryGetValue(marker.Key, out var ship))
            return;

        var now = _timing.CurTime;
        var key = (attacker, ship.Side);
        if (!encounter.Fire.TryGetValue(key, out var fire))
            encounter.Fire[key] = fire = new List<TimeSpan>();
        fire.RemoveAll(hit => now - hit > FireWindow);
        fire.Add(now);
    }

    /// <summary>
    /// Vouches for a vessel to a ship's crew: it is their friend while, over the last half minute, it has hit their
    /// side's enemies at least twice as often as it has hit them, this hit included. In a melee the odd round goes
    /// astray; a vessel plainly fighting their fight is not an enemy for it. Conduct alone decides, an alliance
    /// notwithstanding: an ally that turns its guns on the side loses the excuse, and with it the alliance.
    /// </summary>
    private void OnFriendlyFireQuery(ref WFCrewFriendlyFireQueryEvent args)
    {
        if (args.Friendly || !TryComp<WFEncounterGridComponent>(args.Grid, out var marker)
            || !TryComp<WFEncounterComponent>(marker.Encounter, out var encounter)
            || !encounter.Ships.TryGetValue(marker.Key, out var ship))
            return;

        var now = _timing.CurTime;
        var onUs = 0;
        var onEnemies = 0;
        foreach (var ((attacker, side), fire) in encounter.Fire)
        {
            if (attacker != args.Attacker)
                continue;
            var recent = fire.Count(hit => now - hit <= FireWindow);
            if (side == ship.Side)
                onUs += recent;
            else
                onEnemies += recent;
        }

        args.Friendly = onEnemies >= FriendlyFireRatio * (onUs + 1);
    }

    /// <summary>Credits a player who boarded an encounter ship and put it out of the fight as having hit its side <see cref="MinimumHits"/> times.</summary>
    public void RecordBoarding(WFEncounterComponent encounter, NetUserId player, string side)
    {
        if (encounter.Resolution != null)
            return;

        if (!encounter.Hits.TryGetValue(player, out var bySide))
            encounter.Hits[player] = bySide = new Dictionary<string, int>();
        bySide[side] = Math.Max(bySide.GetValueOrDefault(side), MinimumHits);
    }

    /// <summary>Counts a ship weapon hit by a player-crewed ship on an encounter ship as help for the other sides.</summary>
    public void RecordHit(EntityUid victim, EntityUid attacker)
    {
        if (!TryComp<WFEncounterGridComponent>(victim, out var marker) || HasComp<WFEncounterGridComponent>(attacker)
            || !TryComp<WFEncounterComponent>(marker.Encounter, out var encounter)
            || !encounter.Ships.TryGetValue(marker.Key, out var ship))
            return;

        var crew = new List<NetUserId>();
        var players = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (players.MoveNext(out var uid, out var actor, out var xform))
        {
            if (xform.GridUid == attacker && !HasComp<GhostComponent>(uid) && !_mobs.IsDead(uid))
                crew.Add(actor.PlayerSession.UserId);
        }

        if (crew.Count == 0)
            return;

        // A hit from a crewed ship is a fight that keeps the ship around, excused or after the encounter is over.
        ship.LastFought = _timing.CurTime;
        if (_alerts.IsExcused(victim, attacker) || encounter.Resolution != null)
            return;

        foreach (var player in crew)
        {
            if (!encounter.Hits.TryGetValue(player, out var bySide))
                encounter.Hits[player] = bySide = new Dictionary<string, int>();
            bySide[ship.Side] = bySide.GetValueOrDefault(ship.Side) + 1;
        }

        var hits = encounter.ShipHits.GetValueOrDefault((attacker, ship.Side)) + 1;
        encounter.ShipHits[(attacker, ship.Side)] = hits;
        // Firing on a side that took the ship for an ally ends the alliance.
        if (hits == 1)
            RevokeAlliance(encounter, attacker, ship.Side);
        if (hits != MinimumHits)
            return;

        // Having fought this side and hardly the others, the ship is on their side: they hold their fire for it.
        foreach (var other in encounter.Ships.Values)
        {
            if (other.Side == ship.Side || TerminatingOrDeleted(other.Grid)
                || encounter.ShipHits.GetValueOrDefault((attacker, other.Side)) * FriendlyFireRatio > hits)
                continue;

            _escorts.AddAlly(other.Grid, attacker);
            encounter.Allies.Add((other.Grid, attacker));
        }
    }

    /// <summary>Ends the alliances the ships of a side made with a player ship that has now fired on them.</summary>
    private void RevokeAlliance(WFEncounterComponent encounter, EntityUid attacker, string side)
    {
        for (var i = encounter.Allies.Count - 1; i >= 0; i--)
        {
            var (held, ally) = encounter.Allies[i];
            if (ally != attacker || !encounter.Ships.Values.Any(other => other.Grid == held && other.Side == side))
                continue;

            _escorts.RemoveAlly(held, ally);
            encounter.Allies.RemoveAt(i);
        }
    }

    /// <summary>
    /// The one side with a ship still in the fight, or null when none or several are. Judged as the encounter was:
    /// by who is fit to fight with living crew, so a side whose crews are dead has lost whatever state its hulls are in.
    /// </summary>
    public string? DecidedSide(WFEncounterComponent encounter)
    {
        string? side = null;
        foreach (var ship in encounter.Ships.Values)
        {
            if (!InFight(ship))
                continue;

            if (side != null && side != ship.Side)
                return null;

            side = ship.Side;
        }

        return side;
    }

    /// <summary>A ship is in the fight while it is fit to fight and any of its crew live, aboard or not.</summary>
    private bool InFight(WFEncounterShipState ship)
    {
        return _encounters.InFight(ship);
    }

    private void OnResolved(ref WFEncounterResolvedEvent args)
    {
        if (!TryComp<WFEncounterComponent>(args.Encounter, out var encounter))
            return;

        foreach (var (ship, ally) in encounter.Allies)
        {
            _escorts.RemoveAlly(ship, ally);
        }

        encounter.Allies.Clear();
        // No side is left to win a blockade: whoever broke it shares the reward.
        if (args.Resolution == WFEncounterResolution.Destroyed && encounter.Blockades)
        {
            PayBreakers(args.Encounter, encounter);
            return;
        }

        if (args.Resolution != WFEncounterResolution.Decided || !_prototypes.TryIndex(encounter.Prototype, out var prototype))
            return;

        // The winner is the one side the encounter was decided for.
        if (DecidedSide(encounter) is not { } side
            || prototype.Rewards.FirstOrDefault(entry => entry.Side == side) is not { } reward)
            return;

        var helpers = GetHelpers(encounter, encounter.Ships.Values.Where(ship => ship.Side != side).Select(ship => ship.Side).ToHashSet());
        if (helpers.Count == 0)
            return;

        Pay(args.Encounter, encounter, reward, helpers, $"helping side {side} win");

        // Whichever ship of the winning side can still speak says the thanks.
        if (reward.Thanks is { } thanks)
        {
            foreach (var ship in encounter.Ships.Values)
            {
                if (ship.Side == side && _encounters.TrySay(ship, ThanksChannel, Loc.GetString(thanks, ("count", helpers.Count))))
                    break;
            }
        }
    }

    /// <summary>Pays a destroyed blockade's reward to the players who broke it.</summary>
    private void PayBreakers(EntityUid uid, WFEncounterComponent encounter)
    {
        if (!_prototypes.TryIndex(encounter.Prototype, out var prototype) || prototype.Rewards.FirstOrDefault() is not { } reward)
            return;

        var breakers = GetHelpers(encounter, encounter.Ships.Values.Select(ship => ship.Side).ToHashSet());
        if (breakers.Count > 0)
            Pay(uid, encounter, reward, breakers, "breaking a blockade");
    }

    /// <summary>
    /// The living players who hit the losing sides at least <see cref="MinimumHits"/> times or downed one of their
    /// crewmen in person, hit or downed nobody of another side and are not of a loser's company.
    /// </summary>
    public List<(ICommonSession Session, EntityUid Mob)> GetHelpers(WFEncounterComponent encounter, IReadOnlyCollection<string> losingSides)
    {
        _prototypes.TryIndex(encounter.Prototype, out var prototype);

        // A destroyed hull is gone by now, so its company is read from the prototype as well.
        var losers = new HashSet<string>();
        foreach (var (key, ship) in encounter.Ships)
        {
            if (!losingSides.Contains(ship.Side))
                continue;

            if (prototype?.Ships.FirstOrDefault(entry => entry.Key == key)?.Company?.Id is { Length: > 0 } listed)
                losers.Add(listed);
            if (!TerminatingOrDeleted(ship.Grid)
                && CompOrNull<CompanyComponent>(ship.Grid)?.CompanyName.Id is { Length: > 0 } company)
                losers.Add(company);
        }

        var helpers = new List<(ICommonSession Session, EntityUid Mob)>();
        var empty = new Dictionary<string, int>();
        foreach (var user in encounter.Hits.Keys.Union(encounter.Kills.Keys))
        {
            var hits = encounter.Hits.GetValueOrDefault(user) ?? empty;
            var kills = encounter.Kills.GetValueOrDefault(user) ?? empty;
            var against = hits.Where(pair => losingSides.Contains(pair.Key)).Sum(pair => pair.Value);
            var downed = kills.Where(pair => losingSides.Contains(pair.Key)).Sum(pair => pair.Value);
            if (against < MinimumHits && downed == 0
                || hits.Concat(kills).Any(pair => pair.Value > 0 && !losingSides.Contains(pair.Key))
                || !_playerManager.TryGetSessionById(user, out var session) || session.AttachedEntity is not { } mob
                || HasComp<GhostComponent>(mob) || _mobs.IsDead(mob))
                continue;

            // Nobody is paid for helping to destroy a ship of their own company.
            if (CompOrNull<CompanyComponent>(mob)?.CompanyName.Id is { Length: > 0 } own && losers.Contains(own))
                continue;

            helpers.Add((session, mob));
        }

        return helpers;
    }

    /// <summary>
    /// Pays a bounty on a beaten encounter to its helpers, and a fifth to living players aboard supporters; returns how many were paid.
    /// <paramref name="extraCredits"/> are further rewards of which only the faction credits are handed out. Every
    /// player the bounty is shared among is added to <paramref name="helperMobs"/>, paid or capped.
    /// </summary>
    public int PayBounty(Entity<WFEncounterComponent?> encounter, WFEncounterReward reward, string deed,
        IReadOnlyList<EntityUid>? supporters = null, IReadOnlyList<WFEncounterReward>? extraCredits = null,
        List<EntityUid>? helperMobs = null)
    {
        if (!Resolve(encounter, ref encounter.Comp, false))
            return 0;

        // Helpers share per attacking ship and then per head; the support pool is taken only if someone else is aboard a supporter.

        var comp = encounter.Comp;
        var helpers = GetHelpers(comp, comp.Ships.Values.Select(ship => ship.Side).ToHashSet());
        var aids = new List<(ICommonSession Session, EntityUid Mob)>();
        if (supporters is { Count: > 0 })
        {
            var players = EntityQueryEnumerator<ActorComponent, TransformComponent>();
            while (players.MoveNext(out var uid, out var actor, out var xform))
            {
                if (xform.GridUid is { } grid && supporters.Contains(grid) && !HasComp<GhostComponent>(uid) && !_mobs.IsDead(uid)
                    && helpers.All(helper => helper.Session != actor.PlayerSession))
                    aids.Add((actor.PlayerSession, uid));
            }
        }

        if (helpers.Count == 0 && aids.Count == 0)
        {
            var hits = string.Join(", ", comp.Hits.Select(pair => $"{pair.Key}: {pair.Value.Values.Sum()}"));
            _adminLog.Add(LogType.Action, LogImpact.Medium,
                $"No helpers for {deed} {ToPrettyString(encounter):entity}. Hits by player: {hits}");
            return 0;
        }

        var pool = aids.Count > 0 ? reward.Spesos / 5 : 0;
        var shares = new List<int>();
        if (helpers.Count > 0)
        {
            // Hits are kept per ship; a helper counts for the attacking ship they are aboard, or for none.
            var attackers = comp.ShipHits.Keys.Select(key => key.Attacker).ToHashSet();
            var groups = helpers.Select(helper => Transform(helper.Mob).GridUid is { } grid && attackers.Contains(grid) ? grid : EntityUid.Invalid).ToList();
            var perGroup = (reward.Spesos - pool) / groups.Distinct().Count();
            foreach (var group in groups)
            {
                shares.Add(perGroup / groups.Count(other => other == group));
            }
        }

        foreach (var _ in aids)
        {
            shares.Add(pool / aids.Count);
        }

        helpers.AddRange(aids);
        helperMobs?.AddRange(helpers.Select(helper => helper.Mob));
        return Pay(encounter, comp, reward, helpers, deed, shares, extraCredits);
    }

    /// <summary>
    /// Pays a rescued ship's side reward among the living players aboard it, aboard a ship docked with it, or within
    /// <see cref="RescueRange"/> of it, and the ship says its thanks.
    /// </summary>
    public void PayRescue(Entity<WFEncounterComponent> encounter, WFEncounterShipState ship)
    {
        if (TerminatingOrDeleted(ship.Grid) || !_prototypes.TryIndex(encounter.Comp.Prototype, out var prototype)
            || prototype.Rewards.FirstOrDefault(entry => entry.Side == ship.Side) is not { } reward)
            return;

        var docked = new HashSet<EntityUid>();
        foreach (var dock in _docking.GetDocks(ship.Grid))
        {
            if (dock.Comp.DockedWith is { } other && Transform(other).GridUid is { } grid)
                docked.Add(grid);
        }

        var here = _transform.GetMapCoordinates(ship.Grid);
        var helpers = new List<(ICommonSession Session, EntityUid Mob)>();
        var players = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (players.MoveNext(out var uid, out var actor, out var xform))
        {
            if (HasComp<GhostComponent>(uid) || _mobs.IsDead(uid))
                continue;

            var aboard = xform.GridUid is { } grid && (grid == ship.Grid || docked.Contains(grid));
            if (!aboard && (xform.MapID != here.MapId
                    || (_transform.GetWorldPosition(xform) - here.Position).LengthSquared() > RescueRange * RescueRange))
                continue;

            helpers.Add((actor.PlayerSession, uid));
        }

        if (helpers.Count == 0)
            return;

        Pay(encounter, encounter.Comp, reward, helpers, $"rescuing a ship of side {ship.Side} in");
        if (reward.Thanks is { } thanks)
            _encounters.TrySay(ship, ThanksChannel, Loc.GetString(thanks, ("count", helpers.Count)));
    }

    /// <summary>Pays a reward to the helpers within the hourly cap, equally unless shares are given, and returns how many received anything.</summary>
    private int Pay(EntityUid uid, WFEncounterComponent encounter, WFEncounterReward reward,
        List<(ICommonSession Session, EntityUid Mob)> helpers, string deed, List<int>? shares = null,
        IReadOnlyList<WFEncounterReward>? extraCredits = null)
    {
        return Pay(encounter.Name, $"encounter {encounter.Prototype} ({ToPrettyString(uid):entity})", reward, helpers, deed, shares, extraCredits);
    }

    /// <summary>
    /// Pays spesos to the living players aboard some ships, an equal share to each crewed ship and then to each player
    /// aboard it, within the hourly cap; returns how many were paid. <paramref name="name"/> is what the payment says it
    /// is for and <paramref name="subject"/> names the same in the admin log.
    /// </summary>
    public int PayCrews(string name, string subject, int spesos, IEnumerable<EntityUid> ships, string deed)
    {
        var aboard = new Dictionary<EntityUid, List<(ICommonSession Session, EntityUid Mob)>>();
        foreach (var ship in ships)
        {
            if (!TerminatingOrDeleted(ship))
                aboard[ship] = new List<(ICommonSession, EntityUid)>();
        }

        var players = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (players.MoveNext(out var uid, out var actor, out var xform))
        {
            if (xform.GridUid is { } grid && aboard.TryGetValue(grid, out var crew) && !HasComp<GhostComponent>(uid) && !_mobs.IsDead(uid))
                crew.Add((actor.PlayerSession, uid));
        }

        var helpers = new List<(ICommonSession Session, EntityUid Mob)>();
        var shares = new List<int>();
        var crewed = aboard.Values.Count(crew => crew.Count > 0);
        foreach (var crew in aboard.Values)
        {
            foreach (var helper in crew)
            {
                helpers.Add(helper);
                shares.Add(spesos / crewed / crew.Count);
            }
        }

        return helpers.Count == 0 ? 0 : Pay(name, subject, new WFEncounterReward { Spesos = spesos }, helpers, deed, shares);
    }

    private int Pay(string name, string subject, WFEncounterReward reward,
        List<(ICommonSession Session, EntityUid Mob)> helpers, string deed, List<int>? shares = null,
        IReadOnlyList<WFEncounterReward>? extraCredits = null)
    {
        var received = 0;
        var cap = _config.GetCVar(EncountersCVars.PayoutHourlyCap);
        for (var i = 0; i < helpers.Count; i++)
        {
            var (session, mob) = helpers[i];
            var share = shares?[i] ?? reward.Spesos / helpers.Count;
            var paid = Capped(session.UserId, share, cap);
            if (paid < share)
            {
                _chat.DispatchServerMessage(session, Loc.GetString("wf-encounter-reward-capped", ("name", name), ("cap", cap)));
                _adminLog.Add(LogType.Action, LogImpact.Medium,
                    $"{session.Name} hit the payout cap: {paid} of {share} spesos for {deed} {subject}");
            }

            // A deposit that fails, with no bank account or deposits off, pays nothing and keeps the allowance.
            var cut = _companies.CutFor(session, paid);
            if (paid > 0 && !_bank.TryBankDeposit(mob, paid - cut, false))
            {
                Refund(session.UserId, paid);
                paid = 0;
            }
            else
                _companies.PayCut(session, cut, "reward");

            var credits = GiveCredits(reward, mob);
            if (extraCredits != null)
            {
                foreach (var extra in extraCredits)
                {
                    credits += GiveCredits(extra, mob);
                }
            }

            if (paid == 0 && credits == 0)
                continue;

            received++;
            _chat.DispatchServerMessage(session, Loc.GetString(credits > 0 ? "wf-encounter-reward-paid-credits" : "wf-encounter-reward-paid",
                ("name", name), ("amount", paid), ("credits", credits)));
            _adminLog.Add(LogType.Action, LogImpact.Medium,
                $"{session.Name} was paid {paid} spesos and {credits} faction credits for {deed} {subject}");
        }

        return received;
    }

    /// <summary>Hands a mob the faction credits of a reward if it is of one of its companies, and returns how many.</summary>
    private int GiveCredits(WFEncounterReward reward, EntityUid mob)
    {
        if (reward.Credits <= 0 || reward.CreditEntity is not { } creditEntity
            || CompOrNull<CompanyComponent>(mob)?.CompanyName.Id is not { } company || !reward.CreditCompanies.Contains(company))
            return 0;

        foreach (var stack in _stack.SpawnMultiple(creditEntity, reward.Credits, Transform(mob).Coordinates))
        {
            _hands.PickupOrDrop(mob, stack);
        }

        return reward.Credits;
    }

    /// <summary>What of an amount a player may still be paid within the hourly cap, and counts it. The window opens at the first payout.</summary>
    private int Capped(NetUserId user, int amount, int cap)
    {
        var now = _timing.CurTime;
        var (since, paid) = _paid.TryGetValue(user, out var entry) ? entry : (now, 0);
        if (now - since >= CapWindow)
        {
            since = now;
            paid = 0;
        }

        var allowed = Math.Clamp(cap - paid, 0, amount);
        _paid[user] = (since, paid + allowed);
        return allowed;
    }

    /// <summary>Gives back an allowance <see cref="Capped"/> counted for a payment that did not go through.</summary>
    private void Refund(NetUserId user, int amount)
    {
        if (_paid.TryGetValue(user, out var entry))
            _paid[user] = (entry.Since, Math.Max(0, entry.Paid - amount));
    }
}
