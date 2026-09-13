using Content.Shared._WF.Genitals.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Genitals;

/// <summary>Creator-time anatomy for one character. Organs are built from this in round.</summary>
/// <remarks>Immutable in practice (private setters, fresh factory instances, With* copies), so references can be shared.</remarks>
[DataDefinition, Serializable, NetSerializable]
public sealed partial class GenitalProfile
{
    /// <summary>Schema written by this build.</summary>
    public const int CurrentVersion = 1;

    /// <summary>A fresh, migrated profile with no anatomy.</summary>
    public static GenitalProfile Empty => new() { Version = CurrentVersion };

    /// <summary>A fresh profile that still needs the legacy marking migration.</summary>
    public static GenitalProfile Unmigrated => new() { Version = 0 };

    /// <summary>Schema version. Data with no version field is schema 1; 0 means not migrated yet.</summary>
    [DataField]
    public int Version { get; private set; } = 1;

    [DataField]
    public GenitalRevealMode RevealMode { get; private set; } = GenitalRevealMode.UndergarmentRemoval;

    [DataField]
    public PenisProfile? Penis { get; private set; }

    [DataField]
    public TesticlesProfile? Testicles { get; private set; }

    [DataField]
    public VaginaProfile? Vagina { get; private set; }

    [DataField]
    public bool Womb { get; private set; }

    [DataField]
    public BreastsProfile? Breasts { get; private set; }

    /// <summary>The stored column could not be fully read. The DB keeps its old text until anatomy is edited.</summary>
    /// <remarks>Load-time state: networked for the creator's warning, never written to exports.</remarks>
    public bool LoadFailed { get; private set; }

    /// <summary>Legacy "id@colours" marking strings removed by the migration, kept for one release so a revert can restore them.</summary>
    [DataField]
    public List<string>? LegacyMarkings { get; private set; }

    /// <summary>No organ is configured.</summary>
    public bool IsEmpty => Penis == null && Testicles == null && Vagina == null && !Womb && Breasts == null;

    private GenitalProfile()
    {
    }

    /// <summary>Deep copy.</summary>
    public GenitalProfile Clone()
    {
        return new GenitalProfile
        {
            Version = Version,
            RevealMode = RevealMode,
            Penis = Penis?.Clone(),
            Testicles = Testicles?.Clone(),
            Vagina = Vagina?.Clone(),
            Womb = Womb,
            Breasts = Breasts?.Clone(),
            LoadFailed = LoadFailed,
            LegacyMarkings = LegacyMarkings == null ? null : new List<string>(LegacyMarkings),
        };
    }

    /// <summary>Field-by-field equality; LegacyMarkings is excluded.</summary>
    public bool MemberwiseEquals(GenitalProfile other)
    {
        if (ReferenceEquals(this, other))
            return true;

        return Version == other.Version
               && RevealMode == other.RevealMode
               && Womb == other.Womb
               && LoadFailed == other.LoadFailed
               && (Penis == null ? other.Penis == null : other.Penis != null && Penis.MemberwiseEquals(other.Penis))
               && (Testicles == null ? other.Testicles == null : other.Testicles != null && Testicles.MemberwiseEquals(other.Testicles))
               && (Vagina == null ? other.Vagina == null : other.Vagina != null && Vagina.MemberwiseEquals(other.Vagina))
               && (Breasts == null ? other.Breasts == null : other.Breasts != null && Breasts.MemberwiseEquals(other.Breasts));
    }

    public GenitalProfile WithRevealMode(GenitalRevealMode mode)
    {
        var copy = Edit();
        copy.RevealMode = mode;
        return copy;
    }

    /// <summary>Null clears the penis and its linked testicles.</summary>
    public GenitalProfile WithPenis(PenisProfile? penis)
    {
        var copy = Edit();
        copy.Penis = penis;
        if (penis == null)
            copy.Testicles = null;

        return copy;
    }

    public GenitalProfile WithTesticles(TesticlesProfile? testicles)
    {
        var copy = Edit();
        copy.Testicles = testicles;
        return copy;
    }

    /// <summary>Adding a vagina also adds the womb; removing it removes the womb.</summary>
    public GenitalProfile WithVagina(VaginaProfile? vagina)
    {
        var copy = Edit();
        if (vagina == null)
            copy.Womb = false;
        else if (Vagina == null)
            copy.Womb = true;

        copy.Vagina = vagina;
        return copy;
    }

    public GenitalProfile WithWomb(bool womb)
    {
        var copy = Edit();
        copy.Womb = womb;
        return copy;
    }

    public GenitalProfile WithBreasts(BreastsProfile? breasts)
    {
        var copy = Edit();
        copy.Breasts = breasts;
        return copy;
    }

    /// <summary>Marks the profile as converted by the legacy migration and records the removed marking strings.</summary>
    public GenitalProfile AsMigrated(List<string>? legacy)
    {
        var copy = Edit();
        copy.LoadFailed = LoadFailed;
        copy.Version = CurrentVersion;
        copy.LegacyMarkings = legacy == null ? null : new List<string>(legacy);
        return copy;
    }

