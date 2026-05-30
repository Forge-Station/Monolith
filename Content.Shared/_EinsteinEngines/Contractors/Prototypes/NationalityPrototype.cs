using Content.Shared._EE.Contractors.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;
using Content.Shared.Traits;
using Content.Shared.Roles;

namespace Content.Shared._EE.Contractors.Prototypes;

/// <summary>
/// Prototype representing a character's nationality in YAML.
/// </summary>
[Prototype]
public sealed partial class NationalityPrototype : IPrototype
{
    [IdDataField, ViewVariables]
    public string ID { get; private set; } = string.Empty;

    [DataField("name")]
    public string NameKey { get; private set; } = string.Empty;

    [DataField("description")]
    public string DescriptionKey { get; private set; } = string.Empty;

    [DataField, ViewVariables]
    public HashSet<ProtoId<NationalityPrototype>> Allied { get; private set; } = new();

    [DataField, ViewVariables]
    public HashSet<ProtoId<NationalityPrototype>> Hostile { get; private set; } = new();

    [DataField]
    public List<JobRequirement> Requirements = new();

    // Forge-change-start: We use JobSpecial instead of TraitFunction
    // [DataField(serverOnly: true)]
    // public TraitFunction[] Functions { get; private set; } = Array.Empty<TraitFunction>();

    [DataField("special", serverOnly: true)]
    public JobSpecial[] Special { get; private set; } = Array.Empty<JobSpecial>();

    // Hides nationality from the menu. Used for jokes or events.
    [DataField]
    public bool Hidden { get; private set; } = false;
    // Forge-change-end

    [DataField]
    public ProtoId<EntityPrototype> PassportPrototype { get; private set; } = new();
}
