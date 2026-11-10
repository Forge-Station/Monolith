using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Vehicles.Bison;

/// <summary>Ends remote control and returns the pilot to their body.</summary>
public sealed partial class CMUCargoVehicleReturnActionEvent : InstantActionEvent;

/// <summary>Opens or closes the carrier's cargo bay.</summary>
public sealed partial class CMUCargoVehicleToggleBayActionEvent : InstantActionEvent;

/// <summary>Arms the carrier's self-destruct.</summary>
public sealed partial class CMUCargoVehicleSelfDestructActionEvent : InstantActionEvent;

/// <summary>Ends the do-after of loading a crate into the carrier's bay.</summary>
[Serializable, NetSerializable]
public sealed partial class CMUCargoVehicleLoadDoAfterEvent : SimpleDoAfterEvent;

/// <summary>Ends the do-after of unloading the carrier's crate.</summary>
[Serializable, NetSerializable]
public sealed partial class CMUCargoVehicleUnloadDoAfterEvent : SimpleDoAfterEvent;
