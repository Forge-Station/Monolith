using Robust.Shared.GameObjects;

namespace Content.Shared._WF.Wolfmed.Consciousness;

/// <summary>
/// Playtest 5, on a part: when its wound pain floor last rose. The floor settles toward wolfmed.pain_floor_rest from
/// then, so a wound that is not getting worse stops holding all its pain. Server only.
/// </summary>
[RegisterComponent]
public sealed partial class WolfmedPainSettleComponent : Component
{
    [ViewVariables]
    public TimeSpan FloorRaisedAt;
}
