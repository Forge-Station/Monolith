using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenonids.ClawSharpness;

/// <summary>
/// Xeno claw and tail hits skip this entity's flat damage reductions, so a structure takes RMC's xeno damage
/// instead of nothing.
/// </summary>
[RegisterComponent, NetworkedComponent]
[Access(typeof(XenoClawsSystem))]
public sealed partial class XenoSlashableComponent : Component;
