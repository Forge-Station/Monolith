using Content.Shared.Preferences; // WOLFGATE

namespace Content.Shared._Shitmed.Humanoid.Events;

/// <summary>
///     Raised on an entity when their profile has finished being loaded
/// </summary>
public sealed class ProfileLoadFinishedEvent : EntityEventArgs
{
    public HumanoidCharacterProfile? Profile; // WOLFGATE - lets anatomy read the loaded profile
}

