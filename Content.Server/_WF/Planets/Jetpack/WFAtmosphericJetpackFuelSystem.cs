using Content.Server.Movement.Systems;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.Planets.Jetpack;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;

namespace Content.Server._WF.Planets.Jetpack;

/// <summary>
/// Burns an active atmospheric jetpack's welding fuel and cuts the pack when the tank runs dry, the wearer leaves
/// the air or the gravity is too much. A cut pack drops its wearer; a parachute, if worn, takes it from there.
/// </summary>
public sealed partial class WFAtmosphericJetpackFuelSystem : EntitySystem
{
    [Dependency] private WFAtmosphericJetpackSystem _atmospheric = default!;
    [Dependency] private JetpackSystem _jetpack = default!;
    [Dependency] private SharedSolutionContainerSystem _solution = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    /// <summary>Vertical speed under which the wearer counts as hovering rather than climbing or sinking.</summary>
    private const float HoverSpeed = 0.05f;

    private readonly List<(EntityUid Pack, JetpackComponent Jetpack, EntityUid User, string Reason)> _toCut = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ActiveJetpackComponent, JetpackComponent, WFAtmosphericJetpackComponent>();

        while (query.MoveNext(out var uid, out _, out var jetpack, out var pack))
        {
            if (jetpack.JetpackUser is not { } user)
                continue;

            if (_atmospheric.GetRefusal((uid, pack), user) is { } refusal)
            {
                _toCut.Add((uid, jetpack, user, refusal));
                continue;
            }

            if (!_atmospheric.TryGetAirLayer(user, out var layer))
                continue;

            var rate = IsThrusting(user) ? pack.ThrustUsage : pack.HoverUsage;

            if (layer.Gravity > pack.HeavyGravity)
                rate *= pack.HeavyUsageMultiplier;

            pack.Burned += rate * frameTime;

            if (pack.Burned < pack.BurnStep)
                continue;

            var burn = FixedPoint2.New(pack.Burned);
            pack.Burned = 0f;

            if (!_atmospheric.TryGetFuelSolution((uid, pack), out var soln, out _)
                || _solution.SplitSolution(soln.Value, burn).Volume < burn)
                _toCut.Add((uid, jetpack, user, "wf-jetpack-atmospheric-no-fuel"));
        }

        foreach (var (uid, jetpack, user, reason) in _toCut)
        {
            _jetpack.SetEnabled(uid, jetpack, false, user);
            _popup.PopupEntity(Loc.GetString(reason), uid, user);
        }

        _toCut.Clear();
    }

    /// <summary>Moving under power: a held move key, a held climb or descend key, or still travelling between layers.</summary>
    private bool IsThrusting(EntityUid user)
    {
        if (TryComp<JetpackUserComponent>(user, out var flight) && (flight.AscendHeld || flight.DescendHeld))
            return true;

        if (TryComp<CEZPhysicsComponent>(user, out var zPhysics) && MathF.Abs(zPhysics.Velocity) > HoverSpeed)
            return true;

        return TryComp<InputMoverComponent>(user, out var mover) && (mover.HeldMoveButtons & MoveButtons.AnyDirection) != 0;
    }
}
