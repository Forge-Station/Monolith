using System.Numerics;

namespace Content.Client._WF.Voice;

public sealed partial class SpeechVoiceSynth
{
    private enum SoundKind : byte
    {
        None,
        Plosive,
        Affricate,
        Fricative,
        Nasal,
        Liquid,
        Aspirate,
    }

    /// <summary>A consonant: its formant locus for the voiced part, and a noise band for hiss and bursts.</summary>
    private struct Consonant
    {
        public SoundKind Kind;
        public float F1;
        public float F2;
        public float F3;
        public float NoiseFrequency;
        public float NoiseBandwidth;
        public float NoiseGain;
        public bool Voiced;
    }

    /// <summary>One letter or digit: an onset, a vowel that may glide, a coda. A gap is a pause instead.</summary>
    private struct Blip
    {
        public bool Gap;

        /// <summary>Length in letter slots.</summary>
        public float Length;

        public Consonant Onset;
        public Vector3 VowelStart;
        public Vector3 VowelEnd;
        public Consonant Coda;

        /// <summary>Position in its phrase, 0 at the start and 1 at the end.</summary>
        public float PhrasePosition;

        /// <summary>The punctuation that ends its phrase: '.', '?', '!', ',' or '…'.</summary>
        public char Ending;

        /// <summary>Loudness against other letters, up for shouts, down as an ellipsis trails off.</summary>
        public float Gain;

        /// <summary>Pitch multiplier, raised for shouted words.</summary>
        public float Lift;

        /// <summary>A shouted word, sung with a pressed, brighter source.</summary>
        public bool Shout;

        /// <summary>Index of its word, so pitch steps once per word.</summary>
        public int Word;

        /// <summary>The last letter of its phrase, where ? lifts the pitch and ! the loudness.</summary>
        public bool Last;

        /// <summary>A consonant on its own with no vowel, such as a trailing hiss.</summary>
        public bool ConsonantOnly;

        /// <summary>Share of the slot the onset takes, or 0 for the default of its kind.</summary>
        public float OnsetShare;
    }

    // Vowel formants in Hz, F1 to F3, from Peterson and Barney 1952.
    private static readonly Vector3 Ee = new(290, 2540, 3160);
    private static readonly Vector3 Ih = new(410, 2230, 2810);
    private static readonly Vector3 Eh = new(570, 2090, 2730);
    private static readonly Vector3 Ah = new(700, 1220, 2600);
    private static readonly Vector3 Uh = new(640, 1190, 2390);
    private static readonly Vector3 Oh = new(570, 840, 2410);
    private static readonly Vector3 Uu = new(440, 1020, 2240);
    private static readonly Vector3 Oo = new(335, 910, 2455);

    // Consonant loci from Klatt 1980.
    private static readonly Consonant B = Make(SoundKind.Plosive, 300, 900, 2300, 900, 1500, 1f, true);
    private static readonly Consonant P = Make(SoundKind.Plosive, 300, 900, 2300, 900, 1500, 1.6f);
    private static readonly Consonant D = Make(SoundKind.Plosive, 300, 1700, 2600, 4500, 2500, 1f, true);
    private static readonly Consonant T = Make(SoundKind.Plosive, 300, 1700, 2600, 4500, 2500, 1.6f);
    private static readonly Consonant G = Make(SoundKind.Plosive, 300, 2000, 2600, 2200, 1000, 1f, true);
    private static readonly Consonant K = Make(SoundKind.Plosive, 300, 2000, 2600, 2200, 1000, 1.6f);
    private static readonly Consonant J = Make(SoundKind.Affricate, 300, 2000, 2700, 2800, 1500, 0.35f, true);
    private static readonly Consonant S = Make(SoundKind.Fricative, 300, 1700, 2600, 6000, 2000, 0.6f);
    private static readonly Consonant Z = Make(SoundKind.Fricative, 300, 1700, 2600, 6000, 2000, 0.3f, true);
    private static readonly Consonant F = Make(SoundKind.Fricative, 400, 1100, 2400, 7000, 4000, 0.2f);
    private static readonly Consonant V = Make(SoundKind.Fricative, 400, 1100, 2400, 7000, 4000, 0.12f, true);
    private static readonly Consonant Th = Make(SoundKind.Fricative, 400, 1400, 2600, 5000, 4000, 0.4f);
    private static readonly Consonant H = Make(SoundKind.Aspirate, 640, 1190, 2390);
    private static readonly Consonant M = Make(SoundKind.Nasal, 250, 1000, 2300);
    private static readonly Consonant N = Make(SoundKind.Nasal, 250, 1700, 2600);
    private static readonly Consonant L = Make(SoundKind.Liquid, 360, 1300, 2700);
    private static readonly Consonant R = Make(SoundKind.Liquid, 420, 1300, 1600);
    private static readonly Consonant W = Make(SoundKind.Liquid, 300, 610, 2200);
    private static readonly Consonant Y = Make(SoundKind.Liquid, 290, 2400, 3000);
    private static readonly Consonant Silent = default;

    /// <summary>Burst noise raised for a stop said alone, so b and d don't vanish.</summary>
    private const float LetterBurst = 1.8f;

