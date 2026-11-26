using Robust.Shared.GameStates;

namespace Content.Shared._WF.Administration.AdminRadar;

/// <summary>A grid the admin scanner never labels, even with true IFF on, such as a drifting asteroid.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WFAdminRadarUnlabelledComponent : Component;
