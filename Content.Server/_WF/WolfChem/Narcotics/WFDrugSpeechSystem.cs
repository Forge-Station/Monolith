using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Content.Server._WF.Voice;
using Content.Server.Speech;
using Content.Shared._WF.WolfChem.Narcotics;
using Robust.Shared.Random;

namespace Content.Server._WF.WolfChem.Narcotics;

/// <summary>
/// How a high sounds to everyone. In text, from the heavy phase on: stimulants rush and stutter, opioids slur and
/// trail off, cannabinoids fill and lose the thread, empathogens stretch and soften, and a psychedelic swaps words
/// around (Chorus's top tier scrambles them outright). In the character's voice, each family shifts syllable speed,
/// pitch, intonation, waver and wobble by phase. An overdose pushes both further.
/// </summary>
public sealed class WFDrugSpeechSystem : EntitySystem
{
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private WFIntoxicationSystem _intoxication = default!;

    private static readonly Regex Token = new(@"\S+");
    private static readonly Regex FullStop = new(@"(?<=\p{L})\.(?=\s|$)");
    private static readonly Regex Ending = new(@"[.!?,;:]+$");
    private static readonly Regex Vowels = new(@"[aeiouAEIOU]+(?=[^aeiouAEIOU]*$)");

    private const int Fillers = 4;
    private const int Tails = 3;
    private const int Forgets = 4;
    private const int Gushes = 4;

    /// <summary>
    /// How one phase of a family changes a voice: syllable time, pitch and intonation as multipliers, pitch variance
    /// added, vibrato depth and tremolo as floors. Families stack.
    /// </summary>
    private readonly record struct VoiceShift(
        float Speed = 1f,
        float Pitch = 1f,
        float Intonation = 1f,
        float Variance = 0f,
        float Vibrato = 0f,
        float Tremolo = 0f);

