using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Wolfmed.Autodoc;

/// <summary>A heading in the pod's procedure list. A surgery in no category falls under the last one by order.</summary>
[Prototype("autodocCategory")]
public sealed partial class AutodocCategoryPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Locale key of the heading.</summary>
    [DataField(required: true)]
    public string Name = string.Empty;

    /// <summary>Locale key of the one-line note under the heading.</summary>
    [DataField]
    public string? Note;

    /// <summary>Headings are listed in ascending order.</summary>
    [DataField]
    public int Order;

    /// <summary>Catch-all for surgeries no category names.</summary>
    [DataField]
    public bool Fallback;

    [DataField]
    public List<EntProtoId> Surgeries = new();
}
