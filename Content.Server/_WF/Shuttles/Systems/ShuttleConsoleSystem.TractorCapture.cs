using System.Linq;
using Content.Shared._WF.TractorBeam;
using Content.Shared.Shuttles.Components;

namespace Content.Server.Shuttles.Systems;

public sealed partial class ShuttleConsoleSystem
{
    private Dictionary<EntityUid, string[]> _tractorCaptureSources = new();
    private float _tractorCaptureUpdate;

    private string[] GetTractorCaptureSources(EntityUid? grid)
    {
        return grid is { } uid && _tractorCaptureSources.TryGetValue(uid, out var sources)
            ? sources : Array.Empty<string>();
    }

    private void UpdateTractorCaptureWarnings(float frameTime)
    {
        _tractorCaptureUpdate += frameTime;
        if (_tractorCaptureUpdate < 0.2f)
            return;
        _tractorCaptureUpdate = 0;

        // Query authoritative active locks, so release, power loss, retargeting, and deletion all
        // remove the warning. Multiple dishes on one arrestor still identify that vessel once.
        var shipsByTarget = new Dictionary<EntityUid, HashSet<EntityUid>>();
        var query = EntityQueryEnumerator<TractorBeamEmitterComponent>();
        while (query.MoveNext(out var emitter, out var beam))
        {
            if (!beam.Active || Paused(emitter) || TerminatingOrDeleted(emitter) ||
                beam.Target is not { } target || beam.SourceGrid is not { } source || source == target ||
                TerminatingOrDeleted(source) || TerminatingOrDeleted(target))
                continue;

            if (!shipsByTarget.TryGetValue(target, out var ships))
                shipsByTarget[target] = ships = new HashSet<EntityUid>();
            ships.Add(source);
        }

        var current = new Dictionary<EntityUid, string[]>();
        foreach (var (target, ships) in shipsByTarget)
        {
            current[target] = ships.Select(source =>
                {
                    // Match tractor targeting labels: detection may expose a contact, but does
                    // not reveal the true name of a vessel whose IFF label is hidden.
                    if (TryComp<IFFComponent>(source, out var iff) &&
                        (iff.Flags & (IFFFlags.Hide | IFFFlags.HideLabel | IFFFlags.HideLabelAlways)) != 0)
                        return Loc.GetString("tractor-capture-unknown-ship");
                    var name = MetaData(source).EntityName;
                    return string.IsNullOrWhiteSpace(name) ? Loc.GetString("tractor-capture-unknown-ship") : name;
                })
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
        }

        if (current.Count == _tractorCaptureSources.Count && current.All(entry =>
            _tractorCaptureSources.TryGetValue(entry.Key, out var old) && entry.Value.SequenceEqual(old)))
            return;

        _tractorCaptureSources = current;
        // Refresh on transitions only. Include remote/drone consoles, whose controlled shuttle
        // is resolved by ConsoleShuttleEvent rather than the console's own physical grid.
        RefreshShuttleConsoles();
    }
}
