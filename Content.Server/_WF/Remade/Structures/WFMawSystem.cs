using System.Linq;
using Content.Server._WF.Remade.Conversion;
using Content.Server._WF.Remade.Director;
using Content.Shared._WF.Remade.Structures;
using Content.Shared._WF.Wolfmed.Compat;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.DoAfter;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Remade.Structures;

/// <summary>
/// A maw. Anyone who stands on it is warned, and three seconds later, if still there, is dragged in and held. It bites them,
/// takes a limb every few bites and eats it, and eats the dead that lie on it. Crew free someone by clicking them and pulling for
/// a few seconds, or by killing the maw; the held can wrench themselves out, slowly. It pays the Director biomass for what it eats.
/// </summary>
public sealed partial class WFMawSystem : EntitySystem
{
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedBuckleSystem _buckle = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private WFGrowthSystem _growth = default!;
    [Dependency] private WolfmedBodySystem _wolfmedBody = default!;

    private static readonly ProtoId<DamageTypePrototype> Slash = "Slash";
    private static readonly TimeSpan ScanRate = TimeSpan.FromSeconds(0.25);

    /// <summary>Metres from the maw's middle within which a mob counts as standing on it.</summary>
    private const float StandRadius = 0.55f;

    private readonly HashSet<Entity<MobStateComponent>> _near = new();

