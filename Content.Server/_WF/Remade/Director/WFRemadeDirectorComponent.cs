using Robust.Shared.Audio;

namespace Content.Server._WF.Remade.Director;

/// <summary>Where a Monolith's Director is in its build-up, attack and relax cycle.</summary>
public enum WFRemadeDirectorPhase : byte
{
    Quiet,
    Stalk,
    Peak,
    Relax,
}

/// <summary>The AI that plays a Monolith: spends biomass on the Remade and works on the crew's nerves between attacks.</summary>
[RegisterComponent]
public sealed partial class WFRemadeDirectorComponent : Component
{
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float Biomass;

    /// <summary>Biomass paid out when the Monolith first reaches Growth. DS13 gives the Marker the same.</summary>
    [DataField]
    public float FirstGrowthBiomass = 250f;

    [ViewVariables]
    public bool Seeded;

    /// <summary>Whether the first Growth opens with a quiet spell and a lone sighting before any wave. Tests turn it off.</summary>
    [DataField]
    public bool BuildUp = true;

    /// <summary>Seconds the first Growth stays quiet: corruption and signs, no wave.</summary>
    [DataField]
    public float FirstQuietMin = 150f;

    [DataField]
    public float FirstQuietMax = 210f;

    /// <summary>No wave, stalker, ambush, garrison or morgue burst before this. Set when the stone first reaches Growth.</summary>
    [ViewVariables]
    public TimeSpan FirstWaveAt;

    /// <summary>When the lone sighting comes, part way through the first quiet spell; null once it has.</summary>
    [ViewVariables]
    public TimeSpan? SightingAt;

    /// <summary>Crew further than this leave the Director dormant.</summary>
    [DataField]
    public float AwakeRadius = 64f;

    /// <summary>Crew stress at or above this holds the Director back.</summary>
    [DataField]
    public float StressHigh = 40f;

    /// <summary>Crew stress below this lets the Director press harder.</summary>
    [DataField]
    public float StressCalm = 15f;

    /// <summary>Seconds a crewman is left alone after a wave, below Rapture.</summary>
    [DataField]
    public float WaveRest = 240f;

    /// <summary>Seconds a hurt Remade stays in the vents before it comes back.</summary>
    [DataField]
    public float RetreatMin = 30f;

    [DataField]
    public float RetreatMax = 60f;

    [DataField]
    public SoundSpecifier BurstSound = new SoundCollectionSpecifier("MetalBreak");

    [DataField]
    public SoundSpecifier FalseSound = new SoundCollectionSpecifier("WFRemadeSlasherSpeech");

    [DataField]
    public SoundSpecifier FarCry = new SoundCollectionSpecifier("WFRemadeSlasherShoutLong");

    /// <summary>Rattling heard at each vent a hidden Remade passes.</summary>
    [DataField]
    public SoundSpecifier RattleSound = new SoundCollectionSpecifier("XenoVentCrawl");

    [DataField]
    public SoundSpecifier AlarmSound = new SoundPathSpecifier("/Audio/_WF/Remade/monolith/alarm.ogg");

    [ViewVariables]
    public WFRemadeDirectorPhase Phase = WFRemadeDirectorPhase.Quiet;

    [ViewVariables]
    public TimeSpan PhaseEnd;

    [ViewVariables]
    public TimeSpan NextDread;

    [ViewVariables]
    public TimeSpan NextTick;

    [ViewVariables]
    public TimeSpan NextAmbush;

    [ViewVariables]
    public TimeSpan NextStalker;

    /// <summary>When it next shuts a Remade in a locker near the crew.</summary>
    [ViewVariables]
    public TimeSpan NextCloset;

    [ViewVariables]
    public TimeSpan NextLockdown;

    [ViewVariables]
    public TimeSpan LastStress;

    /// <summary>Since when a wave has been held back by stress, if it has.</summary>
    [ViewVariables]
    public TimeSpan? HeldSince;

    /// <summary>Psychic strength, spent on tricks. It climbs with the Monolith's stage.</summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public float Will;

    /// <summary>The crewman the tricks are being spent on.</summary>
    [ViewVariables]
    public EntityUid? Focus;

    [ViewVariables]
    public TimeSpan FocusUntil;

    /// <summary>The crewman the current attack is built around.</summary>
    [ViewVariables]
    public EntityUid? Prey;

    /// <summary>The Remade answering to this Monolith.</summary>
    [ViewVariables]
    public HashSet<EntityUid> Horde = new();

    /// <summary>Biomass held back from building, so there is always enough for a wave.</summary>
    [DataField]
    public float BuildReserve = 60f;

    /// <summary>Seconds between the Director's thoughts about building, at least.</summary>
    [DataField]
    public float BuildEveryMin = 15f;

    [DataField]
    public float BuildEveryMax = 30f;

    [ViewVariables]
    public TimeSpan NextBuild;

    /// <summary>Growths answering to this Monolith.</summary>
    [ViewVariables]
    public HashSet<EntityUid> Growths = new();

    /// <summary>Growths built so far. Tests read it.</summary>
    [ViewVariables]
    public int Built;

    /// <summary>Vents rattled by hidden Remade so far. Tests read it.</summary>
    [ViewVariables]
    public int Rattles;

    /// <summary>When a Crescendo stone next breaks out on another crewed ship.</summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan NextOutbreak;

    /// <summary>Outbreaks on other ships so far. Tests read it.</summary>
    [ViewVariables]
    public int Outbreaks;
}
