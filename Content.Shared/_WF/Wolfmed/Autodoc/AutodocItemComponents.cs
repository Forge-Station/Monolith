using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Wolfmed.Autodoc;

/// <summary>A program disk. In the pod's disk slot it adds its program to the pod's library.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class AutodocProgramDiskComponent : Component
{
    [DataField(required: true)]
    public ProtoId<AutodocProgramPrototype> Program;
}

/// <summary>
/// The cardiac module. With one fitted the pod shocks an occupant whose heart has stopped, on the rule a
/// medic's paddles follow (BRAIN).
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class AutodocDefibModuleComponent : Component;

/// <summary>
/// The autofix module. With one fitted the pod may plan a triage queue and start it by itself, instead of
/// waiting for somebody to press PLAN and START.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class AutodocAutofixModuleComponent : Component;
