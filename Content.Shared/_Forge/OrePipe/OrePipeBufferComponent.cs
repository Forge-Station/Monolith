using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.OrePipe;

/// <summary>
/// Abstract ore backlog on a drill / inlet. Counts are ore entity prototypes, not world entities.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OrePipeBufferComponent : Component
{
    /// <summary>
    /// Max total ore units held before the drill refuses to gather more.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public int MaxTotalCount = 200;

    /// <summary>
    /// Ore entity prototype id → unit count waiting for the next pipe flush.
    /// </summary>
    [DataField, ViewVariables, AutoNetworkedField]
    public Dictionary<EntProtoId, int> Contents = new();

    [ViewVariables]
    public int TotalCount
    {
        get
        {
            var total = 0;
            foreach (var count in Contents.Values)
                total += count;
            return total;
        }
    }
}
