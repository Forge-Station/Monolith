using System.Linq;
using System.Text;

namespace Content.Shared._WF.MappingMode;

/// <summary>
/// Join codes for mapping sessions: six characters without the easily confused ones (I, O, 0, 1).
/// </summary>
public static class WFMappingSessionCodes
{
    public const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    public const int Length = 6;

    /// <summary>A code not in <paramref name="taken"/>, using <paramref name="nextBelow"/> (0 to n - 1) for randomness.</summary>
    public static string Generate(Func<int, int> nextBelow, ICollection<string> taken)
    {
        var builder = new StringBuilder(Length);
        while (true)
        {
            builder.Clear();
            for (var i = 0; i < Length; i++)
            {
                builder.Append(Alphabet[nextBelow(Alphabet.Length)]);
            }

            var code = builder.ToString();
            if (!taken.Contains(code))
                return code;
        }
    }

    /// <summary>The code as typed, trimmed and upper-cased.</summary>
    public static string Normalize(string code)
    {
        return code.Trim().ToUpperInvariant();
    }

    /// <summary>Whether a normalized code has the right length and alphabet.</summary>
    public static bool IsValid(string code)
    {
        return code.Length == Length && code.All(c => Alphabet.Contains(c));
    }
}
