using Content.Shared._Shitmed.Medical.Surgery.Tools;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.Wolfmed.Surgery;

/// <summary>
/// Something to beat a torn panel back into shape with. The first step of closing a chassis breach, which
/// is the mechanical answer to clamping bleeders: the plating has to line up before anything will weld.
/// </summary>
/// <remarks>
/// Sits on the wrench, which already takes dents out of a chassis through its WeldingHealing block (W6).
/// </remarks>
[RegisterComponent, NetworkedComponent]
public sealed partial class WolfmedHullPlateComponent : Component, ISurgeryToolComponent
{
    public string ToolName => Loc.GetString("wolfmed-surgery-tool-wrench");

    public bool? Used { get; set; }

    [DataField]
    public float Speed { get; set; } = 1f;
}

/// <summary>
/// M2 (OD10): something to test and re-flash a positronic core with. The middle step of core repair, where a brain
/// repair has the surgeon's hands. Sits on the multitool.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WolfmedCoreProbeComponent : Component, ISurgeryToolComponent
{
    public string ToolName => Loc.GetString("wolfmed-surgery-tool-multitool");

    public bool? Used { get; set; }

    [DataField]
    public float Speed { get; set; } = 1f;
}

/// <summary>
/// M2 (OD10): the core housing is unbolted. Added to the torso by the first step of core repair, taken off by the
/// last, so each step knows where the surgery has got to.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WolfmedCoreHousingOpenComponent : Component;

/// <summary>Something to run a seam with. Closes a chassis breach, where flesh would be cauterised.</summary>
/// <remarks>
/// Sits on the welder. The step is a surgery step rather than the welder's ordinary repair because a
/// breach carries <c>healingMultiplier: 0</c>: swinging a lit welder at a torn-open chassis is not a repair.
/// </remarks>
[RegisterComponent, NetworkedComponent]
public sealed partial class WolfmedHullWeldComponent : Component, ISurgeryToolComponent
{
    public string ToolName => Loc.GetString("wolfmed-surgery-tool-welding-tool");

    public bool? Used { get; set; }

    [DataField]
    public float Speed { get; set; } = 1f;
}
