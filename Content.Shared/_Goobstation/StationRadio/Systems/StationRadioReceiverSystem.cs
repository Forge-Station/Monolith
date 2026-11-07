using Content.Goobstation.Shared.StationRadio.Components;
using Content.Goobstation.Shared.StationRadio.Events;
using Content.Shared.Power;
using Content.Shared.Power.EntitySystems;
using Robust.Shared.Audio.Components; // Forge-Change
using Robust.Shared.Audio.Systems;

namespace Content.Goobstation.Shared.StationRadio.Systems;

public sealed class StationRadioReceiverSystem : EntitySystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedPowerReceiverSystem _power = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<StationRadioReceiverComponent, StationRadioMediaPlayedEvent>(OnMediaPlayed);
        SubscribeLocalEvent<StationRadioReceiverComponent, StationRadioMediaStoppedEvent>(OnMediaStopped);
        SubscribeLocalEvent<StationRadioReceiverComponent, PowerChangedEvent>(OnPowerChanged);
    }

    private void OnPowerChanged(EntityUid uid, StationRadioReceiverComponent comp, PowerChangedEvent args)
    {
        if (comp.SoundEntity is not { } sound)
            return;

        // Forge-Change: power ticks keep arriving after the stream entity is gone. SetGain resolves
        // AudioComponent and logs every time, so forget the dead stream instead.
        if (!TryComp<AudioComponent>(sound, out _))
        {
            comp.SoundEntity = null;
            return;
        }

        _audio.SetGain(sound, args.Powered && comp.Active ? comp.DefaultParams.Volume : 0f);
    }

    private void OnMediaPlayed(EntityUid uid, StationRadioReceiverComponent comp, StationRadioMediaPlayedEvent args)
    {
        var audio = _audio.PlayPredicted(args.MediaPlayed, uid, uid, comp.DefaultParams);
        if (audio != null && _power.IsPowered(uid) && comp.Active)
            comp.SoundEntity = audio.Value.Entity;
        else if (audio != null && !_power.IsPowered(uid) || !comp.Active && audio != null)
        {
            comp.SoundEntity = audio.Value.Entity;
            _audio.SetGain(comp.SoundEntity, 0);
        }
    }

    private void OnMediaStopped(EntityUid uid, StationRadioReceiverComponent comp, StationRadioMediaStoppedEvent args)
    {
        if (comp.SoundEntity == null)
            return;

        comp.SoundEntity = _audio.Stop(comp.SoundEntity);
    }
}
