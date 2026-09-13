using Content.Shared.Preferences;

namespace Content.Shared._WF.Genitals;

/// <summary>Raised on the body after arousal changes. Old/new values and states, plus the cause.</summary>
[ByRefEvent]
public readonly record struct ArousalChangedEvent(byte Old, byte New, ArousalState OldState, ArousalState NewState, EntityUid? Source);

/// <summary>Raised on the body after Recompute changed any organ mirror field. Server only.</summary>
[ByRefEvent]
public readonly record struct GenitalsChangedEvent;

/// <summary>Broadcast one tick after an anatomy consent toggle changed on a body, so helpers read the new toggles. Server only.</summary>
[ByRefEvent]
public readonly record struct GenitalConsentChangedEvent(EntityUid Body);

/// <summary>Raised on the body after a shared handler changed a visual field, so a predicting client refreshes at once.</summary>
[ByRefEvent]
public readonly record struct GenitalsVisualsChangedEvent;

/// <summary>Client-only, raised on a lobby doll by the client LoadProfile override.</summary>
public sealed class GenitalPreviewProfileLoadedEvent(HumanoidCharacterProfile profile) : EntityEventArgs
{
    public readonly HumanoidCharacterProfile Profile = profile;
}

/// <summary>Client-only, raised by HumanoidAppearanceSystem.UpdateSprite so genital visuals follow marking rebuilds.</summary>
public sealed class HumanoidMarkingsAppliedEvent : EntityEventArgs;
