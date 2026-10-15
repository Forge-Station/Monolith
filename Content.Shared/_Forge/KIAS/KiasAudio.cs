using Robust.Shared.Serialization;

namespace Content.Shared._Forge.KIAS;

public enum KiasAudioChannel : byte { Notification, Warning, Battle, Emergency }
public enum KiasTonePreset : byte { Silent, Chime, Buzzer, BlueAlert, RedAlert, ReactorAlarm }

[RegisterComponent]
public sealed partial class KiasAudioComponent : Component
{
    [DataField] public KiasTonePreset Notification = KiasTonePreset.Chime;
    [DataField] public KiasTonePreset Warning = KiasTonePreset.Buzzer;
    [DataField] public KiasTonePreset Battle = KiasTonePreset.RedAlert;
    [DataField] public KiasTonePreset Emergency = KiasTonePreset.ReactorAlarm;
    [DataField] public float SpeechCooldown = 5;
    [DataField] public float ToneCooldown = 10;
    [DataField] public float LogCooldown = 2;
    [DataField] public float PreviewCooldown = 5;
    public TimeSpan PreviewAfter;

    public KiasTonePreset Preset(KiasAudioChannel channel) => channel switch
    {
        KiasAudioChannel.Warning => Warning, KiasAudioChannel.Battle => Battle,
        KiasAudioChannel.Emergency => Emergency, _ => Notification,
    };
}

[Serializable, NetSerializable]
public sealed class KiasAudioSettingsMessage : BoundUserInterfaceMessage
{
    public KiasAudioChannel Channel;
    public KiasTonePreset Preset;
    public bool Preview;
}

[Serializable, NetSerializable]
public sealed class KiasAudioSettingsView
{
    public KiasTonePreset Notification;
    public KiasTonePreset Warning;
    public KiasTonePreset Battle;
    public KiasTonePreset Emergency;
}
