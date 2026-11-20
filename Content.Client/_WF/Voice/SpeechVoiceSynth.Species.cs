using System.Numerics;
using Content.Shared._WF.Voice;

namespace Content.Client._WF.Voice;

public sealed partial class SpeechVoiceSynth
{
    /// <summary>Voice-wide settings a species changes, blended from neutral by the filter strength.</summary>
    private struct Style
    {
        public float F0Scale;
        public float FormantScale;
        public float Rd;
        public float Tilt;
        public float Breath;
        public float Jitter;
        public float Shimmer;
        public float Growl;
        public float Wobble;
        public float SlotScale;
        public float FricationBoost;

        /// <summary>Alternates glottal periods strong and weak, putting a rough tone an octave down.</summary>
        public float Subharmonic;

        /// <summary>Scales the pitch step between words.</summary>
        public float PitchSpread;

        /// <summary>How far every vowel is pulled toward <see cref="VowelTarget"/>.</summary>
        public float VowelPull;

        public Vector3 VowelTarget;

        /// <summary>Lengthens s, sh, z and th.</summary>
        public float FricativeStretch;

        /// <summary>A fast pitch flutter on l and r.</summary>
        public float Trill;

        /// <summary>Depth of a 15 Hz loudness ripple on vowels.</summary>
        public float Bubble;

        /// <summary>Strength of a short feedback comb.</summary>
        public float Hollow;

        /// <summary>A 25 Hz flutter in loudness over the whole line.</summary>
        public float Purr;

        /// <summary>Widens the noise band of s, z and sh into a bright hiss.</summary>
        public float HissWidth;

        /// <summary>At half or more, each phrase ends on a long s.</summary>
        public float HissTail;

        /// <summary>Short damped sine pops on each vowel.</summary>
        public float Pops;

        /// <summary>Extra breath that rises and falls at 2.5 Hz.</summary>
        public float BreathGust;

        /// <summary>The hollow comb's delay in seconds, which sets its body note.</summary>
        public float HollowDelay;

        public static readonly Style Neutral = new()
        {
            F0Scale = 1f,
            FormantScale = 1f,
            Rd = 1f,
            Tilt = 1f,
            Breath = 0.05f,
            Jitter = 0.005f,
            Shimmer = 0.03f,
            SlotScale = 1f,
            FricationBoost = 1f,
            PitchSpread = 1f,
            FricativeStretch = 1f,
            HissWidth = 1f,
            HollowDelay = 0.0035f,
        };

        public static Style For(SpeechVoiceFilter filter, float strength)
        {
            var target = Neutral;

            switch (filter)
            {
                case SpeechVoiceFilter.Dog:
                    target.F0Scale = 0.7f;
                    target.FormantScale = 0.75f;
                    target.Rd = 0.7f;
                    target.Breath = 0.3f;
                    target.Jitter = 0.025f;
                    target.Shimmer = 0.08f;
                    target.Growl = 0.25f;
                    target.Subharmonic = 0.35f;
                    target.SlotScale = 0.9f;
                    target.VowelPull = 0.45f;
                    target.VowelTarget = Uh;
                    break;
                case SpeechVoiceFilter.Cat:
                    target.F0Scale = 1.3f;
                    target.FormantScale = 1.15f;
                    target.Rd = 1.2f;
                    target.Breath = 0.1f;
                    target.Purr = 1f;
                    break;
                case SpeechVoiceFilter.Bird:
                    target.F0Scale = 2.3f;
                    target.FormantScale = 1.6f;
                    target.Rd = 3f;
                    target.Tilt = 2.2f;
                    target.Breath = 0.02f;
                    target.Jitter = 0.002f;
                    target.SlotScale = 0.85f;
                    target.PitchSpread = 2f;
                    target.VowelPull = 0.4f;
                    target.VowelTarget = Ee;
                    target.Trill = 0.6f;
                    break;
                case SpeechVoiceFilter.Lizard:
                    target.F0Scale = 0.8f;
                    target.FormantScale = 0.9f;
                    target.Rd = 1.3f;
                    target.Breath = 0.12f;
                    target.FricationBoost = 1.8f;
                    target.FricativeStretch = 1.8f;
                    target.HissWidth = 2.2f;
                    target.HissTail = 1f;
                    target.PitchSpread = 0.3f;
                    target.SlotScale = 1.15f;
                    break;
                case SpeechVoiceFilter.Mouse:
                    target.F0Scale = 2.2f;
                    target.FormantScale = 1.6f;
                    target.Tilt = 1.2f;
                    target.Breath = 0.02f;
                    target.SlotScale = 0.65f;
                    break;
                case SpeechVoiceFilter.Slime:
                    target.F0Scale = 0.9f;
                    target.FormantScale = 0.9f;
                    target.Rd = 2.2f;
                    target.Tilt = 1.6f;
                    target.Wobble = 1.6f;
                    target.Bubble = 1f;
                    target.Pops = 1f;
                    break;
                case SpeechVoiceFilter.Plant:
                    target.F0Scale = 0.95f;
                    target.FormantScale = 0.95f;
                    target.Rd = 1.8f;
                    target.Tilt = 1.2f;
                    target.Breath = 0.12f;
                    target.BreathGust = 1f;
                    target.SlotScale = 1.2f;
                    target.VowelPull = 0.35f;
                    target.VowelTarget = Oo;
                    target.Hollow = 1f;
                    target.HollowDelay = 0.005f;
                    break;
            }

            return new Style
            {
                F0Scale = MathHelper.Lerp(Neutral.F0Scale, target.F0Scale, strength),
                FormantScale = MathHelper.Lerp(Neutral.FormantScale, target.FormantScale, strength),
                Rd = MathHelper.Lerp(Neutral.Rd, target.Rd, strength),
                Tilt = MathHelper.Lerp(Neutral.Tilt, target.Tilt, strength),
                Breath = MathHelper.Lerp(Neutral.Breath, target.Breath, strength),
                Jitter = MathHelper.Lerp(Neutral.Jitter, target.Jitter, strength),
                Shimmer = MathHelper.Lerp(Neutral.Shimmer, target.Shimmer, strength),
                Growl = MathHelper.Lerp(Neutral.Growl, target.Growl, strength),
                Wobble = MathHelper.Lerp(Neutral.Wobble, target.Wobble, strength),
                SlotScale = MathHelper.Lerp(Neutral.SlotScale, target.SlotScale, strength),
                FricationBoost = MathHelper.Lerp(Neutral.FricationBoost, target.FricationBoost, strength),
                Subharmonic = MathHelper.Lerp(Neutral.Subharmonic, target.Subharmonic, strength),
                PitchSpread = MathHelper.Lerp(Neutral.PitchSpread, target.PitchSpread, strength),
                VowelPull = MathHelper.Lerp(Neutral.VowelPull, target.VowelPull, strength),
                VowelTarget = target.VowelTarget,
                FricativeStretch = MathHelper.Lerp(Neutral.FricativeStretch, target.FricativeStretch, strength),
                Trill = MathHelper.Lerp(Neutral.Trill, target.Trill, strength),
                Bubble = MathHelper.Lerp(Neutral.Bubble, target.Bubble, strength),
                Hollow = MathHelper.Lerp(Neutral.Hollow, target.Hollow, strength),
                Purr = MathHelper.Lerp(Neutral.Purr, target.Purr, strength),
                HissWidth = MathHelper.Lerp(Neutral.HissWidth, target.HissWidth, strength),
                HissTail = MathHelper.Lerp(Neutral.HissTail, target.HissTail, strength),
                Pops = MathHelper.Lerp(Neutral.Pops, target.Pops, strength),
                BreathGust = MathHelper.Lerp(Neutral.BreathGust, target.BreathGust, strength),
                HollowDelay = MathHelper.Lerp(Neutral.HollowDelay, target.HollowDelay, strength),
            };
        }
    }

