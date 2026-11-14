using System.Linq;
using Content.Server._WF.Wolfmed.Stasis;
using Content.Server._WF.Wolfmed.Wounds;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Starlight.Actions.Stasis;
using Content.Shared._WF.ArmorSpecialization;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared._WF.Wolfmed.Compat;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Server._WF.ArmorSpecialization;

/// <summary>
/// The civilian suit passives that work over time or need the server: the emergency stasis weave, the rad sink's
/// shedding, living tissue and the clown's honk.
/// </summary>
public sealed class WFSuitUpkeepSystem : EntitySystem
{
    private const string Radiation = "Radiation";
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private WolfmedDamageableSystem _wfDamage = default!;
    [Dependency] private WFArmorSpecializationSystem _specs = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private WolfmedInfectionSystem _infection = default!;
    [Dependency] private WolfmedStasisSystem _stasis = default!;
    [Dependency] private WolfmedWoundTraitSystem _traits = default!;
    [Dependency] private WoundSystem _wounds = default!;

    private TimeSpan _nextTick;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFStasisWeaveComponent, ExaminedEvent>(OnStasisWeaveExamined);
        SubscribeLocalEvent<WFLivingTissueComponent, ExaminedEvent>(OnLivingTissueExamined);
        SubscribeLocalEvent<WFHonkOnHitComponent, InventoryRelayedEvent<DamageModifyEvent>>(OnHonkDamage);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _nextTick)
            return;
        _nextTick = now + TickInterval;
        var seconds = (float) TickInterval.TotalSeconds;

        var weaves = EntityQueryEnumerator<WFStasisWeaveComponent>();
        while (weaves.MoveNext(out var uid, out var weave))
        {
            if (_specs.GetWearer(uid) is { } wearer)
                TryFireWeave((uid, weave), wearer, now);
        }

        // Lifted here rather than in WolfmedStasisSystem, which only walks Avali.
        var holds = EntityQueryEnumerator<WolfmedStasisHoldComponent>();
        var lifted = new List<EntityUid>();
        while (holds.MoveNext(out var uid, out var hold))
        {
            if (hold.HeldUntil is { } until && now >= until)
                lifted.Add(uid);
        }

        foreach (var body in lifted)
            LiftWeave(body);

        var sinks = EntityQueryEnumerator<WFRadSinkComponent>();
        while (sinks.MoveNext(out var uid, out var sink))
        {
            if (_specs.GetWearer(uid) is { } wearer)
                ShedRadiation(wearer, sink.ShedRate * seconds);
        }

        var tissues = EntityQueryEnumerator<WFLivingTissueComponent>();
        while (tissues.MoveNext(out var uid, out var tissue))
        {
            if (_specs.GetWearer(uid) is { } wearer)
                MendSmallBleeds(uid, wearer, FixedPoint2.New(tissue.MendRate * seconds));
        }
    }

    #region Stasis weave

    /// <summary>
    /// A charged weave on a critical wearer holds every bleed for its duration. Returns whether it fired. Public so a
    /// test can skip the clock.
    /// </summary>
    public bool TryFireWeave(Entity<WFStasisWeaveComponent> ent, EntityUid wearer, TimeSpan now)
    {
        if (now < ent.Comp.NextReady || !_mobState.IsCritical(wearer) || !HasComp<WoundHostComponent>(wearer) ||
            HasComp<WolfmedStasisHoldComponent>(wearer))
            return false;

        var hold = AddComp<WolfmedStasisHoldComponent>(wearer);
        hold.HeldUntil = now + ent.Comp.Duration;
        _stasis.RefreshBleeding(wearer);

        ent.Comp.NextReady = now + ent.Comp.Recharge;
        Dirty(ent);
        _popup.PopupEntity(Loc.GetString("wf-armor-spec-stasis-weave-fired"), wearer, PopupType.MediumCaution);
        return true;
    }

    /// <summary>Ends a weave's hold; an Avali in their own stasis keeps theirs.</summary>
    private void LiftWeave(EntityUid body)
    {
        if (!TryComp(body, out WolfmedStasisHoldComponent? hold))
            return;

        if (TryComp(body, out StasisComponent? stasis) && stasis.IsInStasis)
        {
            hold.HeldUntil = null;
            return;
        }

        RemComp<WolfmedStasisHoldComponent>(body);
        _stasis.RefreshBleeding(body);
    }

    private void OnStasisWeaveExamined(Entity<WFStasisWeaveComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString(_timing.CurTime >= ent.Comp.NextReady
            ? "wf-armor-spec-stasis-weave-examine-ready"
            : "wf-armor-spec-stasis-weave-examine-charging"));
    }

    #endregion

    private void ShedRadiation(EntityUid wearer, float amount)
    {
        if (amount <= 0f || !TryComp(wearer, out DamageableComponent? damageable) ||
            _wfDamage.GetAllDamage((wearer, damageable)).DamageDict.GetValueOrDefault(Radiation) <= FixedPoint2.Zero)
            return;

        var heal = new DamageSpecifier { DamageDict = { [Radiation] = FixedPoint2.New(-amount) } };
        _damageable.TryChangeDamage(wearer, heal, ignoreResistances: true, interruptsDoAfters: false,
            damageable: damageable);
    }

    #region Living tissue

    /// <summary>Small, ordinary bleeds on covered parts close a little each second. Arteries are left alone.</summary>
    private void MendSmallBleeds(EntityUid suit, EntityUid wearer, FixedPoint2 amount)
    {
        if (amount <= FixedPoint2.Zero || !HasComp<WoundHostComponent>(wearer))
            return;

        foreach (var (part, _) in _body.GetBodyChildren(wearer).ToList())
        {
            if (!WolfmedArmorCoverage.Covers(EntityManager, suit, part))
                continue;

            foreach (var wound in _wounds.GetWounds(part).ToArray())
            {
                if (wound.Comp.State is WoundState.Healed or WoundState.Scarred ||
                    !TryComp(wound, out WoundBleedingComponent? bleeding) || bleeding.BleedingSeverity <= FixedPoint2.Zero ||
                    !_infection.IsMinor(wound.Comp) ||
                    _traits.TryGetBehavior(wound.AsNullable(), out WolfmedArterialBleedBehavior _))
                    continue;

                _wounds.ChangeSeverity(wound.AsNullable(), -amount);
            }
        }
    }

    private void OnLivingTissueExamined(Entity<WFLivingTissueComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("wf-armor-spec-living-tissue-examine"));
    }

    #endregion

    private void OnHonkDamage(Entity<WFHonkOnHitComponent> ent, ref InventoryRelayedEvent<DamageModifyEvent> args)
    {
        var now = _timing.CurTime;
        if (now < ent.Comp.NextHonk || args.Args.Origin == null ||
            args.Args.Damage.DamageDict.GetValueOrDefault("Blunt") <= FixedPoint2.Zero ||
            _specs.GetWearer(ent.Owner) is not { } wearer)
            return;

        ent.Comp.NextHonk = now + ent.Comp.Cooldown;
        _audio.PlayPvs(ent.Comp.Sound, wearer);
    }
}
