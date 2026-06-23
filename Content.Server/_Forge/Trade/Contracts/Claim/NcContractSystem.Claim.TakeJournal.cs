using Content.Shared._Forge.Trade;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.FixedPoint;
using Content.Shared.Stacks;

namespace Content.Server._Forge.Trade;

public sealed partial class NcContractSystem
{
    private void CommitClaimTakeJournal(
        ClaimTakeJournal journal,
        EntityUid receiver,
        float returnFraction = 0f
    )
    {
        ReturnClaimItemsBestEffort(journal, receiver, returnFraction);

        for (var i = 0; i < journal.PendingDeletes.Count; i++)
        {
            var ent = journal.PendingDeletes[i];
            DeleteFinalEntityBestEffort(ent, "ClaimTake");
        }

        journal.Clear();
    }

    private void ReturnClaimItemsBestEffort(
        ClaimTakeJournal journal,
        EntityUid receiver,
        float returnFraction
    )
    {
        if (returnFraction <= 0f || journal.ReturnCandidates.Count == 0)
            return;

        if (!Exists(receiver) || !TryComp(receiver, out TransformComponent? receiverXform))
            return;

        var returnBudget = CalculateClaimReturnBudget(journal.ReturnCandidates, returnFraction);
        if (returnBudget <= 0)
            return;

        for (var i = 0; i < journal.ReturnCandidates.Count && returnBudget > 0; i++)
        {
            var entry = journal.ReturnCandidates[i];
            if (entry.MatchMode == PrototypeMatchMode.Reagent)
            {
                returnBudget -= ReturnClaimReagentUnitsBestEffort(entry, receiver, receiverXform, returnBudget);
                continue;
            }

            if (ReturnClaimItemBestEffort(entry.Entity, receiver, receiverXform))
                returnBudget--;
        }
    }

    private static int CalculateClaimReturnBudget(
        List<ClaimTakeEntry> returnCandidates,
        float returnFraction
    )
    {
        if (returnFraction <= 0f || returnCandidates.Count == 0)
            return 0;

        var totalUnits = 0;
        for (var i = 0; i < returnCandidates.Count; i++)
            totalUnits = SaturatingAdd(totalUnits, Math.Max(0, returnCandidates[i].Amount));

        return (int) MathF.Floor(totalUnits * Math.Clamp(returnFraction, 0f, 1f));
    }

    private bool ReturnClaimItemBestEffort(
        EntityUid ent,
        EntityUid receiver,
        TransformComponent receiverXform
    )
    {
        if (!TryGetPlanningEntityPrototypeId(ent, out var prototypeId))
            return false;

        try
        {
            var returned = Spawn(prototypeId, receiverXform.Coordinates);
            CopySolutionsBestEffort(ent, returned);
            EnsureComp<NcContractTurnInBlockedComponent>(returned);
            _logic.QueuePickupToHandsOrCrateNextTick(receiver, returned);
            return true;
        }
        catch (Exception e)
        {
            Sawmill.Warning(
                $"[Claim] Failed to return consumed contract item prototype '{prototypeId}' to {ToPrettyString(receiver)}: {e}");
            return false;
        }
    }

    private int ReturnClaimReagentUnitsBestEffort(
        ClaimTakeEntry entry,
        EntityUid receiver,
        TransformComponent receiverXform,
        int maxUnits
    )
    {
        var units = Math.Min(Math.Max(0, entry.Amount), maxUnits);
        if (units <= 0)
            return 0;

        if (!TryGetPlanningEntityPrototypeId(entry.Entity, out var prototypeId))
            return 0;

        if (!TryResolveClaimReagentReturnSolution(
                entry.Entity,
                entry.TargetItem,
                entry.Solution,
                entry.ReagentAmount,
                out var solutionName,
                out var maxVolume))
            return 0;

        var returnedCount = 0;
        for (var i = 0; i < units; i++)
        {
            try
            {
                var returned = Spawn(prototypeId, receiverXform.Coordinates);
                if (!TryFillReturnedReagentUnit(returned, solutionName, maxVolume, entry.TargetItem, entry.ReagentAmount))
                {
                    DeleteFinalEntityBestEffort(returned, "ClaimReturnFailed");
                    continue;
                }

                EnsureComp<NcContractTurnInBlockedComponent>(returned);
                _logic.QueuePickupToHandsOrCrateNextTick(receiver, returned);
                returnedCount++;
            }
            catch (Exception e)
            {
                Sawmill.Warning(
                    $"[Claim] Failed to return consumed reagent unit prototype '{prototypeId}' to {ToPrettyString(receiver)}: {e}");
            }
        }

        return returnedCount;
    }

