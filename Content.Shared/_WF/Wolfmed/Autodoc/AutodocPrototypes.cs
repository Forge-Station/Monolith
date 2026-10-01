using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Wolfmed.Autodoc;

/// <summary>
/// One S.A.M. voice. Every line has exactly one ogg and exactly one transcript, so the audio and the chat
/// line can never disagree; <see cref="Events"/> names the variants an event may pick between.
/// </summary>
[Prototype("autodocVoice")]
public sealed partial class AutodocVoicePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Every line of this voice, by line id.</summary>
    [DataField(required: true)]
    public Dictionary<string, AutodocVoiceLine> Lines = new();

    /// <summary>The line ids each event picks one of at random.</summary>
    [DataField(required: true)]
    public Dictionary<AutodocVoiceEvent, List<string>> Events = new();
}

/// <summary>One voice line: its sound, its transcript and its queue priority.</summary>
[DataDefinition]
public sealed partial class AutodocVoiceLine
{
    [DataField(required: true)]
    public SoundSpecifier Sound = default!;

    [DataField(required: true)]
    public LocId Message;

    /// <summary>How the line behaves in the pod's voice queue. Generated with the line, never hand written.</summary>
    [DataField]
    public AutodocVoicePriority Priority = AutodocVoicePriority.Info;
}

/// <summary>
/// What a line does when the pod is already talking. Only one line is ever audible at a time, so every id
/// carries the rule for its own interruption.
/// </summary>
public enum AutodocVoicePriority : byte
{
    /// <summary>Small talk. Only ever starts into silence, never queues.</summary>
    Chatter,

    /// <summary>Announces a step. Dropped outright if anything is playing or waiting; the next step speaks instead.</summary>
    Step,

    /// <summary>Waits its turn in the queue.</summary>
    Info,

    /// <summary>Cuts off whatever is playing and speaks now.</summary>
    Urgent,
}

/// <summary>Everything S.A.M. has something to say about.</summary>
public enum AutodocVoiceEvent : byte
{
    Boot,
    Greeting,
    SelfService,
    OperatorStart,
    QueueEmpty,
    NoOccupant,
    Anaesthetic,
    NoAnaesthetic,
    StepIncision,
    StepRetract,
    StepClamp,
    StepCauterise,
    StepSaw,
    StepDrill,
    StepSetBone,
    StepBoneGel,
    StepSuture,
    StepClose,
    StepRemovePart,
    StepAttachPart,
    StepRemoveOrgan,
    StepInsertOrgan,
    StepEmbedded,
    StepTend,
    StepRelocate,
    StepWeld,
    StepWrench,
    StepWire,
    StepAmputate,
    StepEvisceration,
    StepCavity,
    StepGeneric,
    RequirePart,
    RequireOrgan,
    RequireItem,
    WrongItem,
    ItemAccepted,
    ReagentMissing,
    ReagentIgnored,
    Paused,
    Resumed,
    Aborted,
    EmergencyEject,
    PowerLost,
    PowerRestored,
    LidForced,
    Slip,
    SlipFix,
    Unconscious,
    Critical,
    DefibMissing,
    DefibCharge,
    DefibSuccess,
    DefibFailure,
    Complete,
    QueueComplete,
    DiskMissing,
    DiskInserted,
    DiskRemoved,
    Idle,
    Emag,
    Offline,
    /// <summary>The pod has filled its own queue from the triage plan.</summary>
    Plan,
    /// <summary>The autofix module has taken over.</summary>
    AutoEngaged,
    /// <summary>The plan came back empty with the module on.</summary>
    AutoNothing,
    /// <summary>The autofix module has been switched off.</summary>
    AutoOff,
    /// <summary>The occupant's brain is running out of oxygen, which the vital alarm says out loud once.</summary>
    Dying,
    /// <summary>An eject with nothing running. Not an emergency, so not the emergency line.</summary>
    Goodbye,
    /// <summary>A step that keeps changing nothing. The procedure is abandoned.</summary>
    Stall,
    /// <summary>Clothing over the part the pod is trying to open. The patient may undress or press CUT.</summary>
    Clothing,
    /// <summary>Nobody is going to undress the patient, so the pod is about to do it with a blade.</summary>
    ClothingAuto,
    /// <summary>The garments are being cut off.</summary>
    Cutting,
    /// <summary>Blood or saline is going in.</summary>
    Transfusing,
    /// <summary>Something about the patient means a shock cannot help, whatever the paddles do.</summary>
    DefibBlocked,
    /// <summary>Every shock the pod is willing to give has been given.</summary>
    DefibGaveUp,
    /// <summary>The patient is as sedated as the pod is willing to make them. No more anaesthetic.</summary>
    SedationLimit,
    /// <summary>The occupant was already dead when the run started, which is not a reason to stop.</summary>
    DeadProceeding,
    /// <summary>Playtest 3 SAM: something the pod does not cut is coming off the patient into the tray.</summary>
    Removing,

    /// <summary>Playtest 3: the hull was breached with somebody inside, so outside air is getting in.</summary>
    HullBreach,

    /// <summary>Playtest 4, IV: the occupant needs blood and the blood reservoir is empty.</summary>
    NoBlood,

