using Content.Shared.Access.Components;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Roles;

/// <summary>Lets players write their own job title for one role. The id is the role loadout id, e.g. JobContractor.</summary>
[Prototype]
public sealed partial class CustomJobTitlePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField]
    public int MaxLength = IdCardConsoleComponent.MaxJobTitleLength;

    /// <summary>Jobs whose names may be used as a title anyway, along with any other job sharing that name.</summary>
    [DataField]
    public List<ProtoId<JobPrototype>> AllowedJobs = new();

    /// <summary>Titles rejected outright on top of every job name. Compared on letters and digits only.</summary>
    [DataField]
    public List<string> BlockedTitles = new();

    /// <summary>
    /// Whole words or phrases rejected anywhere in the title, so "admin" doesn't catch "badminton"
    /// and "head of" catches "Head of Mining".
    /// </summary>
    [DataField]
    public List<string> BlockedWords = new();
}
