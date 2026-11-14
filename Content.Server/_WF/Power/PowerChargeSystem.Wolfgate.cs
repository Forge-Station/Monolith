using Content.Server.Power.Components;

namespace Content.Server.Power.EntitySystems;

// WOLFGATE
public sealed partial class PowerChargeSystem
{
    /// <summary>
    /// Redoes a charged machine's map init for a grid that was loaded from a saved copy. The charge
    /// itself persists, but nothing re-announces it, so a gravity generator that was running before
    /// the sale comes back with the ship's gravity off.
    /// </summary>
    public void ReinitLoadedCharge(EntityUid uid, PowerChargeComponent? charge = null)
    {
        if (!Resolve(uid, ref charge, false) || !TryComp<ApcPowerReceiverComponent>(uid, out var receiver))
            return;

        UpdatePowerState(charge, receiver);
        UpdateState((uid, charge, receiver));

        if (!charge.Intact || !charge.Active)
            return;

        // Consumers track activation themselves and ignore a repeat, so this is safe to run twice.
        var ev = new ChargedMachineActivatedEvent();
        RaiseLocalEvent(uid, ref ev);
    }
}
