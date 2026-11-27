using Content.Server.Body.Components;
using Content.Shared.Body.Prototypes;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.Synthetics;

/// <summary>
/// Flushes a chassis's chemical stream of anything it can't metabolise, so drugs and darts do nothing and never fill it.
/// Only contaminant reagents, such as Letoferol, stay in to work on the chassis.
/// </summary>
public sealed class WFChassisFilterSystem : EntitySystem
{
    /// <summary>The metabolism group a chassis processes.</summary>
    public static readonly ProtoId<MetabolismGroupPrototype> Contaminant = "WFContaminant";

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;

    private readonly List<(ReagentId Id, FixedPoint2 Quantity)> _flush = new();

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WFChassisFilterComponent, BloodstreamComponent>();
        while (query.MoveNext(out var uid, out var filter, out var bloodstream))
        {
            if (now < filter.NextFlush)
                continue;

            filter.NextFlush = now + filter.Interval;
            if (!_solutions.ResolveSolution(uid, bloodstream.ChemicalSolutionName, ref bloodstream.ChemicalSolution, out var chemicals))
                continue;

            _flush.Clear();
            foreach (var (reagent, quantity) in chemicals.Contents)
            {
                if (!IsContaminant(reagent.Prototype))
                    _flush.Add((reagent, quantity));
            }

            foreach (var (reagent, quantity) in _flush)
                _solutions.RemoveReagent(bloodstream.ChemicalSolution.Value, reagent, quantity);
        }
    }

    /// <summary>Whether a chassis metabolises this reagent rather than flushing it.</summary>
    public bool IsContaminant(string reagent)
    {
        return _proto.TryIndex<ReagentPrototype>(reagent, out var proto)
               && proto.Metabolisms?.ContainsKey(Contaminant) == true;
    }
}
