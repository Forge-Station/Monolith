using Robust.Shared.GameStates;

namespace Content.Shared._WF.Audio.InternetSound;

/// <summary>
/// Marks a client's own stream of an internet sound played to everyone at once, so it is held at whatever the
/// listener set on the radio. Ship PA tracks don't get this: they're diegetic and follow the ordinary volume
/// sliders like any other sound a speaker makes.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class InternetSoundAudioComponent : Component;
