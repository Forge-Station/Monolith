using Robust.Shared.Serialization;

namespace Content.Shared._WF.NpcVehicleCrew;

/// <summary>What the vehicle crew panel asks the server to do.</summary>
[Serializable, NetSerializable]
public enum WFVehicleCrewPanelAction : byte
{
    /// <summary>Just send the live crews.</summary>
    List,

    /// <summary>Spawns <see cref="WFVehicleCrewPanelRequest.Count"/> crews beside the admin.</summary>
    Spawn,

    /// <summary>Gives a live crew new orders; Escort follows the admin.</summary>
    Order,

    /// <summary>Makes where the admin stands a crew's home: where it holds, patrols round and retreats to.</summary>
    HomeHere,

    /// <summary>Takes the admin to a crew.</summary>
    GoTo,

    /// <summary>Deletes a crew, its vehicle and its crewmen.</summary>
    Delete,

    /// <summary>Deletes every crew.</summary>
    ClearAll,
}

/// <summary>A request from the vehicle crew panel; the server checks the sender is an admin.</summary>
[Serializable, NetSerializable]
public sealed class WFVehicleCrewPanelRequest : EntityEventArgs
{
    public WFVehicleCrewPanelAction Action;

    /// <summary>The <see cref="WFVehicleCrewPrototype"/> to spawn.</summary>
    public string? Crew;

    /// <summary>The orders to spawn with or give; null spawns with the crew's own.</summary>
    public WFVehicleBehaviour? Behaviour;

    public int Count = 1;

    /// <summary>Metres from the admin to the first vehicle; further ones line up beside it.</summary>
    public float Distance = 8f;

    /// <summary>The live crew acted on.</summary>
    public NetEntity? Vehicle;
}

/// <summary>One live crew as the panel lists it.</summary>
[Serializable, NetSerializable]
public sealed class WFVehicleCrewPanelEntry
{
    public NetEntity Vehicle;
    public string Name = string.Empty;
    public string Crew = string.Empty;
    public WFVehicleBehaviour Behaviour;
    public bool Bailed;
    public int Alive;
    public int Total;

    /// <summary>Hull integrity from 0 to 1.</summary>
    public float Integrity;

    /// <summary>The map it's on and where on it.</summary>
    public string Location = string.Empty;
}

/// <summary>The server's answer: the live crews, and how the last request went.</summary>
[Serializable, NetSerializable]
public sealed class WFVehicleCrewPanelState : EntityEventArgs
{
    public List<WFVehicleCrewPanelEntry> Crews = new();
    public string? Message;
    public bool Error;
}
