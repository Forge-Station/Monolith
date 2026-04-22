using Content.Server.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.FixedPoint;
using Robust.Shared.Timing;

namespace Content.Server.Chemistry.EntitySystems;

public sealed class SolutionRegenerationSystem : EntitySystem
{
    [Dependency] private readonly SharedSolutionContainerSystem _solutionContainer = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    private static readonly TimeSpan MinRegenInterval = TimeSpan.FromSeconds(0.1); // Forge-Change
    private static readonly TimeSpan ResolveFailureRetry = TimeSpan.FromSeconds(2); // Forge-Change

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<SolutionRegenerationComponent, SolutionContainerManagerComponent>();
        while (query.MoveNext(out var uid, out var regen, out var manager))
        {
            if (_timing.CurTime < regen.NextRegenTime)
                continue;

            // Prevent zero/negative durations from forcing work every tick.
            var regenInterval = regen.Duration > TimeSpan.Zero ? regen.Duration : MinRegenInterval; // Forge-Change

            // timer ignores if its full, it's just a fixed cycle
            regen.NextRegenTime = _timing.CurTime + regenInterval; // Forge-Change
            if (!_solutionContainer.ResolveSolution((uid, manager), regen.SolutionName, ref regen.SolutionRef, out var solution)) // Forge-Change
            {
                // Missing/invalid target solution can otherwise cause constant retries forever.
                regen.NextRegenTime = _timing.CurTime + ResolveFailureRetry; // Forge-Change
                continue; // Forge-Change
            }

            var amount = FixedPoint2.Min(solution.AvailableVolume, regen.Generated.Volume);
            if (amount <= FixedPoint2.Zero)
                continue;

            // dont bother cloning and splitting if adding the whole thing
            Solution generated;
            if (amount == regen.Generated.Volume)
            {
                generated = regen.Generated;
            }
            else
            {
                generated = regen.Generated.Clone().SplitSolution(amount);
            }

            _solutionContainer.TryAddSolution(regen.SolutionRef.Value, generated); // Forge-Change
        }
    }
}
