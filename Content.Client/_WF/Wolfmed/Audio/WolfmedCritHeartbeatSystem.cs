using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared._WF.Wolfmed.Hud;
using Content.Shared._WF.Wolfmed.Life;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using JetBrains.Annotations;
using Robust.Client.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Player;

namespace Content.Client._WF.Wolfmed.Audio;

/// <summary>
/// Plays a looping heartbeat while the local player's own body is in critical condition. Never plays for
/// anyone else's mob and works for any entity with MobStateComponent, not only wound hosts.
/// </summary>
[UsedImplicitly]
public sealed class WolfmedCritHeartbeatSystem : EntitySystem
{
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    private const string HeartbeatSound = "/Audio/_WF/Wolfmed/heartbeat_loop.ogg";

    /// <summary>BRAIN: the one flat tone that marks the moment the heart stops. A flatline is silence after it.</summary>
    private const string FlatlineSound = "/Audio/_WF/Wolfmed/flatline.ogg";

    private EntityUid? _stream;
    private bool _cvarEnabled = true;
    private bool _flatlined;

    /// <summary>
    /// True whenever the loop should be playing, even if the headless audio backend returned no stream
    /// (e.g. in integration tests).
    /// </summary>
    public bool Active { get; private set; }

    /// <summary>BRAIN: true while the local body's heart has stopped. A flatline is silence, so the loop is off.</summary>
    public bool Flatlined { get; private set; }

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LocalPlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<LocalPlayerDetachedEvent>(OnPlayerDetached);
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
        // MobStateComponent has no ComponentHandleState of its own (AutoGenerateComponentState), so a state
        // change made purely on the server (the common case: wound/threshold crit is server-only) never raises
        // MobStateChangedEvent on the client. AfterAutoHandleStateEvent fires whenever the networked fields are
        // applied client-side and is the reliable signal here.
        SubscribeLocalEvent<MobStateComponent, AfterAutoHandleStateEvent>(OnMobStateNetworked);
        SubscribeLocalEvent<EntityTerminatingEvent>(OnEntityTerminating);

        Subs.CVar(_cfg, WolfmedCVars.CritHeartbeat, OnCVarChanged, true);
    }

    private void OnCVarChanged(bool value)
    {
        _cvarEnabled = value;
        Refresh();
    }

    private void OnPlayerAttached(LocalPlayerAttachedEvent args)
    {
        Refresh();
    }

    private void OnPlayerDetached(LocalPlayerDetachedEvent args)
    {
        Stop();
    }

    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        if (args.Target != _player.LocalEntity)
            return;

        Refresh();
    }

    private void OnMobStateNetworked(EntityUid uid, MobStateComponent component, ref AfterAutoHandleStateEvent args)
    {
        if (uid != _player.LocalEntity)
            return;

        Refresh();
    }

    private void OnEntityTerminating(ref EntityTerminatingEvent args)
    {
        if (args.Entity.Owner != _player.LocalEntity)
            return;

        Stop();
    }

    /// <summary>Re-checks the local entity's mob state and starts or stops the loop to match.</summary>
    private void Refresh()
    {
        // BRAIN: a stopped heart makes no sound at all. One flat tone marks the moment, then nothing.
        var arrest = _player.LocalEntity is { } arrested && HasComp<WolfmedCardiacArrestComponent>(arrested);
        Flatlined = arrest;
        if (arrest && !_flatlined)
            _audio.PlayGlobal(FlatlineSound, Filter.Local(), false, AudioParams.Default.WithVolume(-6f));

        _flatlined = arrest;

        if (!arrest &&
            _cvarEnabled &&
            _player.LocalEntity is { } local &&
            TryComp<MobStateComponent>(local, out var mobState) &&
            mobState.CurrentState == MobState.Critical &&
            !Silent(local))
        {
            Start();
        }
        else
        {
            Stop();
        }
    }

    /// <summary>
    /// M1a: no heartbeat for a machine (the synthetic readout is its sound), and none for a faint: a few
    /// seconds under from pain is not the dying heartbeat.
    /// </summary>
    private bool Silent(EntityUid local) =>
        HasComp<WolfmedSyntheticHudComponent>(local) ||
        CompOrNull<WolfmedConsciousnessComponent>(local)?.Cause is { } cause && WolfmedCauses.IsFaint(cause);

    /// <summary>
    /// Reconciles every frame as well as on events. A mob state change replayed by prediction reaches the
    /// handlers on a tick that is not first-time-predicted, so the events alone can miss the real transition.
    /// </summary>
    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        Refresh();
    }

    private void Start()
    {
        Active = true;
        if (_stream is { } existing && !Deleted(existing))
            return;

        var played = _audio.PlayGlobal(HeartbeatSound, Filter.Local(), false,
            AudioParams.Default.WithLoop(true).WithVolume(-4f));
        _stream = played?.Entity;
    }

    private void Stop()
    {
        Active = false;
        if (_stream is not { } stream)
            return;

        // Deleted directly: SharedAudioSystem.Stop is a no-op on a tick that is not first-time-predicted, which
        // left the loop orphaned and playing through death.
        _stream = null;
        if (!Deleted(stream))
            QueueDel(stream);
    }
}
