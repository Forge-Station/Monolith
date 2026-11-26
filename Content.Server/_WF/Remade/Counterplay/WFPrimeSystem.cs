using System.Linq;
using Content.Server._NF.BountyContracts;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server._WF.Remade.Director;
using Content.Server._WF.Remade.Monolith;
using Content.Server.Chat.Managers;
using Content.Server.Chat.Systems;
using Content.Shared._NF.BountyContracts;
using Content.Shared.Damage;
using Content.Shared.Destructible;
using Content.Shared.Ghost;
using Content.Shared.Mobs.Systems;
using Robust.Server.Player;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.Remade.Counterplay;

/// <summary>
/// The end of an outbreak. A Monolith that roots at the Crescendo becomes the Prime: it gets armour, a sector bounty and a verse
/// every ten minutes while it stands. When any Monolith is destroyed it leaves a husk and its Remade go feral, and a Prime pays
/// its bounty to the players who brought it down.
/// </summary>
public sealed class WFPrimeSystem : EntitySystem
{
    [Dependency] private BountyContractSystem _board = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private IChatManager _chatManager = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFCrewSystem _crew = default!;
    [Dependency] private WFEncounterBountySystem _payout = default!;
    [Dependency] private WFFeralSystem _feral = default!;

    private static readonly EntProtoId Husk = "WFMonolithHusk";
    private static readonly EntProtoId PrimeHusk = "WFMonolithPrimeHusk";
    private static readonly TimeSpan TickRate = TimeSpan.FromSeconds(1);
    private static readonly Color AnnounceColor = Color.FromHex("#EC3232");

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFMonolithComponent, WFMonolithRootedEvent>(OnRooted);
        SubscribeLocalEvent<WFMonolithComponent, DestructionEventArgs>(OnDestroyed);
        SubscribeLocalEvent<WFPrimeComponent, DamageChangedEvent>(OnDamaged);
    }

    private void OnRooted(Entity<WFMonolithComponent> ent, ref WFMonolithRootedEvent args)
    {
        var now = _timing.CurTime;
        var prime = EnsureComp<WFPrimeComponent>(ent);
        prime.RootedAt = now;
        prime.LastHit = now;
        prime.NextVerse = now + prime.VerseEvery;
        prime.Reward = prime.BaseReward;
        _damageable.SetDamageModifierSetId(ent, prime.Armour);

        var grid = Transform(ent).GridUid;
        var place = grid != null ? MetaData(grid.Value).EntityName : Loc.GetString("wf-prime-bounty-place-unknown");
        var contract = _board.TryPostSystemContract(
            BountyContractSystem.WarrantCollection,
            BountyContractCategory.Criminal,
            Loc.GetString("wf-prime-bounty-name"),
            prime.Reward,
            Loc.GetString("wf-prime-bounty-description", ("place", place)),
            place,
            Loc.GetString("wf-prime-bounty-author"));
        prime.Contract = contract?.ContractId;
    }

    /// <summary>Counts what each player deals the Prime, so the bounty can go to whoever wore it down.</summary>
    private void OnDamaged(Entity<WFPrimeComponent> ent, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased || args.DamageDelta is not { } delta)
            return;

        ent.Comp.LastHit = _timing.CurTime;
        if (args.Origin is not { } origin)
            return;

        var attacker = _crew.Wielder(origin);
        if (TerminatingOrDeleted(attacker) || !TryComp<ActorComponent>(attacker, out var actor))
            return;

        var user = actor.PlayerSession.UserId;
        ent.Comp.Hits[user] = ent.Comp.Hits.GetValueOrDefault(user) + delta.GetTotal().Float();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WFPrimeComponent, WFMonolithComponent>();
        while (query.MoveNext(out var uid, out var prime, out var monolith))
        {
            if (now < prime.NextTick)
                continue;

            prime.NextTick = now + TickRate;
            UpdateReward((uid, prime), now);
            Mend((uid, prime), now);

            if (now >= prime.NextVerse)
                Verse((uid, prime, monolith), now);
        }
    }

    /// <summary>The bounty grows the longer the Prime stands, and the board entry follows.</summary>
    private void UpdateReward(Entity<WFPrimeComponent> ent, TimeSpan now)
    {
        var prime = ent.Comp;
        var minutes = (int) (now - prime.RootedAt).TotalMinutes;
        var reward = (int) Math.Min((long) prime.MaxReward, prime.BaseReward + (long) minutes * prime.RewardPerMinute);
        if (reward == prime.Reward)
            return;

        prime.Reward = reward;
        if (prime.Contract is { } id)
            _board.TrySetContractReward(id, reward);
    }

    /// <summary>Left alone, the Prime heals a little each second.</summary>
    private void Mend(Entity<WFPrimeComponent> ent, TimeSpan now)
    {
        if (now - ent.Comp.LastHit < ent.Comp.MendAfter
            || !TryComp<DamageableComponent>(ent, out var damageable)
            || damageable.TotalDamage <= 0)
        {
            return;
        }

        var heal = new DamageSpecifier(damageable.Damage) * (-ent.Comp.MendPerSecond / damageable.TotalDamage.Float());
        _damageable.TryChangeDamage(ent, heal, ignoreResistances: true, interruptsDoAfters: false);
    }

    /// <summary>An unchallenged Prime sings again: the sector is told and the Director is handed more to spend.</summary>
    private void Verse(Entity<WFPrimeComponent, WFMonolithComponent> ent, TimeSpan now)
    {
        var prime = ent.Comp1;
        prime.Verses++;
        prime.NextVerse = now + prime.VerseEvery;

        if (TryComp<WFRemadeDirectorComponent>(ent, out var director))
            director.Biomass += prime.VerseBiomass;

        _chat.DispatchGlobalAnnouncement(
            Loc.GetString("wf-prime-verse-announcement", ("verse", prime.Verses)),
            Loc.GetString("wf-monolith-crescendo-sender"),
            playSound: true,
            announcementSound: ent.Comp2.CrescendoSound,
            colorOverride: AnnounceColor);
    }

    /// <summary>A destroyed Monolith leaves a husk and frees its Remade. A Prime also pays out and is announced.</summary>
    private void OnDestroyed(Entity<WFMonolithComponent> ent, ref DestructionEventArgs args)
    {
        var prime = CompOrNull<WFPrimeComponent>(ent);
        var coordinates = Transform(ent).Coordinates;
        Spawn(prime != null ? PrimeHusk : Husk, coordinates);
        var freed = _feral.Release(ent);

        if (prime == null)
            return;

        if (prime.Contract is { } id)
            _board.TryRemoveSystemContract(id);

        Pay(ent, prime);
        _chat.DispatchGlobalAnnouncement(
            Loc.GetString("wf-prime-fallen-announcement", ("freed", freed)),
            Loc.GetString("wf-monolith-crescendo-sender"),
            playSound: false,
            colorOverride: AnnounceColor);
    }

    /// <summary>
    /// Splits a pool by what each player dealt. The pool is rounded down, with the odd spesos going to whoever dealt most.
    /// </summary>
    public static Dictionary<NetUserId, int> Split(int pool, Dictionary<NetUserId, float> hits)
    {
        var shares = new Dictionary<NetUserId, int>();
        var total = hits.Values.Sum();
        if (pool <= 0 || total <= 0f)
            return shares;

        var paid = 0;
        NetUserId? top = null;
        foreach (var (user, dealt) in hits)
        {
            var share = (int) (pool * (double) (dealt / total));
            shares[user] = share;
            paid += share;
            if (top == null || dealt > hits[top.Value])
                top = user;
        }

        if (top is { } best)
            shares[best] += pool - paid;

        return shares;
    }

    /// <summary>Pays the bounty by damage dealt. What can't be paid, or all of it if no blow was traced, goes to the living nearby.</summary>
    private void Pay(Entity<WFMonolithComponent> ent, WFPrimeComponent prime)
    {
        var owed = 0;
        foreach (var (user, amount) in Split(prime.Reward, prime.Hits))
        {
            if (_players.TryGetSessionById(user, out var session) && _payout.Payable(session) && _payout.Deposit(session, amount))
                Tell(session, amount);
            else
                owed += amount;
        }

        if (prime.Hits.Count == 0)
            owed = prime.Reward;

        if (owed <= 0)
            return;

        var near = Nearby(ent, prime.PayRange);
        if (near.Count == 0)
        {
            Log.Warning($"The Prime's bounty of {owed} had nobody to go to.");
            return;
        }

        var share = owed / near.Count;
        foreach (var session in near)
        {
            if (share > 0 && _payout.Deposit(session, share))
                Tell(session, share);
        }
    }

    private List<ICommonSession> Nearby(EntityUid prime, float range)
    {
        var found = new List<ICommonSession>();
        var origin = _transform.GetMapCoordinates(prime);
        var players = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (players.MoveNext(out var uid, out var actor, out var xform))
        {
            if (HasComp<GhostComponent>(uid) || _mobState.IsDead(uid) || xform.MapID != origin.MapId
                || (_transform.GetWorldPosition(xform) - origin.Position).LengthSquared() > range * range
                || !_payout.Payable(actor.PlayerSession))
            {
                continue;
            }

            found.Add(actor.PlayerSession);
        }

        return found;
    }

    private void Tell(ICommonSession session, int amount)
    {
        _chatManager.DispatchServerMessage(session, Loc.GetString("wf-prime-bounty-paid", ("amount", amount)));
    }
}
