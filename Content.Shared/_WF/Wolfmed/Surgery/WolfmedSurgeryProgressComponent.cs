using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Wolfmed.Surgery;

/// <summary>
/// Wolfmed procedures a surgeon has begun on this part and not yet closed. Their listing conditions hold until the
/// closing step runs, so the treatment that removes the reason a procedure was listed cannot strand the incision.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WolfmedSurgeryProgressComponent : Component
{
    [DataField, AutoNetworkedField]
    public List<EntProtoId> Surgeries = new();
}
