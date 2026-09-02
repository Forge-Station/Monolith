namespace Content.Shared._Forge.Features.Components;

/// <summary>
/// Allows <see cref="Content.Shared.StepTrigger.Components.StepTriggerComponent"/> to fire.
/// Step triggers stay inert unless some system sets Continue on the attempt event.
/// </summary>
[RegisterComponent]
public sealed partial class AllowStepTriggerComponent : Component;
