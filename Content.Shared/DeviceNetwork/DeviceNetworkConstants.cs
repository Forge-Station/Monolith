namespace Content.Shared.DeviceNetwork;

/// <summary>
/// Keys for the older dictionary device-network packets. Suit sensors still read these when rebuilding a status from a payload.
/// </summary>
public static class DeviceNetworkConstants
{
    public const string Command = "command";

    public const string CmdUpdatedState = "updated_state";
}
