using Robust.Shared.GameStates;

namespace Content.Shared._EE.Contractors.Components;

[RegisterComponent, NetworkedComponent]
public sealed partial class PassportConsoleComponent : Component
{
    [ViewVariables]
    public Guid? SelectedId;
}
