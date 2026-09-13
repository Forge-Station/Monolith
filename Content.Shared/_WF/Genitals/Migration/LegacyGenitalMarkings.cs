using Content.Shared._WF.Genitals.Prototypes;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Genitals.Migration;

/// <summary>Converts HardLight-era genital markings into a GenitalProfile. Pure apart from ContextFor and logging.</summary>
/// <remarks>Decodes with the generated static table, never with marking prototypes, so it keeps working once they are deleted.</remarks>
public static partial class LegacyGenitalMarkings
{
    private const string SawmillName = "genitals.migration";

    /// <summary>Penis shape for synthesised sheaths when the species preset's shape cannot have one.</summary>
    public static readonly ProtoId<GenitalShapePrototype> FallbackSheathShape = "GenitalShapePenisNondescript";

    /// <summary>The generated id-to-entry table.</summary>
    public static IReadOnlyDictionary<string, LegacyEntry> Entries => Table;

    /// <summary>Whether this migration converts the marking id.</summary>
    public static bool IsLegacyId(string markingId)
    {
        return Table.ContainsKey(markingId);
    }

    /// <summary>Strips the table's markings and, for an unmigrated or empty profile, builds the anatomy they described.</summary>
    /// <remarks>
    /// Never edits the inputs: With* copies of a profile share their appearance. A LoadFailed profile is returned unchanged.
    /// A version-1 profile with a configuration keeps it; the old markings are removed and added to its backup. An
    /// unmigrated profile with nothing to convert is marked migrated, so a later anatomy edit on it is saved.
    /// </remarks>
    public static (HumanoidCharacterAppearance Appearance, GenitalProfile Genitals) Migrate(
        HumanoidCharacterAppearance appearance,
        GenitalProfile genitals,
        LegacyContext ctx)
    {
        if (genitals.LoadFailed)
            return (appearance, genitals);

        var markings = appearance.Markings;
        List<Marking>? kept = null;
        List<string>? removed = null;
        List<(LegacyEntry Entry, Color Color)>? decoded = null;
        List<string>? unknown = null;

        for (var i = 0; i < markings.Count; i++)
        {
            var marking = markings[i];
            if (!Table.TryGetValue(marking.MarkingId, out var entry))
            {
                kept?.Add(marking);
                if (LooksGenital(marking.MarkingId))
                {
                    unknown ??= new List<string>();
                    unknown.Add(marking.MarkingId);
                }

                continue;
            }

            // First table id: copy everything before it.
            if (kept == null)
            {
                kept = new List<Marking>(markings.Count);
                for (var j = 0; j < i; j++)
                {
                    kept.Add(markings[j]);
                }
            }

            removed ??= new List<string>();
            removed.Add(marking.ToString());
            decoded ??= new List<(LegacyEntry, Color)>();
            decoded.Add((entry, FirstColor(marking)));
        }

        if (unknown != null)
            Log().Warning($"Anatomy migration: genital-looking marking ids missing from the legacy table, left to marking validation: {string.Join(", ", unknown)}");

        // Nothing to convert. An unmigrated profile counts as migrated from here on; otherwise its edits (With* keeps
        // version 0) would serialise as "" and be lost on reload.
        if (kept == null || removed == null || decoded == null)
            return (appearance, genitals.Version == 0 ? genitals.AsMigrated(null) : genitals);

        var stripped = appearance.WithMarkings(kept);
        var backup = MergeBackup(genitals.LegacyMarkings, removed);
        if (genitals.Version != 0 && !genitals.IsEmpty)
        {
            // Markings re-added under an older build: the configuration wins, and the markings join the backup.
            Log().Warning($"Anatomy migration: a configured profile still had {removed.Count} legacy genital marking(s); removed and added to the backup.");
            return (stripped, genitals.AsMigrated(backup));
        }

        return (stripped, Build(decoded, genitals.RevealMode, ctx).AsMigrated(backup));
    }

    /// <summary>The recorded backup plus the newly removed strings, without repeats, capped at one per table entry as the validator caps it.</summary>
    private static List<string> MergeBackup(List<string>? backup, List<string> removed)
    {
        var merged = backup == null ? new List<string>(removed.Count) : new List<string>(backup);
        foreach (var entry in removed)
        {
            if (merged.Count >= Table.Count)
                break;

            if (!merged.Contains(entry))
                merged.Add(entry);
        }

        return merged;
    }

