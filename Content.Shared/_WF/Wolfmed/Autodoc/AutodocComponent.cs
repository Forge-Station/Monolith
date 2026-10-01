using Content.Shared._Shitmed.Medical.Surgery;
using Content.Shared._Shitmed.Targeting;
using Content.Shared.Body.Part;
using Content.Shared.Construction.Prototypes;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Wolfmed.Autodoc;

/// <summary>
/// A one-tile surgical pod ("S.A.M.") that performs real Shitmed surgery steps on its occupant with an
/// internal toolset. Every runtime field below is written on the server only; the component is networked so
/// the shared surgery system can see that an occupant is lying on an operating platform.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class AutodocComponent : Component
{
    public const string BodyContainerId = "autodoc_body";
    public const string ToolContainerId = "autodoc_tools";
    public const string TraySlotId = "autodoc_tray";
    public const string DiskSlotId = "autodoc_disk";
    public const string ModuleSlotId = "autodoc_module";
    public const string AutofixSlotId = "autodoc_autofix";

    /// <summary>The three reservoir beaker slots, in UI order.</summary>
    public static readonly string[] ReservoirSlotIds = { "autodoc_beaker1", "autodoc_beaker2", "autodoc_beaker3" };

    /// <summary>Tool entities spawned into the tool container on map init. The pod never uses anything else.</summary>
    [DataField]
    public List<EntProtoId> Tools = new();

    /// <summary>The voice lines the pod speaks with.</summary>
    [DataField]
    public ProtoId<AutodocVoicePrototype> Voice = "WFWolfmedAutodocVoiceSam";

    /// <summary>The reagents the pod will draw from its reservoir, and what each is for.</summary>
    [DataField]
    public ProtoId<AutodocReagentsPrototype> Reagents = "WFWolfmedAutodocReagents";

    /// <summary>The order the pod treats a patient in when it plans for itself.</summary>
    [DataField]
    public ProtoId<AutodocTriagePrototype> Triage = "WFWolfmedAutodocTriage";

    /// <summary>Programs the pod knows without any disk in the slot.</summary>
    [DataField]
    public List<ProtoId<AutodocProgramPrototype>> BasePrograms = new();

    /// <summary>Step time multiplier before machine parts. 1.25 means a quarter slower than a surgeon.</summary>
    [DataField]
    public float StepTime = 1.25f;

    /// <summary>Step time at the best manipulator tier, interpolated from <see cref="StepTime"/> by part rating.</summary>
    [DataField]
    public float BestStepTime = 0.9f;

    /// <summary>Malfunction chance per step before machine damage scales it.</summary>
    [DataField]
    public float MalfunctionChance = 0.02f;

    /// <summary>Wound the malfunction slip opens on the part being operated on.</summary>
    [DataField]
    public ProtoId<Content.Shared._Onyx.Wounds.WoundPrototype> SlipWound = "SlashWound";

    /// <summary>Severity of the <see cref="SlipWound"/>.</summary>
    [DataField]
    public float SlipSeverity = 8f;

    /// <summary>Anaesthetic units drawn before the first incision of a procedure that opens the body.</summary>
    [DataField]
    public float DefaultAnaesthetic = 15f;

    /// <summary>Antibiotic units pushed after a procedure closes.</summary>
    [DataField]
    public float DefaultAntibiotic = 10f;

    /// <summary>Seconds of painkiller left under which the pod tops the anaesthetic up mid-queue.</summary>
    [DataField]
    public float AnaestheticTopUp = 20f;

    /// <summary>Units of a dexalin-class chem pushed to counter the pod's own respiratory depression.</summary>
    [DataField]
    public float OxygenDose = 5f;

    /// <summary>Seconds between oxygen pushes, so one long queue cannot flood the patient.</summary>
    [DataField]
    public float OxygenInterval = 10f;

    /// <summary>Sedation past which the pod starts pushing oxygen, if it has any.</summary>
    [DataField]
    public float OxygenSedation = 0.6f;

    /// <summary>Reservoir capacity the UI draws each beaker bar against, before matter bins.</summary>
    [DataField]
    public float BaseReservoirSize = 50f;

    [DataField]
    public ProtoId<MachinePartPrototype> MachinePartManipulator = "Manipulator";

    [DataField]
    public ProtoId<MachinePartPrototype> MachinePartMatterBin = "MatterBin";

    /// <summary>Shortest random gap, in seconds, between idle chatter lines.</summary>
    [DataField]
    public float IdleChatterMin = 90f;

    /// <summary>Longest random gap, in seconds, between idle chatter lines.</summary>
    [DataField]
    public float IdleChatterMax = 180f;

    /// <summary>The monitor beep the vital alarm repeats while the occupant is failing.</summary>
    [DataField]
    public SoundSpecifier? AlarmSound = new SoundPathSpecifier("/Audio/Machines/quickbeep.ogg");

    /// <summary>The one long tone a brain-dead occupant gets, once.</summary>
    [DataField]
    public SoundSpecifier? FlatlineSound = new SoundPathSpecifier("/Audio/_WF/Wolfmed/flatline.ogg");

    /// <summary>Seconds between beeps while the occupant is unconscious or their brain is draining.</summary>
    [DataField]
    public float AlarmCritInterval = 4f;

    /// <summary>Seconds between beeps while the occupant's heart has stopped.</summary>
    [DataField]
    public float AlarmArrestInterval = 2f;

    /// <summary>Volume of the alarm and flatline sounds, in dB.</summary>
    [DataField]
    public float AlarmGain = -4f;

    /// <summary>Seconds the autofix module waits before looking at the patient again.</summary>
    [DataField]
    public float AutoPlanInterval = 5f;

    /// <summary>
    /// Plans the autofix module may run against a body it is not changing before it stops looking. Without
    /// a bound a procedure that lists again the moment it finishes would have the pod loop on it for ever.
    /// </summary>
    [DataField]
    public int AutoReplanLimit = 2;

    [DataField]
    public SoundSpecifier? LidOpenSound = new SoundPathSpecifier("/Audio/Machines/airlock_ext_open.ogg");

    [DataField]
    public SoundSpecifier? LidCloseSound = new SoundPathSpecifier("/Audio/Machines/airlock_ext_close.ogg");

    /// <summary>Damage on the pod itself past which it faults instead of operating.</summary>
    [DataField]
    public float FaultDamage = 60f;

    /// <summary>Volume of the voice lines, in dB.</summary>
    [DataField]
    public float VoiceGain = -2f;

    /// <summary>Volume (dB) the pod plays its tool sounds at; a hand tool is held next to the ear, the pod is a machine in the room.</summary>
    [DataField] public float ToolVolume = -8f;

    /// <summary>Units of blood or saline one transfusion push moves.</summary>
    [DataField] public float TransfuseDose = 10f;

    /// <summary>Blood volume fraction the pod transfuses up to.</summary>
    [DataField] public float TransfuseTarget = 0.9f;

    /// <summary>Seconds the pod gives a patient to undress before it cuts the clothing off them (playtest 5: any run).</summary>
    [DataField] public float ClothingCutDelay = 5f;

    /// <summary>Seconds between one shock and the next.</summary>
    [DataField] public float DefibRetryDelay = 5f;

    /// <summary>The shears. An existing cutting sound, not a new one.</summary>
    [DataField] public SoundSpecifier? CutSound = new SoundPathSpecifier("/Audio/Items/wirecutter.ogg");

    /// <summary>The transcript of the last line spoken, for the terminal window.</summary>
    [ViewVariables] public string? LastLine;

    /// <summary>Lines waiting behind the one being spoken. Anything past this is dropped.</summary>
    [DataField]
    public int VoiceQueueMax = 3;

    /// <summary>Seconds of silence idle chatter waits for before it will speak.</summary>
    [DataField]
    public float VoiceChatterSilence = 8f;

    /// <summary>Gain the step and tool sounds drop to while a voice line is playing.</summary>
    [DataField]
    public float DuckedToolGain = 0.6f;

    // Runtime state, written on the server only.

    /// <summary>Where the pod is in a run.</summary>
    [ViewVariables]
    public AutodocState State = AutodocState.Idle;

    /// <summary>Manipulator rating, 1 to 4, written by the machine part refresh.</summary>
    [ViewVariables]
    public float PartRating = 1f;

    /// <summary>Matter bin scaled capacity.</summary>
    [ViewVariables]
    public float ReservoirSize = 50f;

    /// <summary>The surgery that owns the step being performed, which may be a requirement of the queued one.</summary>
    [ViewVariables]
    public EntProtoId? CurrentSurgery;

    /// <summary>Paused because the power went out rather than because somebody asked.</summary>
    [ViewVariables]
    public bool PowerPaused;

    /// <summary>Pause at the next step boundary, rather than mid-step.</summary>
    [ViewVariables]
    public bool PauseRequested;

    /// <summary>Abort requested; a running step finishes first.</summary>
    [ViewVariables]
    public bool AbortRequested;

    /// <summary>The "patient is asleep" line is said once per occupant, not once per tick.</summary>
    [ViewVariables]
    public bool SaidUnconscious;

    /// <summary>The queue of procedures, in execution order.</summary>
    [ViewVariables]
    public List<AutodocQueued> Queue = new();

    /// <summary>The lid refuses to open while this is set.</summary>
    [ViewVariables]
    public bool Locked;

    /// <summary>Set once the emagged pod has said its first line, which is when examine gives it away.</summary>
    [ViewVariables]
    public bool EmagRevealed;

    /// <summary>Operator-less mode: the occupant picked the procedure itself and gets no queue.</summary>
    [ViewVariables]
    public bool SelfService;

    /// <summary>Self-service anaesthesia toggle.</summary>
    [ViewVariables]
    public bool Anaesthesia = true;

    /// <summary>Whoever pressed Start, for the admin log. Null in self-service.</summary>
    [ViewVariables]
    public EntityUid? Operator;

    /// <summary>Seconds left in the current step.</summary>
    [ViewVariables]
    public float StepRemaining;

    /// <summary>Seconds the current step started with, so the UI can draw a bar.</summary>
    [ViewVariables]
    public float StepLength;

    /// <summary>The step prototype the pod is currently performing, for the UI and the voice.</summary>
    [ViewVariables]
    public EntProtoId? CurrentStep;

    /// <summary>What the pod is waiting for, or null.</summary>
    [ViewVariables]
    public AutodocRequirement? Pending;

    /// <summary>The anaesthetic dose for the running QUEUE has been pushed. One dose, not one per procedure.</summary>
    [ViewVariables]
    public bool AnaestheticGiven;

    /// <summary>The pod put this occupant under and owes them a wake-up.</summary>
    [ViewVariables]
    public bool Sedated;

    /// <summary>"SEDATION AT LIMIT." is said once a run, not once a procedure.</summary>
    [ViewVariables]
    public bool SaidSedationLimit;

    /// <summary>When the pod may push oxygen again.</summary>
    [ViewVariables]
    public TimeSpan NextOxygen;

    /// <summary>The missing-defib line has been said for this occupant; it is not repeated every procedure.</summary>
    [ViewVariables] public bool DefibWarned;

    [ViewVariables]
    public TimeSpan NextIdleChatter;

    /// <summary>Lines waiting for the current one to finish, in speaking order.</summary>
    [ViewVariables]
    public List<AutodocVoiceRequest> VoiceQueue = new();

    /// <summary>When the line being spoken ends. Silence before that is what chatter waits for.</summary>
    [ViewVariables]
    public TimeSpan VoiceBusyUntil;

    /// <summary>The audio stream of the line being spoken, so an urgent line can cut it off.</summary>
    [ViewVariables]
    public EntityUid? VoiceStream;

    /// <summary>Lines actually spoken, as opposed to queued or dropped. Counts for the queue tests.</summary>
    [ViewVariables]
    public int VoiceSpoken;

    /// <summary>Step families already announced in the running procedure; each one speaks once.</summary>
    [ViewVariables]
    public HashSet<AutodocStepFamily> SpokenFamilies = new();

    /// <summary>Test seam: forces the next malfunction roll to succeed.</summary>
    [ViewVariables]
    public bool ForceMalfunction;

    /// <summary>The step the stall guard is counting repeats of.</summary>
    [ViewVariables]
    public EntProtoId? StallStep;

    /// <summary>What the part looked like after the last run of <see cref="StallStep"/>.</summary>
    [ViewVariables]
    public string? StallSignature;

    /// <summary>Runs of <see cref="StallStep"/> that left the part exactly as they found it.</summary>
    [ViewVariables]
    public int StallCount;

    /// <summary>
    /// Surgery and part pairs the pod gave up on for the occupant now in it. The planner never queues one
    /// again, so a procedure that cannot work is tried once and not once a plan. Cleared when they leave.
    /// </summary>
    [ViewVariables]
    public HashSet<(string Surgery, TargetBodyPart Part)> FailedProcedures = new();

    /// <summary>
    /// How often each step of the running procedure has been begun. Diagnostic only: the stall guard is
    /// what bounds it, and this is how a test and a ViewVariables window can see that it did.
    /// </summary>
    [ViewVariables]
    public Dictionary<string, int> StepRuns = new();

    /// <summary>
    /// Set while a step cannot start for something the patient can fix, such as clothing over the part.
    /// The pod waits and retries instead of faulting, and says so once.
    /// </summary>
    [ViewVariables]
    public StepInvalidReason? BlockedReason;

    /// <summary>When the clothing block started, so AUTO can cut once the patient has had their chance.</summary>
    [ViewVariables]
    public TimeSpan ClothingSince;

    /// <summary>Somebody pressed CUT CLOTHING. Acted on at the next look at the blocked step.</summary>
    [ViewVariables]
    public bool CutClothingRequested;

    /// <summary>The occupant is low on blood and the reservoir can do something about it.</summary>
    [ViewVariables]
    public bool Transfusing;

    /// <summary>Shocks given to the occupant now in the pod.</summary>
    [ViewVariables]
    public int DefibAttempt;

    /// <summary>When the pod may charge again.</summary>
    [ViewVariables]
    public TimeSpan DefibNext;

    /// <summary>The refusal the pod has already said out loud, so it only repeats when the reason changes.</summary>
    [ViewVariables]
    public string? DefibBlocked;

    /// <summary>The autofix module's automatic mode. Nothing without the module in its slot.</summary>
    [ViewVariables]
    public bool Auto;

    /// <summary>When the autofix module may look at the patient again.</summary>
    [ViewVariables]
    public TimeSpan AutoNextPlan;

    /// <summary>"NOTHING MORE I CAN DO." is said once per patient, not once every time it re-plans.</summary>
    [ViewVariables]
    public bool AutoSaidNothing;

    /// <summary>What the occupant's whole body looked like the last time the module planned.</summary>
    [ViewVariables]
    public string? AutoSignature;

    /// <summary>Plans run against an unchanged body. Past <see cref="AutoReplanLimit"/> the module idles.</summary>
    [ViewVariables]
    public int AutoReplans;

    /// <summary>
    /// The wounds the occupant already had a moment ago. Anything that appears on them while the pod is
    /// working is the pod's own work and is marked as such.
    /// </summary>
    [ViewVariables]
    public HashSet<EntityUid> PreProcedureWounds = new();

    /// <summary>
    /// How long after a run the pod still owns what turns up on the occupant. The damage a cautery deals
    /// does not become a wound in the same tick as the step, so the window outlives the last step.
    /// </summary>
    [DataField]
    public float PodWoundGrace = 1f;

    /// <summary>
    /// Playtest 5: when the pod next hands what it made on the occupant back to them as ordinary wounds, so a plan
    /// after a run treats the burn a cautery left or the incision a stalled procedure left open. Set at the end of a
    /// run, past <see cref="PodWoundGrace"/>; null when nothing is due.
    /// </summary>
    [ViewVariables]
    public TimeSpan? LeftoverReleaseAt;

    /// <summary>Times the pod has handed its leftovers back for the occupant now in it.</summary>
    [ViewVariables]
    public int LeftoverPasses;

    /// <summary>
    /// How many times per occupant the pod may treat its own leftovers. Bounded because a run always ends with wounds
    /// that were not there before, and one that listed again every time would have the pod operate for ever.
    /// </summary>
    [DataField]
    public int LeftoverPassLimit = 2;

    /// <summary>Game time that window closes at.</summary>
    [ViewVariables]
    public TimeSpan PodWoundUntil;

    /// <summary>
    /// The occupant was already dead or arrested when this run started, or the pod has already held once for
    /// this death. Operating on a corpse is the whole point of brain repair, so it is not a reason to stop.
    /// </summary>
    [ViewVariables]
    public bool OccupantWasDead;

    /// <summary>
    /// Playtest 5, server: when the pod next sweeps its own tile. A limb, an organ, a spent round or a garment taken
    /// off the patient inside the pod drops where the patient is, which is the pod's tile, under the pod's sprite
    /// and out of reach; the sweep puts it beside the pod.
    /// </summary>
    [ViewVariables]
    public TimeSpan NextTileSweep;

    /// <summary>What the vital alarm read on its last tick, so an escalation is heard as one.</summary>
    [ViewVariables]
    public AutodocAlarm AlarmLevel;

    /// <summary>When the next beep is due.</summary>
    [ViewVariables]
    public TimeSpan AlarmNext;

    // Playtest 3 SAM.

    /// <summary>
    /// An AUTO run is under way for this occupant: it began with the module's first plan and ends only when a
    /// re-plan after the last procedure finds nothing. Queues inside it follow each other without a word.
    /// </summary>
    [ViewVariables]
    public bool AutoSession;

    /// <summary>Every voice event raised for the occupant now in the pod, spoken or dropped. Counts for the tests.</summary>
    [ViewVariables]
    public Dictionary<AutodocVoiceEvent, int> VoiceEvents = new();

    /// <summary>Procedures that announced their first step for the occupant now in the pod. Counts for the tests.</summary>
    [ViewVariables]
    public int ProceduresAnnounced;

    /// <summary>The garment the pod is waiting on, which is what the WAITING line names.</summary>
    [ViewVariables]
    public EntityUid? BlockingGarment;

    /// <summary>The inventory slot <see cref="BlockingGarment"/> is in.</summary>
    [ViewVariables]
    public string? BlockingSlot;

    /// <summary>
    /// <see cref="BlockingGarment"/> is something the pod cannot take off (locked, unremovable). The pod gives the
    /// procedure up once it has waited <see cref="ClothingCutDelay"/> on it.
    /// </summary>
    [ViewVariables]
    public bool GarmentStuck;
}

