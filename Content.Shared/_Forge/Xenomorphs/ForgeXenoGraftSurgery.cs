using Robust.Shared.GameStates;

namespace Content.Shared._Forge.Xenomorphs;

/// <summary>
/// Two body-part organ slots a surgeon can fill with harvested xenomorph glands.
/// </summary>
public static class ForgeXenoGraftSlots
{
    public const int Max = 2;

    public static readonly string[] Ids = ["xeno_graft_a", "xeno_graft_b"];
}

/// <summary>
/// Surgery is offered when a torso can take a gland (default) or already has one to remove (<see cref="Inverse"/>).
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class SurgeryXenoGraftConditionComponent : Component
{
    /// <summary>True = insert (need a free slot). False = remove (need a grafted gland).</summary>
    [DataField]
    public bool Inverse;
}

[RegisterComponent, NetworkedComponent]
public sealed partial class SurgeryInsertXenoGraftStepComponent : Component;

/// <summary>Clears the temporary reattached mark after a gland is sewn in.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class SurgeryAffixXenoGraftStepComponent : Component;

[RegisterComponent, NetworkedComponent]
public sealed partial class SurgeryRemoveXenoGraftStepComponent : Component;

/// <summary>
/// Set on the torso after a graft is surgically pulled out so the remove step can complete
/// even when another graft remains.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class SurgeryXenoGraftExtractedComponent : Component;
