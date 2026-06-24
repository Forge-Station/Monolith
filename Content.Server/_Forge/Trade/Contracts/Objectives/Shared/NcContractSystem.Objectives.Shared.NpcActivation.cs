using Content.Server.NPC.HTN;

namespace Content.Server._Forge.Trade;

public sealed partial class NcContractSystem : EntitySystem
{
    private void ActivateContractNpc(EntityUid uid)
    {
        if (!TryComp(uid, out HTNComponent? htn))
            return;

        var range = Math.Max(htn.SleepPlayerCheckRangeOverride ?? 0f, NcContractTuning.ContractNpcPlayerWakeRange);
        htn.SleepPlayerCheckRangeOverride = range;
        _contractNpc.WakeNPC(uid, htn);
    }

    private void ActivateContractNpcsOnGrid(EntityUid grid)
    {
        if (!TryComp(grid, out TransformComponent? gridXform))
            return;

        var children = gridXform.ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            ActivateContractNpc(child);
        }
    }
}
