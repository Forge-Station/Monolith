using Content.Server._WF.Cockpit;
using Content.Server.Shuttles.Components;
using Content.Shared._WF.AsteroidMining;
using Content.Shared.Shuttles.Components;

namespace Content.Server._WF.AsteroidMining;

public sealed partial class WFMiningBeamSystem
{
    [Dependency] private WFCockpitGunnerySystem _cockpit = default!;

    /// <summary>Each linked cockpit pilot's helm, the rock their ship's lasers were last on, and the scan last sent.</summary>
    private readonly Dictionary<EntityUid, (EntityUid Helm, EntityUid? Rock, WFMiningBeamGunneryState Sent)> _cockpits = new();
    private readonly List<(EntityUid Actor, EntityUid Helm, EntityUid Console)> _cockpitLinks = new();
    private readonly List<EntityUid> _cockpitsLost = new();

    private void InitializeCockpit()
    {
        // A cockpit pilot sets the lasers through the gunnery console their helm is linked to.
        Subs.BuiEvents<ShuttleConsoleComponent>(ShuttleConsoleUiKey.Key, subs =>
        {
            subs.Event<WFMiningBeamPowerMessage>(OnCockpitPower);
        });
    }

    private void OnCockpitPower(Entity<ShuttleConsoleComponent> ent, ref WFMiningBeamPowerMessage args)
    {
        if (_cockpit.GetConsole(args.Actor, ent) is { } console)
            SetPower(console, args.Power);
    }

    /// <summary>
    /// Sends each linked cockpit pilot, through their helm, the scan their gunnery console would show, and an empty one
    /// to a pilot whose link is gone, which hides the cockpit's mining page. Unchanged scans aren't resent.
    /// </summary>
    private void UpdateCockpits(bool grouped)
    {
        _cockpitLinks.Clear();
        _cockpitLinks.AddRange(_cockpit.Links());
        if (_cockpitLinks.Count == 0 && _cockpits.Count == 0)
            return;

        if (_cockpitLinks.Count > 0 && !grouped)
            GroupShipLasers();

        _cockpitsLost.Clear();
        _cockpitsLost.AddRange(_cockpits.Keys);
        foreach (var (actor, helm, console) in _cockpitLinks)
        {
            _cockpitsLost.Remove(actor);
            var known = _cockpits.TryGetValue(actor, out var last) && last.Helm == helm;
            var rock = known ? last.Rock : null;
            var state = GunneryState(GunneryGrid(console), ref rock);
            _cockpits[actor] = (helm, rock, state);
            if (!known || !SameState(last.Sent, state))
                _ui.ServerSendUiMessage(helm, ShuttleConsoleUiKey.Key, new WFMiningBeamGunneryUpdateMessage(state), actor);
        }

        foreach (var actor in _cockpitsLost)
        {
            var (helm, _, sent) = _cockpits[actor];
            _cockpits.Remove(actor);
            if (sent.Lasers > 0 && !TerminatingOrDeleted(helm))
                _ui.ServerSendUiMessage(helm, ShuttleConsoleUiKey.Key, new WFMiningBeamGunneryUpdateMessage(default), actor);
        }
    }
}
