using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Goobstation.Shared.StationRadio.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class StationRadioReceiverComponent : Component
{
    /// <summary>
    /// The sound entity being played
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? SoundEntity;

    /// <summary>
    /// Is the radio turned on
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Active = true;

    /// <summary>
    /// Frequency this receiver is tuned to for music and RadioShow voice.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int Frequency = 1285;

    /// <summary>
    /// Default audio params for the played audio.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public AudioParams DefaultParams = AudioParams.Default.WithVolume(3.5f).WithMaxDistance(8f); // 8 is just the edge of the screen usually
}
