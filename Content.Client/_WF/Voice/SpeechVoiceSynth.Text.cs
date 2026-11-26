namespace Content.Client._WF.Voice;

public sealed partial class SpeechVoiceSynth
{
    // Pauses, in letter slots.
    private const float WordGap = 1f;
    private const float CommaGap = 6f;
    private const float SentenceGap = 12f;

    /// <summary>The blips for a message, one per letter or digit, with phrases split at punctuation.</summary>
    private static List<Blip> Blips(string text)
    {
        var blips = new List<Blip>();
        var phraseStart = 0;
        var wordStart = -1;
        var lastWordStart = 0;
        var letters = 0;
        var upper = 0;
        var wordIndex = 0;

        void AddGap(float length)
        {
            if (blips.Count == 0)
                return;

            if (blips[^1].Gap)
            {
                var gap = blips[^1];
                gap.Length = MathF.Max(gap.Length, length);
                blips[^1] = gap;
                return;
            }

            blips.Add(new Blip { Gap = true, Length = length });
        }

        void Add(Blip blip)
        {
            if (wordStart < 0)
                wordStart = blips.Count;

            blip.Word = wordIndex;
            blips.Add(blip);
        }

        // A word of two or more letters, all capitals, is shouted.
        void EndWord()
        {
            if (wordStart < 0)
                return;

            if (letters >= 2 && upper == letters)
            {
                for (var i = wordStart; i < blips.Count; i++)
                {
                    var blip = blips[i];
                    blip.Shout = true;
                    blip.Gain = 1.4f;
                    blip.Lift = 1.15f;
                    blips[i] = blip;
                }
            }

            lastWordStart = wordStart;
            wordStart = -1;
            letters = upper = 0;
            wordIndex++;
            AddGap(WordGap);
        }

        void EndPhrase(char ending, float gap)
        {
            EndWord();

            var end = blips.Count > 0 && blips[^1].Gap ? blips.Count - 1 : blips.Count;
            if (end <= phraseStart)
            {
                AddGap(gap);
                return;
            }

            for (var i = phraseStart; i < end; i++)
            {
                var blip = blips[i];
                blip.PhrasePosition = end - phraseStart > 1 ? (i - phraseStart) / (float) (end - phraseStart - 1) : 0f;
                blip.Ending = ending;
                blip.Last = i == end - 1;
                blips[i] = blip;
            }

            if (ending == '…')
            {
                var count = end - lastWordStart;
                for (var i = lastWordStart; i < end; i++)
                {
                    var blip = blips[i];
                    blip.Length = 1.3f;
                    blip.Gain *= 1f - 0.6f * (i - lastWordStart + 1) / count;
                    blips[i] = blip;
                }
            }

            AddGap(gap);
            phraseStart = blips.Count;
        }

        var index = 0;
        while (index < text.Length && blips.Count < MaxBlips)
        {
            var c = text[index];

            if (char.IsAsciiLetter(c))
            {
                var end = index;
                while (end < text.Length && char.IsAsciiLetter(text[end]))
                {
                    end++;
                }

                for (var i = index; i < end; i++)
                {
                    Add(Letter(char.ToLowerInvariant(text[i]), i == index));
                    letters++;
                    if (char.IsAsciiLetterUpper(text[i]))
                        upper++;
                }

                index = end;
                continue;
            }

            if (char.IsAsciiDigit(c))
            {
                EndWord();
                Add(Digits[c - '0']);
                EndWord();
                index++;
                continue;
            }

            switch (c)
            {
                case ',':
                case ';':
                case ':':
                    EndPhrase(',', CommaGap);
                    break;
                case '?':
                    EndPhrase('?', SentenceGap);
                    break;
                case '!':
                    EndPhrase('!', SentenceGap);
                    break;
                case '…':
                    EndPhrase('…', SentenceGap);
                    break;
                case '.':
                    if (index + 2 < text.Length && text[index + 1] == '.' && text[index + 2] == '.')
                    {
                        EndPhrase('…', SentenceGap);
                        index += 2;
                    }
                    else
                    {
                        EndPhrase('.', SentenceGap);
                    }

                    break;
                default:
                    EndWord();
                    break;
            }

            index++;
        }

        EndPhrase('.', 0f);

        if (blips.Count > MaxBlips)
            blips.RemoveRange(MaxBlips, blips.Count - MaxBlips);

        while (blips.Count > 0 && blips[^1].Gap)
        {
            blips.RemoveAt(blips.Count - 1);
        }

        return blips;
    }
}