    private static readonly Dictionary<(WFDrugFamily, WFDrugPhase), VoiceShift> Voices = new()
    {
        [(WFDrugFamily.Stimulant, WFDrugPhase.Light)] = new(Speed: 0.85f),
        [(WFDrugFamily.Stimulant, WFDrugPhase.Heavy)] = new(Speed: 0.7f, Pitch: 1.04f),
        [(WFDrugFamily.Stimulant, WFDrugPhase.Overdose)] = new(Speed: 0.55f, Pitch: 1.08f, Variance: 0.1f),
        [(WFDrugFamily.Opioid, WFDrugPhase.Light)] = new(Speed: 1.15f),
        [(WFDrugFamily.Opioid, WFDrugPhase.Heavy)] = new(Speed: 1.4f, Pitch: 0.94f, Intonation: 0.5f),
        [(WFDrugFamily.Opioid, WFDrugPhase.Overdose)] = new(Speed: 1.7f, Pitch: 0.9f, Intonation: 0f),
        [(WFDrugFamily.Cannabinoid, WFDrugPhase.Heavy)] = new(Speed: 1.2f, Intonation: 0.7f),
        [(WFDrugFamily.Cannabinoid, WFDrugPhase.Overdose)] = new(Speed: 1.35f, Intonation: 0.5f),
        [(WFDrugFamily.Psychedelic, WFDrugPhase.Heavy)] = new(Variance: 0.15f, Vibrato: 0.12f),
        [(WFDrugFamily.Psychedelic, WFDrugPhase.Overdose)] = new(Variance: 0.3f, Vibrato: 0.25f),
        [(WFDrugFamily.Empathogen, WFDrugPhase.Heavy)] = new(Pitch: 1.03f, Intonation: 1.6f),
        [(WFDrugFamily.Empathogen, WFDrugPhase.Overdose)] = new(Pitch: 1.05f, Intonation: 2f, Tremolo: 0.3f),
    };

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFIntoxicationComponent, AccentGetEvent>(OnAccent);
        SubscribeLocalEvent<WFIntoxicationComponent, SpeechVoiceModifyEvent>(OnVoice);
    }

    private void OnVoice(Entity<WFIntoxicationComponent> ent, ref SpeechVoiceModifyEvent args)
    {
        foreach (var family in Enum.GetValues<WFDrugFamily>())
        {
            if (!Voices.TryGetValue((family, _intoxication.FamilyPhase(ent.Comp, family)), out var shift))
                continue;

            var voice = args.Voice;
            voice.Speed *= shift.Speed;
            voice.Pitch *= shift.Pitch;
            voice.Intonation *= shift.Intonation;
            voice.PitchVariance += shift.Variance;
            voice.VibratoDepth = Math.Max(voice.VibratoDepth, shift.Vibrato);
            voice.Tremolo = Math.Max(voice.Tremolo, shift.Tremolo);
            args.Changed = true;
        }
    }

    private void OnAccent(EntityUid uid, WFIntoxicationComponent comp, AccentGetEvent args)
    {
        var stimulant = _intoxication.FamilyPhase(comp, WFDrugFamily.Stimulant);
        if (stimulant >= WFDrugPhase.Heavy)
            args.Message = Rush(args.Message, stimulant == WFDrugPhase.Overdose);

        var opioid = _intoxication.FamilyPhase(comp, WFDrugFamily.Opioid);
        if (opioid >= WFDrugPhase.Heavy)
            args.Message = Trail(Slur(args.Message, opioid == WFDrugPhase.Overdose), opioid == WFDrugPhase.Overdose);

        var cannabis = _intoxication.FamilyPhase(comp, WFDrugFamily.Cannabinoid);
        if (cannabis >= WFDrugPhase.Heavy)
            args.Message = Haze(args.Message, cannabis == WFDrugPhase.Overdose);

        var empathogen = _intoxication.FamilyPhase(comp, WFDrugFamily.Empathogen);
        if (empathogen >= WFDrugPhase.Heavy)
            args.Message = Gush(args.Message, empathogen == WFDrugPhase.Overdose);

        // The overdose tier scrambles every word through ScrambledAccent.
        if (_intoxication.FamilyPhase(comp, WFDrugFamily.Psychedelic) == WFDrugPhase.Heavy)
            args.Message = Muddle(args.Message);
    }

    /// <summary>
    /// Doubles and stutters some words and ends sentences with exclamations; an overdose doubles more, shouts and
    /// drops the commas so it all runs on.
    /// </summary>
    public string Rush(string message, bool overdose)
    {
        var repeat = overdose ? 0.45f : 0.25f;
        var shout = overdose ? 0.4f : 0f;
        var stutter = overdose ? 0.2f : 0.15f;
        var rushed = Token.Replace(message, match =>
        {
            var word = match.Value;
            if (overdose)
                word = word.Replace(",", string.Empty);

            if (_random.Prob(shout))
                word = word.ToUpperInvariant();

            if (word.Length > 1 && char.IsLetter(word[0]) && _random.Prob(stutter))
                word = Stutter(word, _random.Next(1, 3));

            var core = Ending.Replace(word, string.Empty);
            if (core.Length == 0 || !_random.Prob(repeat))
                return word;

            return overdose ? $"{core} {word}" : $"{core}, {word}";
        });

        return FullStop.Replace(rushed, "!");
    }

    /// <summary>The word with its first letter caught on that many times ("g-g-go").</summary>
    private static string Stutter(string word, int times)
    {
        var shouted = word == word.ToUpperInvariant();
        var first = word[0];
        var echo = shouted ? first : char.ToLowerInvariant(first);
        var result = new StringBuilder();
        result.Append(first).Append('-');
        for (var i = 1; i < times; i++)
        {
            result.Append(echo).Append('-');
        }

        return result.Append(echo).Append(word, 1, word.Length - 1).ToString();
    }

    /// <summary>Softens some sounds and drags others out, like a drunk slur; an overdose slurs twice as much.</summary>
    public string Slur(string message, bool overdose)
    {
        var soften = overdose ? 0.2f : 0.1f;
        var drag = overdose ? 0.08f : 0.04f;
        var result = new StringBuilder(message.Length + 8);
        foreach (var c in message)
        {
            if (_random.Prob(soften))
            {
                var swap = char.ToLowerInvariant(c) switch
                {
                    'o' => "u",
                    's' => "sh",
                    'a' => "ah",
                    'u' => "oo",
                    'c' => "k",
                    _ => null,
                };

                if (swap != null)
                {
                    result.Append(char.IsUpper(c) ? char.ToUpperInvariant(swap[0]) + swap[1..] : swap);
                    continue;
                }
            }

            result.Append(c);
            if (char.IsLetter(c) && _random.Prob(drag))
                result.Append(char.ToLowerInvariant(c));
        }

        return result.ToString();
    }

    /// <summary>Puts pauses after some words and lets the end trail off; an overdose loses a few words too.</summary>
    public string Trail(string message, bool overdose)
    {
        var pause = overdose ? 0.4f : 0.25f;
        var drop = overdose ? 0.12f : 0f;
        var tokens = Token.Matches(message);
        var result = new StringBuilder();
        var from = 0;
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            var last = i == tokens.Count - 1;
            if (i > 0 && !last && !Ending.IsMatch(token.Value) && _random.Prob(drop))
            {
                from = token.Index + token.Length;
                continue;
            }

            result.Append(message, from, token.Index - from);
            from = token.Index + token.Length;

            if (last)
            {
                result.Append(Ending.Replace(token.Value, string.Empty)).Append("...");
                continue;
            }

            result.Append(token.Value);
            if (!Ending.IsMatch(token.Value) && _random.Prob(pause))
                result.Append("...");
        }

        result.Append(message, from, message.Length - from);
        return result.ToString();
    }

    /// <summary>
    /// Drops fillers between words and lets the end drift off ("..., man..."); an overdose fills more and often
    /// forgets where the sentence was going halfway through.
    /// </summary>
    public string Haze(string message, bool overdose)
    {
        var filler = overdose ? 0.2f : 0.1f;
        var tokens = Token.Matches(message).Select(m => m.Value).ToList();
        if (tokens.Count == 0)
            return message;

        if (overdose && tokens.Count >= 4 && _random.Prob(0.35f))
        {
            tokens.RemoveRange((tokens.Count + 1) / 2, tokens.Count / 2);
            tokens[^1] = $"{Ending.Replace(tokens[^1], string.Empty)}... {Pick("wf-drug-speech-forget", Forgets)}";
        }
        else if (_random.Prob(overdose ? 0.5f : 0.4f) && Ending.Replace(tokens[^1], string.Empty) is { Length: > 0 } end)
        {
            tokens[^1] = $"{end}, {Pick("wf-drug-speech-tail", Tails)}...";
        }

        var result = new StringBuilder(tokens[0]);
        for (var i = 1; i < tokens.Count; i++)
        {
            if (_random.Prob(filler))
                result.Append(' ').Append(Pick("wf-drug-speech-filler", Fillers));

            result.Append(' ').Append(tokens[i]);
        }

        return result.ToString();
    }

    /// <summary>
    /// Draws out the last vowel of some words ("sooo good") and softens full stops into "~"; an overdose stretches
    /// more and sometimes gushes a line on the end.
    /// </summary>
    public string Gush(string message, bool overdose)
    {
        var stretch = overdose ? 0.3f : 0.15f;
        var gushed = Token.Replace(message, match =>
        {
            if (!_random.Prob(stretch) || Vowels.Match(match.Value) is not { Success: true } vowel)
                return match.Value;

            var drawn = new string(vowel.Value[^1], _random.Next(2, 4));
            return match.Value.Insert(vowel.Index + vowel.Length, drawn);
        });

        gushed = FullStop.Replace(gushed, "~");
        if (overdose && _random.Prob(0.4f))
            gushed = $"{gushed} {Pick("wf-drug-speech-gush", Gushes)}";

        return gushed;
    }

    /// <summary>Swaps a few neighbouring words.</summary>
    public string Muddle(string message)
    {
        var tokens = Token.Matches(message).Select(m => m.Value).ToArray();
        for (var i = 0; i < tokens.Length - 1; i++)
        {
            if (!_random.Prob(0.15f))
                continue;

            (tokens[i], tokens[i + 1]) = (tokens[i + 1], tokens[i]);
            i++;
        }

        return string.Join(' ', tokens);
    }

    private string Pick(string prefix, int count)
    {
        return Loc.GetString($"{prefix}-{_random.Next(1, count + 1)}");
    }
}