    private bool TryResolveClaimReagentReturnSolution(
        EntityUid source,
        string reagentId,
        string solution,
        FixedPoint2 reagentAmount,
        out string solutionName,
        out FixedPoint2 maxVolume
    )
    {
        solutionName = string.IsNullOrWhiteSpace(solution) || IsAnySolutionName(solution)
            ? "drink"
            : solution;
        maxVolume = FixedPoint2.Max(reagentAmount, FixedPoint2.New(1));

        if (!TryComp(source, out SolutionContainerManagerComponent? manager))
            return true;

        if (!IsAnySolutionName(solution))
        {
            if (!_solutions.TryGetSolution((source, manager), solution, out _, out var sourceSolution))
                return true;

            solutionName = solution;
            maxVolume = FixedPoint2.Max(sourceSolution.MaxVolume, reagentAmount);
            return true;
        }

        foreach (var (name, solutionEnt) in _solutions.EnumerateSolutions((source, manager), false))
        {
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var sourceSolution = solutionEnt.Comp.Solution;
            if (sourceSolution.GetTotalPrototypeQuantity(reagentId) < reagentAmount)
                continue;

            solutionName = name;
            maxVolume = FixedPoint2.Max(sourceSolution.MaxVolume, reagentAmount);
            return true;
        }

        return true;
    }

    private bool TryFillReturnedReagentUnit(
        EntityUid returned,
        string solutionName,
        FixedPoint2 maxVolume,
        string reagentId,
        FixedPoint2 reagentAmount
    )
    {
        if (string.IsNullOrWhiteSpace(solutionName) ||
            string.IsNullOrWhiteSpace(reagentId) ||
            reagentAmount <= FixedPoint2.Zero)
            return false;

        if (!_solutions.EnsureSolutionEntity((returned, null), solutionName, out var targetSolutionEnt, maxVolume) ||
            targetSolutionEnt == null)
            return false;

        _solutions.RemoveAllSolution(targetSolutionEnt.Value);
        _solutions.SetCapacity(targetSolutionEnt.Value, FixedPoint2.Max(maxVolume, reagentAmount));
        return _solutions.TryAddSolution(targetSolutionEnt.Value, new Solution(reagentId, reagentAmount));
    }

    private void CopySolutionsBestEffort(EntityUid source, EntityUid target)
    {
        if (!TryComp(source, out SolutionContainerManagerComponent? sourceManager))
            return;

        foreach (var (name, sourceSolutionEnt) in _solutions.EnumerateSolutions((source, sourceManager), false))
        {
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var sourceSolution = sourceSolutionEnt.Comp.Solution;
            if (!_solutions.EnsureSolutionEntity((target, null), name, out var targetSolutionEnt, sourceSolution.MaxVolume) ||
                targetSolutionEnt == null)
                continue;

            _solutions.RemoveAllSolution(targetSolutionEnt.Value);
            _solutions.SetCapacity(targetSolutionEnt.Value, sourceSolution.MaxVolume);
            _solutions.TryAddSolution(targetSolutionEnt.Value, new(sourceSolution));
        }
    }

    private void RollbackClaimTakeJournal(ClaimTakeJournal journal)
    {
        RestoreClaimTurnInState(journal);
        RestoreClaimRetrievalCargo(journal);
        RestoreClaimStacks(journal);
        journal.Clear();
    }

