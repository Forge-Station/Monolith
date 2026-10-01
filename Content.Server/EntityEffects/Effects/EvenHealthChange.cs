using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.EntityEffects;
using Content.Shared.FixedPoint;
using Content.Shared.Localizations;
using Content.Shared._Onyx.Wounds; // WOLFGATE(Wolfmed): HOOK 9 - treatment-capability scope
using JetBrains.Annotations;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using System.Linq; // WOLFGATE(Wolfmed): HOOK 9 - Any() on the healing test

namespace Content.Server.EntityEffects.Effects;

/// <summary>
/// Version of <see cref="HealthChange"/> that distributes the healing to groups
/// </summary>
[UsedImplicitly]
public sealed partial class EvenHealthChange : EntityEffect
{
    /// <summary>
    /// Damage to heal, collected into entire damage groups.
    /// </summary>
    [DataField(required: true)]
    public Dictionary<ProtoId<DamageGroupPrototype>, FixedPoint2> Damage = new();

    /// <summary>
    /// Should this effect scale the damage by the amount of chemical in the solution?
    /// Useful for touch reactions, like styptic powder or acid.
    /// Only usable if the EntityEffectBaseArgs is an EntityEffectReagentArgs.
    /// </summary>
    [DataField]
    public bool ScaleByQuantity;

    /// <summary>
    /// Should this effect ignore damage modifiers?
    /// </summary>
    [DataField]
    public bool IgnoreResistances = true;

    // WOLFGATE(Wolfmed): HOOK 9 - which body-part materials this healing can treat on a wound host.
    [DataField]
    public HashSet<TreatmentCapability> TreatmentCapabilities = [TreatmentCapability.Biological];

    protected override string ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
    {
        var damages = new List<string>();
        var heals = false;
        var deals = false;

        var damagableSystem = entSys.GetEntitySystem<DamageableSystem>();
        var universalReagentDamageModifier = damagableSystem.UniversalReagentDamageModifier;
        var universalReagentHealModifier = damagableSystem.UniversalReagentHealModifier;

        foreach (var (group, amount) in Damage)
        {
            var groupProto = prototype.Index(group);

            var sign = FixedPoint2.Sign(amount);
            var mod = 1f;

            if (sign < 0)
            {
                heals = true;
                mod = universalReagentHealModifier;
            }
            else if (sign > 0)
            {
                deals = true;
                mod = universalReagentDamageModifier;
            }

            damages.Add(
                Loc.GetString("health-change-display",
                    ("kind", groupProto.LocalizedName),
                    ("amount", MathF.Abs(amount.Float() * mod)),
                    ("deltasign", sign)
                ));
        }

        var healsordeals = heals ? (deals ? "both" : "heals") : (deals ? "deals" : "none");
        return Loc.GetString("reagent-effect-guidebook-even-health-change",
            ("chance", Probability),
            ("changes", ContentLocalizationManager.FormatList(damages)),
            ("healsordeals", healsordeals));
    }

    public override void Effect(EntityEffectBaseArgs args)
    {
        if (!args.EntityManager.TryGetComponent<DamageableComponent>(args.TargetEntity, out var damageable))
            return;

        var protoMan = IoCManager.Resolve<IPrototypeManager>();

        var scale = FixedPoint2.New(1);

        if (args is EntityEffectReagentArgs reagentArgs)
        {
            scale = ScaleByQuantity ? reagentArgs.Quantity * reagentArgs.Scale : reagentArgs.Scale;
        }

        var damagableSystem = args.EntityManager.System<DamageableSystem>();
        var universalReagentDamageModifier = damagableSystem.UniversalReagentDamageModifier;
        var universalReagentHealModifier = damagableSystem.UniversalReagentHealModifier;

        var dspec = new DamageSpecifier();

        foreach (var (group, amount) in Damage)
        {
            var groupProto = protoMan.Index(group);
            var groupDamage = new Dictionary<string, FixedPoint2>();
            foreach (var damageId in groupProto.DamageTypes)
            {
                var damageAmount = damageable.Damage.DamageDict.GetValueOrDefault(damageId);
                if (damageAmount != FixedPoint2.Zero)
                    groupDamage.Add(damageId, damageAmount);
            }

            var sum = groupDamage.Values.Sum();
            foreach (var (damageId, damageAmount) in groupDamage)
            {
                var existing = dspec.DamageDict.GetOrNew(damageId);
                dspec.DamageDict[damageId] = existing + damageAmount / sum * amount;
            }
        }

        if (universalReagentDamageModifier != 1 || universalReagentHealModifier != 1)
        {
            foreach (var (type, val) in dspec.DamageDict)
            {
                if (val < 0f)
                {
                    dspec.DamageDict[type] = val * universalReagentHealModifier;
                }
                if (val > 0f)
                {
                    dspec.DamageDict[type] = val * universalReagentDamageModifier;
                }
            }
        }

        // WOLFGATE(Wolfmed) START: HOOK 9, the call becomes a delegate so healing can run inside a treatment-capability scope.
        // damagableSystem.TryChangeDamage(
        void Apply() => damagableSystem.TryChangeDamage(
            args.TargetEntity,
            dspec * scale,
            IgnoreResistances,
            interruptsDoAfters: false);

        // WOLFGATE(Wolfmed): HOOK 9 - scope only the healing case on a wound host; everything else is unchanged (D2).
        if (Damage.Values.Any(amount => amount < 0) &&
            args.EntityManager.HasComponent<WoundHostComponent>(args.TargetEntity))
            args.EntityManager.System<WoundDamageRoutingSystem>()
                .WithTreatmentCapabilities(args.TargetEntity, TreatmentCapabilities, Apply);
        else
            Apply();
        // WOLFGATE END
    }
}
