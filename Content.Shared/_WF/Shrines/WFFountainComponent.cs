using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Shrines;

/// <summary>A shrine fountain: its heart stops once its solution runs dry.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WFFountainComponent : Component
{
    /// <summary>The solution that runs dry.</summary>
    [DataField]
    public string Solution = "fountain";
}

[Serializable, NetSerializable]
public enum WFFountainVisuals : byte
{
    Dry,
}