/// <summary>How bad the occupant is, as the pod's vital alarm reads it. Highest wins.</summary>
public enum AutodocAlarm : byte
{
    /// <summary>Nothing to say: no occupant, or one whose brain is not losing ground.</summary>
    None,

    /// <summary>The brain's oxygen is draining but the body is still up.</summary>
    Dying,

    /// <summary>Unconscious.</summary>
    Critical,

    /// <summary>The heart has stopped.</summary>
    Arrest,

    /// <summary>The brain is gone. One long tone and then silence; there is nothing left to alarm about.</summary>
    Flatline,
}

/// <summary>A line the pod has accepted but is not speaking yet, with the arguments it was raised with.</summary>
public sealed class AutodocVoiceRequest
{
    public AutodocVoiceRequest(string line, (string, object)[] args)
    {
        Line = line;
        Args = args;
    }

    public readonly string Line;

    public readonly (string, object)[] Args;
}

/// <summary>
/// Step lines are grouped so a procedure announces each kind of work once instead of once a step. Which
/// family a step belongs to is read off the voice event the step resolved to.
/// </summary>
public enum AutodocStepFamily : byte
{
    Incision,
    Bleeding,
    Bone,
    Closing,
    Part,
    Organ,
    Mechanical,
    Other,
}

/// <summary>One queued procedure and the requirements worked out when it was queued.</summary>
public sealed class AutodocQueued
{
    public EntProtoId Surgery;
    public TargetBodyPart Part;
    public List<AutodocRequirement> Requirements = new();

