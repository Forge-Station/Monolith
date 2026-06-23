using Content.Shared._Forge.Trade;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Stacks;

namespace Content.Server._Forge.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private const string AnySolutionName = "any";
    private readonly List<(string ProtoId, PrototypeMatchMode MatchMode, int Depth)> _claimOrderedKeysScratch = new();
    private readonly Dictionary<(string ProtoId, PrototypeMatchMode MatchMode), int> _claimRequiredByKeyScratch = new();
    private readonly Dictionary<EntityUid, int> _claimVirtualStackLeftScratch = new();

    private void BuildOrderedRequiredKeys(
        Dictionary<(string ProtoId, PrototypeMatchMode MatchMode), int> requiredByKey,
        List<(string ProtoId, PrototypeMatchMode MatchMode, int Depth)> orderedKeys
    )
    {
        orderedKeys.Clear();

        foreach (var (key, required) in requiredByKey)
        {
            if (required <= 0)
                continue;

            orderedKeys.Add((key.ProtoId, key.MatchMode, GetProtoDepth(key.ProtoId)));
        }

        orderedKeys.Sort(static (a, b) =>
        {
            var depth = b.Depth.CompareTo(a.Depth);
            if (depth != 0)
                return depth;

            var mode = ((int)a.MatchMode).CompareTo((int)b.MatchMode);
            if (mode != 0)
                return mode;

            return string.CompareOrdinal(a.ProtoId, b.ProtoId);
        });
    }

    private void ClearClaimPlanningScratch()
    {
        _claimRequiredByKeyScratch.Clear();
        _claimOrderedKeysScratch.Clear();
        _claimVirtualStackLeftScratch.Clear();
    }

    private bool MatchesPrototypeId(
        EntityUid candidateEntity,
        string candidateId,
        string expectedProtoId,
        PrototypeMatchMode matchMode
    )
    {
        if (matchMode == PrototypeMatchMode.Tag)
        {
            return TryResolveContractTagTargetId(expectedProtoId, out var tagId) &&
                   ContractPrototypeHasTag(candidateId, tagId);
        }

        if (matchMode != PrototypeMatchMode.Matcher)
            return candidateId == expectedProtoId;

        if (!TryGetContractMatcherSpec(expectedProtoId, out var matcher))
            return false;

        if (matcher.MatchItems.Contains(candidateId))
            return true;

        if (TryComp(candidateEntity, out StackComponent? stack) &&
            !string.IsNullOrWhiteSpace(stack.StackTypeId) &&
            matcher.MatchStackTypes.Contains(stack.StackTypeId))
            return true;

        return false;
    }

    private bool MatchesReagentTarget(
        EntityUid candidateEntity,
        string reagentId,
        string solutionName,
        FixedPoint2 requiredAmount
    )
    {
        if (string.IsNullOrWhiteSpace(reagentId) || string.IsNullOrWhiteSpace(solutionName))
            return false;

        if (requiredAmount <= FixedPoint2.Zero)
            return false;

        return CountReagentTargetUnits(candidateEntity, reagentId, solutionName, requiredAmount, 1) > 0;
    }

    private int CountReagentTargetUnits(
        EntityUid candidateEntity,
        string reagentId,
        string solutionName,
        FixedPoint2 requiredAmount,
        int maxUnits = int.MaxValue
    )
    {
        if (string.IsNullOrWhiteSpace(reagentId) ||
            string.IsNullOrWhiteSpace(solutionName) ||
            requiredAmount <= FixedPoint2.Zero ||
            maxUnits <= 0)
            return 0;

        if (!TryComp(candidateEntity, out SolutionContainerManagerComponent? manager))
            return 0;

        if (IsAnySolutionName(solutionName))
        {
            var available = FixedPoint2.Zero;
            foreach (var (_, solutionEnt) in _solutions.EnumerateSolutions((candidateEntity, manager), false))
            {
                available += solutionEnt.Comp.Solution.GetTotalPrototypeQuantity(reagentId);
                if (CountReagentUnits(available, requiredAmount) >= maxUnits)
                    return maxUnits;
            }

            return CountReagentUnits(available, requiredAmount);
        }

        if (!_solutions.TryGetSolution((candidateEntity, manager), solutionName, out _, out var solution))
            return 0;

        return Math.Min(
            maxUnits,
            CountReagentUnits(solution.GetTotalPrototypeQuantity(reagentId), requiredAmount));
    }

    private static int CountReagentUnits(FixedPoint2 available, FixedPoint2 requiredAmount)
    {
        if (available <= FixedPoint2.Zero || requiredAmount <= FixedPoint2.Zero)
            return 0;

        return Math.Max(0, available.Value / requiredAmount.Value);
    }

    private static bool IsAnySolutionName(string solutionName)
    {
        return string.Equals(solutionName, AnySolutionName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(solutionName, "*", StringComparison.Ordinal);
    }

    private bool CanUseContractPlanningEntity(EntityUid root, EntityUid ent, bool worldTurnInSource)
    {
        if (ent == EntityUid.Invalid || !Exists(ent))
            return false;

        if (TryComp(ent, out MobStateComponent? mobState) && mobState.CurrentState != MobState.Dead)
            return false;

        if (HasComp<NcContractTurnInBlockedComponent>(ent))
            return false;

        if (worldTurnInSource)
        {
            if (!TryComp(ent, out TransformComponent? xform))
                return false;

            return CanUseNearbyStoreTurnInEntity(ent, xform);
        }

        return !_logic.IsProtectedFromDirectSale(root, ent);
    }

    private int ReserveAvailableStackAmount(
        EntityUid ent,
        int need,
        Dictionary<EntityUid, int> virtualStackLeft,
        out bool exhausted
    )
    {
        exhausted = false;

        if (need <= 0 || !TryComp(ent, out StackComponent? stack))
            return 0;

        var available = virtualStackLeft.TryGetValue(ent, out var virtualLeft)
            ? virtualLeft
            : Math.Max(stack.Count, 0);
        if (available <= 0)
        {
            exhausted = true;
            virtualStackLeft.Remove(ent);
            return 0;
        }

        var take = Math.Min(available, need);
        if (take <= 0)
            return 0;

        var left = available - take;
        exhausted = left <= 0;

        if (left > 0)
            virtualStackLeft[ent] = left;
        else
            virtualStackLeft.Remove(ent);

        return take;
    }

    private bool TryGetPlanningEntityPrototypeId(EntityUid ent, out string prototypeId)
    {
        prototypeId = string.Empty;

        if (!TryComp(ent, out MetaDataComponent? meta) || meta.EntityPrototype == null)
            return false;

        prototypeId = meta.EntityPrototype.ID;
        return !string.IsNullOrWhiteSpace(prototypeId);
    }
}
