using Robust.Shared.Serialization;

namespace Content.Shared._WF.Newbie;

/// <summary>
/// Sent once per join to a new player: opens the welcome popup, and the crew popup if <see cref="Fresh"/>.
/// </summary>
[Serializable, NetSerializable]
public sealed class WFNewbieWelcomeEvent : EntityEventArgs
{
    /// <summary>The player is under <see cref="WFNewbieCVars.FreshHours"/> and should find a crew first.</summary>
    public bool Fresh;

    public WFNewbieWelcomeEvent(bool fresh)
    {
        Fresh = fresh;
    }
}
