using Content.Shared.Actions;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.Wolfmed.Consciousness;

/// <summary>
/// M2 (plan §5.3, OD20): a Downed body lying still on purpose. Examine at range reads "appears lifeless" until the
/// player moves, speaks or does anything to anything. Replaces upstream's Fake Death, which only ever existed in crit.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WolfmedPlayingDeadComponent : Component;

/// <summary>M2 (plan §5.3): the crawling stage's extra actions a body holds right now. Server only.</summary>
[RegisterComponent]
public sealed partial class WolfmedCrawlActionsComponent : Component
{
    /// <summary>Play dead, while Downed.</summary>
    [DataField]
    public EntityUid? PlayDead;

    /// <summary>Check yourself, while Up or Downed.</summary>
    [DataField]
    public EntityUid? CheckYourself;
}

/// <summary>M2 (OD20): lie still and look dead, or stop.</summary>
public sealed partial class WolfmedPlayDeadActionEvent : InstantActionEvent;

/// <summary>M2 (plan §5.3): your own symptoms in words, what you would see examining yourself plus your condition.</summary>
public sealed partial class WolfmedCheckYourselfActionEvent : InstantActionEvent;
