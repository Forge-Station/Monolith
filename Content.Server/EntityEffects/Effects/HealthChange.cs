using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.EntityEffects;
using Content.Shared.FixedPoint;
using Content.Shared.Localizations;
using Content.Shared._Onyx.Wounds; // WOLFGATE(Wolfmed): HOOK 9 - treatment-capability scope
using Content.Server._WF.Wolfmed.Medical; // WOLFGATE(Wolfmed): a metabolising reagent's damage is toxin load on a wound host
using Content.Shared._Shitmed.Targeting; // Shitmed Change
using JetBrains.Annotations;
using Robust.Shared.Prototypes;
using System.Linq;
using System.Text.Json.Serialization;

namespace Content.Server.EntityEffects.Effects
{
    /// <summary>
    /// Default metabolism used for medicine reagents.
    /// </summary>
    [UsedImplicitly]
    public sealed partial class HealthChange : EntityEffect
    {
        /// <summary>
        /// Damage to apply every cycle. Damage Ignores resistances.
        /// </summary>
        [DataField(required: true)]
        [JsonPropertyName("damage")]
        public DamageSpecifier Damage = default!;

        /// <summary>
        ///     Should this effect scale the damage by the amount of chemical in the solution?
        ///     Useful for touch reactions, like styptic powder or acid.
        ///     Only usable if the EntityEffectBaseArgs is an EntityEffectReagentArgs.
        /// </summary>
        [DataField]
        [JsonPropertyName("scaleByQuantity")]
        public bool ScaleByQuantity;

        [DataField]
        [JsonPropertyName("ignoreResistances")]
        public bool IgnoreResistances = true;

        // WOLFGATE(Wolfmed): HOOK 9 - which body-part materials this healing can treat on a wound host.
        [DataField]
        public HashSet<TreatmentCapability> TreatmentCapabilities = [TreatmentCapability.Biological];

        protected override string ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        {
            var damages = new List<string>();
            var heals = false;
            var deals = false;

            var damageSpec = new DamageSpecifier(Damage);

            var universalReagentDamageModifier = entSys.GetEntitySystem<DamageableSystem>().UniversalReagentDamageModifier;
            var universalReagentHealModifier = entSys.GetEntitySystem<DamageableSystem>().UniversalReagentHealModifier;

            if (universalReagentDamageModifier != 1 || universalReagentHealModifier != 1)
            {
                foreach (var (type, val) in damageSpec.DamageDict)
                {
                    if (val < 0f)
                    {
                        damageSpec.DamageDict[type] = val * universalReagentHealModifier;
                    }
                    if (val > 0f)
                    {
                        damageSpec.DamageDict[type] = val * universalReagentDamageModifier;
                    }
                }
            }

            damageSpec = entSys.GetEntitySystem<DamageableSystem>().ApplyUniversalAllModifiers(damageSpec);

            foreach (var group in prototype.EnumeratePrototypes<DamageGroupPrototype>())
            {
                if (!damageSpec.TryGetDamageInGroup(group, out var amount))
                    continue;

                var relevantTypes = damageSpec.DamageDict
                    .Where(x => x.Value != FixedPoint2.Zero && group.DamageTypes.Contains(x.Key)).ToList();

                if (relevantTypes.Count != group.DamageTypes.Count)
                    continue;

                // Frontier: explicitly check that all damage types have the same amount
                var firstValue = relevantTypes.FirstOrDefault().Value;
                bool allValuesEqual = true;
                foreach (var type in relevantTypes)
                {
                    if (FixedPoint2.Abs(type.Value - firstValue) >= 0.02) // Need to account for remainder issues
                    {
                        allValuesEqual = false;
                        break;
                    }
                }
                if (!allValuesEqual)
                    continue;
                // End Frontier

                var sign = FixedPoint2.Sign(amount);

                if (sign < 0)
                    heals = true;
                if (sign > 0)
                    deals = true;

                damages.Add(
                    Loc.GetString("health-change-display",
                        ("kind", group.LocalizedName),
                        ("amount", MathF.Abs(amount.Float())),
                        ("deltasign", sign)
                    ));

                foreach (var type in group.DamageTypes)
                {
                    damageSpec.DamageDict.Remove(type);
                }
            }

            foreach (var (kind, amount) in damageSpec.DamageDict)
            {
                var sign = FixedPoint2.Sign(amount);

                if (sign < 0)
                    heals = true;
                if (sign > 0)
                    deals = true;

                damages.Add(
                    Loc.GetString("health-change-display",
                        ("kind", prototype.Index<DamageTypePrototype>(kind).LocalizedName),
                        ("amount", MathF.Abs(amount.Float())),
                        ("deltasign", sign)
                    ));
            }

            var healsordeals = heals ? (deals ? "both" : "heals") : (deals ? "deals" : "none");

            return Loc.GetString("reagent-effect-guidebook-health-change",
                ("chance", Probability),
                ("changes", ContentLocalizationManager.FormatList(damages)),
                ("healsordeals", healsordeals));
        }

        public override void Effect(EntityEffectBaseArgs args)
        {
            var scale = FixedPoint2.New(1);
            var damageSpec = new DamageSpecifier(Damage);

            if (args is EntityEffectReagentArgs reagentArgs)
            {
                scale = ScaleByQuantity ? reagentArgs.Quantity * reagentArgs.Scale : reagentArgs.Scale;
            }

            var universalReagentDamageModifier = args.EntityManager.System<DamageableSystem>().UniversalReagentDamageModifier;
            var universalReagentHealModifier = args.EntityManager.System<DamageableSystem>().UniversalReagentHealModifier;

            if (universalReagentDamageModifier != 1 || universalReagentHealModifier != 1)
            {
                foreach (var (type, val) in damageSpec.DamageDict)
                {
                    if (val < 0f)
                    {
                        damageSpec.DamageDict[type] = val * universalReagentHealModifier;
                    }
                    if (val > 0f)
                    {
                        damageSpec.DamageDict[type] = val * universalReagentDamageModifier;
                    }
                }
            }

            // WOLFGATE(Wolfmed) START: HOOK 9, the call becomes a delegate so healing can run inside a treatment-capability scope.
            // args.EntityManager.System<DamageableSystem>().TryChangeDamage(
            //     args.TargetEntity,
            //     Damage * scale,
            var change = Damage * scale;
            // WOLFGATE(Wolfmed): playtest 5, what a metabolising reagent deals to a wound host is toxin load, not wounds.
            if (args is EntityEffectReagentArgs { Method: null } &&
                args.EntityManager.HasComponent<WoundHostComponent>(args.TargetEntity))
                change = args.EntityManager.System<WolfmedReagentDamageSystem>().ForWoundHost(args.TargetEntity, change);
            void Apply() => args.EntityManager.System<DamageableSystem>().TryChangeDamage(
                args.TargetEntity,
                change,
                IgnoreResistances,
                interruptsDoAfters: false,
                // Shitmed Change Start
                targetPart: TargetBodyPart.All,
                partMultiplier: 1.00f, // Mono, 0.5f->1.00f
                canSever: false);
            // Shitmed Change End

            // WOLFGATE(Wolfmed): HOOK 9 - scope only the healing case on a wound host; everything else is unchanged (D2).
            if (change.DamageDict.Values.Any(amount => amount < 0) &&
                args.EntityManager.HasComponent<WoundHostComponent>(args.TargetEntity))
                args.EntityManager.System<WoundDamageRoutingSystem>()
                    .WithTreatmentCapabilities(args.TargetEntity, TreatmentCapabilities, Apply);
            else
                Apply();
            // WOLFGATE END
        }
    }
}