    /// <summary>The blip for one lower-case letter, said by its sound. A y opening a word is a consonant.</summary>
    private static Blip Letter(char c, bool wordStart)
    {
        return c switch
        {
            'a' => Syllable(Silent, Ah),
            'e' => Syllable(Silent, Eh),
            'i' => Syllable(Silent, Ee),
            'o' => Syllable(Silent, Oh),
            'u' => Syllable(Silent, Oo),
            'y' => wordStart ? Glide(Y) : Syllable(Silent, Ih),
            's' => Held(S),
            'z' => Held(Z),
            'f' => Held(F),
            'v' => Held(V),
            'm' => Held(M),
            'n' => Held(N),
            'l' => Glide(L),
            'r' => Glide(R),
            'w' => Glide(W),
            'h' => Glide(H),
            'b' => Stop(B),
            'p' => Stop(P),
            'd' => Stop(D),
            't' => Stop(T),
            'g' => Stop(G),
            'j' => Stop(J),
            'x' => Syllable(Loud(K), Uh, Uh, S),
            _ => Stop(K),
        };
    }

    /// <summary>A consonant for most of the blip and a short "uh" to the cut, such as "s-" or "m-".</summary>
    private static Blip Held(Consonant consonant)
    {
        var blip = Syllable(consonant, Uh);
        blip.OnsetShare = 0.6f;
        return blip;
    }

    /// <summary>A long consonant with a short "uh" after it, for sounds that need a vowel to be heard.</summary>
    private static Blip Glide(Consonant consonant)
    {
        var blip = Syllable(consonant, Uh);
        blip.OnsetShare = 0.45f;
        return blip;
    }

    /// <summary>A loud burst and a clipped "uh".</summary>
    private static Blip Stop(Consonant consonant)
    {
        var blip = Syllable(Loud(consonant), Uh);
        blip.OnsetShare = 0.35f;
        return blip;
    }

    private static Consonant Loud(Consonant consonant)
    {
        consonant.NoiseGain *= LetterBurst;
        return consonant;
    }

    /// <summary>Each digit as one blip: its number word squeezed into a syllable.</summary>
    private static readonly Blip[] Digits =
    {
        Syllable(Z, Ee, Oh),
        Syllable(W, Uh, Uh, N),
        Syllable(T, Oo),
        Syllable(Th, Ee),
        Syllable(F, Oh, Oh, R),
        Syllable(F, Ah, Ih, V),
        Syllable(S, Ih, Ih, S),
        Syllable(S, Eh, Eh, V),
        Syllable(Silent, Eh, Ih, T),
        Syllable(N, Ah, Ih, N),
    };

    private static Consonant Make(SoundKind kind,
        float f1,
        float f2,
        float f3,
        float noiseFrequency = 0f,
        float noiseBandwidth = 0f,
        float noiseGain = 0f,
        bool voiced = false)
    {
        return new Consonant
        {
            Kind = kind,
            F1 = f1,
            F2 = f2,
            F3 = f3,
            NoiseFrequency = noiseFrequency,
            NoiseBandwidth = noiseBandwidth,
            NoiseGain = noiseGain,
            Voiced = voiced,
        };
    }

    private static Blip Syllable(Consonant onset, Vector3 vowel)
    {
        return Syllable(onset, vowel, vowel, Silent);
    }

    private static Blip Syllable(Consonant onset, Vector3 vowelStart, Vector3 vowelEnd, Consonant coda = default)
    {
        return new Blip
        {
            Length = 1f,
            Onset = onset,
            VowelStart = vowelStart,
            VowelEnd = vowelEnd,
            Coda = coda,
            Gain = 1f,
            Lift = 1f,
        };
    }

    /// <summary>Loudness of the voiced, frication and breath parts of a consonant at a point inside it.</summary>
    private static (float Voice, float Frication, float Aspiration) ConsonantGains(in Consonant sound, float u)
    {
        switch (sound.Kind)
        {
            case SoundKind.Plosive:
                if (u < 0.4f)
                    return (sound.Voiced ? 0.1f : 0f, 0f, 0f);
                if (u < 0.75f)
                    return (0f, 1f, 0f);
                return sound.Voiced ? (0.7f, 0f, 0f) : (0f, 0f, 0.6f);
            case SoundKind.Affricate:
                if (u < 0.25f)
                    return (sound.Voiced ? 0.1f : 0f, 0f, 0f);
                return (sound.Voiced ? 0.35f : 0f, 1f, 0f);
            case SoundKind.Fricative:
                return (sound.Voiced ? 0.35f : 0f, 1f, 0f);
            case SoundKind.Nasal:
                return (0.7f, 0f, 0f);
            case SoundKind.Liquid:
                return (0.85f, 0f, 0f);
            case SoundKind.Aspirate:
                return (0f, 0f, 1f);
            default:
                return (0f, 0f, 0f);
        }
    }

    /// <summary>Share of the slot an onset takes. The vowel gets the rest.</summary>
    private static float OnsetLength(SoundKind kind)
    {
        return kind switch
        {
            SoundKind.Plosive => 0.25f,
            SoundKind.Affricate => 0.33f,
            SoundKind.Fricative => 0.33f,
            SoundKind.Nasal => 0.25f,
            SoundKind.Liquid => 0.25f,
            SoundKind.Aspirate => 0.25f,
            _ => 0f,
        };
    }
}