    private static void RestoreClaimTurnInState(ClaimTakeJournal journal)
    {
        if (journal.TurnInState == null)
            return;

        for (var i = journal.TurnInRestores.Count - 1; i >= 0; i--)
        {
            RestoreClaimTurnInEntry(journal.TurnInState, journal.TurnInRestores[i]);
        }
    }

    private static void RestoreClaimTurnInEntry(ObjectiveRuntimeState state, TurnInRestore restore)
    {
        if (restore.HadValue)
            state.TurnedInByTarget[restore.Key] = restore.PreviousValue;
        else
            state.TurnedInByTarget.Remove(restore.Key);
    }

    private void RestoreClaimRetrievalCargo(ClaimTakeJournal journal)
    {
        for (var i = journal.RetrievalCargoRestores.Count - 1; i >= 0; i--)
        {
            RestoreClaimRetrievalCargoEntry(journal.RetrievalCargoRestores[i]);
        }
    }

    private void RestoreClaimRetrievalCargoEntry((EntityUid Cargo, (EntityUid Store, string ContractId) Key) restore)
    {
        if (Exists(restore.Cargo))
            _objectiveRuntime.ByRetrievalCargo[restore.Cargo] = restore.Key;
    }

    private void RestoreClaimStacks(ClaimTakeJournal journal)
    {
        for (var i = journal.StackRestores.Count - 1; i >= 0; i--)
        {
            RestoreClaimStackEntry(journal.StackRestores[i]);
        }
    }

    private void RestoreClaimStackEntry((EntityUid Ent, int PreviousCount) restore)
    {
        if (TryComp(restore.Ent, out StackComponent? stack))
            _stacks.SetCount(restore.Ent, restore.PreviousCount, stack);
    }

    private sealed class ClaimTakeJournal
    {
        public readonly List<EntityUid> PendingDeletes = new();
        public readonly List<ClaimTakeEntry> ReturnCandidates = new();

        public readonly List<(EntityUid Cargo, (EntityUid Store, string ContractId) Key)>
            RetrievalCargoRestores = new();

        public readonly List<(EntityUid Ent, int PreviousCount)> StackRestores = new();
        public readonly List<TurnInRestore> TurnInRestores = new();
        public ObjectiveRuntimeState? TurnInState;

        public void TrackStack(EntityUid ent, int previousCount)
        {
            for (var i = 0; i < StackRestores.Count; i++)
            {
                if (StackRestores[i].Ent == ent)
                    return;
            }

            StackRestores.Add((ent, previousCount));
        }

        public void TrackRetrievalCargo(EntityUid cargo, (EntityUid Store, string ContractId) key)
        {
            for (var i = 0; i < RetrievalCargoRestores.Count; i++)
            {
                if (RetrievalCargoRestores[i].Cargo == cargo)
                    return;
            }

            RetrievalCargoRestores.Add((cargo, key));
        }

        public void TrackTurnIn(
            ObjectiveRuntimeState state,
            (string TargetItem, PrototypeMatchMode MatchMode) key
        )
        {
            TurnInState ??= state;

            for (var i = 0; i < TurnInRestores.Count; i++)
            {
                if (TurnInRestores[i].Key == key)
                    return;
            }

            var hadValue = state.TurnedInByTarget.TryGetValue(key, out var previousValue);
            TurnInRestores.Add(new TurnInRestore(key, hadValue, previousValue));
        }

        public void Clear()
        {
            PendingDeletes.Clear();
            ReturnCandidates.Clear();
            RetrievalCargoRestores.Clear();
            StackRestores.Clear();
            TurnInRestores.Clear();
            TurnInState = null;
        }
    }

    private readonly record struct TurnInRestore(
        (string TargetItem, PrototypeMatchMode MatchMode) Key,
        bool HadValue,
        int PreviousValue);
}
