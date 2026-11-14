namespace Content.Server.Wires;

// WOLFGATE
public sealed partial class WiresSystem
{
    /// <summary>
    /// Rebuilds a board's wires for a grid that was loaded from a saved copy and so never raised
    /// MapInitEvent. None of the wire state is a data field, so a loaded board has no wires at all.
    /// The construction-graph step of map init is deliberately left out: its node actions are not
    /// safe to run twice.
    /// </summary>
    public void ReinitLoadedWires(EntityUid uid, WiresComponent? wires = null)
    {
        if (!Resolve(uid, ref wires, false))
            return;

        if (wires.WiresList.Count == 0 && !string.IsNullOrEmpty(wires.LayoutId))
            SetOrCreateWireLayout(uid, wires);

        if (wires.SerialNumber == null)
            GenerateSerialNumber(uid, wires);

        if (wires.WireSeed == 0)
            wires.WireSeed = _random.Next(1, int.MaxValue);

        UpdateUserInterface(uid);
    }
}
