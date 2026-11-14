using Robust.Shared.GameStates;
using Robust.Shared.Player;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Hailing;

/// <summary>
/// One end of a ship-to-ship hail, on the shuttle console it runs through. It is only there while the
/// console is hailing, ringing or in a call, so a console without it is idle.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, UnsavedComponent]
public sealed partial class HailConsoleComponent : Component
{
    /// <summary>Longest line a console will send.</summary>
    public const int MaxLineLength = 300;

    /// <summary>Most answers one offer can hold.</summary>
    public const int MaxOptions = 4;

    /// <summary>Longest label an answer can have.</summary>
    public const int MaxOptionLength = 60;

    [ViewVariables, AutoNetworkedField]
    public HailState State = HailState.Calling;

    /// <summary>The ship on the other end, as this console's radar would label it.</summary>
    [ViewVariables, AutoNetworkedField]
    public string PeerShip = string.Empty;

    /// <summary>Who answered on the other end, once the channel is open.</summary>
    [ViewVariables, AutoNetworkedField]
    public string PeerName = string.Empty;

    /// <summary>The person on the other end, shown on this console's screen.</summary>
    [ViewVariables, AutoNetworkedField]
    public NetEntity? PeerOperator;

    /// <summary>Who is on the call at this console. Only they can transmit.</summary>
    [ViewVariables, AutoNetworkedField]
    public NetEntity? Operator;

    [ViewVariables, AutoNetworkedField]
    public List<HailLine> Log = new();

    /// <summary>
    /// Answers the other end has put in front of the person at this console. While there are any, they pick
    /// one instead of typing.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public List<HailOption> Options = new();

    /// <summary>
    /// This end is run by an NPC with no UI open: it counts as manned while its operator is alive and on the
    /// console's ship.
    /// </summary>
    [ViewVariables]
    public bool Automated;

    /// <summary>The other end is an admin speaking as God: there is no ship and nobody to show.</summary>
    [ViewVariables, AutoNetworkedField]
    public bool Divine;

    /// <summary>
    /// This end is not a console at all but an admin's stand-in, so none of the rules about power, range,
    /// radar or staying at the helm apply to it.
    /// </summary>
    [ViewVariables]
    public bool Remote;

    /// <summary>The console on the other end: the caller while ringing, the other party once connected.</summary>
    [ViewVariables]
    public EntityUid? Peer;

    /// <summary>The ship on the other end.</summary>
    [ViewVariables]
    public EntityUid? PeerGrid;

    [ViewVariables]
    public EntityUid? OperatorUid;

    /// <summary>Whoever was sent the other operator for the screen, and that operator.</summary>
    public ICommonSession? Viewer;

    [ViewVariables]
    public EntityUid? Viewed;

    /// <summary>That operator and everything they wear and carry, as last sent to the viewer.</summary>
    public HashSet<EntityUid> Sent = new();

    [ViewVariables]
    public TimeSpan StateTime;

    [ViewVariables]
    public TimeSpan NextRing;

    [ViewVariables]
    public int NextLine;
}

[Serializable, NetSerializable]
public enum HailState : byte
{
    /// <summary>This console hailed a ship and is waiting for an answer.</summary>
    Calling,

    /// <summary>Another ship is hailing this one.</summary>
    Ringing,

    Connected,
}

/// <summary>One answer the person at a console can pick.</summary>
[Serializable, NetSerializable]
public struct HailOption
{
    public string Id;
    public string Text;

    public HailOption(string id, string text)
    {
        Id = id;
        Text = text;
    }
}

/// <summary>One line of a hail's chat, as one end sees it.</summary>
[Serializable, NetSerializable]
public struct HailLine
{
    /// <summary>Counts up over the call, so a client can tell which lines it has shown.</summary>
    public int Sequence;

    public string Sender;
    public string Text;

    /// <summary>True on the console the line was sent from.</summary>
    public bool Own;
}

/// <summary>Why a hail or a call stopped.</summary>
[Serializable, NetSerializable]
public enum HailEndReason : byte
{
    /// <summary>This console closed it.</summary>
    Closed,

    /// <summary>The other end closed it.</summary>
    PeerClosed,

    Declined,
    NoAnswer,

    /// <summary>The ship can't be reached: out of range, off the scope or with no working console.</summary>
    Unavailable,

    Busy,

    /// <summary>The link dropped: power, range or a jump.</summary>
    Lost,

    /// <summary>This console hailed too recently.</summary>
    Cooldown,
}
