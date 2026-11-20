using Robust.Shared.Serialization;

namespace Content.Shared._WF.ExamineCard;

/// <summary>How a status chip is coloured.</summary>
[Serializable, NetSerializable]
public enum ExamineChipTone : byte
{
    Neutral,
    Good,
    Calm,
    Warn,
    Danger,
    Mind,
}

/// <summary>An examine line, by its Fluent id, that becomes a chip, or is dropped when the card already shows it.</summary>
public sealed class ExamineChipRule(string message, string? chip, ExamineChipTone tone = ExamineChipTone.Neutral)
{
    public readonly string Message = message;

    /// <summary>The chip's label key after <c>wf-examine-chip-</c>, or null to drop the line.</summary>
    public readonly string? Chip = chip;

    public readonly ExamineChipTone Tone = tone;
}

/// <summary>The examine lines the card folds away.</summary>
public static class ExamineChipRules
{
    private const ExamineChipTone Good = ExamineChipTone.Good;
    private const ExamineChipTone Calm = ExamineChipTone.Calm;
    private const ExamineChipTone Warn = ExamineChipTone.Warn;
    private const ExamineChipTone Danger = ExamineChipTone.Danger;
    private const ExamineChipTone Mind = ExamineChipTone.Mind;

    public static readonly ExamineChipRule[] All =
    {
        // Already on the card: appearance, species and held items.
        new("humanoid-appearance-component-examine", null),
        new("humanoid-appearance-component-examine-selfaware", null),
        new("comp-hands-examine", null),
        new("comp-hands-examine-empty", null),
        new("comp-hands-examine-selfaware", null),
        new("comp-hands-examine-empty-selfaware", null),
        new("wolfmed-look-title-other", null),
        new("wolfmed-look-title-self", null),

        new("comp-mind-examined-catatonic", "catatonic", Mind),
        new("comp-mind-examined-ssd", "ssd", Warn),
        new("comp-mind-examined-dead", "dead", Danger),
        new("comp-mind-examined-dead-and-ssd", "dormant", Warn),
        new("comp-mind-examined-dead-and-irrecoverable", "departed", Mind),
        new("sleep-examined", "asleep", Calm),
        new("rotting-rotting", "rotting", Warn),
        new("rotting-bloated", "bloated", Danger),
        new("rotting-extremely-bloated", "bloated", Danger),

        new("wolfmed-look-none-other", "no-injuries", Good),
        new("wolfmed-look-none-self", "no-injuries", Good),
        new("wolfmed-look-covered", "covered"),
        new("wolfmed-look-hidden", "covered"),
        new("wolfmed-look-distant", "distant"),
        new("wolfmed-look-avpu-alert", "alert", Good),
        new("wolfmed-look-avpu-voice", "drowsy", Warn),
        new("wolfmed-look-avpu-pain", "out-cold", Danger),
        new("wolfmed-look-avpu-unresponsive", "unresponsive", Danger),
        new("wolfmed-look-ventilated-other", "ventilated", Calm),
        new("wolfmed-look-ventilated-self", "ventilated", Calm),
        new("wolfmed-look-appears-dead-other", "appears-dead", Danger),
        new("wolfmed-look-appears-dead-self", "appears-dead", Danger),
        new("wolfmed-look-lifeless-other", "lifeless", Danger),
        new("wolfmed-look-shutdown-other", "shut-down", Danger),
        new("wolfmed-look-shutdown-self", "shut-down", Danger),
        new("wolfmed-look-no-pulse-other", "no-pulse", Danger),
        new("wolfmed-look-no-pulse-self", "no-pulse", Danger),
        new("wolfmed-look-pulse-faint-other", "faint-pulse", Danger),
        new("wolfmed-look-pulse-faint-self", "faint-pulse", Danger),
        new("wolfmed-look-pulse-weak-other", "weak-pulse", Danger),
        new("wolfmed-look-pulse-weak-self", "weak-pulse", Danger),
        new("wolfmed-look-pulse-irregular-other", "irregular-pulse", Warn),
        new("wolfmed-look-pulse-irregular-self", "irregular-pulse", Warn),
        new("wolfmed-look-not-breathing-other", "not-breathing", Danger),
        new("wolfmed-look-not-breathing-self", "not-breathing", Danger),
        new("wolfmed-look-gasping-other", "gasping", Danger),
        new("wolfmed-look-gasping-self", "gasping", Danger),
        new("wolfmed-look-breathing-slow-other", "shallow-breathing", Warn),
        new("wolfmed-look-breathing-slow-self", "shallow-breathing", Warn),
        new("wolfmed-look-short-of-breath-other", "short-of-breath", Warn),
        new("wolfmed-look-short-of-breath-self", "short-of-breath", Warn),
        new("wolfmed-look-blue-lips", "blue-lips", Danger),
        new("wolfmed-look-pale-other", "pale", Warn),
        new("wolfmed-look-pale-self", "pale", Warn),
        new("wolfmed-look-pupils-pinpoint", "pinpoint-pupils", Warn),
        new("wolfmed-look-pupils-unequal", "unequal-pupils", Danger),
        new("wolfmed-look-unequal-pupils-other", "unequal-pupils", Danger),
        new("wolfmed-look-unequal-pupils-self", "confused", Warn),
        new("wolfmed-look-sepsis-other", "septic", Danger),
        new("wolfmed-look-sepsis-self", "septic", Danger),
        new("wolfmed-look-septic-shock-other", "septic-shock", Danger),
        new("wolfmed-look-septic-shock-self", "septic-shock", Danger),
        new("wolfmed-look-poisoned-other", "poisoned", Danger),
        new("wolfmed-look-poisoned-self", "poisoned", Danger),
        new("wolfmed-look-radiation-sick-other", "radiation-sick", Danger),
        new("wolfmed-look-radiation-sick-self", "radiation-sick", Danger),
        new("wolfmed-look-hypothermic-other", "hypothermic", Danger),
        new("wolfmed-look-hypothermic-self", "hypothermic", Danger),
        new("wolfmed-look-overheated-other", "overheated", Danger),
        new("wolfmed-look-overheated-self", "overheated", Danger),
        new("wolfmed-look-too-hot-other", "smoking", Danger),
        new("wolfmed-look-too-hot-self", "smoking", Danger),
        new("wolfmed-look-weeping-burns-other", "weeping-burns", Warn),
        new("wolfmed-look-weeping-burns-self", "weeping-burns", Warn),
    };
}