    private TimeSpan _nextScan;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFMawComponent, StrappedEvent>(OnStrapped);
        SubscribeLocalEvent<WFMawComponent, UnstrappedEvent>(OnUnstrapped);
        SubscribeLocalEvent<WFMawComponent, UnstrapAttemptEvent>(OnUnstrapAttempt);
        SubscribeLocalEvent<WFMawHeldComponent, WFMawFreeDoAfterEvent>(OnFreed);
    }

    private void OnStrapped(Entity<WFMawComponent> ent, ref StrappedEvent args)
    {
        var held = EnsureComp<WFMawHeldComponent>(args.Buckle);
        held.Maw = ent;
        ent.Comp.Pending.Remove(args.Buckle);
        ent.Comp.NextBite = _timing.CurTime + ent.Comp.BiteEvery;
        if (TryComp<WFGrowthComponent>(ent, out var growth))
            _growth.SetState((ent, growth), WFGrowthState.Active);
    }

    private void OnUnstrapped(Entity<WFMawComponent> ent, ref UnstrappedEvent args)
    {
        RemComp<WFMawHeldComponent>(args.Buckle);
        if (ent.Comp.Eating.Count == 0
            && args.Strap.Comp.BuckledEntities.Count == 0
            && TryComp<WFGrowthComponent>(ent, out var growth))
        {
            _growth.SetState((ent, growth), WFGrowthState.Idle);
        }
    }

    /// <summary>Nobody just clicks free of a maw: the click starts a pull instead.</summary>
    private void OnUnstrapAttempt(Entity<WFMawComponent> ent, ref UnstrapAttemptEvent args)
    {
        if (!TryComp<WFMawHeldComponent>(args.Buckle, out var held) || held.Releasing)
            return;

        args.Cancelled = true;
        if (args.Popup && args.User is { } user && !held.Freeing)
            StartFree(ent, (args.Buckle.Owner, held), user);
    }

    private void StartFree(Entity<WFMawComponent> maw, Entity<WFMawHeldComponent> held, EntityUid user)
    {
        var self = user == held.Owner;
        var time = self ? maw.Comp.StruggleTime : maw.Comp.PullTime;
        var args = new DoAfterArgs(EntityManager, user, (float) time.TotalSeconds, new WFMawFreeDoAfterEvent(), held.Owner, held.Owner)
        {
            BreakOnMove = !self,
            BreakOnDamage = !self,
            NeedHand = !self,
            BlockDuplicate = true,
        };

        if (!_doAfter.TryStartDoAfter(args))
            return;

        held.Comp.Freeing = true;
        var message = Loc.GetString(self ? "wf-remade-maw-struggle" : "wf-remade-maw-pull", ("target", held.Owner), ("user", user));
        _popup.PopupEntity(message, held, user, PopupType.Medium);
    }

    private void OnFreed(Entity<WFMawHeldComponent> ent, ref WFMawFreeDoAfterEvent args)
    {
        ent.Comp.Freeing = false;
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;
        if (TryComp<WFMawComponent>(ent.Comp.Maw, out var maw))
            maw.Immune[ent] = _timing.CurTime + maw.Mercy;

        Release(ent);
        _popup.PopupEntity(Loc.GetString("wf-remade-maw-freed", ("target", ent.Owner)), ent, PopupType.Medium);
    }

    private void Release(Entity<WFMawHeldComponent> held)
    {
        held.Comp.Releasing = true;
        _buckle.TryUnbuckle(held.Owner, null, popup: false);
        held.Comp.Releasing = false;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _nextScan)
            return;

        _nextScan = now + ScanRate;
        var query = EntityQueryEnumerator<WFMawComponent, StrapComponent, WFGrowthComponent>();
        while (query.MoveNext(out var uid, out var maw, out var strap, out var growth))
        {
            if (!growth.Active)
            {
                LetGo((uid, maw, strap));
                continue;
            }

            Scan((uid, maw, strap, growth), now);
        }
    }

    /// <summary>A maw that has gone dormant lets everything go.</summary>
    private void LetGo(Entity<WFMawComponent, StrapComponent> ent)
    {
        ent.Comp1.Pending.Clear();
        ent.Comp1.Eating.Clear();
        foreach (var victim in ent.Comp2.BuckledEntities.ToArray())
        {
            if (TryComp<WFMawHeldComponent>(victim, out var held))
                Release((victim, held));
        }
    }

    private void Scan(Entity<WFMawComponent, StrapComponent, WFGrowthComponent> ent, TimeSpan now)
    {
        var maw = ent.Comp1;

        // Whoever is on it now. Physics contacts miss a body that is moved off, so look.
        _near.Clear();
        _lookup.GetEntitiesInRange(Transform(ent).Coordinates, StandRadius, _near);
        maw.Touching.Clear();
        maw.Eating.Clear();
        foreach (var near in _near)
        {
            maw.Touching.Add(near);
        }

        foreach (var mob in maw.Touching)
        {
            if (_mobState.IsDead(mob))
            {
                if (!HasComp<WFMawHeldComponent>(mob))
                    maw.Eating.Add(mob);

                continue;
            }

            if (HasComp<WFRemadeComponent>(mob) || HasComp<WFMawHeldComponent>(mob) || !HasComp<BuckleComponent>(mob))
                continue;

            if (maw.Immune.TryGetValue(mob, out var until))
            {
                if (now < until)
                    continue;

                maw.Immune.Remove(mob);
            }

            if (!maw.Pending.ContainsKey(mob))
                Warn(ent, mob, now);
        }

        foreach (var (mob, at) in maw.Pending.ToArray())
        {
            if (!maw.Touching.Contains(mob) || TerminatingOrDeleted(mob) || _mobState.IsDead(mob))
            {
                maw.Pending.Remove(mob);
                continue;
            }

            if (now >= at)
                Grab(ent, mob);
        }

        if (now >= maw.NextBite)
        {
            maw.NextBite = now + maw.BiteEvery;
            Feed(ent);
        }
    }

    private void Warn(Entity<WFMawComponent, StrapComponent, WFGrowthComponent> ent, EntityUid mob, TimeSpan now)
    {
        ent.Comp1.Pending[mob] = now + ent.Comp1.Warning;
        _audio.PlayPvs(ent.Comp1.WarnSound, ent);
        _popup.PopupEntity(Loc.GetString("wf-remade-maw-warn-self"), mob, mob, PopupType.LargeCaution);
        _popup.PopupEntity(Loc.GetString("wf-remade-maw-warn-other", ("target", mob)), ent.Owner, Filter.PvsExcept(mob), true, PopupType.MediumCaution);
    }

    private void Grab(Entity<WFMawComponent, StrapComponent, WFGrowthComponent> ent, EntityUid mob)
    {
        ent.Comp1.Pending.Remove(mob);
        if (!_buckle.TryBuckle(mob, null, ent.Owner, popup: false))
            return;

        ent.Comp1.Grabs++;
        _audio.PlayPvs(ent.Comp1.GrabSound, ent);
        _popup.PopupEntity(Loc.GetString("wf-remade-maw-grab", ("target", mob)), mob, PopupType.LargeCaution);
    }

    /// <summary>One bite for everyone it holds and a mouthful for each body lying on it.</summary>
    private void Feed(Entity<WFMawComponent, StrapComponent, WFGrowthComponent> ent)
    {
        var maw = ent.Comp1;
        foreach (var victim in ent.Comp2.BuckledEntities.ToArray())
        {
            if (_mobState.IsDead(victim))
                Eat(ent, victim);
            else
                Bite(ent, victim);
        }

        foreach (var body in maw.Eating.ToArray())
        {
            if (TerminatingOrDeleted(body))
                maw.Eating.Remove(body);
            else
                Eat(ent, body);
        }
    }

    private void Bite(Entity<WFMawComponent, StrapComponent, WFGrowthComponent> ent, EntityUid victim)
    {
        var maw = ent.Comp1;
        maw.Bites++;
        _damageable.TryChangeDamage(victim, new DamageSpecifier(_proto.Index(Slash), maw.BiteDamage), origin: ent.Owner, canSever: false);
        _audio.PlayPvs(maw.BiteSound, victim);
        Pay(ent, maw.BiteBiomass);

        if (maw.LimbEvery > 0 && maw.Bites % maw.LimbEvery == 0 && TakeLimb(ent, victim))
            _popup.PopupEntity(Loc.GetString("wf-remade-maw-limb", ("target", victim)), victim, PopupType.LargeCaution);
        else
            _popup.PopupEntity(Loc.GetString("wf-remade-maw-bite", ("target", victim)), victim, victim, PopupType.MediumCaution);
    }

    /// <summary>Tears an arm or a leg off a body and eats it. False if there are none left.</summary>
    private bool TakeLimb(Entity<WFMawComponent, StrapComponent, WFGrowthComponent> ent, EntityUid victim)
    {
        if (!TryComp<BodyComponent>(victim, out var body))
            return false;

        var limbs = _body.GetBodyChildrenOfType(victim, BodyPartType.Arm, body)
            .Concat(_body.GetBodyChildrenOfType(victim, BodyPartType.Leg, body))
            .ToList();

        if (limbs.Count == 0)
            return false;

        var limb = _random.Pick(limbs).Id;
        if (!_wolfmedBody.TryDetachPart(limb))
            return false;

        QueueDel(limb);
        _audio.PlayPvs(ent.Comp1.EatSound, victim);
        Pay(ent, ent.Comp1.LimbBiomass);
        return true;
    }

    /// <summary>Eats a dead body a piece at a time, and the Remade whole.</summary>
    private void Eat(Entity<WFMawComponent, StrapComponent, WFGrowthComponent> ent, EntityUid body)
    {
        var maw = ent.Comp1;
        if (HasComp<WFRemadeComponent>(body))
        {
            Pay(ent, maw.RemadeBiomass);
        }
        else if (TakeLimb(ent, body))
        {
            return;
        }
        else
        {
            Pay(ent, maw.CorpseBiomass);
        }

        _audio.PlayPvs(maw.EatSound, ent);
        maw.Eating.Remove(body);
        QueueDel(body);
    }

    private void Pay(Entity<WFMawComponent, StrapComponent, WFGrowthComponent> ent, float biomass)
    {
        if (TryComp<WFRemadeDirectorComponent>(ent.Comp3.Source, out var director))
            director.Biomass += biomass;
    }
}
