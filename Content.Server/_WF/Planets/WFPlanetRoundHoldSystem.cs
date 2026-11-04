using Content.Server._WF.Planets.Bounds;
using Content.Server.Chat.Managers;
using Content.Server.GameTicking;
using Content.Shared._WF.CCVar;
using Content.Shared.GameTicking;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Server._WF.Planets;

/// <summary>
/// Holds the round start while a round-start world built in the lobby is still generating, for at most
/// wf.planet_round_hold_max seconds, and starts the round itself once they are done or the time is up.
/// </summary>
public sealed class WFPlanetRoundHoldSystem : EntitySystem
{
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private WFPlanetNetworkSystem _networks = default!;
    [Dependency] private WFPlanetPreloadSystem _preload = default!;

    /// <summary>When the held start was first asked for; null while nothing is held.</summary>
    private TimeSpan? _heldSince;

    /// <summary>Whether the held start was forced, as by an admin.</summary>
    private bool _force;

    /// <summary>Whether the held start was asked for while the countdown was paused, which only an admin can do.</summary>
    private bool _pausedRequest;

    /// <summary>Set once the cap has let a start through, until the next lobby.</summary>
    private bool _released;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFRoundStartHoldEvent>(OnHold);
        SubscribeLocalEvent<GameRunLevelChangedEvent>(OnRunLevelChanged);
    }

    /// <summary>Whether a round-start world built in the lobby is still generating.</summary>
    public bool WorldsForming()
    {
        if (!_cfg.GetCVar(PlanetCVars.PlanetNetworks))
            return false;

        foreach (var ground in _networks.PrebuiltGrounds())
        {
            if (_preload.IsPreloading(ground))
                return true;
        }

        return false;
    }

    /// <summary>Whether a start is being held back right now.</summary>
    public bool Holding => _heldSince != null;

    private void OnHold(ref WFRoundStartHoldEvent args)
    {
        if (_released || !WorldsForming())
            return;

        var cap = TimeSpan.FromSeconds(_cfg.GetCVar(PlanetCVars.RoundHoldMax));
        if (_heldSince is { } since && _timing.CurTime - since >= cap)
        {
            Release();
            return;
        }

        args.Hold = true;
        _force |= args.Force;

        if (_heldSince != null)
            return;

        _heldSince = _timing.CurTime;
        _pausedRequest = _ticker.Paused;
        Log.Info($"Holding the round start for up to {cap.TotalSeconds:F0} s: round-start worlds are still forming.");
        _chat.DispatchServerAnnouncement(Loc.GetString("wf-planet-round-hold"));
    }

    private void OnRunLevelChanged(GameRunLevelChangedEvent args)
    {
        _heldSince = null;
        _force = false;
        _released = false;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_heldSince is not { } since)
            return;

        if (_ticker.RunLevel != GameRunLevel.PreRoundLobby)
        {
            _heldSince = null;
            return;
        }

        // Paused since the start was asked for: the start is called off, and the countdown asks again once unpaused.
        if (_ticker.Paused && !_pausedRequest)
        {
            _heldSince = null;
            _force = false;
            return;
        }

        var capped = _timing.CurTime - since >= TimeSpan.FromSeconds(_cfg.GetCVar(PlanetCVars.RoundHoldMax));
        if (WorldsForming() && !capped)
            return;

        if (capped)
            Release();
        else
            Log.Info($"Round-start worlds formed after {(_timing.CurTime - since).TotalSeconds:F0} s; starting the round.");

        var force = _force;
        _heldSince = null;
        _force = false;
        _ticker.StartRound(force);
    }

    /// <summary>Lets the start through for the rest of this lobby, with the worlds still forming.</summary>
    private void Release()
    {
        if (_released)
            return;

        _released = true;
        _heldSince = null;
        Log.Warning("The round-start hold ran out; starting the round with worlds still forming.");
        _chat.DispatchServerAnnouncement(Loc.GetString("wf-planet-round-hold-expired"));
    }
}
