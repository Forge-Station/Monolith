using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.Armor;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._WF.ArmorSpecialization;

/// <summary>
/// The worn-armour specializations that answer Wolfmed: wound wards, limb locks and pain dampeners, plus the ablative
/// coat that soaks burns at the armour step.
/// </summary>
public sealed class WFArmorSpecializationSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFWoundWardComponent, InventoryRelayedEvent<WolfmedWoundRuleAttemptEvent>>(OnWoundRuleAttempt);
        SubscribeLocalEvent<WFWoundWardComponent, ExaminedEvent>(OnWardExamined);

        SubscribeLocalEvent<WFLimbLockComponent, InventoryRelayedEvent<WolfmedSeverAttemptEvent>>(OnSeverAttempt);
        SubscribeLocalEvent<WFLimbLockComponent, ExaminedEvent>(OnLimbLockExamined);

        SubscribeLocalEvent<WFPainDampenerComponent, InventoryRelayedEvent<WolfmedArmorPainReliefEvent>>(OnPainRelief);
        SubscribeLocalEvent<WFPainDampenerComponent, ExaminedEvent>(OnDampenerExamined);

        SubscribeLocalEvent<WFAblativeCoatComponent, InventoryRelayedEvent<DamageModifyEvent>>(OnCoatDamageModify);
        SubscribeLocalEvent<WFAblativeCoatComponent, InventoryRelayedEvent<PartDamageModifyEvent>>(OnCoatPartDamageModify,
            before: [typeof(WolfmedPartArmorSystem)]);
        SubscribeLocalEvent<WFAblativeCoatComponent, ExaminedEvent>(OnCoatExamined);
    }

    private void OnWoundRuleAttempt(Entity<WFWoundWardComponent> ent, ref InventoryRelayedEvent<WolfmedWoundRuleAttemptEvent> args)
    {
        if (!WolfmedArmorCoverage.Covers(EntityManager, ent.Owner, args.Args.Part))
            return;

        if (ent.Comp.Blocks.Contains(args.Args.Wound))
            args.Args.Cancelled = true;
        else if (ent.Comp.Hardens.TryGetValue(args.Args.Wound, out var multiplier))
            args.Args.MinDamageMultiplier = MathF.Max(args.Args.MinDamageMultiplier, multiplier);
    }

    private void OnSeverAttempt(Entity<WFLimbLockComponent> ent, ref InventoryRelayedEvent<WolfmedSeverAttemptEvent> args)
    {
        if (!WolfmedArmorCoverage.Covers(EntityManager, ent.Owner, args.Args.Part))
            return;

        if (ent.Comp.Breakthrough <= FixedPoint2.Zero || args.Args.HitDamage < ent.Comp.Breakthrough)
            args.Args.Cancelled = true;
    }

    private void OnPainRelief(Entity<WFPainDampenerComponent> ent, ref InventoryRelayedEvent<WolfmedArmorPainReliefEvent> args)
    {
        args.Args.Relief += ent.Comp.Relief;
    }

    private void OnCoatDamageModify(Entity<WFAblativeCoatComponent> ent, ref InventoryRelayedEvent<DamageModifyEvent> args)
    {
        // A wound host's burns are localized and armoured per part; they are soaked in OnCoatPartDamageModify instead.
        if (_inventory.TryGetContainingEntity(ent.Owner, out var wearer) && HasComp<WoundHostComponent>(wearer))
            return;

        args.Args.Damage = Soak(ent, args.Args.Damage);
    }

    private void OnCoatPartDamageModify(Entity<WFAblativeCoatComponent> ent, ref InventoryRelayedEvent<PartDamageModifyEvent> args)
    {
        if (WolfmedArmorCoverage.Covers(EntityManager, ent.Owner, args.Args.Part))
            args.Args.Damage = Soak(ent, args.Args.Damage);
    }

    /// <summary>Soaks the coat's share of its damage group from its charge and returns the rest.</summary>
    private DamageSpecifier Soak(Entity<WFAblativeCoatComponent> ent, DamageSpecifier damage)
    {
        if (ent.Comp.Charge <= 0f || !_prototypes.TryIndex(ent.Comp.DamageGroup, out DamageGroupPrototype? group))
            return damage;

        var requested = FixedPoint2.Zero;
        foreach (var type in group.DamageTypes)
        {
            if (damage.DamageDict.TryGetValue(type, out var amount) && amount > 0)
                requested += amount * ent.Comp.Reduction;
        }

        if (requested <= 0)
            return damage;

        var share = requested.Float() > ent.Comp.Charge
            ? ent.Comp.Reduction * ent.Comp.Charge / requested.Float()
            : ent.Comp.Reduction;

        var result = new DamageSpecifier(damage);
        var absorbed = FixedPoint2.Zero;
        foreach (var type in group.DamageTypes)
        {
            if (!result.DamageDict.TryGetValue(type, out var amount) || amount <= 0)
                continue;

            var soaked = amount * share;
            result.DamageDict[type] = amount - soaked;
            absorbed += soaked;
        }

        ent.Comp.Charge = MathF.Max(ent.Comp.Charge - absorbed.Float(), 0f);
        ent.Comp.LastHit = _timing.CurTime;
        Dirty(ent);
        return result;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_net.IsServer)
            return;

        // Once a second, so a recovering coat isn't sent to clients every tick.
        var now = _timing.CurTime;
        if (now < _nextCoatRecovery)
            return;
        _nextCoatRecovery = now + CoatRecoveryInterval;

        var query = EntityQueryEnumerator<WFAblativeCoatComponent>();
        while (query.MoveNext(out var uid, out var coat))
        {
            if (coat.Charge >= coat.MaxCharge || now < coat.LastHit + coat.RecoveryDelay)
                continue;

            coat.Charge = MathF.Min(coat.Charge + coat.RegenRate * (float) CoatRecoveryInterval.TotalSeconds, coat.MaxCharge);
            Dirty(uid, coat);
        }
    }

    private static readonly TimeSpan CoatRecoveryInterval = TimeSpan.FromSeconds(1);
    private TimeSpan _nextCoatRecovery;

    private void OnWardExamined(Entity<WFWoundWardComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.Examine is { } examine)
            args.PushMarkup(Loc.GetString(examine));
    }

    private void OnLimbLockExamined(Entity<WFLimbLockComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(ent.Comp.Breakthrough > FixedPoint2.Zero
            ? Loc.GetString("wf-armor-spec-limb-lock-examine", ("breakthrough", ent.Comp.Breakthrough.Int()))
            : Loc.GetString("wf-armor-spec-limb-lock-total-examine"));
    }

    private void OnDampenerExamined(Entity<WFPainDampenerComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("wf-armor-spec-pain-dampener-examine"));
    }

    private void OnCoatExamined(Entity<WFAblativeCoatComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("wf-armor-spec-ablative-examine",
            ("percent", (int) MathF.Round(ent.Comp.Charge / MathF.Max(ent.Comp.MaxCharge, 1f) * 100f))));
    }
}
