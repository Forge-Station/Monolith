using Content.Shared._WF.Shrines;
using Content.Shared.Chemistry.EntitySystems;

namespace Content.Server._WF.Shrines;

/// <summary>Shows a shrine fountain dry once its solution is gone.</summary>
public sealed class WFFountainSystem : EntitySystem
{
    [Dependency] private SharedAppearanceSystem _appearance = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFFountainComponent, SolutionChangedEvent>(OnSolutionChanged);
    }

    private void OnSolutionChanged(Entity<WFFountainComponent> ent, ref SolutionChangedEvent args)
    {
        _appearance.SetData(ent, WFFountainVisuals.Dry, args.Solution.Comp.Solution.Volume <= 0);
    }
}
