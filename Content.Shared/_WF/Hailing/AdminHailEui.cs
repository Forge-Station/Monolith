using Content.Shared.Eui;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Hailing;

/// <summary>Points an admin's hail window at the stand-in entity carrying their end of the hail.</summary>
[Serializable, NetSerializable]
public sealed class AdminHailEuiState : EuiStateBase
{
    public NetEntity Endpoint;

    public AdminHailEuiState(NetEntity endpoint)
    {
        Endpoint = endpoint;
    }
}

public static class AdminHailEuiMsg
{
    /// <summary>The admin sent a line down the channel.</summary>
    [Serializable, NetSerializable]
    public sealed class Say : EuiMessageBase
    {
        public string Text;

        public Say(string text)
        {
            Text = text;
        }
    }

    /// <summary>The admin cancelled the hail or closed the channel, keeping the window.</summary>
    [Serializable, NetSerializable]
    public sealed class End : EuiMessageBase;

    /// <summary>Tells the admin's window why the hail or call stopped.</summary>
    [Serializable, NetSerializable]
    public sealed class Ended : EuiMessageBase
    {
        public HailEndReason Reason;
        public string Ship;

        public Ended(HailEndReason reason, string ship)
        {
            Reason = reason;
            Ship = ship;
        }
    }
}
