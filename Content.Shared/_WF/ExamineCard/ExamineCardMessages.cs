using Content.Shared.Mobs;
using Content.Shared.StatusIcon;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared._WF.ExamineCard;

/// <summary>What the card stamps across a character's record.</summary>
[Serializable, NetSerializable]
public enum ExamineCardStamp : byte
{
    /// <summary>No ID card on them.</summary>
    NoId,

    /// <summary>Their face is covered, so the ID can't be checked against it.</summary>
    Unverified,

    /// <summary>The face matches the name on the ID.</summary>
    Match,

    /// <summary>The face doesn't match the name on the ID.</summary>
    Mismatch,

    /// <summary>Too far away to read an ID.</summary>
    TooFar,

    /// <summary>The examiner can't see them at all.</summary>
    NoVisual,
}

/// <summary>How much of a character the examiner can make out.</summary>
[Serializable, NetSerializable]
public enum ExamineCardRange : byte
{
    /// <summary>Within details range: everything.</summary>
    Detailed,

    /// <summary>In sight but too far for detail.</summary>
    Distant,

    /// <summary>Out of sight; built on the client from the server's refusal.</summary>
    Unseen,
}

/// <summary>One worn item on the card.</summary>
[Serializable, NetSerializable]
public sealed class ExamineCardSlot(string slot, NetEntity item, string name)
{
    /// <summary>Inventory slot id, such as <c>head</c>.</summary>
    public string Slot = slot;

    public NetEntity Item = item;

    public string Name = name;
}

/// <summary>An examine line the client folds away, as the fixed text between its variables.</summary>
[Serializable, NetSerializable]
public sealed class ExamineCardPattern(string? label, ExamineChipTone tone, string[] fragments)
{
    /// <summary>The chip's label, or null to drop the line.</summary>
    public string? Label = label;

    public ExamineChipTone Tone = tone;

    public string[] Fragments = fragments;
}

/// <summary>Sent to an examiner just before the examine response; the client draws it as an ID card.</summary>
[Serializable, NetSerializable]
public sealed class ExamineCardEvent : EntityEventArgs
{
    public NetEntity Target;

    public ExamineCardRange Range;

    /// <summary>The examiner is looking at themselves.</summary>
    public bool Self;

    public MobState State = MobState.Alive;

    /// <summary>The name the examiner can see, or null when the face is covered.</summary>
    public string? Name;

    /// <summary>The character's own description, when their face shows and they are close enough.</summary>
    public string? Description;

    /// <summary>The character's adult description; only sent to examiners who may read anatomy.</summary>
    public string? NsfwText;

    /// <summary>Age and build, such as "young woman".</summary>
    public string? Appearance;

    public string? Species;

    public string? IdName;

    public string? IdJob;

    public ProtoId<JobIconPrototype>? JobIcon;

    /// <summary>The player company on their ID, as "[TAG] Name".</summary>
    public string? Company;

    public string? CompanyRank;

    public int CompanyId;

    public int CompanyLogoVersion;

    /// <summary>The rank whose job icon their ID carries, or 0.</summary>
    public int CompanyRankId;

    /// <summary>The rank icon's version; only the server fills it.</summary>
    public int CompanyIconVersion;

    public ExamineCardStamp Stamp;

    /// <summary>Worn items, then held items under <c>hand-left</c>, <c>hand-right</c> or <c>hand-middle</c>.</summary>
    public List<ExamineCardSlot> Worn = new();

    /// <summary>The health look belongs on this card, even before a predicted card has it.</summary>
    public bool HasHealth;

    /// <summary>What the health examine verb would show, folded into the card.</summary>
    public FormattedMessage? Health;

    /// <summary>The lines this character's examine text may carry that become chips or are dropped.</summary>
    public List<ExamineCardPattern> Patterns = new();
}
