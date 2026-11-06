using Content.Shared._WF.Lattice;
using Content.Shared.CCVar;
using Content.Shared.Ghost;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Client._WF.Lattice;

/// <summary>
/// Shows the hallucinations the server sends this player: figures spawned only on this client, radar contacts that
/// aren't there, and a warped screen that grows with Drift.
/// </summary>
public sealed partial class WFLatticeHallucinationSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <summary>What a glimpse or phantom looks like.</summary>
    private const string FigurePrototype = "WFRemadeGlimpse";

    /// <summary>A phantom goes when the player comes this close, in metres.</summary>
    private const float VanishRange = 3f;

    private readonly List<(EntityUid Figure, TimeSpan Ends, bool Vanishes)> _figures = new();
    private WFDriftPulseOverlay _pulse = default!;

    /// <summary>Radar contacts that aren't there: where, until when, and a seed for their garbled label.</summary>
    public readonly List<(MapCoordinates Position, TimeSpan Ends, int Seed)> Blips = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<WFLatticeHallucinationEvent>(OnHallucination);
        _overlays.AddOverlay(new WFDriftOverlay());
        _pulse = new WFDriftPulseOverlay();
        _overlays.AddOverlay(_pulse);
        Subs.CVar(_config, CCVars.ReducedMotion, still => _pulse.Still = still, true);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _overlays.RemoveOverlay<WFDriftOverlay>();
        _overlays.RemoveOverlay(_pulse);
        Clear();
    }

    /// <summary>The local player's Drift level: their ship's or their own, or what they carry in their head if that is more.</summary>
    public int LocalLevel()
    {
        // The Choir see nothing wrong, and the dead are past it.
        if (_player.LocalEntity is { } mob && (HasComp<WFChoirMemberComponent>(mob) || HasComp<GhostComponent>(mob)))
            return 0;

        return Math.Max(LocalDrift()?.Level ?? 0, LocalMind()?.MindLevel ?? 0);
    }

    /// <summary>The local player's own Drift, which holds what a Monolith or another source has put in their head.</summary>
    public WFDriftComponent? LocalMind()
    {
        return _player.LocalEntity is { } mob && TryComp<WFDriftComponent>(mob, out var own) ? own : null;
    }

    /// <summary>The Drift the local player carries: their ship's, or their own off a ship.</summary>
    public WFDriftComponent? LocalDrift()
    {
        if (_player.LocalEntity is not { } mob)
            return null;

        if (Transform(mob).GridUid is { } grid && TryComp<WFDriftComponent>(grid, out var ship))
            return ship;

        return TryComp<WFDriftComponent>(mob, out var own) ? own : null;
    }

    private void OnHallucination(WFLatticeHallucinationEvent ev)
    {
        var coordinates = GetCoordinates(ev.Coordinates);
        var ends = _timing.CurTime + TimeSpan.FromSeconds(ev.Seconds);
        switch (ev.Kind)
        {
            case WFLatticeHallucinationKind.Pulse:
                _pulse.Start(ev.Seconds, ev.Seed);
                break;
            case WFLatticeHallucinationKind.Blip:
                Blips.Add((_transform.ToMapCoordinates(coordinates), ends, ev.Seed));
                break;
            case WFLatticeHallucinationKind.Glimpse:
            case WFLatticeHallucinationKind.Phantom:
                if (!coordinates.IsValid(EntityManager))
                    return;

                var figure = Spawn(FigurePrototype, coordinates);
                _figures.Add((figure, ends, ev.Kind == WFLatticeHallucinationKind.Phantom));
                break;
        }
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var now = _timing.CurTime;
        Blips.RemoveAll(blip => blip.Ends <= now);
        var viewer = _player.LocalEntity;
        for (var i = _figures.Count - 1; i >= 0; i--)
        {
            var (figure, ends, vanishes) = _figures[i];
            var gone = TerminatingOrDeleted(figure);
            var near = vanishes
                       && !gone
                       && viewer is { } mob
                       && _transform.InRange(Transform(figure).Coordinates, Transform(mob).Coordinates, VanishRange);
            if (now < ends && !near && !gone)
                continue;

            if (!gone)
                Del(figure);

            _figures.RemoveAt(i);
        }
    }

    private void Clear()
    {
        foreach (var (figure, _, _) in _figures)
        {
            if (!TerminatingOrDeleted(figure))
                Del(figure);
        }

        _figures.Clear();
        Blips.Clear();
    }
}