    /// <summary>Species preset data the decode needs: a sheath-capable shape, the tissue colour and the shapes that allow a sheath.</summary>
    public static LegacyContext ContextFor(string species, Color skinColor, IPrototypeManager proto)
    {
        var sheathCapable = new HashSet<string>();
        foreach (var shape in proto.EnumeratePrototypes<GenitalShapePrototype>())
        {
            if (shape.Slot == GenitalSlot.Penis && shape.AllowedSheaths.Contains(SheathType.Sheath))
                sheathCapable.Add(shape.ID);
        }

        var sheathShape = FallbackSheathShape;
        if (GenitalColorDefaults.FindPreset(species, proto)?.Male.Penis is { } presetPenis
            && sheathCapable.Contains(presetPenis.Shape.Id))
        {
            sheathShape = presetPenis.Shape;
        }

        return new LegacyContext(sheathShape, GenitalColorDefaults.TissueColorFor(species, proto), skinColor, sheathCapable);
    }

    /// <summary>Colour of one migrated part from the marking's first colour.</summary>
    /// <remarks>
    /// Skin-tone art keeps its baked colour (tinted by a non-white marking colour). Untinted art follows the default
    /// policy: tissue colour for the shaft and vulva on non-human-toned species, otherwise match skin. A colour equal to
    /// the skin colour matches skin; anything else is kept as a custom colour.
    /// </remarks>
    public static (bool MatchSkin, Color Color) ResolveColor(LegacyEntry entry, Color markingColor, bool tissue, LegacyContext ctx)
    {
        if (entry.BakedColor is { } baked)
        {
            var tinted = IsWhite(markingColor) ? baked : baked * markingColor;
            return (false, GenitalProfileValidator.NormaliseColor(tinted));
        }

        if (IsWhite(markingColor))
            return tissue ? DefaultTissue(ctx) : (true, Color.White);

        if (SameRgb(markingColor, ctx.SkinColor))
            return (true, Color.White);

        return (false, GenitalProfileValidator.NormaliseColor(markingColor));
    }

    /// <summary>Pass 1 picks the first entry per slot; pass 2 builds the organs, so the result never depends on marking order.</summary>
    private static GenitalProfile Build(List<(LegacyEntry Entry, Color Color)> decoded, GenitalRevealMode reveal, LegacyContext ctx)
    {
        (LegacyEntry Entry, Color Color)? penis = null;
        (LegacyEntry Entry, Color Color)? testicles = null;
        (LegacyEntry Entry, Color Color)? vagina = null;
        (LegacyEntry Entry, Color Color)? breasts = null;
        var removesPenis = false;

        foreach (var item in decoded)
        {
            // Amputation applies wherever it is listed.
            if (item.Entry.RemovesPenis)
            {
                removesPenis = true;
                continue;
            }

            switch (item.Entry.Slot)
            {
                case GenitalSlot.Penis:
                    penis ??= item;
                    break;
                case GenitalSlot.Testicles:
                    testicles ??= item;
                    break;
                case GenitalSlot.Vagina:
                    vagina ??= item;
                    break;
                case GenitalSlot.Breasts:
                    breasts ??= item;
                    break;
            }
        }

        PenisProfile? penisProfile = null;
        if (penis is { } p && !removesPenis)
        {
            var (match, color) = ResolveColor(p.Entry, p.Color, true, ctx);
            penisProfile = new PenisProfile(p.Entry.Shape, GenitalStateBuilder.StepToLength(p.Entry.Step), matchSkin: match, color: color);
        }

        TesticlesProfile? testiclesProfile = null;
        if (testicles is { } t)
        {
            var (match, color) = ResolveColor(t.Entry, t.Color, false, ctx);
            testiclesProfile = new TesticlesProfile(TesticleType.External,
                Math.Clamp(t.Entry.Step, GenitalProfileValidator.MinTesticleSize, GenitalProfileValidator.MaxTesticleSize), match, color);

            if (penisProfile == null)
            {
                // Testicles are a linked option of the penis, so one is synthesised that reproduces the old look:
                // the fused sheath art becomes a sheathed penis, bare testicles get a penis that is never drawn.
                var sheathed = t.Entry.Sheathed && !removesPenis;
                var (shaftMatch, shaftColor) = DefaultTissue(ctx);
                penisProfile = sheathed
                    ? new PenisProfile(ctx.SheathShape, GenitalStateBuilder.StepToLength(1), SheathType.Sheath,
                        shaftMatch, shaftColor, match, color)
                    : new PenisProfile(ctx.SheathShape, GenitalStateBuilder.StepToLength(1), SheathType.None,
                        shaftMatch, shaftColor, visibility: GenitalVisibility.AlwaysHidden);

                Log().Info(sheathed
                    ? "Anatomy migration: fused sheath art without a penis marking became a sheathed penis."
                    : "Anatomy migration: testicles without a penis marking got an always-hidden penis.");
            }
            else if (t.Entry.Sheathed)
            {
                if (ctx.AllowsSheath(penisProfile.Shape))
                    penisProfile = penisProfile.With(sheath: SheathType.Sheath, sheathMatchSkin: match, sheathColor: color);
                else
                    Log().Info($"Anatomy migration: penis shape {penisProfile.Shape.Id} cannot have a sheath; the fused sheath art was dropped.");
            }
        }

        var profile = GenitalProfile.Empty.WithRevealMode(reveal)
            .WithPenis(penisProfile)
            .WithTesticles(testiclesProfile);

        if (vagina is { } v)
        {
            var (match, color) = ResolveColor(v.Entry, v.Color, true, ctx);

            // Adding a vagina also adds the womb.
            profile = profile.WithVagina(new VaginaProfile(v.Entry.Shape, match, color));
        }

        if (breasts is { } b)
        {
            var (match, color) = ResolveColor(b.Entry, b.Color, false, ctx);
            var cup = Math.Clamp(b.Entry.Step, GenitalProfileValidator.MinCup, GenitalProfileValidator.MaxCup);
            profile = profile.WithBreasts(new BreastsProfile(b.Entry.Shape, cup, false, match, color));
        }

        return profile;
    }

