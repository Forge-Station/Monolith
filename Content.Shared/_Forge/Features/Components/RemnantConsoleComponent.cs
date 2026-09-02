using Robust.Shared.GameStates;

namespace Content.Shared._Forge.Features.Components;

/// <summary>
/// Console with mapper-renameable signal buttons. Stored text is shown when probed with a multitool.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class RemnantConsoleComponent : Component
{
    /// <summary>
    /// Flavor / log text shown when a network configurator is used on this console.
    /// </summary>
    [DataField]
    public string Message = string.Empty;

    [DataField]
    public List<RemnantConsoleButton> Buttons = new();
}

[DataDefinition]
public sealed partial class RemnantConsoleButton
{
    [DataField(required: true)]
    public string Port = "Pressed";

    [DataField(required: true)]
    public string Label = "Button";
}
