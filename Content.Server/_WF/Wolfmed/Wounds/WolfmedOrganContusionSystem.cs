using System.Linq;
using Content.Shared._Onyx.Body.Systems;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.Body;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Systems;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WF.Wolfmed.Wounds;

/// <summary>
/// Organ contusions: a heavy blow to the torso bruises something inside it. Onyx's own organ damage is a
/// small per-hit roll that can destroy an organ outright; this is the opposite end, a reliable consequence
/// of a big hit that never takes an organ below the health it needs to keep working.
/// </summary>
/// <remarks>
/// Answers <see cref="WolfmedWoundLifecycleEvent"/> because the directed wound-lifecycle subscriptions are
/// taken. Server-side: organ health, like bleeding and healing, is not simulated on the client.
/// </remarks>
public sealed class WolfmedOrganContusionSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private OrganHealthSystem _organs = default!;
    [Dependency] private SharedBodySystem _body = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WolfmedWoundLifecycleEvent>(OnWoundLifecycle);
    }

    private void OnWoundLifecycle(ref WolfmedWoundLifecycleEvent args)
    {
        // A later hit merges into the existing wound, so worsening bruises an organ again.
        if (args.Kind == WolfmedWoundLifecycle.Removed || args.Severity <= args.OldSeverity ||
            !_prototypes.TryIndex(args.Prototype, out var prototype) ||
            !prototype.TryGetBehavior(args.Severity, out WolfmedOrganContusionBehavior behavior))
            return;

        TryBruise(args.Part, behavior.Damage, behavior.MinRemainingHealth);
    }

    /// <summary>
    /// Takes health off one organ in the part, never below <paramref name="minRemaining"/>. Returns the
    /// organ that took it, or null when the part holds nothing that could be bruised.
    /// </summary>
    public EntityUid? TryBruise(EntityUid part, FixedPoint2 damage, FixedPoint2 minRemaining)
    {
        if (damage <= FixedPoint2.Zero || TerminatingOrDeleted(part))
            return null;

        var organs = _body.GetPartOrgans(part)
            .Select(organ => (organ.Id, Component: CompOrNull<WolfmedOrganComponent>(organ.Id)))
            .Where(organ => organ.Component is { } health && health.Health > minRemaining)
            .Select(organ => (organ.Id, Component: organ.Component!))
            .ToList();
        if (organs.Count == 0)
            return null;

        var (uid, component) = _random.Pick(organs);
        var applied = FixedPoint2.Min(damage, component.Health - minRemaining);
        if (applied <= FixedPoint2.Zero)
            return null;

        _organs.ChangeHealth((uid, component), -applied);
        return uid;
    }
}
