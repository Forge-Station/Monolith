using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Content.Shared.EntityEffects;

namespace Content.Shared._Forge.Chemistry.Addiction;

[Prototype]
public sealed partial class AddictionPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; set; } = default!;

    // Seconds without a dose before withdrawal begins
    [DataField]
    public float WithdrawalDelay { get; set; } = 600f;

    // Tolerance lost per second while not in withdrawal
    [DataField]
    public float ToleranceDecayRate { get; set; } = 0.05f;

    // Tolerance lost per second during active withdrawal
    [DataField]
    public float WithdrawalDecayRate { get; set; } = 0.0005f;

    // Stages sorted by MinTolerance ascending. Picks the highest matching one.
    [DataField]
    public List<AddictionStageData> Stages { get; set; } = new();
}

#region AddictionData
[DataDefinition]
[Serializable, NetSerializable]
public sealed partial class AddictionData
{
    [DataField]
    public string PrototypeId { get; set; } = string.Empty;

    // Used to fire the onset message only once
    [DataField]
    public bool IsNew { get; set; } = true;

    // Current tolerance value (0.0–1.0)
    [DataField]
    public float Tolerance { get; set; } = 0f;

    [DataField]
    public TimeSpan LastDoseTime { get; set; } = TimeSpan.Zero;

    [DataField]
    public bool WithdrawalActive { get; set; } = false;

    [DataField]
    public float NextEffectTimer { get; set; } = 0f;

    // Addiction is removed from the dictionary once tolerance drops below this.
    public const float RemoveThreshold = 0.01f;
}

[DataDefinition]
[Serializable, NetSerializable]
public sealed partial class AddictionStageData
{
    [DataField(required: true)]
    public float MinTolerance { get; set; }

    // If true, effects only fire during active withdrawal
    [DataField]
    public bool WithdrawalOnly { get; set; } = false;

    // Seconds between effect applications for this stage
    [DataField]
    public float EffectInterval { get; set; } = 10f;

    // EntityEffects to apply
    [DataField]
    public List<EntityEffect> Effects { get; set; } = new();

    [DataField]
    public List<EntityEffect> WithdrawalEffects { get; set; } = new();
}
#endregion
