using Content.Shared.Humanoid.Markings;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Polymorph;
using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.Genetics;

/// <summary>
/// A human DNA mutation that can be discovered, isolated, and injected.
/// </summary>
[Prototype]
public sealed partial class GenePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = string.Empty;

    [DataField(required: true)]
    public LocId Name { get; private set; }

    [DataField]
    public LocId Description { get; private set; } = string.Empty;

    [DataField]
    public GeneQuality Quality { get; private set; } = GeneQuality.Neutral;

    /// <summary>
    /// Instability added while this gene is active. Too much instability causes collapse (extra disabilities / cellular damage).
    /// </summary>
    [DataField]
    public int Instability { get; private set; }

    /// <summary>
    /// Relative chance this gene appears from irradiation.
    /// </summary>
    [DataField]
    public float Weight { get; private set; } = 1f;

    /// <summary>
    /// Only one copy; activating again is a no-op.
    /// </summary>
    [DataField]
    public bool Unique { get; private set; } = true;

    [DataField]
    public HashSet<ProtoId<GenePrototype>> Conflicts { get; private set; } = new();

    /// <summary>
    /// Fallback branch if the round shuffle has not assigned one yet.
    /// Each shift reassigns genes to random branches so recipes cannot be templated.
    /// </summary>
    [DataField]
    public GeneBranch Branch { get; private set; } = GeneBranch.Physical;

    /// <summary>
    /// Optional entity transformations applied when this gene expresses. One is picked per shift.
    /// </summary>
    [DataField]
    public List<ProtoId<PolymorphPrototype>> PolymorphPool { get; private set; } = new();

    /// <summary>
    /// Optional forced markings (tails, horns, wings). One is picked per shift.
    /// </summary>
    [DataField]
    public List<ProtoId<MarkingPrototype>> MarkingPool { get; private set; } = new();

    /// <summary>
    /// If set, this gene always lives on <see cref="Branch"/> instead of the round shuffle.
    /// </summary>
    [DataField]
    public bool LockBranch { get; private set; }

    /// <summary>
    /// Rewrite the whole body into this species when the gene expresses.
    /// </summary>
    [DataField]
    public ProtoId<SpeciesPrototype>? SpeciesTarget { get; private set; }

    /// <summary>
    /// Rewrite the whole body into a round-start species picked for this shift.
    /// </summary>
    [DataField]
    public bool SpeciesForm { get; private set; }

    /// <summary>
    /// Replace one body region (head, chest, arm, or leg) with another species' sprites, picked for this shift.
    /// </summary>
    [DataField]
    public bool ReplaceLimb { get; private set; }

    /// <summary>
    /// How many leading branch blocks must match the round recipe to assemble this gene.
    /// </summary>
    [DataField]
    public int SequenceLength { get; private set; } = 4;

    [DataField]
    public Color OverlayColor { get; private set; } = Color.Transparent;

    [DataField]
    public Color GlowColor { get; private set; } = Color.Transparent;

    [DataField]
    public float GlowEnergy { get; private set; }

    [DataField]
    public float VisualScale { get; private set; } = 1f;

    [DataField]
    public bool VisualFlicker { get; private set; }

    /// <summary>
    /// Components copied onto the mob when the gene becomes active.
    /// Server-only so client prototype load does not require server components.
    /// </summary>
    [DataField(serverOnly: true)]
    public ComponentRegistry? Components;
}