    /// <summary>Playtest 5: ADD was pressed on a procedure the subject's condition no longer allows.</summary>
    QueueRefused,

    /// <summary>Playtest 5: the antibiotic course out of the reservoir has started.</summary>
    Antibiotics,

    /// <summary>Playtest 5: the occupant is infected and no antibiotic is loaded.</summary>
    NoAntibiotic,
}

/// <summary>
/// The order the pod treats a patient in when it plans for itself. Each step names surgeries, whole
/// categories, or the cardiac module; the planner walks the steps in order and queues everything the
/// occupant's condition currently allows. Nothing here does anything an operator could not queue by hand.
/// </summary>
[Prototype("autodocTriage")]
public sealed partial class AutodocTriagePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>The triage steps, walked in order.</summary>
    [DataField(required: true)]
    public List<AutodocTriageStep> Steps = new();
}

/// <summary>One step of a triage plan: the surgeries or the shock it queues, and when it applies.</summary>
[DataDefinition]
public sealed partial class AutodocTriageStep
{
    /// <summary>Surgeries this step covers, queued in the order they are written.</summary>
    [DataField]
    public List<EntProtoId> Surgeries = new();

    /// <summary>Categories whose surgeries this step covers, after the ones it names itself.</summary>
    [DataField]
    public List<ProtoId<AutodocCategoryPrototype>> Categories = new();

    /// <summary>What the occupant's condition has to be for this step to contribute at all.</summary>
    [DataField]
    public AutodocTriageCondition Condition = AutodocTriageCondition.Always;

    /// <summary>A shock rather than a surgery: the cardiac module, if one is fitted.</summary>
    [DataField]
    public bool Defibrillate;

    /// <summary>Playtest 4, IV: the blood reservoir's transfusion rather than a surgery. It runs beside the queue.</summary>
    [DataField]
    public bool Transfuse;

    /// <summary>Playtest 5: the antibiotic course out of the reservoir rather than a surgery. It runs beside the queue.</summary>
    [DataField]
    public bool Antibiotics;

    /// <summary>
    /// Only queue these when the body is already past the procedure's own requirements. Closing an incision
    /// is valid on anybody, because the pod would open one first; this is what stops it doing that.
    /// </summary>
    [DataField]
    public bool RequiresStarted;

    /// <summary>
    /// Skip this step on a part whose every wound the pod made itself. Surgery leaves an incision and a
    /// sutured slash behind it, and a wound step that counted those as work re-planned after every run.
    /// </summary>
    [DataField]
    public bool IgnorePodWounds;
}

/// <summary>The occupant's condition a triage step needs before it contributes.</summary>
public enum AutodocTriageCondition : byte
{
    Always,

    /// <summary>The occupant's heart has stopped.</summary>
    Arrest,
}

/// <summary>A block of surgeries the pod can perform. The base library ships on the machine; the rest need a disk.</summary>
[Prototype("autodocProgram")]
public sealed partial class AutodocProgramPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name;

    /// <summary>Surgery prototype ids this program unlocks.</summary>
    [DataField(required: true)]
    public List<EntProtoId> Surgeries = new();
}

/// <summary>
/// Per-surgery autodoc overrides. The prototype id is the surgery's own id; a surgery with no entry uses the
/// pod's defaults.
/// </summary>
[Prototype("autodocProcedure")]
public sealed partial class AutodocProcedurePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Anaesthetic units before the first step. Zero means the procedure does not open the body.</summary>
    [DataField]
    public float? Anaesthetic;

    /// <summary>Antibiotic units after the last step.</summary>
    [DataField]
    public float? Antibiotic;
}

/// <summary>
/// The whole list of reagents the pod will draw out of a beaker. Nothing outside this list is administrable,
/// which keeps the rule in one Wolfmed-side file instead of a flag on every reagent prototype.
/// </summary>
[Prototype("autodocReagents")]
public sealed partial class AutodocReagentsPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public List<AutodocReagentEntry> Reagents = new();
}

/// <summary>A reagent the pod may draw from its reservoir, with the role it fills.</summary>
[DataDefinition]
public sealed partial class AutodocReagentEntry
{
    [DataField(required: true)]
    public ProtoId<ReagentPrototype> Reagent;

    [DataField(required: true)]
    public AutodocReagentRole Role;

    /// <summary>False parks a reagent in the list without letting the pod use it.</summary>
    [DataField]
    public bool AutodocAdministrable = true;

    /// <summary>
    /// Playtest 3 IPC 2: a machine's own fluid. Given only to a body that runs on it, and a body that runs on one is
    /// given nothing else: saline is not hydraulic fluid.
    /// </summary>
    [DataField]
    public bool Machine;

    /// <summary>
    /// Playtest 5: the most of this reagent the pod leaves in the blood, under its overdose line; 0 is no limit. The
    /// sedation cap only ever bounded the sedatives, and an analgesic topped up through a long brain repair poisoned
    /// the patient.
    /// </summary>
    [DataField]
    public float SafeUnits;
}

/// <summary>What the pod uses a reagent for.</summary>
public enum AutodocReagentRole : byte
{
    Anaesthetic,
    Antibiotic,
    Antiseptic,
    Fluid,
    Oxygen,
}
