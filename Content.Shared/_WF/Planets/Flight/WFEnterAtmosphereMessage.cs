using Robust.Shared.Serialization;

namespace Content.Shared._WF.Planets.Flight;

/// <summary>
/// Sent by the shuttle console to drop out of orbit into the atmosphere; a hull without enough lift must confirm.
/// </summary>
[Serializable, NetSerializable]
public sealed class WFEnterAtmosphereMessage : BoundUserInterfaceMessage
{
    /// <summary>The console the request came from; the server re-resolves the hull from it.</summary>
    public NetEntity Console;

    /// <summary>True once the pilot has answered the lift warning, which is the only thing that lets a sinker through.</summary>
    public bool Confirmed;

    public WFEnterAtmosphereMessage(NetEntity console, bool confirmed)
    {
        Console = console;
        Confirmed = confirmed;
    }
}
