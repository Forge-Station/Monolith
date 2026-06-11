using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;

namespace Content.Shared._Forge.Body.Syndromes;

#region Syndromes
[DataDefinition]
public sealed partial class SyndromeData
{
    [DataField]
    public string PrototypeId { get; set; } = string.Empty;

    [DataField]
    public float Severity { get; set; } = 0f;

    [DataField]
    public int Stage { get; set; } = 0;

    [DataField(serverOnly: true)]
    public int LastStage { get; set; } = -1;

    [DataField]
    public TimeSpan FlareSupressed { get; set; } = TimeSpan.Zero;

    [DataField]
    public TimeSpan NextUpdate { get; set; } = TimeSpan.Zero;

    [DataField]
    public TimeSpan LastProgression { get; set; } = TimeSpan.Zero;

    [DataField(serverOnly: true)]
    public bool Frozen { get; set; } = false;

    [DataField(serverOnly: true)]
    public HashSet<string> ActiveStatus { get; set; } = new();
}
#endregion

#region Tolerance
[DataDefinition]
public sealed partial class ToleranceData
{
    [DataField]
    public float Value { get; set; } = 0f;

    [DataField]
    public TimeSpan LastChanged { get; set; } = TimeSpan.Zero;

    [DataField]
    public float DecayRate { get; set; } = 0.001f;
}
#endregion
