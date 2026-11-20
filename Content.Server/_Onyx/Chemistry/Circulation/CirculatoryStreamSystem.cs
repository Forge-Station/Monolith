using Content.Server.Body.Components; // WOLFGATE(Wolfmed): D13/D15, BloodstreamComponent is server-only in Wolfgate.
using Content.Server.Body.Systems; // WOLFGATE(Wolfmed): D13/D15, BloodstreamSystem is server-only in Wolfgate.
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared._Onyx.Wounds;
using Robust.Shared.Prototypes;

namespace Content.Shared._Onyx.Chemistry.Circulation;

// WOLFGATE(Wolfmed): D15. Trimmed to the primary-stream paths. "Organic" is the primary stream and the only
// circulatoryStream prototype at this pin, so every secondary-stream path is unreachable in phase 1.
// Dropped: Update, SynchronizeStreams, GetAttachedStreams, ConfigureMetabolizer, InitializeStream,
// HasStageConflict, RemoveStream, DeleteSolution, OnMetabolizerInit, OnStreamState, OnStreamShutdown,
// OnMetabolismExclusion, OnReactionAttempt and all five SubscribeLocalEvents. Wolfgate has no
// MetabolismStagePrototype, MetabolismExclusionEvent or TryCreateCirculatorySolution, and phase 5's
// species streams need the shared metabolizer rewrite before any of it can come back.
public sealed partial class CirculatoryStreamSystem : EntitySystem
{
    [Dependency] private BloodstreamSystem _bloodstream = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;

    public ProtoId<CirculatoryStreamPrototype> GetPartStream(Entity<WoundableComponent> part)
    {
        return _prototypes.TryIndex(part.Comp.Profile, out var profile)
            ? profile.CirculatoryStream
            : CirculatoryStreamPrototype.PrimaryStream;
    }

    public bool TryGetPartSolution(EntityUid body, EntityUid part, out Entity<SolutionComponent> solution)
    {
        solution = default;
        if (!_body.BodyHasChild(body, part) || !TryComp(part, out WoundableComponent? woundable))
            return false;

        var stream = GetPartStream((part, woundable));
        if (stream == CirculatoryStreamPrototype.PrimaryStream && TryComp(body, out BloodstreamComponent? bloodstream))
        {
            if (!_solutions.ResolveSolution(body, bloodstream.BloodSolutionName, ref bloodstream.BloodSolution, out _))
                return false;

            solution = bloodstream.BloodSolution.Value;
            return true;
        }

        // WOLFGATE(Wolfmed): D15, secondary streams have no solutions in phase 1.
        return false;
    }

    public bool TryGetStreamSolution(EntityUid body,
        ProtoId<CirculatoryStreamPrototype> stream,
        out Entity<SolutionComponent> solution)
    {
        solution = default;
        if (stream == CirculatoryStreamPrototype.PrimaryStream && TryComp(body, out BloodstreamComponent? bloodstream))
        {
            if (!_solutions.ResolveSolution(body, bloodstream.BloodSolutionName, ref bloodstream.BloodSolution, out _))
                return false;

            solution = bloodstream.BloodSolution.Value;
            return true;
        }

        // WOLFGATE(Wolfmed): D15, secondary streams are never initialised in phase 1.
        return false;
    }

    public void SetBleedRates(EntityUid body, Dictionary<ProtoId<CirculatoryStreamPrototype>, float> rates)
    {
        if (!TryComp(body, out BloodstreamComponent? bloodstream))
            return;

        // WOLFGATE(Wolfmed): D15, only the primary stream exists, so the CirculatoryStreamComponent bookkeeping
        // and the SynchronizeStreams fallback below it are both dead code in phase 1.
        // WOLFGATE(Wolfmed): P5-2/P5-D3. Wolfgate has exactly one blood solution per body (server-only
        // BloodstreamComponent), so every stream's bleed rate lands in the same place. Summing instead of
        // reading only the primary key makes a profile that sets `circulatoryStream:` degrade to "bleeds
        // normally" rather than "silently stops bleeding". Identical while Organic is the only stream.
        var total = 0f;
        foreach (var rate in rates.Values)
            total += rate;

        _bloodstream.TryModifyWoundBleedProjection(body, total - bloodstream.BleedAmount, bloodstream);
    }
}
