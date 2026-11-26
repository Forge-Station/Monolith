namespace Content.Shared._WF.NpcCrew;

/// <summary>What a crew pilot does with the ship once it has the helm.</summary>
public enum WFPilotOrder : byte
{
    /// <summary>Stop the ship and keep station where it is.</summary>
    Hold,

    /// <summary>Fly the waypoints in turn, then hold.</summary>
    GoTo,

    /// <summary>Circle a point at a radius.</summary>
    Loiter,

    /// <summary>Keep within range of another grid.</summary>
    Follow,

    /// <summary>Fly to another grid and dock with it by hand, then hold.</summary>
    Dock,

    /// <summary>Undock from everything, back off, then hold.</summary>
    Undock,
}

/// <summary>Where a crew pilot is in a hand-flown docking.</summary>
public enum WFDockPhase : byte
{
    /// <summary>No plan yet: picking a dock pair and its approach.</summary>
    None,

    /// <summary>Flying to the standoff point out from the target dock.</summary>
    Approach,

    /// <summary>At the standoff, waiting for the ship to stop moving and turning.</summary>
    Settle,

    /// <summary>Creeping in to the final pose until the docks can connect.</summary>
    Creep,
}
