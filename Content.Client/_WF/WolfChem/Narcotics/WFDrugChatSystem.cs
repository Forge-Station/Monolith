using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using Content.Shared._WF.WolfChem.Narcotics;
using Content.Shared.Chat;
using Robust.Client.Player;

namespace Content.Client._WF.WolfChem.Narcotics;

/// <summary>
/// Chat seen through the local player's high, on their screen only: psychedelics colour each word and, when heavy,
/// jumble a few; opioids grey speech out, empathogens warm it and cannabinoids tint it green. Only speech, radio and
/// emotes change, and the strength follows the high.
/// </summary>
public sealed class WFDrugChatSystem : EntitySystem
{
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private WFIntoxicationSystem _intoxication = default!;

    private const ChatChannel Distorted = ChatChannel.Local | ChatChannel.Whisper | ChatChannel.Radio | ChatChannel.Emotes;

    /// <summary>A markup tag; the text between tags is what gets distorted.</summary>
    private static readonly Regex Tag = new(@"(?<!\\)\[[^\]]*\]");

    private static readonly Regex Word = new(@"\p{L}[\p{L}']*");

    private static readonly Dictionary<WFDrugFamily, Color> Tints = new()
    {
        [WFDrugFamily.Opioid] = Color.FromHex("#8f98ab"),
        [WFDrugFamily.Empathogen] = Color.FromHex("#ffa8cc"),
        [WFDrugFamily.Cannabinoid] = Color.FromHex("#b4dca0"),
    };

    /// <summary>Hue step from one word to the next on a psychedelic.</summary>
    private const float HueStep = 0.09f;

    /// <summary>Rewrites a message's text in place, if the local player is high on something that changes chat.</summary>
    public void Distort(ChatMessage msg)
    {
        if ((msg.Channel & Distorted) == 0 || string.IsNullOrEmpty(msg.Message))
            return;

        var start = msg.WrappedMessage.LastIndexOf(msg.Message, StringComparison.Ordinal);
        if (start < 0 || DistortMarkup(msg.Message, msg.WrappedMessage.GetHashCode()) is not { } body)
            return;

        msg.WrappedMessage = msg.WrappedMessage.Remove(start, msg.Message.Length).Insert(start, body);
    }

    /// <summary>
    /// Markup as the local player sees it through their high, or null when they aren't high on something that changes
    /// text. The same seed gives the same result.
    /// </summary>
    public string? DistortMarkup(string markup, int seed)
    {
        if (_player.LocalEntity is not { } local
            || !TryComp<WFIntoxicationComponent>(local, out var comp)
            || _intoxication.DominantHigh(comp) is not { } high)
            return null;

        if (high.Family == WFDrugFamily.Psychedelic)
            return Kaleidoscope(markup, high, new Random(seed));

        if (Tints.TryGetValue(high.Family, out var tint))
            return $"[color={Color.InterpolateBetween(Color.White, tint, 0.35f + 0.65f * high.Strength).ToHex()}]{markup}[/color]";

        return null;
    }

    /// <summary>Colours each word along a drifting hue and, when heavy, shuffles the middle letters of a few.</summary>
    private static string Kaleidoscope(string text, WFDominantHigh high, Random random)
    {
        var hue = random.NextSingle();
        var saturation = 0.2f + 0.5f * high.Strength;
        var jumble = high.Phase switch
        {
            WFDrugPhase.Heavy => 0.06f,
            WFDrugPhase.Overdose => 0.15f,
            _ => 0f,
        };

        var result = new StringBuilder();
        var last = 0;
        foreach (Match tag in Tag.Matches(text))
        {
            Colour(text.Substring(last, tag.Index - last));
            result.Append(tag.Value);
            last = tag.Index + tag.Length;
        }

        Colour(text.Substring(last));
        return result.ToString();

        void Colour(string plain)
        {
            var from = 0;
            foreach (Match word in Word.Matches(plain))
            {
                result.Append(plain, from, word.Index - from);
                var shown = word.Length >= 4 && random.NextSingle() < jumble ? Shuffle(word.Value, random) : word.Value;
                var color = Color.FromHsv(new Vector4(hue, saturation, 1f, 1f));
                result.Append($"[color={color.ToHex()}]{shown}[/color]");
                hue = (hue + HueStep) % 1f;
                from = word.Index + word.Length;
            }

            result.Append(plain, from, plain.Length - from);
        }
    }

    /// <summary>The word with its middle letters in a random order.</summary>
    private static string Shuffle(string word, Random random)
    {
        var letters = word.ToCharArray();
        for (var i = letters.Length - 2; i > 1; i--)
        {
            var j = random.Next(1, i + 1);
            (letters[i], letters[j]) = (letters[j], letters[i]);
        }

        return new string(letters);
    }
}
