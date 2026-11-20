using Content.Shared.Timing.Components;
using Content.Shared.Timing.Systems;

namespace Content.Server._WF.SafetyDepositBox;

// Wolfgate's cleanup of saved cooldowns on withdrawn items, kept out of the upstream file.
public sealed partial class SafetyDepositBoxSystem
{
    [Dependency] private UseDelaySystem _useDelay = default!;

    /// <summary>
    /// Ends the cooldowns a withdrawn item and its contents were saved with. They hold the old round's clock, so a
    /// tool saved late in a round stayed on cooldown (jaws of life wouldn't pry) for that long into the next.
    /// </summary>
    private void ClearSavedCooldowns(EntityUid item)
    {
        if (TryComp<UseDelayComponent>(item, out var delay))
        {
            foreach (var id in delay.Delays.Keys)
                _useDelay.CancelDelay((item, delay), id);
        }

        var children = Transform(item).ChildEnumerator;
        while (children.MoveNext(out var child))
            ClearSavedCooldowns(child);
    }
}
