namespace Content.Shared._WF.Genitals;

/// <summary>Localised cup and testicle size words, shared by examine, the Anatomy panel and the creator.</summary>
public static class GenitalWords
{
    /// <summary>Cup letters for cups 1-15; cups 16-19 are oversize grades 1-4.</summary>
    private static readonly string[] CupLetters =
    {
        "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O",
    };

    /// <summary>Size words for testicle sizes 1-5 (examine.ftl).</summary>
    private static readonly string[] TesticleSizes =
    {
        "wf-genitals-testicles-size-1",
        "wf-genitals-testicles-size-2",
        "wf-genitals-testicles-size-3",
        "wf-genitals-testicles-size-4",
        "wf-genitals-testicles-size-5",
    };

    /// <summary>"cup A" to "cup O", then "oversize grade 1" to "4". Clamped to the validator's cup range.</summary>
    public static string CupText(int cup)
    {
        cup = Math.Clamp(cup, GenitalProfileValidator.MinCup, GenitalProfileValidator.MaxCup);
        return cup <= CupLetters.Length
            ? Loc.GetString("wf-genitals-cup-letter", ("letter", CupLetters[cup - 1]))
            : Loc.GetString("wf-genitals-cup-oversize", ("grade", cup - CupLetters.Length));
    }

    /// <summary>Size word for a testicle size, clamped to the validator's range.</summary>
    public static string TesticleSize(int size)
    {
        size = Math.Clamp(size, GenitalProfileValidator.MinTesticleSize, GenitalProfileValidator.MaxTesticleSize);
        return Loc.GetString(TesticleSizes[size - GenitalProfileValidator.MinTesticleSize]);
    }
}
