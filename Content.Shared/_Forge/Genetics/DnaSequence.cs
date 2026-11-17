using System.Text;
using Robust.Shared.Random;

namespace Content.Shared._Forge.Genetics;

public static class DnaSequence
{
    /// <summary>
    /// Structural enzyme blocks. Thirteen is the cap: a longer cycle cannot be assembled by hand.
    /// </summary>
    public static readonly string[] Codons =
    [
        "AA", "CC", "GG", "TT", "FF", "ET", "HE", "CG", "AT", "TA", "GA", "TC", "BD",
    ];

    public const int BranchLength = 7;

    public static string Format(IReadOnlyList<string> sequence)
    {
        if (sequence.Count == 0)
            return "*******";

        var sb = new StringBuilder();
        for (var i = 0; i < sequence.Count; i++)
        {
            if (i > 0)
                sb.Append('-');
            sb.Append(string.IsNullOrEmpty(sequence[i]) ? "**" : sequence[i]);
        }

        return sb.ToString();
    }

    public static string FormatOccupied(int offset, IReadOnlyList<string> recipe)
    {
        var slots = new List<string>(BranchLength);
        for (var i = 0; i < BranchLength; i++)
            slots.Add("**");

        for (var i = 0; i < recipe.Count && offset + i < BranchLength; i++)
            slots[offset + i] = recipe[i];

        return Format(slots);
    }

    public static string NextCodon(string codon)
    {
        var index = Array.IndexOf(Codons, codon);
        if (index < 0)
            return Codons[0];

        return Codons[(index + 1) % Codons.Length];
    }

    public static List<string> Random(IRobustRandom random, int length)
    {
        var list = new List<string>(length);
        for (var i = 0; i < length; i++)
            list.Add(random.Pick(Codons));
        return list;
    }

    public static int MatchCount(IReadOnlyList<string> current, IReadOnlyList<string> target, int offset = 0)
    {
        if (offset < 0)
            return 0;

        var matches = 0;
        for (var i = 0; i < target.Count; i++)
        {
            var index = offset + i;
            if (index >= current.Count)
                break;

            if (current[index] == target[i])
                matches++;
        }

        return matches;
    }

    public static bool Matches(IReadOnlyList<string> current, IReadOnlyList<string> target, int offset = 0)
    {
        if (target.Count == 0 || offset < 0 || current.Count < offset + target.Count)
            return false;

        for (var i = 0; i < target.Count; i++)
        {
            if (current[offset + i] != target[i])
                return false;
        }

        return true;
    }

    /// <summary>
    /// White: codon matches no uniqueness here.
    /// Yellow: codon matches a uniqueness whose assembly window start is wrong.
    /// Green: the uniqueness window is matching from its first codon.
    /// </summary>
    public static GeneBlockHint[] ComputeBlockHints(
        IReadOnlyList<string> strand,
        IEnumerable<(int Offset, IReadOnlyList<string> Codons)> recipes)
    {
        var hints = new GeneBlockHint[strand.Count];
        foreach (var (offset, recipe) in recipes)
        {
            var prefixOk = true;
            for (var i = 0; i < recipe.Count; i++)
            {
                var index = offset + i;
                if (index < 0 || index >= strand.Count)
                {
                    prefixOk = false;
                    continue;
                }

                if (strand[index] == recipe[i])
                {
                    if (prefixOk)
                        hints[index] = GeneBlockHint.OnTrack;
                    else if (hints[index] != GeneBlockHint.OnTrack)
                        hints[index] = GeneBlockHint.OffTrack;
                }
                else
                {
                    prefixOk = false;
                }
            }
        }

        return hints;
    }
}
