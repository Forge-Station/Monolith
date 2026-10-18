using Content.Shared._Mono.PersonalShield;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.Armor;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Armor;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._WF.SoftShield;

/// <summary>
/// Soft shields: dampens the shield's damage group at the armour step, so a hit lands once and burns reach the
/// suit's armour untouched, and refuses bleeds while the field is up.
/// </summary>
public sealed class WFSoftShieldSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedPersonalShieldSystem _shield = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFSoftShieldComponent, InventoryRelayedEvent<DamageModifyEvent>>(OnDamageModify,
            before: [typeof(SharedArmorSystem)]);
        SubscribeLocalEvent<WFSoftShieldComponent, InventoryRelayedEvent<PartDamageModifyEvent>>(OnPartDamageModify,
            before: [typeof(WolfmedPartArmorSystem)]);
        SubscribeLocalEvent<WFSoftShieldComponent, InventoryRelayedEvent<WolfmedBleedAttemptEvent>>(OnBleedAttempt);
        SubscribeLocalEvent<WFSoftShieldComponent, ExaminedEvent>(OnExamined);
    }

    private void OnDamageModify(Entity<WFSoftShieldComponent> ent, ref InventoryRelayedEvent<DamageModifyEvent> args)
    {
        if (!TryGetActiveShield(ent, out var shield))
            return;

        if (args.Args.Tool is { } tool && HasComp<PersonalShieldBreakerComponent>(tool))
        {
            _shield.Fracture((ent, shield));
            return;
        }

        // A wound host's brute is localized and armoured per part; it is dampened in OnPartDamageModify instead.
        if (_inventory.TryGetContainingEntity(ent.Owner, out var wearer) && HasComp<WoundHostComponent>(wearer))
            return;

        args.Args.Damage = Dampen(ent, shield, args.Args.Damage, args.Args.Origin != null);
    }

    private void OnPartDamageModify(Entity<WFSoftShieldComponent> ent, ref InventoryRelayedEvent<PartDamageModifyEvent> args)
    {
        if (TryGetActiveShield(ent, out var shield))
            args.Args.Damage = Dampen(ent, shield, args.Args.Damage, true);
    }

    private void OnBleedAttempt(Entity<WFSoftShieldComponent> ent, ref InventoryRelayedEvent<WolfmedBleedAttemptEvent> args)
    {
        if (TryGetActiveShield(ent, out _))
            args.Args.Cancelled = true;
    }

    private void OnExamined(Entity<WFSoftShieldComponent> ent, ref ExaminedEvent args)
    {
        var group = _prototypes.Index(ent.Comp.DamageGroup);
        args.PushMarkup(Loc.GetString("wf-soft-shield-examine",
            ("percent", (int) MathF.Round(ent.Comp.Reduction * 100f)),
            ("group", group.LocalizedName)));
    }

    /// <summary>Soaks the component's share of its damage group from the shield's charge and returns the rest.</summary>
    private DamageSpecifier Dampen(Entity<WFSoftShieldComponent> ent, PersonalShieldComponent shield,
        DamageSpecifier damage, bool flare)
    {
        var group = _prototypes.Index(ent.Comp.DamageGroup);
        var result = new DamageSpecifier(damage);
        var requested = FixedPoint2.Zero;
        foreach (var type in group.DamageTypes)
        {
            if (result.DamageDict.TryGetValue(type, out var amount) && amount > 0)
                requested += amount * ent.Comp.Reduction;
        }

        // A field with less charge left than the hit asks for soaks only what it has.
        var share = requested.Float() > shield.Runtime.Charge
            ? ent.Comp.Reduction * shield.Runtime.Charge / requested.Float()
            : ent.Comp.Reduction;

        var absorbed = FixedPoint2.Zero;
        foreach (var type in group.DamageTypes)
        {
            if (!result.DamageDict.TryGetValue(type, out var amount) || amount <= 0)
                continue;

            var soaked = amount * share;
            result.DamageDict[type] = amount - soaked;
            absorbed += soaked;
        }

        if (absorbed <= 0)
            return damage;

        shield.Runtime.Charge = MathF.Max(shield.Runtime.Charge - absorbed.Float(), 0f);
        if (shield.Runtime.Charge <= 0f)
        {
            _shield.Fracture((ent, shield));
            return result;
        }

        if (flare && shield.Visuals.DamageFlareTime > 0f)
            shield.Runtime.DamageFlareUntil = _timing.CurTime + TimeSpan.FromSeconds(shield.Visuals.DamageFlareTime);

        Dirty(ent, shield);
        return result;
    }

    /// <summary>The suit's personal shield, if its field is up and the suit is worn in the shield's slot.</summary>
    private bool TryGetActiveShield(EntityUid suit, out PersonalShieldComponent shield)
    {
        return TryComp(suit, out shield!) && shield.IsUp && shield.Runtime.Charge > 0f &&
               _inventory.InSlotWithFlags(suit, shield.Shield.RequiredSlot);
    }
}
