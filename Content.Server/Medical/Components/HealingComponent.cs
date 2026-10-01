using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared._Onyx.Wounds; // WOLFGATE(Wolfmed): HOOK 7, TreatmentCapability.
using Robust.Shared.Audio;
using Robust.Shared.Prototypes; // WOLFGATE(Wolfmed): W0, ProtoId for TreatedDamageTypes.
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.List;

namespace Content.Server.Medical.Components
{
    /// <summary>
    /// Applies a damage change to the target when used in an interaction.
    /// </summary>
    [RegisterComponent]
    public sealed partial class HealingComponent : Component
    {
        [DataField("damage", required: true)]
        [ViewVariables(VVAccess.ReadWrite)]
        public DamageSpecifier Damage = default!;

        /// <remarks>
        ///     This should generally be negative,
        ///     since you're, like, trying to heal damage.
        /// </remarks>
        [DataField("bloodlossModifier")]
        [ViewVariables(VVAccess.ReadWrite)]
        public float BloodlossModifier = 0.0f;

        /// <summary>
        ///     Restore missing blood.
        /// </summary>
        [DataField("ModifyBloodLevel")]
        [ViewVariables(VVAccess.ReadWrite)]
        public float ModifyBloodLevel = 0.0f;

        /// <remarks>
        ///     The supported damage types are specified using a <see cref="DamageContainerPrototype"/>s. For a
        ///     HealingComponent this filters what damage container type this component should work on. If null,
        ///     all damage container types are supported.
        /// </remarks>
        [DataField("damageContainers", customTypeSerializer: typeof(PrototypeIdListSerializer<DamageContainerPrototype>))]
        public List<string>? DamageContainers;

        /// <summary>
        /// How long it takes to apply the damage.
        /// </summary>
        [ViewVariables(VVAccess.ReadWrite)]
        [DataField("delay")]
        public float Delay = 2f; //Was 3f, changed due to Surgery Changes (Goobstation)

        /// <summary>
        /// Delay multiplier when healing yourself.
        /// </summary>
        [DataField("selfHealPenaltyMultiplier")]
        public float SelfHealPenaltyMultiplier = 2f; //Was 3f, changed due to Surgery Changes (Goobstation)

        // WOLFGATE(Wolfmed) START: HOOK 7 / D14, Wolfmed wound treatment data.
        // The two collection types are load-bearing: ResolveHealingPartEvent takes IReadOnlySet<>, which List<> does
        // not implement (D31).
        /// <summary>
        ///     Whether this item heals flat damage on the resolved body part.
        /// </summary>
        [DataField]
        public bool HealDamage = true;

        /// <summary>
        ///     Whether this item treats wounds on the resolved body part.
        /// </summary>
        [DataField]
        public bool HealWounds = true;

        /// <summary>
        ///     Which body-part treatment profiles this item can work on.
        /// </summary>
        [DataField]
        public HashSet<TreatmentCapability> TreatmentCapabilities = [TreatmentCapability.Biological];

        /// <summary>
        ///     Wound stages this item may treat. Null allows every stage.
        /// </summary>
        [DataField]
        public HashSet<string>? AllowedWoundStages;

        /// <summary>
        ///     Damage types this item may treat on a wound host. Null or empty means everything
        ///     <see cref="Damage"/> carries. A bruise pack lists Blunt, so it cannot close a cut or its bleed.
        /// </summary>
        [DataField]
        public HashSet<ProtoId<DamageTypePrototype>>? TreatedDamageTypes;
        // WOLFGATE END

        /// <summary>
        ///     Sound played on healing begin
        /// </summary>
        [DataField("healingBeginSound")]
        public SoundSpecifier? HealingBeginSound = null;

        /// <summary>
        ///     Sound played on healing end
        /// </summary>
        [DataField("healingEndSound")]
        public SoundSpecifier? HealingEndSound = null;
    }
}
