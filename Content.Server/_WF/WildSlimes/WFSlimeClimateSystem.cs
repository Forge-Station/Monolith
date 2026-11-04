using Content.Server.Atmos.EntitySystems;
using Content.Shared._WF.WildSlimes;
using Content.Shared._WF.Xenobiology;
using Content.Shared.Atmos;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Examine;
using Robust.Shared.Physics.Systems;

namespace Content.Server._WF.WildSlimes;

/// <summary>Holds slimes with <see cref="WFSlimeClimateComponent"/> back from growing and splitting outside their climate.</summary>
public sealed class WFSlimeClimateSystem : EntitySystem
{
    [Dependency] private AtmosphereSystem _atmos = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;

    /// <summary>Less gas than this round a slime counts as vacuum.</summary>
    public const float MinMoles = 1f;

    private readonly HashSet<EntityUid> _contacts = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFSlimeClimateComponent, SlimeGrowthAttemptEvent>(OnGrowthAttempt);
        SubscribeLocalEvent<WFSlimeClimateComponent, ExaminedEvent>(OnExamined);
    }

    private void OnGrowthAttempt(Entity<WFSlimeClimateComponent> ent, ref SlimeGrowthAttemptEvent args)
    {
        if (!InClimate(ent))
            args.Cancelled = true;
    }

    private void OnExamined(Entity<WFSlimeClimateComponent> ent, ref ExaminedEvent args)
    {
        if (!InClimate(ent))
            args.PushMarkup(Loc.GetString(ent.Comp.Unsettled));
    }

    /// <summary>Whether the slime's air is in its range and, if it needs one, it touches its pool.</summary>
    public bool InClimate(Entity<WFSlimeClimateComponent> ent)
    {
        // A tile emptied to vacuum keeps its old temperature with no gas in it, so count near-empty air as space.
        var air = _atmos.GetContainingMixture(ent.Owner);
        var temperature = air is { TotalMoles: >= MinMoles } ? air.Temperature : Atmospherics.TCMB;
        if (temperature < ent.Comp.MinTemperature || temperature > ent.Comp.MaxTemperature)
            return false;

        return ent.Comp.Underfoot.Count == 0 || TouchesUnderfoot(ent);
    }

    private bool TouchesUnderfoot(Entity<WFSlimeClimateComponent> ent)
    {
        _contacts.Clear();
        _physics.GetContactingEntities(ent.Owner, _contacts);
        foreach (var contact in _contacts)
        {
            if (!TryComp<SolutionContainerManagerComponent>(contact, out var manager))
                continue;

            foreach (var (_, solution) in _solutions.EnumerateSolutions((contact, manager)))
            {
                foreach (var reagent in ent.Comp.Underfoot)
                {
                    if (solution.Comp.Solution.ContainsPrototype(reagent.Id))
                        return true;
                }
            }
        }

        return false;
    }
}
