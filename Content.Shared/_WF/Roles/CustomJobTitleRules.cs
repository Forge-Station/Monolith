using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using Content.Shared.Clothing;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Roles;

/// <summary>Cleans and checks player-written job titles. Shared so the editor and the server agree.</summary>
public static class CustomJobTitleRules
{
    private const string AllowedPunctuation = " -'.,&/()";
    private static readonly char[] WordSeparators = AllowedPunctuation.ToCharArray();

    /// <summary>Trims and collapses runs of whitespace.</summary>
    public static string Clean(string title)
    {
        return string.Join(' ', title.Split((char[]?) null, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Lower-case letters and digits only, with look-alike digits folded (1 and l read as i, 0 as o...),
    /// so spacing, punctuation and "P1lot" can't dodge a match. For comparing only, never for display.
    /// </summary>
    public static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c))
                builder.Append(Fold(char.ToLowerInvariant(c)));
        }

        return builder.ToString();
    }

    private static char Fold(char c)
    {
        return c switch
        {
            '0' => 'o',
            '1' or 'l' => 'i',
            '3' => 'e',
            '4' => 'a',
            '5' => 's',
            '7' => 't',
            '8' => 'b',
            _ => c,
        };
    }

    /// <summary>Normalized words. Runs of single letters join up, so "A D M I N" and "A.D.M.I.N" read as one word.</summary>
    private static List<string> Words(string text)
    {
        var words = new List<string>();
        var letters = new StringBuilder();
        foreach (var part in text.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            var word = Normalize(part);
            if (word.Length == 1)
            {
                letters.Append(word);
                continue;
            }

            if (letters.Length > 0)
            {
                words.Add(letters.ToString());
                letters.Clear();
            }

            if (word.Length > 0)
                words.Add(word);
        }

        if (letters.Length > 0)
            words.Add(letters.ToString());

        return words;
    }

    /// <summary>True if the phrase's words appear back to back in the title's words.</summary>
    private static bool ContainsPhrase(List<string> words, List<string> phrase)
    {
        if (phrase.Count == 0)
            return false;

        for (var i = 0; i + phrase.Count <= words.Count; i++)
        {
            var match = true;
            for (var j = 0; j < phrase.Count && match; j++)
            {
                match = words[i + j] == phrase[j];
            }

            if (match)
                return true;
        }

        return false;
    }

    /// <summary>Checks a cleaned title against the role's rules. Empty is valid and means the job's own name.</summary>
    public static bool IsValid(string title, CustomJobTitlePrototype rules, IPrototypeManager protoManager, [NotNullWhen(false)] out string? reason)
    {
        reason = null;
        if (title.Length == 0)
            return true;

        if (title.Length > rules.MaxLength)
        {
            reason = Loc.GetString("custom-job-title-too-long", ("max", rules.MaxLength));
            return false;
        }

        // ASCII only, so look-alike letters can't spell out a real job.
        foreach (var c in title)
        {
            if (char.IsAsciiLetterOrDigit(c) || AllowedPunctuation.Contains(c))
                continue;

            reason = Loc.GetString("custom-job-title-bad-character", ("character", c.ToString()));
            return false;
        }

        if (!title.Any(char.IsAsciiLetter))
        {
            reason = Loc.GetString("custom-job-title-no-letters");
            return false;
        }

        var normalized = Normalize(title);
        var allowed = rules.AllowedJobs.Any(id => protoManager.TryIndex(id, out var job)
                                                  && Normalize(job.LocalizedName) == normalized);

        foreach (var job in protoManager.EnumeratePrototypes<JobPrototype>())
        {
            if (allowed || Normalize(job.LocalizedName) != normalized)
                continue;

            reason = Loc.GetString("custom-job-title-matches-job", ("job", job.LocalizedName));
            return false;
        }

        foreach (var blocked in rules.BlockedTitles)
        {
            if (Normalize(blocked) != normalized)
                continue;

            reason = Loc.GetString("custom-job-title-blocked");
            return false;
        }

        var words = Words(title);
        foreach (var word in rules.BlockedWords)
        {
            if (!ContainsPhrase(words, Words(word)))
                continue;

            reason = Loc.GetString("custom-job-title-blocked-word", ("word", word));
            return false;
        }

        return true;
    }

    /// <summary>The profile's valid custom title for a job, or null to keep the job's own name.</summary>
    public static string? GetTitle(HumanoidCharacterProfile profile, string? jobId, IPrototypeManager protoManager)
    {
        if (string.IsNullOrEmpty(jobId)
            || !profile.Loadouts.TryGetValue(LoadoutSystem.GetJobPrototype(jobId), out var loadout))
            return null;

        return Sanitize(loadout.CustomJobTitle, loadout.Role, protoManager);
    }

    /// <summary>Job name for the join menu, e.g. "Vagrant (Bounty Hunter)".</summary>
    public static string JoinMenuName(JobPrototype job, HumanoidCharacterProfile profile, IPrototypeManager protoManager)
    {
        return GetTitle(profile, job.ID, protoManager) is { } title
            ? Loc.GetString("custom-job-title-join-name", ("job", job.LocalizedName), ("title", title))
            : job.LocalizedName;
    }

    /// <summary>Returns the cleaned title if the role allows one and it passes the rules, otherwise null.</summary>
    public static string? Sanitize(string? title, ProtoId<RoleLoadoutPrototype> role, IPrototypeManager protoManager)
    {
        if (title == null || !protoManager.TryIndex<CustomJobTitlePrototype>(role.Id, out var rules))
            return null;

        var cleaned = Clean(title);
        if (cleaned.Length == 0 || !IsValid(cleaned, rules, protoManager, out _))
            return null;

        return cleaned;
    }
}
