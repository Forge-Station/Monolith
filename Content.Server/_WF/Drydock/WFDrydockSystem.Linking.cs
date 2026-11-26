using System.Linq;
using Content.Server.DeviceLinking.Systems;
using Content.Shared._WF.Drydock;
using Content.Shared.DeviceLinking;
using Content.Shared.DeviceLinking.Events;
using Robust.Shared.GameObjects;

namespace Content.Server._WF.Drydock;

/// <summary>
/// Multitool links between drydock consoles and cranes. A crane serves one console, so linking it again moves it.
/// </summary>
public sealed partial class WFDrydockSystem
{
    [Dependency] private DeviceLinkSystem _deviceLink = default!;

    private void InitializeLinking()
    {
        SubscribeLocalEvent<WFDrydockConsoleComponent, NewLinkEvent>(OnCraneLinked);
        SubscribeLocalEvent<WFDrydockConsoleComponent, MapInitEvent>(OnConsoleMapInit);
        SubscribeLocalEvent<WFDrydockCraneComponent, PortDisconnectedEvent>(OnCraneUnlinked);
    }

    /// <summary>Points a newly linked crane at this console, dropping its link to any other drydock console.</summary>
    private void OnCraneLinked(EntityUid uid, WFDrydockConsoleComponent comp, NewLinkEvent args)
    {
        if (args.SourcePort != WFDrydockConsoleComponent.CranePort || !TryComp<WFDrydockCraneComponent>(args.Sink, out var crane))
            return;

        var query = EntityQueryEnumerator<DeviceLinkSourceComponent, WFDrydockConsoleComponent>();
        while (query.MoveNext(out var other, out var source, out _))
        {
            if (other != uid && _deviceLink.GetLinks((other, source), args.Sink).Count > 0)
                _deviceLink.RemoveSinkFromSource((other, source), args.Sink);
        }

        crane.LinkedConsole = uid;
        Dirty(args.Sink, crane);
        UpdateUi(uid, comp);
    }

    /// <summary>Reads the crane links a map was saved with, since no link event runs on map load.</summary>
    private void OnConsoleMapInit(EntityUid uid, WFDrydockConsoleComponent comp, MapInitEvent args)
    {
        if (!TryComp<DeviceLinkSourceComponent>(uid, out var source))
            return;

        foreach (var sink in source.Outputs.Values.SelectMany(sinks => sinks))
        {
            if (TryComp<WFDrydockCraneComponent>(sink, out var crane) &&
                _deviceLink.GetLinks((uid, source), sink).Any(link => link.Source.Id == WFDrydockConsoleComponent.CranePort))
            {
                crane.LinkedConsole = uid;
                Dirty(sink, crane);
            }
        }
    }

    /// <summary>Clears a crane's console when its link goes, then refreshes that console.</summary>
    private void OnCraneUnlinked(EntityUid uid, WFDrydockCraneComponent crane, PortDisconnectedEvent args)
    {
        if (args.Port != WFDrydockConsoleComponent.CraneReceiverPort || crane.LinkedConsole is not { } console)
            return;

        crane.LinkedConsole = null;
        Dirty(uid, crane);
        if (TryComp<WFDrydockConsoleComponent>(console, out var comp))
            UpdateUi(console, comp);
    }

    /// <summary>Whether a crane is working a job for a console other than this one.</summary>
    private bool CraneBusyElsewhere(EntityUid console, EntityUid crane)
    {
        var query = EntityQueryEnumerator<WFDrydockJobComponent>();
        while (query.MoveNext(out var uid, out var job))
        {
            if (uid != console && job.Slots.Any(slot => slot.Crane == crane))
                return true;
        }

        return false;
    }
}
