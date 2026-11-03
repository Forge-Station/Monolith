using Robust.Shared.GameStates;

namespace Content.Shared._Forge.Xenomorphs;

/// <summary>
/// Marks a Colonial Marines xenomorph. Other xenos treat this as hive-friendly.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ForgeXenoComponent : Component;
