using Content.Shared.TextScreen;
using Content.Server.Screens.Components;
using Content.Shared.DeviceNetwork.Events;
using Content.Shared.RoundEnd;
using Content.Shared.Screens;
using Robust.Shared.Timing;


namespace Content.Server.Screens.Systems;

/// <summary>
/// Controls the wallmounted screens on stations and shuttles displaying e.g. FTL duration, ETA
/// </summary>
public sealed partial class ScreenSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private SharedAppearanceSystem _appearanceSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ScreenComponent, DeviceNetworkPacketEvent<ScreenTextPayload>>(OnScreenText);
        SubscribeLocalEvent<ScreenComponent, DeviceNetworkPacketEvent<ScreenShuttlePayload>>(OnShuttleTimer);
    }

    /// <summary>
    ///     Send a text update to every screen on the same MapUid as the originating comms console.
    /// </summary>
    private void OnScreenText(EntityUid uid, ScreenComponent component, ref DeviceNetworkPacketEvent<ScreenTextPayload> args)
    {
        // don't allow text updates if there's an active timer
        // (and just check here so the server doesn't have to track them)
        if (_appearanceSystem.TryGetData(uid, TextScreenVisuals.TargetTime, out TimeSpan target)
            && target > _gameTiming.CurTime)
            return;

        var screenMap = Transform(uid).MapUid;
        var argsMap = Transform(args.Sender).MapUid;

        if (screenMap != null
            && argsMap != null
            && screenMap == argsMap
            && args.Data.Text != null)
        {
            _appearanceSystem.SetData(uid, TextScreenVisuals.DefaultText, args.Data.Text);
            _appearanceSystem.SetData(uid, TextScreenVisuals.ScreenText, args.Data.Text);
        }
    }

    /// <summary>
    /// Determines if/how a timer packet affects this screen.
    /// Currently there are 2 broadcast domains: Arrivals, and every other screen.
    /// Domain is determined by the <see cref="Shared.DeviceNetwork.Components.DeviceNetworkComponent.TransmitFrequencyId"/> on each timer.
    /// Each broadcast domain is divided into subnets. Screen MapUid determines subnet.
    /// Subnets are the shuttle, source, and dest. Source/dest change each jump.
    /// This is required to send different timers to the shuttle/terminal/station.
    /// </summary>
    private void OnShuttleTimer(EntityUid uid, ScreenComponent component, ref DeviceNetworkPacketEvent<ScreenShuttlePayload> args)
    {
        var timerXform = Transform(uid);

        if (timerXform.MapUid == null)
            return;

        var shuttleMap = args.Data.Shuttle;
        var source = args.Data.SourceMap;
        var dest = args.Data.DestinationMap;
        var text = args.Data.Docked ? ShuttleTimerMasks.ETD : ShuttleTimerMasks.ETA;
        TimeSpan duration;

        switch (timerXform.MapUid)
        {
            case var local when local == shuttleMap || timerXform.GridUid == shuttleMap:
                duration = args.Data.ShuttleTime;
                break;
            case var origin when origin == source:
                duration = args.Data.SourceTime;
                break;
            case var remote when remote == dest:
                duration = args.Data.DestinationTime;
                text = ShuttleTimerMasks.ETA;
                break;
            default:
                return;
        }

        if (args.Data.OverrideText != null)
            text = args.Data.OverrideText;

        _appearanceSystem.SetData(uid, TextScreenVisuals.ScreenText, text);
        _appearanceSystem.SetData(uid, TextScreenVisuals.TargetTime, _gameTiming.CurTime + duration);

        if (args.Data.OverrideColor != null)
            _appearanceSystem.SetData(uid, TextScreenVisuals.Color, args.Data.OverrideColor);
    }
}