    /// <summary>Default shaft or vulva colour: the tissue colour, or match skin on human-toned species.</summary>
    private static (bool MatchSkin, Color Color) DefaultTissue(LegacyContext ctx)
    {
        return ctx.TissueColor is { } tissue
            ? (false, GenitalProfileValidator.NormaliseColor(tissue))
            : (true, Color.White);
    }

    private static Color FirstColor(Marking marking)
    {
        return marking.MarkingColors.Count > 0 ? marking.MarkingColors[0] : Color.White;
    }

    private static bool LooksGenital(string markingId)
    {
        return markingId.StartsWith("Genital", StringComparison.Ordinal)
               || markingId.StartsWith("Knotted-", StringComparison.Ordinal);
    }

    private static bool IsWhite(Color color)
    {
        return ToByte(color.R) == 255 && ToByte(color.G) == 255 && ToByte(color.B) == 255;
    }

    /// <summary>Equal at 8 bits per channel, ignoring alpha.</summary>
    private static bool SameRgb(Color a, Color b)
    {
        return ToByte(a.R) == ToByte(b.R) && ToByte(a.G) == ToByte(b.G) && ToByte(a.B) == ToByte(b.B);
    }

    private static int ToByte(float channel)
    {
        return (int) MathF.Round(Math.Clamp(channel, 0f, 1f) * 255f);
    }

    private static ISawmill Log()
    {
        return Logger.GetSawmill(SawmillName);
    }
}

/// <summary>One decoded legacy marking. Step is the sprite size step, or the cup for breasts.</summary>
/// <param name="BakedColor">Mean colour of skin-tone (_s) art, kept as a custom colour; null for greyscale art.</param>
/// <param name="Sheathed">Fused sheath testicle art.</param>
/// <param name="RemovesPenis">The amputated marking.</param>
public readonly record struct LegacyEntry(
    GenitalSlot Slot,
    string Shape,
    int Step,
    Color? BakedColor,
    bool Sheathed,
    bool RemovesPenis = false)
{
    public static readonly LegacyEntry PenisRemoved = new(GenitalSlot.Penis, "", 0, null, false, RemovesPenis: true);
}

/// <summary>Species data one migration needs.</summary>
/// <param name="SheathShape">The species preset's male penis shape if it allows a sheath, else GenitalShapePenisNondescript.</param>
/// <param name="TissueColor">The preset tissue colour for species that are not HumanToned; null for HumanToned.</param>
/// <param name="SkinColor">The profile's skin colour; a marking colour equal to it becomes match skin.</param>
/// <param name="SheathCapableShapes">Penis shapes whose allowedSheaths include Sheath; null allows every shape.</param>
public readonly record struct LegacyContext(
    ProtoId<GenitalShapePrototype> SheathShape,
    Color? TissueColor,
    Color SkinColor,
    IReadOnlySet<string>? SheathCapableShapes = null)
{
    public bool AllowsSheath(ProtoId<GenitalShapePrototype> shape)
    {
        return SheathCapableShapes?.Contains(shape.Id) ?? true;
    }
}
