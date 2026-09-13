using Content.Shared._Common.Consent;
using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Genitals.Prototypes;

/// <summary>Global anatomy settings. A single prototype, id Default, read through SharedGenitalsSystem.Settings.</summary>
[Prototype]
public sealed partial class GenitalSettingsPrototype : IPrototype
{
    /// <summary>Id of the settings prototype the game reads.</summary>
    public static readonly ProtoId<GenitalSettingsPrototype> DefaultId = "Default";

    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Minimum character age for any anatomy, for every species.</summary>
    [DataField]
    public int AdultAge = 18;

    /// <summary>Species that never have configurable anatomy.</summary>
    [DataField]
    public List<ProtoId<SpeciesPrototype>> ExcludedSpecies = new();

    [DataField]
    public byte PartialArousal = 30;

    [DataField]
    public byte FullArousal = 70;

    /// <summary>Arousal state from which the vagina's aroused variant is drawn.</summary>
    [DataField]
    public ArousalState VaginaArousedFrom = ArousalState.Full;

    [DataField]
    public int MinLengthCm = 5;

    [DataField]
    public int MaxLengthCm = 60;

    [DataField]
    public int DefaultLengthCm = 15;

    /// <summary>Upper bounds (cm) of penis sprite steps 1-4; anything longer is the last step.</summary>
    [DataField]
    public List<int> LengthStepMaxCm = new() { 17, 24, 32, 44 };

    [DataField]
    public int DefaultTesticleSize = 2;

    [DataField]
    public int DefaultCup = 3;

    /// <summary>Shape used to draw external testicles; the profile has no testicle shape of its own.</summary>
    [DataField]
    public ProtoId<GenitalShapePrototype> TesticlesShape = "GenitalShapeTesticlesPair";

    /// <summary>Seconds another player needs to remove or put back an undergarment.</summary>
    [DataField]
    public float StripDelay = 3f;

    /// <summary>Seconds after a completed removal by another player during which others cannot start another.</summary>
    [DataField]
    public float StripCooldown = 30f;

    /// <summary>Seconds in which a session earns back one owner request of the same kind and slot (server).</summary>
    [DataField]
    public float RequestInterval = 0.1f;

    /// <summary>Owner requests of the same kind and slot a session may send at once; after that, one per RequestInterval (server).</summary>
    [DataField]
    public int RequestBurst = 3;

    [DataField]
    public ProtoId<ConsentTogglePrototype> MasterConsent = "GenitalMarkings";

    /// <summary>Toggle a target needs before others may remove their undergarments (UndergarmentStrip). Unset refuses.</summary>
    [DataField]
    public ProtoId<ConsentTogglePrototype>? StripConsent;

    /// <summary>Toggle a patient needs before anatomy surgery by someone else (AnatomySurgery). Unset refuses.</summary>
    [DataField]
    public ProtoId<ConsentTogglePrototype>? SurgeryConsent;

    /// <summary>Pixel offsets per species and region, applied to every anatomy layer of that region (+y is up).</summary>
    [DataField]
    public Dictionary<ProtoId<SpeciesPrototype>, Dictionary<GenitalRegion, Vector2i>> SpeciesOffsets = new();

    /// <summary>Ordinary markings that also require an adult character (removed below AdultAge, hidden in the picker).</summary>
    [DataField]
    public List<string> AdultOnlyMarkings = new();
}