    /// <summary>This profile without the LoadFailed flag, so the next save writes it.</summary>
    public GenitalProfile WithoutLoadFailed()
    {
        return LoadFailed ? Edit() : this;
    }

    /// <summary>What could be read from a damaged column, flagged LoadFailed.</summary>
    public static GenitalProfile Failed(GenitalProfile partial)
    {
        var copy = partial.Clone();
        copy.LoadFailed = true;
        return copy;
    }

    /// <summary>Validator output: keeps LoadFailed, takes the filtered legacy list, and returns this instance when nothing changed.</summary>
    internal GenitalProfile Normalised(int version, GenitalRevealMode reveal, PenisProfile? penis, TesticlesProfile? testicles,
        VaginaProfile? vagina, bool womb, BreastsProfile? breasts, List<string>? legacy)
    {
        if (version == Version
            && reveal == RevealMode
            && ReferenceEquals(penis, Penis)
            && ReferenceEquals(testicles, Testicles)
            && ReferenceEquals(vagina, Vagina)
            && womb == Womb
            && ReferenceEquals(breasts, Breasts)
            && ReferenceEquals(legacy, LegacyMarkings))
            return this;

        return new GenitalProfile
        {
            Version = version,
            RevealMode = reveal,
            Penis = penis,
            Testicles = testicles,
            Vagina = vagina,
            Womb = womb,
            Breasts = breasts,
            LoadFailed = LoadFailed,
            LegacyMarkings = legacy,
        };
    }

    /// <summary>Shallow copy for With*: sub-profiles are immutable, so they are shared. Every edit clears LoadFailed.</summary>
    private GenitalProfile Edit()
    {
        return new GenitalProfile
        {
            Version = Version,
            RevealMode = RevealMode,
            Penis = Penis,
            Testicles = Testicles,
            Vagina = Vagina,
            Womb = Womb,
            Breasts = Breasts,
            LoadFailed = false,
            LegacyMarkings = LegacyMarkings,
        };
    }
}

/// <summary>Penis configuration. Testicles are a linked option held on GenitalProfile.</summary>
[DataDefinition, Serializable, NetSerializable]
public sealed partial class PenisProfile
{
    [DataField(required: true)]
    public ProtoId<GenitalShapePrototype> Shape { get; private set; }

    /// <summary>Erect length in centimetres, clamped to the settings range.</summary>
    [DataField]
    public int LengthCm { get; private set; } = 15;

    [DataField]
    public SheathType Sheath { get; private set; } = SheathType.None;

    [DataField]
    public bool MatchSkin { get; private set; } = true;

    /// <summary>Used when MatchSkin is false.</summary>
    [DataField]
    public Color Color { get; private set; } = Color.White;

    [DataField]
    public bool SheathMatchSkin { get; private set; } = true;

    /// <summary>Sheath outer colour, used when SheathMatchSkin is false.</summary>
    [DataField]
    public Color SheathColor { get; private set; } = Color.White;

    [DataField]
    public GenitalVisibility Visibility { get; private set; } = GenitalVisibility.Normal;

    private PenisProfile()
    {
    }

    public PenisProfile(ProtoId<GenitalShapePrototype> shape, int lengthCm = 15, SheathType sheath = SheathType.None,
        bool matchSkin = true, Color? color = null, bool sheathMatchSkin = true, Color? sheathColor = null,
        GenitalVisibility visibility = GenitalVisibility.Normal)
    {
        Shape = shape;
        LengthCm = lengthCm;
        Sheath = sheath;
        MatchSkin = matchSkin;
        Color = color ?? Color.White;
        SheathMatchSkin = sheathMatchSkin;
        SheathColor = sheathColor ?? Color.White;
        Visibility = visibility;
    }

    /// <summary>Copy with the given values changed.</summary>
    public PenisProfile With(ProtoId<GenitalShapePrototype>? shape = null, int? lengthCm = null, SheathType? sheath = null,
        bool? matchSkin = null, Color? color = null, bool? sheathMatchSkin = null, Color? sheathColor = null,
        GenitalVisibility? visibility = null)
    {
        return new PenisProfile(
            shape ?? Shape,
            lengthCm ?? LengthCm,
            sheath ?? Sheath,
            matchSkin ?? MatchSkin,
            color ?? Color,
            sheathMatchSkin ?? SheathMatchSkin,
            sheathColor ?? SheathColor,
            visibility ?? Visibility);
    }

    public PenisProfile Clone()
    {
        return With();
    }

    public bool MemberwiseEquals(PenisProfile other)
    {
        return Shape == other.Shape
               && LengthCm == other.LengthCm
               && Sheath == other.Sheath
               && MatchSkin == other.MatchSkin
               && Color.Equals(other.Color)
               && SheathMatchSkin == other.SheathMatchSkin
               && SheathColor.Equals(other.SheathColor)
               && Visibility == other.Visibility;
    }
}

/// <summary>Testicles configuration. A null profile means none; Internal builds an organ with no art.</summary>
[DataDefinition, Serializable, NetSerializable]
public sealed partial class TesticlesProfile
{
    [DataField]
    public TesticleType Type { get; private set; } = TesticleType.External;

