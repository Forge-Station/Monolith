using Content.Shared._WF.NpcCrew;

namespace Content.Server._WF.NpcCrew.Components;

/// <summary>Configured responses to unauthorized visitors and arriving ships.</summary>
[RegisterComponent]
public sealed partial class WFCrewSecurityComponent : Component
{
    [DataField]
    public WFCrewSecurityResponse Boarding = WFCrewSecurityResponse.Hostile;

    [DataField]
    public WFCrewSecurityResponse Docking = WFCrewSecurityResponse.Hostile;

    /// <summary>Warn boarding: how long a stranger the crew has noticed may stay aboard before its fighters turn on him.</summary>
    [DataField]
    public TimeSpan WarnTime = TimeSpan.FromSeconds(30);
}
