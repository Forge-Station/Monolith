using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;

namespace Content.Shared._Forge.Materials;

/// <summary>
/// Applied to a mob that is carrying a heavy material stack.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class BulkCarrySlowdownComponent : Component
{
    [DataField, AutoNetworkedField]
    public float Modifier = 1f;
}