    /// <summary>1-5; ignored for Internal.</summary>
    [DataField]
    public int Size { get; private set; } = 2;

    [DataField]
    public bool MatchSkin { get; private set; } = true;

    [DataField]
    public Color Color { get; private set; } = Color.White;

    [DataField]
    public GenitalVisibility Visibility { get; private set; } = GenitalVisibility.Normal;

    private TesticlesProfile()
    {
    }

    public TesticlesProfile(TesticleType type, int size = 2, bool matchSkin = true, Color? color = null,
        GenitalVisibility visibility = GenitalVisibility.Normal)
    {
        Type = type;
        Size = size;
        MatchSkin = matchSkin;
        Color = color ?? Color.White;
        Visibility = visibility;
    }

    /// <summary>Copy with the given values changed.</summary>
    public TesticlesProfile With(TesticleType? type = null, int? size = null, bool? matchSkin = null, Color? color = null,
        GenitalVisibility? visibility = null)
    {
        return new TesticlesProfile(
            type ?? Type,
            size ?? Size,
            matchSkin ?? MatchSkin,
            color ?? Color,
            visibility ?? Visibility);
    }

    public TesticlesProfile Clone()
    {
        return With();
    }

    public bool MemberwiseEquals(TesticlesProfile other)
    {
        return Type == other.Type
               && Size == other.Size
               && MatchSkin == other.MatchSkin
               && Color.Equals(other.Color)
               && Visibility == other.Visibility;
    }
}

/// <summary>Vagina configuration. The womb is a separate flag on GenitalProfile.</summary>
[DataDefinition, Serializable, NetSerializable]
public sealed partial class VaginaProfile
{
    [DataField(required: true)]
    public ProtoId<GenitalShapePrototype> Shape { get; private set; }

    [DataField]
    public bool MatchSkin { get; private set; } = true;

    [DataField]
    public Color Color { get; private set; } = Color.White;

    [DataField]
    public GenitalVisibility Visibility { get; private set; } = GenitalVisibility.Normal;

    private VaginaProfile()
    {
    }

    public VaginaProfile(ProtoId<GenitalShapePrototype> shape, bool matchSkin = true, Color? color = null,
        GenitalVisibility visibility = GenitalVisibility.Normal)
    {
        Shape = shape;
        MatchSkin = matchSkin;
        Color = color ?? Color.White;
        Visibility = visibility;
    }

    /// <summary>Copy with the given values changed.</summary>
    public VaginaProfile With(ProtoId<GenitalShapePrototype>? shape = null, bool? matchSkin = null, Color? color = null,
        GenitalVisibility? visibility = null)
    {
        return new VaginaProfile(
            shape ?? Shape,
            matchSkin ?? MatchSkin,
            color ?? Color,
            visibility ?? Visibility);
    }

    public VaginaProfile Clone()
    {
        return With();
    }

    public bool MemberwiseEquals(VaginaProfile other)
    {
        return Shape == other.Shape
               && MatchSkin == other.MatchSkin
               && Color.Equals(other.Color)
               && Visibility == other.Visibility;
    }
}

/// <summary>Breasts configuration.</summary>
[DataDefinition, Serializable, NetSerializable]
public sealed partial class BreastsProfile
{
    [DataField(required: true)]
    public ProtoId<GenitalShapePrototype> Shape { get; private set; }

    /// <summary>1-19: cups A-O, then oversize grades 1-4.</summary>
    [DataField]
    public int Cup { get; private set; } = 3;

    [DataField]
    public bool Lactation { get; private set; }

    [DataField]
    public bool MatchSkin { get; private set; } = true;

    [DataField]
    public Color Color { get; private set; } = Color.White;

    [DataField]
    public GenitalVisibility Visibility { get; private set; } = GenitalVisibility.Normal;

    private BreastsProfile()
    {
    }

    public BreastsProfile(ProtoId<GenitalShapePrototype> shape, int cup = 3, bool lactation = false, bool matchSkin = true,
        Color? color = null, GenitalVisibility visibility = GenitalVisibility.Normal)
    {
        Shape = shape;
        Cup = cup;
        Lactation = lactation;
        MatchSkin = matchSkin;
        Color = color ?? Color.White;
        Visibility = visibility;
    }

    /// <summary>Copy with the given values changed.</summary>
    public BreastsProfile With(ProtoId<GenitalShapePrototype>? shape = null, int? cup = null, bool? lactation = null,
        bool? matchSkin = null, Color? color = null, GenitalVisibility? visibility = null)
    {
        return new BreastsProfile(
            shape ?? Shape,
            cup ?? Cup,
            lactation ?? Lactation,
            matchSkin ?? MatchSkin,
            color ?? Color,
            visibility ?? Visibility);
    }

    public BreastsProfile Clone()
    {
        return With();
    }

    public bool MemberwiseEquals(BreastsProfile other)
    {
        return Shape == other.Shape
               && Cup == other.Cup
               && Lactation == other.Lactation
               && MatchSkin == other.MatchSkin
               && Color.Equals(other.Color)
               && Visibility == other.Visibility;
    }
}
