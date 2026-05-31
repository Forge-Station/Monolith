using Robust.Shared.GameStates;

namespace Content.Shared._Forge.Movement.Components;

/// <summary>
/// Gas on this jetpack is thrust fuel only; it cannot be connected to breath mask internals.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class JetpackOnlyFuelComponent : Component;
