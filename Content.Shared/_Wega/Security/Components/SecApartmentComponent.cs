// Forge-Change
using Content.Shared.Medical.SuitSensor;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared.SecApartment;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SecApartmentComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? Station;

    /// <summary>
    /// Department whose crew can be assigned to squads by this tablet.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string Department = "Security";

    /// <summary>
    /// If true, the user must have a job from <see cref="Department"/> to open the tablet.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool RequireUserDepartment;

    /// <summary>
    /// Prefix used to resolve squad status icon prototype IDs.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string SquadIconPrefix = "SecuritySquadIcon";

    /// <summary>
    /// When enabled, any crew role from the station manifest can be assigned to squads.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Debug;

    /// <summary>
    /// Signal timers linked to this tablet by clicking the tablet on them.
    /// </summary>
    [DataField, AutoNetworkedField]
    public List<NetEntity> TrackedTimers = new();
}

[Serializable, NetSerializable]
public sealed class Squad
{
    public string SquadId { get; set; }
    public string Department { get; set; }
    public string SquadIconPrefix { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public List<CrewMemberInfo> Members { get; set; } = new();
    public SquadStatus Status { get; set; } = SquadStatus.Active;
    public SquadIconNum IconId { get; set; } = SquadIconNum.Alpha;

    public Squad(string squadId, string name, string department = "Security", string squadIconPrefix = "SecuritySquadIcon")
    {
        SquadId = squadId;
        Department = department;
        SquadIconPrefix = squadIconPrefix;
        Name = name;
        Description = string.Empty;
    }
}

[Serializable, NetSerializable]
public sealed class CrewMemberInfo
{
    public string MemberId { get; }
    public NetEntity? OwnerUid { get; set; }
    public string Name { get; }
    public string JobTitle { get; }
    public string JobIcon { get; }
    public SuitSensorStatus? SensorStatus { get; }

    public CrewMemberInfo(string memberId, NetEntity? ownerUid, string name, string jobTitle, string jobIcon, SuitSensorStatus? suitSensor)
    {
        MemberId = memberId;
        OwnerUid = ownerUid;
        Name = name;
        JobTitle = jobTitle;
        JobIcon = jobIcon;
        SensorStatus = suitSensor;
    }
}

[Serializable, NetSerializable]
public sealed class TimerEntry
{
    public NetEntity TimerUid { get; set; }
    public string Label { get; set; }
    public TimeSpan RemainingTime { get; set; }
    public TimeSpan TotalTime { get; set; }
    public TimeSpan? FinishedAt { get; set; }

    public TimerEntry(NetEntity timerUid, string label, TimeSpan remainingTime, TimeSpan totalTime, TimeSpan? finishedAt = null)
    {
        TimerUid = timerUid;
        Label = label;
        RemainingTime = remainingTime;
        TotalTime = totalTime;
        FinishedAt = finishedAt;
    }
}
