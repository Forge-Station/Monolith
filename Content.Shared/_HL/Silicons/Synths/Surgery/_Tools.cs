using Content.Shared._Shitmed.Medical.Surgery.Tools;
using Robust.Shared.GameStates;

namespace Content.Shared._HL.Silicons.Synths.Surgery;

[RegisterComponent, NetworkedComponent]
public sealed partial class MultitoolSurgeryComponent : Component, ISurgeryToolComponent
{
    public string ToolName => "a multitool";
    public bool? Used { get; set; }

    [DataField]
    public float Speed { get; set; } = 1f;
}

[RegisterComponent, NetworkedComponent]
public sealed partial class CrowbarSurgeryComponent : Component, ISurgeryToolComponent
{
    public string ToolName => "a crowbar";
    public bool? Used { get; set; }

    [DataField]
    public float Speed { get; set; } = 1f;
}

[RegisterComponent, NetworkedComponent]
public sealed partial class WirecutterSurgeryComponent : Component, ISurgeryToolComponent
{
    public string ToolName => "a wirecutter";
    public bool? Used { get; set; }

    [DataField]
    public float Speed { get; set; } = 1f;
}

[RegisterComponent, NetworkedComponent]
public sealed partial class ScrewdriverSurgeryComponent : Component, ISurgeryToolComponent
{
    public string ToolName => "a screwdriver";
    public bool? Used { get; set; }

    [DataField]
    public float Speed { get; set; } = 1f;
}
