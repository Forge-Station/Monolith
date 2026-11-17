using Content.Goobstation.Shared.StationRadio.Components;
using Content.Goobstation.Shared.StationRadio.Events;
using Content.Server.Popups;
using Content.Server.Radio.EntitySystems;
using Content.Shared._Forge.Radio;
using Content.Shared._Forge.StationRadio;
using Content.Shared.Examine;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Radio.Components;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;

namespace Content.Server._Forge.StationRadio;

public sealed class StationRadioFrequencySystem : EntitySystem
{
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly RadioDeviceSystem _radioDevice = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly SharedPowerReceiverSystem _power = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();

        Subs.BuiEvents<StationRadioReceiverComponent>(StationRadioFrequencyUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnUiOpened);
            subs.Event<SelectStationRadioFrequencyMessage>(OnSelectReceiverFrequency);
            subs.Event<ToggleStationRadioReceiverMessage>(OnToggleReceiver);
        });

        Subs.BuiEvents<RadioRigComponent>(StationRadioFrequencyUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnUiOpened);
            subs.Event<SelectStationRadioFrequencyMessage>(OnSelectRigFrequency);
        });

        Subs.BuiEvents<StationRadioServerComponent>(StationRadioFrequencyUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnUiOpened);
            subs.Event<SelectStationRadioFrequencyMessage>(OnSelectServerFrequency);
        });

        SubscribeLocalEvent<StationRadioReceiverComponent, ExaminedEvent>(OnReceiverExamined);
        SubscribeLocalEvent<RadioRigComponent, ExaminedEvent>(OnRigExamined);
    }

    private void OnUiOpened(EntityUid uid, StationRadioReceiverComponent component, BoundUIOpenedEvent args)
    {
        UpdateUi(uid);
    }

    private void OnUiOpened(EntityUid uid, RadioRigComponent component, BoundUIOpenedEvent args)
    {
        UpdateUi(uid);
    }

    private void OnUiOpened(EntityUid uid, StationRadioServerComponent component, BoundUIOpenedEvent args)
    {
        UpdateUi(uid);
    }

    private void OnSelectReceiverFrequency(EntityUid uid, StationRadioReceiverComponent component, SelectStationRadioFrequencyMessage args)
    {
        if (!args.Actor.Valid)
            return;

        if (!TrySetFrequency(uid, args.Frequency, args.Actor, out var frequency))
        {
            UpdateUi(uid);
            return;
        }

        if (component.Frequency == frequency)
        {
            UpdateUi(uid);
            return;
        }

        component.Frequency = frequency;
        Dirty(uid, component);
        RetuneReceiver(uid, component);
        UpdateUi(uid);
    }

    private void OnToggleReceiver(EntityUid uid, StationRadioReceiverComponent component, ToggleStationRadioReceiverMessage args)
    {
        if (!args.Actor.Valid)
            return;

        component.Active = !component.Active;
        Dirty(uid, component);

        if (component.SoundEntity != null)
        {
            _audio.SetGain(component.SoundEntity, component.Active && _power.IsPowered(uid)
                ? component.DefaultParams.Volume
                : 0f);
        }

        UpdateUi(uid);
    }

    private void OnSelectRigFrequency(EntityUid uid, RadioRigComponent component, SelectStationRadioFrequencyMessage args)
    {
        if (!args.Actor.Valid)
            return;

        if (!TrySetFrequency(uid, args.Frequency, args.Actor, out var frequency))
        {
            UpdateUi(uid);
            return;
        }

        if (component.Frequency == frequency)
        {
            UpdateUi(uid);
            return;
        }

        var oldFrequency = component.Frequency;
        component.Frequency = frequency;
        Dirty(uid, component);
        RebroadcastRig(uid, oldFrequency, frequency);
        UpdateUi(uid);
    }

    private void OnSelectServerFrequency(EntityUid uid, StationRadioServerComponent component, SelectStationRadioFrequencyMessage args)
    {
        if (!args.Actor.Valid)
            return;

        if (!TrySetFrequency(uid, args.Frequency, args.Actor, out var frequency))
        {
            UpdateUi(uid);
            return;
        }

        _radioDevice.SetMicrophoneFrequency(uid, frequency);
        UpdateUi(uid);
    }

    private void OnReceiverExamined(EntityUid uid, StationRadioReceiverComponent component, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        args.PushMarkup(Loc.GetString("station-radio-frequency-examine-listen", ("frequency", component.Frequency)));
    }

    private void OnRigExamined(EntityUid uid, RadioRigComponent component, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        args.PushMarkup(Loc.GetString("station-radio-frequency-examine-music", ("frequency", component.Frequency)));
    }

    private bool TrySetFrequency(EntityUid uid, int frequency, EntityUid actor, out int resolved)
    {
        resolved = frequency;
        if (frequency < 0)
            return false;

        const int min = RadioFrequencyPresetListPrototype.DefaultMinFrequency;
        const int max = RadioFrequencyPresetListPrototype.DefaultMaxFrequency;
        if (frequency < min || frequency > max)
        {
            _popup.PopupEntity(
                Loc.GetString("configurable-encryption-key-frequency-out-of-range", ("min", min), ("max", max)),
                uid,
                actor);
            return false;
        }

        return true;
    }

    private void UpdateUi(EntityUid uid)
    {
        StationRadioFrequencyMode mode;
        int frequency;
        var receiverOn = false;

        if (TryComp<StationRadioReceiverComponent>(uid, out var receiver))
        {
            mode = StationRadioFrequencyMode.Listen;
            frequency = receiver.Frequency;
            receiverOn = receiver.Active;
        }
        else if (TryComp<RadioRigComponent>(uid, out var rig))
        {
            mode = StationRadioFrequencyMode.Music;
            frequency = rig.Frequency;
        }
        else if (TryComp<RadioMicrophoneComponent>(uid, out var mic))
        {
            mode = StationRadioFrequencyMode.Voice;
            frequency = mic.Frequency;
        }
        else
            return;

        _ui.SetUiState(uid,
            StationRadioFrequencyUiKey.Key,
            new StationRadioFrequencyBoundUIState(
                frequency,
                RadioFrequencyPresetListPrototype.DefaultMinFrequency,
                RadioFrequencyPresetListPrototype.DefaultMaxFrequency,
                mode,
                receiverOn));
    }

    private void RetuneReceiver(EntityUid uid, StationRadioReceiverComponent receiver)
    {
        RaiseLocalEvent(uid, new StationRadioMediaStoppedEvent());

        var players = EntityQueryEnumerator<VinylPlayerComponent>();
        while (players.MoveNext(out var player, out var playerComp))
        {
            if (playerComp.SoundEntity == null)
                continue;

            if (!StationRadioFrequencyHelpers.TryGetBroadcastFrequency(EntityManager, player, out var broadcast) ||
                broadcast != receiver.Frequency)
                continue;

            if (!TryGetVinylSong(player, out var song))
                continue;

            RaiseLocalEvent(uid, new StationRadioMediaPlayedEvent(song));
            break;
        }
    }

    private void RebroadcastRig(EntityUid rig, int oldFrequency, int newFrequency)
    {
        var receivers = EntityQueryEnumerator<StationRadioReceiverComponent>();
        while (receivers.MoveNext(out var receiver, out var receiverComp))
        {
            if (receiverComp.Frequency == oldFrequency || receiverComp.Frequency == newFrequency)
                RaiseLocalEvent(receiver, new StationRadioMediaStoppedEvent());
        }

        var players = EntityQueryEnumerator<VinylPlayerComponent>();
        while (players.MoveNext(out var player, out var playerComp))
        {
            if (playerComp.SoundEntity == null)
                continue;

            if (!StationRadioFrequencyHelpers.TryGetBroadcastFrequency(EntityManager, player, out var broadcast) ||
                broadcast != newFrequency)
                continue;

            if (!TryGetVinylSong(player, out var song))
                continue;

            var matching = EntityQueryEnumerator<StationRadioReceiverComponent>();
            while (matching.MoveNext(out var receiver, out var receiverComp))
            {
                if (receiverComp.Frequency == newFrequency)
                    RaiseLocalEvent(receiver, new StationRadioMediaPlayedEvent(song));
            }
        }
    }

    private bool TryGetVinylSong(EntityUid player, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Robust.Shared.Audio.SoundPathSpecifier? song)
    {
        song = null;
        if (!_containers.TryGetContainer(player, "vinyl", out var container))
            return false;

        foreach (var contained in container.ContainedEntities)
        {
            if (!TryComp<VinylComponent>(contained, out var vinyl) || vinyl.Song == null)
                continue;

            song = vinyl.Song;
            return true;
        }

        return false;
    }
}
