namespace Content.Shared._WF.Wolfmed.Life;

/// <summary>
/// A pain shock has just fired on this body. Broadcast from Onyx's <c>PainSystem</c>, which raises nothing
/// of its own; BRAIN reads it because a shock on a body that has already bled out stops the heart.
/// </summary>
[ByRefEvent]
public readonly record struct WolfmedPainShockEvent(EntityUid Body);

/// <summary>
/// Asked while the concussion state is being rebuilt, so effects that are not wounds can contribute to it.
/// Brain organ damage and the trauma a repaired brain carries both answer here rather than making a second
/// blur-and-slur system of their own.
/// </summary>
[ByRefEvent]
public record struct WolfmedConcussionSourcesEvent(EntityUid Body, float Blur = 0f, bool Stutter = false,
    bool Drop = false);
