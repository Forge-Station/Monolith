using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Lathe;

/// <summary>
/// Ejects one stored part or stack.
/// </summary>
[Serializable, NetSerializable]
public sealed class EjectFabricationSiloPartMessage(NetEntity part) : BoundUserInterfaceMessage
{
    /// <summary>
    /// The stored part to eject.
    /// </summary>
    public readonly NetEntity Part = part;
}

/// <summary>
/// Moves one stored reagent, with its data, into the container in a chemical silo's slot.
/// </summary>
[Serializable, NetSerializable]
public sealed class WithdrawFabricationSiloReagentMessage(ProtoId<ReagentPrototype> reagent, FixedPoint2? amount)
    : BoundUserInterfaceMessage
{
    /// <summary>
    /// The reagent to withdraw.
    /// </summary>
    public readonly ProtoId<ReagentPrototype> Reagent = reagent;

    /// <summary>
    /// Most to withdraw; null withdraws as much as fits.
    /// </summary>
    public readonly FixedPoint2? Amount = amount;
}

/// <summary>
/// Deletes every unit of one reagent from a chemical silo.
/// </summary>
[Serializable, NetSerializable]
public sealed class DiscardFabricationSiloReagentMessage(ProtoId<ReagentPrototype> reagent) : BoundUserInterfaceMessage
{
    /// <summary>
    /// The reagent to discard.
    /// </summary>
    public readonly ProtoId<ReagentPrototype> Reagent = reagent;
}