    /// <summary>The consonant a species puts before words that start with a vowel.</summary>
    private static Consonant WordOnsetFor(SpeechVoiceFilter filter)
    {
        return filter switch
        {
            SpeechVoiceFilter.Dog => R,
            SpeechVoiceFilter.Cat => M,
            _ => default,
        };
    }

    /// <summary>How a species bends one point of a word: pitch multiplier and a pull on the formants.</summary>
    private struct Shape
    {
        public float F0;

        /// <summary>Multiplies the first formant.</summary>
        public float F1;

        /// <summary>Multiplies the second formant.</summary>
        public float F2;

        public float Pull;
        public Vector3 Formants;
    }

    /// <summary>A species' pitch and formant bend at one point of a word.</summary>
    /// <param name="s">The filter strength, 0 to 1.</param>
    /// <param name="t">Position in the letter, 0 to 1.</param>
    /// <param name="wordT">Position in the word, 0 to 1.</param>
    private static Shape ShapeFor(SpeechVoiceFilter filter, float s, float t, float wordT)
    {
        var shape = new Shape { F0 = 1f, F1 = 1f, F2 = 1f };

        switch (filter)
        {
            case SpeechVoiceFilter.Dog:
                shape.F0 = MathHelper.Lerp(1f, 1.05f - 0.25f * t, s);
                break;
            case SpeechVoiceFilter.Cat:
                shape.F0 = MathHelper.Lerp(1f, 0.85f + 0.45f * MathF.Sin(MathF.PI * wordT), s);
                shape.Pull = 0.7f * s;
                shape.Formants = wordT < 0.4f
                    ? Vector3.Lerp(new Vector3(300, 2300, 3000), new Vector3(800, 1300, 2600), wordT / 0.4f)
                    : Vector3.Lerp(new Vector3(800, 1300, 2600), new Vector3(450, 900, 2300), (wordT - 0.4f) / 0.6f);
                break;
            case SpeechVoiceFilter.Bird:
                shape.F0 = MathHelper.Lerp(1f, 1f + 0.35f * t, s);
                break;
            case SpeechVoiceFilter.Mouse:
                shape.F0 = MathHelper.Lerp(1f, 0.75f + 0.3f * (1f - MathF.Exp(-t / 0.12f)) + 0.15f * t, s);
                break;
            case SpeechVoiceFilter.Slime:
                shape.F0 = MathHelper.Lerp(1f, 1f + 0.5f * t, s);
                shape.F1 = MathHelper.Lerp(1f, t < 0.33f ? 0.55f + 0.45f * t / 0.33f : 1f, s);
                shape.F2 = shape.F1;
                break;
            case SpeechVoiceFilter.Plant:
                shape.F0 = MathHelper.Lerp(1f,
                    1f - 0.12f * MathF.Sin(MathF.PI * wordT) + 0.03f * MathF.Sin(MathF.Tau * 2f * wordT), s);
                break;
        }

        return shape;
    }
}