    /// <summary>Steps the pod has actually performed, so a surgery that stops being valid reads as finished.</summary>
    public int StepsDone;

    /// <summary>
    /// M3: a closure the pod queued itself to finish the procedure before it. Part of that procedure, so it takes
    /// no fresh anaesthetic.
    /// </summary>
    public bool Continuation;

    /// <summary>
    /// Playtest 3 SAM: planned ahead of the state the pod itself will leave the part in (the lodged object out, the
    /// deep wound tended down). Checked against the planner's own rules at its turn and dropped quietly if they fail.
    /// </summary>
    public bool FollowUp;

    /// <summary>
    /// Playtest 3 SAM: the follow-up came from a triage step that skips a part whose every wound the pod made, and is
    /// dropped at its turn if that is all the part has left.
    /// </summary>
    public bool IgnorePodWounds;
}

/// <summary>Something the pod needs in the tray or the reservoir before a step can run.</summary>
public sealed class AutodocRequirement
{
    public AutodocRequirementKind Kind;

    /// <summary>Component name an item must carry to satisfy an organ or item requirement.</summary>
    public string? Component;

    /// <summary>Body part type and symmetry for a part requirement.</summary>
    public BodyPartType PartType;

    public BodyPartSymmetry? Symmetry;

    /// <summary>Reagent and units for a reagent requirement.</summary>
    public string? Reagent;

    public float Units;

    /// <summary>Already in the tray, or already pushed.</summary>
    public bool Satisfied;
}

/// <summary>What kind of thing a requirement asks for.</summary>
public enum AutodocRequirementKind : byte
{
    Part,
    Organ,
    Item,
    Reagent,
}

/// <summary>Where the pod is in a run.</summary>
[Serializable, NetSerializable]
public enum AutodocState : byte
{
    Idle,
    Preparing,
    Step,
    Waiting,
    Paused,
    Complete,
    Faulted,
}

/// <summary>Appearance keys for the pod.</summary>
[Serializable, NetSerializable]
public enum AutodocVisuals : byte
{
    State,
}

/// <summary>What the pod's sprite shows: lid open or closed, operating, or unpowered.</summary>
[Serializable, NetSerializable]
public enum AutodocVisualState : byte
{
    Open,
    Closed,
    Operating,
    Unpowered,
}

/// <summary>
/// Raised on a surgery performer that has no hands to ask it for its own toolset. Answered by the autodoc
/// with its internal tools plus whatever is in the delivery tray.
/// </summary>
[ByRefEvent]
public record struct WolfmedSurgeryToolsEvent(List<EntityUid>? Tools = null);
