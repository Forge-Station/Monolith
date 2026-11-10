using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids;
using Content.Shared.Fluids.Components;
using Content.Shared.Mobs.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.BloodTrail;

/// <summary>
/// Stains a mob that touches a puddle, so <see cref="FootPrintsSystem"/> tracks it across the floor. Works on
/// floor contact alone: a puddle too small to slip on still stains. Each visit carries some of the puddle away, and
/// a puddle with less than that to give leaves a fainter stain.
/// </summary>
public sealed partial class PuddleFootPrintsSystem : EntitySystem
{
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private FootPrintsSystem _footPrints = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;

    /// <summary>A puddle with more water in it than this share leaves no stain.</summary>
    public const float WaterShare = 0.8f;

    /// <summary>What a visit takes from a puddle; this much makes a full stain.</summary>
    public static readonly FixedPoint2 StainDrain = FixedPoint2.New(5);

    private static readonly ProtoId<ReagentPrototype> Water = "Water";

    private EntityQuery<MobStateComponent> _mobQuery;

    public override void Initialize()
    {
        base.Initialize();

        _mobQuery = GetEntityQuery<MobStateComponent>();

        SubscribeLocalEvent<PuddleComponent, StartCollideEvent>(OnStartCollide);
    }

    private void OnStartCollide(Entity<PuddleComponent> ent, ref StartCollideEvent args)
    {
        var other = args.OtherEntity;
        if (!args.OtherFixture.Hard || !_mobQuery.HasComp(other) || !_footPrints.IsGrounded(other))
            return;

        if (!_solutions.TryGetSolution(ent.Owner, ent.Comp.SolutionName, out var soln, out var solution)
            || solution.Volume <= FixedPoint2.Zero)
            return;

        if (solution.GetTotalPrototypeQuantity(Water) > solution.Volume * WaterShare)
            return;

        if (!_appearance.TryGetData<Color>(ent, PuddleVisuals.SolutionColor, out var color))
            return;

        var taken = FixedPoint2.Min(solution.Volume, StainDrain);
        _footPrints.Stain(other, color.WithAlpha(color.A * (taken / StainDrain).Float()));
        _solutions.SplitSolution(soln.Value, taken);
    }
}
