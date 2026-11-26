namespace Content.Shared._WF.CustomMarkings;

// Forge-Change: moved here from the removed anatomy module, custom markings still listen for it.
/// <summary>Client-only, raised by HumanoidAppearanceSystem.UpdateSprite so markings follow a sprite rebuild.</summary>
public sealed class HumanoidMarkingsAppliedEvent : EntityEventArgs;
