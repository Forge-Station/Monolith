using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenonids.Hive;

/// <summary>
/// Projectiles shot from this get autoassigned a hive from the weapon's hivemember
/// </summary>
[RegisterComponent, NetworkedComponent]
[Access(typeof(SharedXenoHiveSystem))]
public sealed partial class HiveGunComponent : Component;
