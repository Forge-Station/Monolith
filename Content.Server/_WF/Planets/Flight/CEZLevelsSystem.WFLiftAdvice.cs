using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.Planets.Flight;
using Content.Shared.Shuttles.Components;

namespace Content.Server._CE.ZLevels.Core;

/// <summary>How many thrusters a hull needs converted or added to reach full lift, for the console checklist.</summary>
public sealed partial class CEZLevelsSystem
{
    private readonly List<float> _wfConversionGains = new();

    /// <summary>
    /// Counts the conversions, then extra landing thrusters, that would bring the rigid set to full lift.
    /// Counts every enabled engine as powered, so a hull short only of power gets no thruster advice.
    /// </summary>
    public void WfGetLiftAdvice(EntityUid grid, out int convert, out int add)
    {
        convert = 0;
        add = 0;

        if (!WfTryGetPlanetGravity(grid, out var gravity) || gravity <= 0f)
            return;

        _wfConversionGains.Clear();
        var lift = 0f;
        var mass = 0f;
        var ratingSum = 0f;
        var ratingCount = 0;

        foreach (var member in CollectRigidSet(grid))
        {
            if (HasComp<CEZMappingAnchorGridComponent>(member))
                return;

            if (_physQuery.TryComp(member, out var body))
                mass += body.FixturesMass;

            mass += GetWFVirtualMass(member);

            var children = Transform(member).ChildEnumerator;
            while (children.MoveNext(out var uid))
            {
                if (!TryComp<ThrusterComponent>(uid, out var thruster)
                    || thruster.Type != ThrusterType.Linear
                    || !thruster.Enabled
                    || !Transform(uid).Anchored)
                {
                    continue;
                }

                var rated = _wfThrusters.WfRatedThrust(uid, thruster) / ThrusterSystem.WfStandardGravity;
                ratingSum += rated;
                ratingCount++;

                if (HasComp<WFLandingThrusterComponent>(uid))
                {
                    lift += rated;
                    continue;
                }

                lift += rated * ThrusterSystem.WfAtmosphereThrustMultiplier;
                _wfConversionGains.Add(rated * (1f - ThrusterSystem.WfAtmosphereThrustMultiplier));
            }
        }

        var missing = mass * gravity * WFFullLiftRatio - lift;
        if (missing <= 0f)
            return;

        // Biggest engines first, so the count is the fewest conversions that do it.
        _wfConversionGains.Sort((a, b) => b.CompareTo(a));
        foreach (var gain in _wfConversionGains)
        {
            if (missing <= 0f)
                return;

            missing -= gain;
            convert++;
        }

        if (missing <= 0f)
            return;

        var typical = ratingCount > 0
            ? ratingSum / ratingCount
            : new ThrusterComponent().Thrust / ThrusterSystem.WfStandardGravity;

        if (typical > 0f)
            add = (int) MathF.Ceiling(missing / typical);
    }
}
