/// Forge-Change-Start
using Robust.Shared.GameStates;

namespace Content.Shared._NF.Emp.Components;
/// <summary>
///     Temporary payload used to pass desired visual range into <see cref="EmpBlastSystem"/> on startup.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class EmpBlastRangeComponent : Component
{
    [DataField, AutoNetworkedField]
    public float Range = 5f;
}
/// Forge-Change-End
