namespace Content.Shared._Forge.Trade;


/// <summary>
///     Marks an entity as rejected by contract turn-in planning.
///     Used for items returned after a contract claim so they cannot be submitted again.
/// </summary>
[RegisterComponent]
public sealed partial class NcContractTurnInBlockedComponent : Component
{
}
