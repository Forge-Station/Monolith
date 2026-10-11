using Content.Shared.CCVar;
using Robust.Shared.Configuration;
using Robust.Client.Audio;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Client._WF.CombatConsole;

/// <summary>Plays local mechanical control cues through the player's normal audio mixer.</summary>
public sealed class WFConsoleAudio : EntitySystem
{
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IConfigurationManager _configuration = default!;
    private TimeSpan _nextWarning;
    private readonly WFDetentThrottle _bearing = new();

    /// <summary>Sounds only explicit user presses, never replicated switch-state updates.</summary>
    public static void Press(BaseButton.ButtonEventArgs args)
    {
        var button = args.Button;
        var cue = button.ToggleMode && button.Group == null ?
            button.Pressed ? "switch_on" : "switch_off" : "key";
        IoCManager.Resolve<IEntityManager>().System<WFConsoleAudio>().Play(cue);
    }

    /// <summary>Gives a range adjustment a single detent sound on release.</summary>
    public static void Release(Slider slider) => IoCManager.Resolve<IEntityManager>().System<WFConsoleAudio>().Play("selector");

    /// <summary>Plays short rotary detents without overlapping during continuous bearing drags.</summary>
    public void TurnBearing()
    {
        if (_bearing.TryPlay(_timing.RealTime))
            Play("bearing");
    }

    /// <summary>Announces a new missile lock with a shared cooldown across console windows.</summary>
    public void Warn()
    {
        if (_timing.CurTime < _nextWarning)
            return;
        _nextWarning = _timing.CurTime + TimeSpan.FromSeconds(4);
        Play("warning");
    }

    private void Play(string cue)
    {
        var gain = _configuration.GetCVar(CCVars.InterfaceVolume);
        if (gain <= 0)
            return;
        _audio.PlayGlobal($"/Audio/_WF/CombatConsole/HighFleet/{cue}.ogg", Filter.Local(), false,
            AudioParams.Default.WithVolume(-12f + SharedAudioSystem.GainToVolume(gain)));
    }
}

/// <summary>Announces new visible threats once, without repeating on telemetry refreshes.</summary>
public sealed class WFThreatAnnunciator
{
    private bool _incoming;

    /// <summary>Tracks threat edges even while the console is hidden.</summary>
    public bool Update(int threats, bool visible)
    {
        var incoming = threats > 0;
        var announce = incoming && !_incoming && visible;
        _incoming = incoming;
        return announce;
    }
}
