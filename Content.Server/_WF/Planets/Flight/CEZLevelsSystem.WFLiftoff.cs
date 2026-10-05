using System.Diagnostics.CodeAnalysis;
using Content.Server._CE.ZLevels.Core.Components;
using Content.Server._NF.Shuttles.Components;
using Content.Server._WF.Planets.Flight;
using Content.Server.Shuttles.Components;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.Planets.Flight;
using Content.Shared._WF.Planets;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Map.Components;

namespace Content.Server._CE.ZLevels.Core;

/// <summary>Planet liftoff intent layered over CE's vertical input, takeoff spool and transit.</summary>
public sealed partial class CEZLevelsSystem
{
    private static readonly TimeSpan WFLiftoffInputPopupCooldown = TimeSpan.FromSeconds(4);

    private readonly Dictionary<EntityUid, TimeSpan> _wfNextLiftoffInputPopup = new();
    private readonly List<(EntityUid Grid, WFLiftoffComponent Liftoff)> _wfLiftoffQueue = new();

    /// <summary>Whether the console offers liftoff; only ground contact, so a blocked press can say why.</summary>
    public bool WfCanOfferLiftoff(EntityUid grid)
    {
        return TryComp<MapGridComponent>(grid, out var gridComp)
               && Transform(grid).MapUid is { } map
               && HasComp<WFPlanetLayerComponent>(map)
               && HasGroundUnderFootprint((grid, gridComp), map);
    }

    /// <summary>Shared ascent gate; raises <see cref="WFLiftoffAttemptEvent"/> on each transit-set member.</summary>
    public bool WfCanLiftoff(EntityUid grid, [NotNullWhen(false)] out string? reason)
    {
        reason = null;

        if (TerminatingOrDeleted(grid) || !TryComp<MapGridComponent>(grid, out _))
        {
            reason = Loc.GetString("wf-liftoff-no-hull");
            return false;
        }

        var transitSet = CollectTransitSet(grid);
        string? eventReason = null;

        foreach (var member in transitSet)
        {
            var attempt = new WFLiftoffAttemptEvent();
            RaiseLocalEvent(member, ref attempt);

            if (attempt.Cancelled && eventReason is null)
                eventReason = attempt.Reason ?? Loc.GetString("wf-liftoff-blocked");
        }

        if (eventReason is not null)
        {
            reason = eventReason;
            return false;
        }

        if (!WfIsPlanetFlight(grid))
        {
            reason = Loc.GetString("wf-liftoff-not-planet");
            return false;
        }

        if (HasComp<FTLComponent>(grid))
        {
            reason = Loc.GetString("wf-flight-busy");
            return false;
        }

        foreach (var member in transitSet)
        {
            if (HasComp<ForceAnchorComponent>(member))
            {
                reason = Loc.GetString("wf-liftoff-force-anchored");
                return false;
            }
        }

        WfGetAtmospherePower(grid, out _, out var powerDeficit);
        if (powerDeficit)
        {
            reason = Loc.GetString("wf-liftoff-power-deficit");
            return false;
        }

        if (!WfTryGetLiftRatio(grid, out var ratio) || ratio < WFFullLiftRatio)
        {
            reason = Loc.GetString("wf-liftoff-insufficient-lift", ("ratio", ratio.ToString("F2")));
            return false;
        }

        return true;
    }

    /// <summary>Validates and latches a grounded button request.</summary>
    public bool WfTryBeginLiftoff(
        EntityUid grid,
        EntityUid console,
        EntityUid pilot,
        [NotNullWhen(false)] out string? reason)
    {
        if (TerminatingOrDeleted(console)
            || Transform(console).GridUid != grid
            || !TryComp<PilotComponent>(pilot, out var pilotComp)
            || pilotComp.Console != console)
        {
            reason = Loc.GetString("wf-liftoff-pilot-lost");
            return false;
        }

        if (!WfCanOfferLiftoff(grid))
        {
            reason = Loc.GetString("wf-liftoff-not-grounded");
            return false;
        }

        if (!WfCanLiftoff(grid, out reason))
            return false;

        var liftoff = EnsureComp<WFLiftoffComponent>(grid);
        liftoff.Console = console;
        liftoff.Pilot = pilot;
        return true;
    }

