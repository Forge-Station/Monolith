using Content.Shared.Radio;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.ShipPa;

/// <summary>
/// A ship's situation code and alarm state. Lives on the grid entity and is ensured by the server
/// the moment the grid has a speaker on it.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ShipAlertComponent : Component
{
    [DataField, AutoNetworkedField] public ProtoId<ShipAlertCodePrototype> Code = "WFShipCodeGreen";

    [DataField, AutoNetworkedField] public bool GeneralQuarters;

    [DataField, AutoNetworkedField] public int SpeakersOnline;

    [DataField, AutoNetworkedField] public int SpeakersTotal;

    /// <summary>True while the counts above are the ship's air alarms standing in for speakers.</summary>
    [DataField, AutoNetworkedField] public bool SpeakersFallback;

    /// <summary>Quietest the pilot may turn the PA down to.</summary>
    public const float MinVolume = 0.2f;

    /// <summary>Gain multiplier for everything the ship's speakers play, between <see cref="MinVolume"/> and 1.</summary>
    [DataField, AutoNetworkedField] public float Volume = 1f;

    /// <summary>Looped on every speaker while general quarters is sounded.</summary>
    [DataField] public SoundSpecifier GeneralQuartersAlarm = new SoundPathSpecifier("/Audio/Misc/redalert.ogg");

    [DataField] public LocId GeneralQuartersAnnouncement = "ship-pa-general-quarters-announcement";

    [DataField] public LocId GeneralQuartersSecureAnnouncement = "ship-pa-general-quarters-secure";

    /// <summary>Colour of the general quarters text over the speakers and in chat.</summary>
    [DataField] public Color GeneralQuartersColor = Color.FromHex("#ff3030");

    /// <summary>Played through the speakers when general quarters is sounded; the chime when unset.</summary>
    [DataField] public SoundSpecifier? GeneralQuartersSound;

    /// <summary>Played when securing from general quarters; the chime when unset.</summary>
    [DataField] public SoundSpecifier? GeneralQuartersSecureSound;

    /// <summary>Played before free-text announcements.</summary>
    [DataField] public SoundSpecifier AnnouncementChime = new SoundPathSpecifier("/Audio/Announcements/attention.ogg");

    [DataField] public TimeSpan AnnouncementCooldown = TimeSpan.FromSeconds(5);

    [DataField] public int MaxAnnouncementLength = 256;

    /// <summary>Server only.</summary>
    [ViewVariables] public TimeSpan NextAnnouncement;

    /// <summary>Longest link the console will take, so a pasted wall of text never reaches yt-dlp.</summary>
    [DataField] public int MaxUrlLength = 512;

    /// <summary>Server only. When this ship may next queue an internet sound from its console.</summary>
    [ViewVariables] public TimeSpan NextInternetSound;

    /// <summary>True while the ship's distress call stands: it is marked on every radar and the sector map.</summary>
    [DataField, AutoNetworkedField] public bool Distress;

    /// <summary>How long a distress call stands before it lapses on its own.</summary>
    [DataField] public TimeSpan DistressDuration = TimeSpan.FromMinutes(20);

    /// <summary>Shortest time between two distress calls, so the button can't spam the radio.</summary>
    [DataField] public TimeSpan DistressCooldown = TimeSpan.FromSeconds(60);

    /// <summary>The radio channel the call goes out on.</summary>
    [DataField] public ProtoId<RadioChannelPrototype> DistressChannel = "Common";

    [DataField] public Color DistressColor = Color.FromHex("#ff8c1a");

    /// <summary>Server only. When the standing call lapses.</summary>
    [ViewVariables] public TimeSpan DistressEnds;

    /// <summary>Server only. When this ship may next send a distress call.</summary>
    [ViewVariables] public TimeSpan NextDistress;
}
