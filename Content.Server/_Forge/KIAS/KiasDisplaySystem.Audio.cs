using System.Linq;
using Content.Shared._Forge.KIAS;
using Robust.Shared.Audio;

namespace Content.Server._Forge.KIAS;

public sealed partial class KiasDisplaySystem
{
    private readonly KiasEmissionGate _speechGate = new();
    private readonly KiasEmissionGate _audioGate = new();

    private static string? TonePath(KiasTonePreset preset) => preset switch
    {
        KiasTonePreset.Chime => "/Audio/Effects/alert.ogg",
        KiasTonePreset.Buzzer => "/Audio/Machines/warning_buzzer.ogg",
        KiasTonePreset.BlueAlert => "/Audio/Misc/bluealert.ogg",
        KiasTonePreset.RedAlert => "/Audio/Misc/redalert.ogg",
        KiasTonePreset.ReactorAlarm => "/Audio/_FarHorizons/Machines/reactor_alarm_3.ogg",
        _ => null,
    };

    private KiasAudioComponent? AudioSettings(EntityUid grid) => TryComp<KiasGridComponent>(grid, out var runtime)
        && runtime.Core is { } core && TryComp<KiasAudioComponent>(core, out var audio) ? audio : null;

    private void OnAudioSettings(Entity<KiasManagementComponent> ent, ref KiasAudioSettingsMessage args)
    {
        if (!Enum.IsDefined(args.Channel) || !Enum.IsDefined(args.Preset) || !_kias.IsOnline(ent)
            || Transform(ent).GridUid is not { } grid || !_kias.CanConfigure(grid, args.Actor)
            || AudioSettings(grid) is not { } settings) return;
        if (args.Preview)
        {
            if (_timing.CurTime < settings.PreviewAfter) return;
            settings.PreviewAfter = _timing.CurTime + TimeSpan.FromSeconds(Math.Clamp(settings.PreviewCooldown, 1, 60));
            foreach (var speaker in Comp<KiasGridComponent>(grid).Online.Where(uid => _kias.IsOnline(uid) && HasComp<KiasSpeakerComponent>(uid)))
                PlayTone(speaker, args.Preset);
            return;
        }
        switch (args.Channel)
        {
            case KiasAudioChannel.Notification: settings.Notification = args.Preset; break;
            case KiasAudioChannel.Warning: settings.Warning = args.Preset; break;
            case KiasAudioChannel.Battle: settings.Battle = args.Preset; break;
            case KiasAudioChannel.Emergency: settings.Emergency = args.Preset; break;
        }
        RefreshOpen(grid);
    }

    private void PlayTone(EntityUid speaker, KiasTonePreset preset)
    {
        var component = Comp<KiasSpeakerComponent>(speaker);
        component.Tone = _audio.Stop(component.Tone);
        component.FinishesShutdownTone = Transform(speaker).GridUid is { } grid
            && EntityManager.System<Controllers.KiasControllerRuntimeSystem>().IsFinishingShutdown(grid);
        if (TonePath(preset) is { } path)
            component.Tone = _audio.PlayPvs(new SoundPathSpecifier(path), speaker, AudioParams.Default.WithVolume(-4))?.Entity;
    }
}
