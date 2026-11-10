using System.Linq;
using Content.Server._WF.Remade.Conversion;
using Content.Server._WF.Remade.Monolith;
using Content.Server._WF.Wolfmed.Life;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Shared._WF.Remade;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Damage;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Humanoid;
using Content.Shared.IdentityManagement;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Server._WF.Remade.Gifts;

/// <summary>
/// The Flesh gift: heals everyone near the Scion and slowly raises the dead there. Anyone raised is marked, and rises as one of the
/// Remade the moment they die again. A body without a head, or one that has rotted, stays dead.
/// </summary>
public sealed partial class WFFleshSystem : EntitySystem
{
    [Dependency] private BloodstreamSystem _bloodstream = default!;
    [Dependency] private BodySystem _body = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private WFMonolithSystem _monolith = default!;
    [Dependency] private WFRemadeConversionSystem _conversion = default!;
    [Dependency] private WolfmedLifeSystem _life = default!;
    [Dependency] private WolfmedRevivalSystem _revival = default!;

    private static readonly TimeSpan TickRate = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LostAfter = TimeSpan.FromSeconds(3);
    private static readonly SoundSpecifier WritheSound = new SoundCollectionSpecifier("WFRemadeInfectorWork");
    private static readonly SoundSpecifier RiseSound = new SoundCollectionSpecifier("WFRemadeDividerPosession");

    private readonly HashSet<Entity<HumanoidAppearanceComponent>> _humanoids = new();
    private readonly List<EntityUid> _lost = new();
    private TimeSpan _nextTick;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFFleshMarkedComponent, MobStateChangedEvent>(OnMarkedState);
        SubscribeLocalEvent<WFFleshMarkedComponent, ExaminedEvent>(OnMarkedExamined);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _nextTick)
            return;

        _nextTick = now + TickRate;
        var query = EntityQueryEnumerator<WFMonolithComponent>();
        while (query.MoveNext(out var uid, out var monolith))
        {
            Tend((uid, monolith), now);
        }

        _lost.Clear();
        var reviving = EntityQueryEnumerator<WFFleshRevivingComponent>();
        while (reviving.MoveNext(out var uid, out var comp))
        {
            if (now - comp.LastSeen > LostAfter)
                _lost.Add(uid);
        }

        foreach (var uid in _lost)
        {
            RemCompDeferred<WFFleshRevivingComponent>(uid);
        }
    }

    private void Tend(Entity<WFMonolithComponent> scion, TimeSpan now)
    {
        var dial = _monolith.GetDial(scion, WFGift.Flesh);
        if (dial <= 0f)
            return;

        var resonance = scion.Comp.Resonance;
        var heal = WFMonolithMath.FleshHealRate(dial, resonance) * (float) TickRate.TotalSeconds;
        var reviveTime = WFMonolithMath.FleshReviveTime(dial, resonance);

        _humanoids.Clear();
        _lookup.GetEntitiesInRange(Transform(scion).Coordinates, WFMonolithMath.FleshReach(resonance), _humanoids);
        foreach (var body in _humanoids)
        {
            if (TerminatingOrDeleted(body))
                continue;

            if (!_mobState.IsDead(body))
            {
                Heal(body, heal);
                continue;
            }

            if (HasComp<WFFleshMarkedComponent>(body) || HasComp<WFRemadeConvertingComponent>(body) || !HasHead(body))
                continue;

            var reviving = EnsureComp<WFFleshRevivingComponent>(body);
            reviving.LastSeen = now;
            if (now < reviving.RetryAt)
                continue;

            reviving.Progress += (float) TickRate.TotalSeconds / reviveTime;
            if (reviving.Progress < 1f)
            {
                if (now >= reviving.NextWrithe)
                {
                    reviving.NextWrithe = now + TimeSpan.FromSeconds(4);
                    _audio.PlayPvs(WritheSound, body);
                }

                continue;
            }

            reviving.Progress = 0f;
            if (!TryRaise(scion, body))
                reviving.RetryAt = now + RetryAfter;
        }
    }

    private bool HasHead(EntityUid body)
    {
        return TryComp<BodyComponent>(body, out var comp) && _body.GetBodyChildrenOfType(body, BodyPartType.Head, comp).Any();
    }

    /// <summary>Heals up to the amount of damage a person has, spread over every kind of it.</summary>
    private void Heal(EntityUid body, float amount)
    {
        if (!TryComp<DamageableComponent>(body, out var damageable))
            return;

        var total = damageable.Damage.GetTotal();
        if (total <= FixedPoint2.Zero)
            return;

        var share = Math.Min(1f, amount / total.Float());
        var heal = new DamageSpecifier();
        foreach (var (type, value) in damageable.Damage.DamageDict)
        {
            if (value > FixedPoint2.Zero)
                heal.DamageDict[type] = -value * share;
        }

        _damageable.TryChangeDamage(body, heal, true, false, damageable, canSever: false, canEvade: false);
    }

    /// <summary>
    /// Brings a body back: heals it, fills its blood and restarts it through Wolfmed's own revival, which sets the brain
    /// and the post-shock course. False when Wolfmed refuses, such as a rotten body or one with no brain.
    /// </summary>
    public bool TryRaise(Entity<WFMonolithComponent> scion, EntityUid body)
    {
        if (!HasHead(body))
            return false;

        Heal(body, float.MaxValue);
        if (TryComp<BloodstreamComponent>(body, out var blood))
        {
            var volume = blood.BloodMaxVolume;
            var missing = (1f - _life.GetBlood(body)) * volume.Float();
            if (missing > 0f)
                _bloodstream.TryModifyBloodLevel(body, FixedPoint2.New(missing), blood);
        }

        if (_revival.OwnsRevival(body))
        {
            if (_revival.GetRefusal(body) != null)
                return false;

            _revival.Revive(body);
        }
        else
        {
            _mobState.ChangeMobState(body, MobState.Critical);
        }

        if (_mobState.IsDead(body))
            return false;

        var marked = EnsureComp<WFFleshMarkedComponent>(body);
        marked.Source = scion;
        _audio.PlayPvs(RiseSound, body);
        _popup.PopupEntity(Loc.GetString("wf-flesh-rise", ("target", Identity.Entity(body, EntityManager))), body, PopupType.LargeCaution);
        RemCompDeferred<WFFleshRevivingComponent>(body);
        return true;
    }

    private void OnMarkedState(Entity<WFFleshMarkedComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead)
            return;

        // A Scion that is gone takes its marks with it.
        if (ent.Comp.Source is not { } source || TerminatingOrDeleted(source) || !HasComp<WFMonolithComponent>(source))
        {
            RemCompDeferred<WFFleshMarkedComponent>(ent);
            return;
        }

        _conversion.TryStartConversion(ent, source, instant: true);
    }

    private void OnMarkedExamined(Entity<WFFleshMarkedComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("wf-flesh-marked-examine", ("target", Identity.Entity(ent, EntityManager))));
    }
}
