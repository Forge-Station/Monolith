using System.Diagnostics.CodeAnalysis;
using Content.Shared._WF.Lathe;
using Content.Shared.ActionBlocker;
using Content.Shared.Administration.Logs;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Database;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Lathe;

/// <summary>
/// Takes single reagents out of a chemical silo: withdrawn into the slotted container, or discarded.
/// </summary>
public sealed partial class FabricationSiloSystem
{
    [Dependency] private ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;

    // Smaller discards are not admin-logged.
    private static readonly FixedPoint2 DiscardLogThreshold = FixedPoint2.New(10);

    private void InitializeChemicals()
    {
        SubscribeLocalEvent<FabricationSiloComponent, WithdrawFabricationSiloReagentMessage>(OnWithdrawReagent);
        SubscribeLocalEvent<FabricationSiloComponent, DiscardFabricationSiloReagentMessage>(OnDiscardReagent);
    }

    /// <summary>Moves one reagent into the slotted container (null: as much as fits); returns the amount moved.</summary>
    public FixedPoint2 WithdrawReagent(Entity<FabricationSiloComponent> ent,
        EntityUid user,
        ProtoId<ReagentPrototype> reagent,
        FixedPoint2? amount)
    {
        if (amount is { } requested && requested <= FixedPoint2.Zero ||
            !TryGetStock(ent, user, reagent, out var soln, out var solution, out var stored))
            return FixedPoint2.Zero;

        if (!TryGetSlottedSolution(ent, out var container, out var target, out var targetSolution))
        {
            _popup.PopupEntity(Loc.GetString("fabrication-silo-no-container"), ent, user);
            return FixedPoint2.Zero;
        }

        var take = FixedPoint2.Min(stored, targetSolution.AvailableVolume);
        if (amount != null)
            take = FixedPoint2.Min(take, amount.Value);

        if (take <= FixedPoint2.Zero)
        {
            _popup.PopupEntity(Loc.GetString("fabrication-silo-container-full", ("container", container)), ent, user);
            return FixedPoint2.Zero;
        }

        var moved = solution.SplitSolutionWithOnly(take, reagent.Id);
        _solutions.UpdateChemicals(soln.Value);
        _solutions.TryAddSolution(target.Value, moved);

        _adminLogger.Add(LogType.Action, LogImpact.Low,
            $"{ToPrettyString(user):player} withdrew {SharedSolutionContainerSystem.ToPrettyString(moved)} from {ToPrettyString(ent.Owner):entity} into {ToPrettyString(container):target}");

        return moved.Volume;
    }

    /// <summary>
    /// Deletes every unit of one reagent from a chemical silo and returns the amount deleted.
    /// </summary>
    public FixedPoint2 DiscardReagent(Entity<FabricationSiloComponent> ent, EntityUid user, ProtoId<ReagentPrototype> reagent)
    {
        if (!TryGetStock(ent, user, reagent, out var soln, out var solution, out var stored))
            return FixedPoint2.Zero;

        var removed = solution.SplitSolutionWithOnly(stored, reagent.Id);
        _solutions.UpdateChemicals(soln.Value);

        if (removed.Volume >= DiscardLogThreshold)
        {
            _adminLogger.Add(LogType.Action, LogImpact.Low,
                $"{ToPrettyString(user):player} discarded {SharedSolutionContainerSystem.ToPrettyString(removed)} from {ToPrettyString(ent.Owner):entity}");
        }

        return removed.Volume;
    }

    /// <summary>
    /// Checks the user can work the silo and the silo holds the reagent.
    /// </summary>
    private bool TryGetStock(Entity<FabricationSiloComponent> ent,
        EntityUid user,
        ProtoId<ReagentPrototype> reagent,
        [NotNullWhen(true)] out Entity<SolutionComponent>? soln,
        [NotNullWhen(true)] out Solution? solution,
        out FixedPoint2 stored)
    {
        stored = FixedPoint2.Zero;
        if (!TryGetSolution(ent, out soln, out solution) ||
            !_prototypes.HasIndex(reagent) ||
            !_actionBlocker.CanInteract(user, ent.Owner) ||
            !_interaction.InRangeUnobstructed(user, ent.Owner))
            return false;

        stored = solution.GetTotalPrototypeQuantity(reagent.Id);
        return stored > FixedPoint2.Zero;
    }

    private void OnWithdrawReagent(Entity<FabricationSiloComponent> ent, ref WithdrawFabricationSiloReagentMessage args)
    {
        WithdrawReagent(ent, args.Actor, args.Reagent, args.Amount);
    }

    private void OnDiscardReagent(Entity<FabricationSiloComponent> ent, ref DiscardFabricationSiloReagentMessage args)
    {
        DiscardReagent(ent, args.Actor, args.Reagent);
    }
}
