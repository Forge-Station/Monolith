namespace Content.Shared._WF.Xenonids.Devour;

/// <summary>Raised on a user in combat mode to override whether a click on the target hand-interacts instead of attacking.</summary>
[ByRefEvent]
public record struct RMCCombatModeInteractOverrideUserEvent(EntityUid? Target, bool CanInteract = true, bool Handled = false);
