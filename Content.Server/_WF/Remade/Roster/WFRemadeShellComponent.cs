using Content.Shared._Shitmed.Targeting;

namespace Content.Server._WF.Remade.Roster;

/// <summary>A shell that covers a Remade's weak limbs. Shut, they can't be hit and it's armoured; open, they show and it isn't.</summary>
[RegisterComponent]
public sealed partial class WFRemadeShellComponent : Component
{
    /// <summary>The limbs the shell covers while it's shut.</summary>
    [DataField]
    public List<string> Covered = new();

    /// <summary>The damage resistances while it's shut.</summary>
    [DataField]
    public string ShutArmour = "WFRemadeLurkerShell";

    /// <summary>The damage resistances while it's open.</summary>
    [DataField]
    public string OpenArmour = "WFRemadeLurkerOpen";

    [ViewVariables]
    public bool Open = true;

    /// <summary>What the covered limbs were before the shell hid them, to give back when it opens.</summary>
    [ViewVariables]
    public Dictionary<string, (float Weight, List<TargetBodyPart> Zones)> Hidden = new();
}
