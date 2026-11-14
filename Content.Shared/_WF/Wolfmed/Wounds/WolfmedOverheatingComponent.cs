using Robust.Shared.GameStates;

namespace Content.Shared._WF.Wolfmed.Wounds;

/// <summary>
/// On a mechanical body part carrying an open overheating wound. Exists so the cooling tick walks hot
/// parts instead of every wound in the round, and so a client can tell a hot part from a cold one.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WolfmedOverheatingComponent : Component
{
    /// <summary>Seconds since the last cooling step.</summary>
    [ViewVariables]
    public float Accumulator;

    /// <summary>Severity the part is currently shedding per minute, for the analyzer and for display.</summary>
    [DataField, AutoNetworkedField]
    public float CoolingPerMinute;
}
