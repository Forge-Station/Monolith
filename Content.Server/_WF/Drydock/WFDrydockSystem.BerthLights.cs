using Content.Shared._WF.Drydock;

namespace Content.Server._WF.Drydock;

/// <summary>
/// Walk-in berth lights: a console can light its whole berth work area for a while, so a pilot can line up without a booking.
/// </summary>
public sealed partial class WFDrydockSystem
{
    private void InitializeBerthLights()
    {
        SubscribeLocalEvent<WFDrydockConsoleComponent, WFDrydockToggleBerthLightsMessage>(OnToggleBerthLights);
    }

    private void OnToggleBerthLights(EntityUid uid, WFDrydockConsoleComponent comp, WFDrydockToggleBerthLightsMessage args)
    {
        if (comp.BerthLightsUntil != null)
        {
            TurnOffBerthLights(uid, comp);
            UpdateUi(uid, comp);
            return;
        }

        if (HasComp<WFDrydockJobComponent>(uid))
        {
            Deny(uid, comp, args.Actor, Loc.GetString("drydock-console-busy"));
            return;
        }

        if (!TryGetBerth(uid, comp, out var berth))
        {
            Deny(uid, comp, args.Actor, Loc.GetString("drydock-berth-lights-no-berth"));
            return;
        }

        var guide = EnsureComp<WFDrydockGuideComponent>(uid);
        FillGuide(guide, berth, comp.ApproachLength, berth.Area.Size);
        Dirty(uid, guide);
        comp.BerthLightsUntil = _timing.CurTime + TimeSpan.FromSeconds(comp.BerthLightsTime);
        UpdateUi(uid, comp);
    }

    /// <summary>Removes the console's berth guide and clears its expiry.</summary>
    private void TurnOffBerthLights(EntityUid console, WFDrydockConsoleComponent comp)
    {
        comp.BerthLightsUntil = null;
        RemComp<WFDrydockGuideComponent>(console);
    }

    /// <summary>Switches off berth lights whose time has run out.</summary>
    private void UpdateBerthLights(TimeSpan curTime)
    {
        var query = EntityQueryEnumerator<WFDrydockConsoleComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.BerthLightsUntil is not { } until || curTime < until)
                continue;

            TurnOffBerthLights(uid, comp);
            UpdateUi(uid, comp);
        }
    }
}