    /// <summary>Cancels an active ascent intent. Airborne CE settling then resumes normally.</summary>
    public void WfCancelLiftoff(EntityUid grid)
    {
        RemComp<WFLiftoffComponent>(grid);
    }

    /// <summary>Descend cancels liftoff; grounded ascend input points the pilot to the console button.</summary>
    private bool WfHandleLiftoffPilotInput(EntityUid pilot, EntityUid console, EntityUid grid, float vertical)
    {
        if (vertical < 0f && HasComp<WFLiftoffComponent>(grid))
        {
            WfCancelLiftoff(grid);
            return false;
        }

        if (vertical <= 0f || !WfIsPlanetFlight(grid))
            return false;

        var active = HasComp<WFLiftoffComponent>(grid);
        if (WfCanOfferLiftoff(grid))
        {
            // Raised for tether subscribers; grounded input never launches.
            WfCanLiftoff(grid, out _);

            if (!active && _timing.CurTime >= _wfNextLiftoffInputPopup.GetValueOrDefault(grid))
            {
                _wfNextLiftoffInputPopup[grid] = _timing.CurTime + WFLiftoffInputPopupCooldown;
                _popup.PopupEntity(Loc.GetString("wf-liftoff-use-button"), console, pilot);
            }

            return true;
        }

        if (WfCanLiftoff(grid, out var reason))
            return false;

        if (_timing.CurTime >= _wfNextLiftoffInputPopup.GetValueOrDefault(grid))
        {
            _wfNextLiftoffInputPopup[grid] = _timing.CurTime + WFLiftoffInputPopupCooldown;
            _popup.PopupEntity(reason, console, pilot);
        }

        return true;
    }

    // Progress up the ground gap that counts as taken off; past the settle zone so the hull drifts on up.
    private const float WFTakeoffProgress = 0.8f;

    /// <summary>True once the hull is off the ground for good: high in the first gap, or anywhere above it.</summary>
    private bool WfHasTakenOff(EntityUid grid, EntityUid map)
    {
        if (HasComp<WFOrbitLayerComponent>(map))
            return true;

        if (TryComp<CEZTransitMapComponent>(map, out var transit))
        {
            if (transit.LowerMap is not { } lower || !WfIsGroundLayer(lower))
                return true;

            return TryComp<CEZPhysicsComponent>(grid, out var zPhysics) && zPhysics.LocalPosition >= WFTakeoffProgress;
        }

        return HasComp<WFPlanetLayerComponent>(map) && !WfIsGroundLayer(map);
    }

    private bool WfIsGroundLayer(EntityUid map)
    {
        return HasComp<CEZGroundLayerComponent>(map) || TryComp<CEZMapComponent>(map, out var zMap) && zMap.Depth == 0;
    }

    /// <summary>Adds every valid latch to CE's ordinary per-grid upward input.</summary>
    private void WfCollectLiftoffInputs()
    {
        _wfLiftoffQueue.Clear();

        var query = EntityQueryEnumerator<WFLiftoffComponent>();
        while (query.MoveNext(out var grid, out var liftoff))
            _wfLiftoffQueue.Add((grid, liftoff));

        foreach (var (grid, liftoff) in _wfLiftoffQueue)
        {
            if (TerminatingOrDeleted(grid))
                continue;

            // Liftoff stops at the first air layer; settling carries the hull the rest of the way.
            if (Transform(grid).MapUid is { } map && WfHasTakenOff(grid, map))
            {
                WfCancelLiftoff(grid);

                // Drop the climb's momentum so it doesn't carry the hull on into orbit.
                if (TryComp<CEZGridFallerComponent>(grid, out var faller))
                    faller.Velocity = 0f;

                continue;
            }

            if (TerminatingOrDeleted(liftoff.Console)
                || Transform(liftoff.Console).GridUid != grid
                || !TryComp<PilotComponent>(liftoff.Pilot, out var pilot)
                || pilot.Console != liftoff.Console)
            {
                WfCancelLiftoff(grid);
                continue;
            }

            if (!WfCanLiftoff(grid, out var reason))
            {
                WfCancelLiftoff(grid);
                _popup.PopupEntity(reason, liftoff.Console, liftoff.Pilot);
                continue;
            }

            _pilotVerticalInput[grid] = Math.Clamp(_pilotVerticalInput.GetValueOrDefault(grid) + 1f, -1f, 1f);
        }
    }
}
