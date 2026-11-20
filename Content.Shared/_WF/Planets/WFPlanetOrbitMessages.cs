using Robust.Shared.Serialization;

namespace Content.Shared._WF.Planets;

/// <summary>Sent by the shuttle console when the pilot asks to drop into a sector body's orbit layer.</summary>
[Serializable, NetSerializable]
public sealed class WFEnterPlanetOrbitMessage : BoundUserInterfaceMessage
{
    /// <summary>The console the request came from; the server re-resolves the hull from it.</summary>
    public NetEntity Console;

    /// <summary>The sector body whose orbit was asked for, so a stale button cannot silently target another world.</summary>
    public NetEntity Planet;

    /// <summary>True once the pilot has pressed through the unsanctioned-world warning.</summary>
    public bool Confirmed;

    public WFEnterPlanetOrbitMessage(NetEntity console, NetEntity planet, bool confirmed = false)
    {
        Console = console;
        Planet = planet;
        Confirmed = confirmed;
    }
}

/// <summary>Sent by the shuttle console when the pilot asks to climb out of orbit back onto the sector map.</summary>
[Serializable, NetSerializable]
public sealed class WFLeavePlanetOrbitMessage : BoundUserInterfaceMessage
{
    /// <summary>The console the request came from; the server re-resolves the hull from it.</summary>
    public NetEntity Console;

    public WFLeavePlanetOrbitMessage(NetEntity console)
    {
        Console = console;
    }
}

/// <summary>Sent by the shuttle console to engage or cancel its grounded planet liftoff latch.</summary>
[Serializable, NetSerializable]
public sealed class WFLiftoffMessage : BoundUserInterfaceMessage
{
    /// <summary>The console the request came from; the server re-resolves the hull and current pilot from it.</summary>
    public NetEntity Console;

    public WFLiftoffMessage(NetEntity console)
    {
        Console = console;
    }
}
