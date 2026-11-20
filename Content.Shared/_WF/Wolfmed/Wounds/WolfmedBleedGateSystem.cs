using Content.Shared.Body.Part;
using Content.Shared.Inventory;

namespace Content.Shared._WF.Wolfmed.Wounds;

/// <summary>
/// Raised on a body, and relayed to its worn items, before a hit opens or deepens a bleed. Cancel it to stop the bleed.
/// </summary>
[ByRefEvent]
public record struct WolfmedBleedAttemptEvent(bool Cancelled = false) : IInventoryRelayEvent
{
    public SlotFlags TargetSlots => SlotFlags.WITHOUT_POCKET;
}

/// <summary>Asks a body whether a hit may make it bleed, through <see cref="WolfmedBleedAttemptEvent"/>.</summary>
public sealed class WolfmedBleedGateSystem : EntitySystem
{
    private int _bypass;

    /// <summary>Runs <paramref name="action"/> with the gate open, for bleeding no hit caused (a reagent, a script).</summary>
    public T Bypass<T>(Func<T> action)
    {
        _bypass++;
        try
        {
            return action();
        }
        finally
        {
            _bypass--;
        }
    }

    /// <summary>False when the body or something it wears refuses a new or deeper bleed.</summary>
    public bool CanStartBleeding(EntityUid body)
    {
        if (_bypass > 0)
            return true;

        var ev = new WolfmedBleedAttemptEvent();
        RaiseLocalEvent(body, ref ev);
        return !ev.Cancelled;
    }

    /// <summary><see cref="CanStartBleeding"/> for the body a part is attached to. A loose part always may.</summary>
    public bool PartCanStartBleeding(EntityUid part)
    {
        return !TryComp<BodyPartComponent>(part, out var comp) || comp.Body is not { } body || CanStartBleeding(body);
    }
}
