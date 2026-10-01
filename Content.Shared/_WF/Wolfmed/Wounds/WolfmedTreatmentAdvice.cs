using Content.Shared._Onyx.Wounds;

namespace Content.Shared._WF.Wolfmed.Wounds;

/// <summary>
/// UI3: where the analyzer's "what is this and what do I do" text comes from. Advice is locale data keyed
/// off the wound prototype id or off a condition name, never a table in C#, so a new wound is a prototype
/// plus two FTL lines and the coverage test fails until both exist.
/// </summary>
/// <remarks>
/// The short key is the tooltip, one or two lines, and the procedure window reuses it as its summary.
/// UI4 replaced the old numbered-text key with a <see cref="WolfmedTreatmentProcedurePrototype"/> whose id
/// is derived here, so the steps are rows with tools and completion checks rather than a blob of markup.
/// A <c>-mechanical</c> variant (and a <c>Mechanical</c> procedure) is used when the part is a chassis and
/// the advice differs; where it does not, the base one serves both.
/// </remarks>
public static class WolfmedTreatmentAdvice
{
    public const string ShortPrefix = "wolfmed-treatment-short-";
    public const string MechanicalSuffix = "-mechanical";

    /// <summary>UI4: the procedure id suffix that marks a chassis variant.</summary>
    public const string MechanicalId = "Mechanical";

    /// <summary>UI4: the procedure id prefix for the findings that are not wounds.</summary>
    public const string ConditionId = "Cond";

    private const string ConditionInfix = "cond-";
    private const string CategoryInfix = "cat-";

    /// <summary>
    /// Every part-level or body-level finding the panel draws that is not a wound row. The names are the
    /// tail of the locale key, and <see cref="MechanicalConditions"/> says which of them read differently
    /// on a chassis.
    /// </summary>
    public static readonly string[] Conditions =
    [
        "bleeding",
        "internal-bleeding",
        "fracture",
        "clotting",
        "embedded",
        "infection-local",
        "infection-spreading",
        "infection-septic",
        "necrosis",
        "necrosis-risk",
        "overheating",
        "impaired",
        "disabled",
        "unavailable",
        "sepsis",
        "blood-low",
        "cardiac-arrest",
        "brain-death",
    ];

    /// <summary>The conditions a chassis reports differently. W6 already splits their wording; the advice follows.</summary>
    public static readonly string[] MechanicalConditions =
    [
        "bleeding",
        "fracture",
        "clotting",
    ];

    /// <summary>The wounds both flesh and a chassis can carry, so both need a procedure.</summary>
    public static readonly string[] MechanicalWounds =
    [
        "ElectricalWound",
        "SurgicalIncisionWound",
        "DismembermentWound",
        "AmputationConsequenceWound",
        "SystemicBleedingWound",
    ];

    public static string ShortKey(string woundId) => ShortPrefix + Slug(woundId);

    public static string ConditionShortKey(string condition) => ShortPrefix + ConditionInfix + condition;

    /// <summary>UI4: a wound's procedure prototype id. The chassis variant appends <see cref="MechanicalId"/>.</summary>
    public static string ProcedureId(string woundId, bool mechanical) =>
        mechanical ? woundId + MechanicalId : woundId;

    /// <summary>UI4: a condition's procedure prototype id, e.g. <c>internal-bleeding</c> to <c>CondInternalBleeding</c>.</summary>
    public static string ConditionProcedureId(string condition, bool mechanical) =>
        ProcedureId(ConditionId + Pascal(condition), mechanical);

    public static string CategoryShortKey(WolfmedWoundCategory category) =>
        ShortPrefix + CategoryInfix + category.ToString().ToLowerInvariant();

    /// <summary>The condition name for a functionality state the panel draws.</summary>
    public static string FunctionalityCondition(BodyPartFunctionalityState state) =>
        state.ToString().ToLowerInvariant();

    /// <summary>The condition name for an infection stage the panel draws.</summary>
    public static string InfectionCondition(WolfmedInfectionStage stage) =>
        "infection-" + stage.ToString().ToLowerInvariant();

    /// <summary>
    /// INFECTION: the condition for a part's own infection. Antiseptic never reaches it, so a local one takes the
    /// spreading advice (antibiotics) rather than the local wound's.
    /// </summary>
    public static string PartInfectionCondition(WolfmedInfectionStage stage) =>
        InfectionCondition(stage == WolfmedInfectionStage.Septic
            ? WolfmedInfectionStage.Septic
            : WolfmedInfectionStage.Spreading);

    /// <summary>
    /// A PascalCase prototype id as the kebab-case tail of a locale key. The module's <c>WF</c> id prefix is left
    /// out: it is a namespace rather than a word, so <c>WFWolfmedGrazeWound</c> keys <c>wolfmed-graze-wound</c>.
    /// </summary>
    public static string Slug(string id)
    {
        var start = id.Length > 2 && id[0] == 'W' && id[1] == 'F' && char.IsUpper(id[2]) ? 2 : 0;
        // StringBuilder, not string += char: that compiles to a ReadOnlySpan<char> concat the client sandbox rejects.
        var result = new System.Text.StringBuilder(id.Length + 8);
        for (var i = start; i < id.Length; i++)
        {
            if (i > start && char.IsUpper(id[i]))
                result.Append('-');

            result.Append(char.ToLowerInvariant(id[i]));
        }

        return result.ToString();
    }

    /// <summary>UI4: a kebab-case condition name as the PascalCase tail of a prototype id.</summary>
    public static string Pascal(string name)
    {
        // StringBuilder for the same reason Slug uses one: the client sandbox rejects span concatenation.
        var result = new System.Text.StringBuilder(name.Length);
        var upper = true;
        for (var i = 0; i < name.Length; i++)
        {
            if (name[i] == '-')
            {
                upper = true;
                continue;
            }

            result.Append(upper ? char.ToUpperInvariant(name[i]) : name[i]);
            upper = false;
        }

        return result.ToString();
    }
}
