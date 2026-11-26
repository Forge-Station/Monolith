using System.Numerics;
using Content.Shared._WF.Voice;

namespace Content.Client._WF.Voice;

/// <summary>Builds a line of babble from code alone, one short blip per letter.</summary>
// Voiced sound is a sum of harmonics shaped by formant curves over an LF glottal spectrum. Hiss and bursts are
// filtered noise.
public sealed partial class SpeechVoiceSynth
{
    /// <summary>Output samples per second.</summary>
    public const int SampleRate = 32000;

    /// <summary>The most samples a line can take: the four second cap and the Ghost filter's tail.</summary>
    public const int MaxSamples = (int) (MaxSeconds * SampleRate) + GhostTail;

    private const int MaxBlips = 64;
    private const float MaxSeconds = 4f;
    private const int GhostTail = SampleRate / 2;
    private const float BaseF0 = 220f;
    private const float BaseFormantScale = 1.35f;
    private const float TremoloRate = 7f;
    private const int Block = 32;
    private const int MaxHarmonics = 64;
    private const float MaxFrequency = 0.45f * SampleRate;
    private const float TargetPeak = 0.6f;
    private const float RadioDrive = 1.5f;
    private const float WordFadeSeconds = 0.012f;
    private const float PhraseDeclination = 0.08f;
    private const float Coarticulation = 0.4f;

    /// <summary>Letter slot against the Speed setting, so letters run short and chopped.</summary>
    private const float LetterScale = 0.5f;

    /// <summary>Loudness of a pop against the voiced part.</summary>
    private const float PopLevel = 0.25f;

    private const int MaxPops = 4;

    // Levels of the noise parts against the voiced part, which is normalised to an RMS of about 0.7.
    private const float FricationLevel = 1.2f;
    private const float AspirationLevel = 0.5f;
    private const float BreathLevel = 0.6f;

    private NoiseSource _noise;

    /// <summary>
    /// Renders a message into <see cref="SpeechVoiceBuffers.Pcm"/>, capped at four seconds, and returns how many
    /// samples it wrote. Zero when it has no letters or digits.
    /// </summary>
    /// <param name="radio">Plays it as heard through a radio, on top of any filter.</param>
    public int Render(string text, SpeechVoice voice, bool radio, SpeechVoiceBuffers buffers)
    {
        var blips = Blips(text);
        if (blips.Count == 0)
            return 0;

        // Seeded from the text, so every listener renders the same line the same way.
        _noise = new NoiseSource((int) SpeechVoice.Hash(text));
        var style = Style.For(voice.Filter, voice.FilterStrength);

        var wordOnset = WordOnsetFor(voice.Filter);
        if (wordOnset.Kind != SoundKind.None && voice.FilterStrength >= 0.5f)
        {
            var previousWord = -1;
            for (var i = 0; i < blips.Count; i++)
            {
                var blip = blips[i];
                if (blip.Gap || blip.Word == previousWord)
                    continue;

                previousWord = blip.Word;
                if (blip.Onset.Kind != SoundKind.None || blip.ConsonantOnly)
                    continue;

                blip.Onset = wordOnset;
                blips[i] = blip;
            }
        }

        for (var i = blips.Count - 1; i >= 0; i--)
        {
            var blip = blips[i];
            if (style.HissTail >= 0.5f && blip.Last && blip.Ending != ',' && !blip.Gap && blips.Count < MaxBlips)
            {
                var hiss = Syllable(S, Uh);
                hiss.ConsonantOnly = true;
                hiss.Length = 0.8f * style.FricativeStretch;
                hiss.Word = blip.Word;
                hiss.Gain = 0.7f * blip.Gain;
                blips.Insert(i + 1, hiss);
            }
        }

        var slot = (int) (voice.Speed * style.SlotScale * LetterScale * SampleRate);
        var starts = new int[blips.Count + 1];
        for (var i = 0; i < blips.Count; i++)
        {
            starts[i + 1] = starts[i] + (int) (slot * blips[i].Length);
        }

        var tail = voice.Filter == SpeechVoiceFilter.Ghost ? GhostTail : 0;
        var length = Math.Min(starts[^1], (int) (MaxSeconds * SampleRate));
        var buffer = buffers.Samples.AsSpan(0, length + tail);
        buffer.Clear();

        Speak(buffer, length, blips, starts, voice, style);

        if (style.Hollow > 0f)
        {
            var delay = (int) (style.HollowDelay * SampleRate);
            var feedback = 0.3f * style.Hollow;
            var wet = 0.5f * style.Hollow;
            var ring = new float[delay];
            for (var i = 0; i < length; i++)
            {
                var combed = buffer[i] + (i >= delay ? feedback * ring[i % delay] : 0f);
                ring[i % delay] = combed;
                buffer[i] += (combed - buffer[i]) * wet;
            }
        }

        // A line cut at the cap fades out over 20 ms rather than stopping with a click.
        if (starts[^1] > length)
        {
            var fade = Math.Min(length, SampleRate / 50);
            for (var i = 0; i < fade; i++)
            {
                buffer[length - 1 - i] *= i / (float) fade;
            }
        }

        if (voice.Tremolo > 0f)
        {
            for (var i = 0; i < buffer.Length; i++)
            {
                var wobble = 0.5f * (1f + MathF.Sin(MathF.Tau * TremoloRate * i / SampleRate));
                buffer[i] *= 1f - voice.Tremolo * wobble;
            }
        }

        if (voice.FilterStrength > 0f)
            ApplyEffect(buffer, voice.Filter, voice.FilterStrength, buffers);

        if (radio)
            Radio(buffer);

        ToPcm(buffer, buffers.Pcm);
        return buffer.Length;
    }

    /// <summary>Renders every letter into the buffer, each its own blip.</summary>
    private void Speak(Span<float> buffer,
        int length,
        List<Blip> blips,
        int[] starts,
        SpeechVoice voice,
        Style style)
    {
        var filter = voice.Filter;
        var s = voice.FilterStrength;
        var pitch = voice.Pitch * style.F0Scale;
        var formantScale = BaseFormantScale * voice.Pitch * style.FormantScale;

        var tilt = new float[MaxHarmonics + 1];
        var shoutTilt = new float[MaxHarmonics + 1];
        for (var k = 1; k <= MaxHarmonics; k++)
        {
            tilt[k] = LfHarmonic(k, style.Rd) * MathF.Pow(k, 1f - style.Tilt);
            shoutTilt[k] = LfHarmonic(k, style.Rd * 0.7f) * MathF.Pow(k, 1f - style.Tilt);
        }

        var ampFrom = new float[MaxHarmonics + 1];
        var ampTo = new float[MaxHarmonics + 1];
        var harmonics = 0;

        var f0Glide = Smoothing(0.008f);
        var gainGlide = Smoothing(0.0015f);
        var formantGlide = 1f - MathF.Exp(-Block / (0.008f * SampleRate));
        var attack = 0.003f * SampleRate;
        var fade = 0.004f * SampleRate;

        var formants = Eh * formantScale;
        var f0 = BaseF0 * pitch;
        var f0Target = f0;
        var frication = new NoiseBand();
        var breath = new NoiseBand();

        var voiceGain = 0f;
        var fricationGain = 0f;
        var aspirationGain = 0f;
        var phase = 0f;
        var periodRate = 1f;
        var periodAmp = 1f;
        var alternate = false;
        var jitter = 0f;
        var vibrato = 0f;
        var vibratoStep = MathF.Tau * voice.VibratoRate / SampleRate;
        var wobblePhase = 0f;

        var words = 0;
        foreach (var blip in blips)
        {
            words = Math.Max(words, blip.Word + 1);
        }

        var wordFrom = new int[words];
        var wordTo = new int[words];
        for (var b = 0; b < blips.Count; b++)
        {
            if (blips[b].Gap)
                continue;

            var w = blips[b].Word;
            if (wordTo[w] == 0)
                wordFrom[w] = starts[b];
            wordTo[w] = starts[b + 1];
        }
        var letterPitch = 1f;
        var popStart = new int[MaxPops];
        var popFrequency = new float[MaxPops];
        var popDecay = new float[MaxPops];
        var popEnd = new int[MaxPops];
        var popFade = new float[MaxPops];
        var popAmp = new float[MaxPops];

        // A slow pitch and loudness swing, each cycle 0.33 to 0.47 s long.
        var swingPhase = 0f;
        var swingStep = MathF.Tau / (0.4f * SampleRate);
        var swingDepth = 0.03f + 0.08f * voice.Intonation;

        for (var b = 0; b < blips.Count; b++)
        {
            var start = starts[b];
            if (start >= length)
                break;

            var blip = blips[b];
            var slotLength = starts[b + 1] - start;
            var end = Math.Min(starts[b + 1], length);

            if (blip.Gap)
            {
                voiceGain = fricationGain = aspirationGain = 0f;
                continue;
            }

            var blipTilt = blip.Shout ? shoutTilt : tilt;

            // Each letter gets its own note, kept a step away from the last one.
            var spread = 0.7f * voice.PitchVariance * style.PitchSpread;
            var note = 1f + spread * _noise.Next();
            var step = 0.02f * style.PitchSpread;
            if (MathF.Abs(note - letterPitch) < step)
                note = letterPitch + (note >= letterPitch ? step : -step);
            letterPitch = note;

            var blipGain = blip.Gain;
            var lastLift = 1f;
            if (blip.Last && blip.Ending == '?')
                lastLift = 1.13f + 0.17f * MathF.Min(1f, voice.Intonation / SpeechVoice.MaxIntonation);
            if (blip.Last && blip.Ending == '!')
                blipGain *= 1.25f;

            var blipF0 = BaseF0 * pitch * letterPitch * Contour(blip, voice.Intonation) * blip.Lift * lastLift;
            var blipFormants = 1f + 0.03f * _noise.Next();
            var slotSeconds = slotLength / (float) SampleRate;

            // Micro-prosody: pitch starts a little high after a voiceless consonant, a little low after a voiced one.
            var bump = blip.Onset.Kind == SoundKind.None ? 0f : blip.Onset.Voiced ? -0.03f : 0.05f;
            var onsetEnd = blip.ConsonantOnly ? 0.8f
                : blip.OnsetShare > 0f ? blip.OnsetShare
                : OnsetLength(blip.Onset.Kind);
            if (blip.Onset.Kind is SoundKind.Fricative or SoundKind.Affricate && !blip.ConsonantOnly)
                onsetEnd = MathF.Min(0.7f, onsetEnd * style.FricativeStretch);
            var trilled = blip.Onset.Kind == SoundKind.Liquid || blip.Coda.Kind == SoundKind.Liquid;
            var wordStart = wordFrom[blip.Word];
            var wordLength = MathF.Max(1f, wordTo[blip.Word] - wordFrom[blip.Word]);
            var hasCoda = blip.Coda.Kind != SoundKind.None;
            var codaStart = hasCoda ? 0.5f : 1f;
            var noiseInCoda = false;
            var snap = true;
            var endsWord = b + 1 >= blips.Count || blips[b + 1].Gap;
            var wordFade = MathF.Min(WordFadeSeconds * SampleRate, 0.5f * slotLength);
            wobblePhase = MathF.Tau * (0.5f + 0.5f * _noise.Next());

            if (blip.Onset.NoiseGain > 0f)
                TuneNoise(ref frication, blip.Onset, style);

            // Small bubble pops on the vowel: damped sines that rise as they fade (van den Doel 2005).
            var popCount = 0;
            if (style.Pops > 0f && !blip.ConsonantOnly)
            {
                popCount = Math.Clamp(3 + (int) MathF.Round(_noise.Next()), 2, MaxPops);
                for (var k = 0; k < popCount; k++)
                {
                    popStart[k] = start + (int) (slotLength * (0.25f + 0.25f * (_noise.Next() + 1f)));
                    popFrequency[k] = 1150f + 650f * _noise.Next();
                    popDecay[k] = 0.014f + 0.006f * _noise.Next();
                    popEnd[k] = popStart[k] + (int) (4.6f * popDecay[k] * SampleRate);
                    popFade[k] = MathF.Exp(-1f / (popDecay[k] * SampleRate));
                }
            }

            for (var i = start; i < end; i++)
            {
                var t = (i - start) / (float) slotLength;

                Consonant sound;
                float u;
                Vector3 target;
                float voiced, fric, aspiration;

                if (t < onsetEnd)
                {
                    sound = blip.Onset;
                    u = t / onsetEnd;
                    target = Vector3.Lerp(new Vector3(sound.F1, sound.F2, sound.F3), blip.VowelStart, Coarticulation);
                    (voiced, fric, aspiration) = ConsonantGains(sound, u);
                }
                else if (t < codaStart)
                {
                    sound = blip.Onset;
                    u = (t - onsetEnd) / (codaStart - onsetEnd);
                    target = Vector3.Lerp(Vector3.Lerp(blip.VowelStart, blip.VowelEnd, u), style.VowelTarget,
                        style.VowelPull);
                    (voiced, fric, aspiration) = blip.ConsonantOnly ? (0f, 0f, 0f) : (1f, 0f, 0f);
                }
                else
                {
                    sound = blip.Coda;
                    u = (t - codaStart) / (1f - codaStart);
                    target = new Vector3(sound.F1, sound.F2, sound.F3);
                    (voiced, fric, aspiration) = ConsonantGains(sound, 0.5f + 0.5f * u);

                    if (!noiseInCoda && sound.NoiseGain > 0f)
                    {
                        TuneNoise(ref frication, sound, style);
                        noiseInCoda = true;
                    }
                }

                // Cut off at the end of the slot with a short fade, so letters run back to back.
                var envelope = MathF.Min(1f, MathF.Min((i - start) / attack, (start + slotLength - i) / fade));

                // The last letter of a word has silence after it, where a sharp cut clicks, so it eases out for longer.
                if (endsWord)
                {
                    var ease = MathF.Min(1f, (start + slotLength - i) / wordFade);
                    envelope = MathF.Min((i - start) / attack, ease * ease * (3f - 2f * ease));
                    envelope = MathF.Min(1f, envelope);
                }

                envelope *= blipGain;

                if (i % Block == 0)
                {
                    var wordT = Math.Clamp((i - wordStart) / wordLength, 0f, 1f);
                    var shape = ShapeFor(filter, s, t, wordT);
                    var mouth = Vector3.Lerp(target, shape.Formants, shape.Pull) * (formantScale * blipFormants);
                    if (style.Wobble > 0f)
                    {
                        var ripple = 1f + 0.12f * style.Wobble
                            * MathF.Sin(MathF.Tau * 9f * i / SampleRate + wobblePhase);
                        mouth.X *= ripple;
                        mouth.Y *= 2f - ripple;
                    }

                    mouth.X *= shape.F1;
                    mouth.Y *= shape.F2;

                    // A new letter starts on its own mouth shape, so neighbours don't blur.
                    formants = snap ? mouth : formants + (mouth - formants) * formantGlide;
                    snap = false;
                    Array.Copy(ampTo, ampFrom, ampTo.Length);
                    harmonics = Harmonics(ampTo, blipTilt, f0, formants, formantScale);
                    breath.Tune(MathF.Min(formants.Z, MaxFrequency), 3000f * formantScale, SampleRate);
                    var sinceVowel = MathF.Max(0f, t - onsetEnd) * slotSeconds;
                    f0Target = blipF0 * shape.F0 * (1f + bump * MathF.Exp(-sinceVowel / 0.03f));
                }

                f0 += (f0Target - f0) * f0Glide;
                vibrato += vibratoStep;
                swingPhase += swingStep;
                if (swingPhase >= MathF.Tau)
                {
                    swingPhase -= MathF.Tau;
                    swingStep = MathF.Tau / ((0.4f + 0.07f * _noise.Next()) * SampleRate);
                }

                var swing = swingDepth * MathF.Sin(swingPhase);
                var currentF0 = f0 * (1f + swing);
                if (voice.VibratoDepth > 0f)
                    currentF0 *= 1f + voice.VibratoDepth * MathF.Sin(vibrato);
                var growl = style.Growl > 0f ? MathF.Sin(MathF.Tau * 40f * i / SampleRate) : 0f;
                if (style.Growl > 0f)
                    currentF0 *= 1f + 0.04f * style.Growl * growl;
                if (style.Trill > 0f && trilled)
                    currentF0 *= 1f + 0.08f * style.Trill * MathF.Sin(MathF.Tau * 25f * i / SampleRate);

                phase += currentF0 * periodRate / SampleRate;
                if (phase >= 1f)
                {
                    phase -= 1f;
                    jitter = 0.7f * jitter + 0.3f * _noise.Next();
                    periodRate = 1f + style.Jitter * 2f * jitter;
                    periodAmp = 1f + style.Shimmer * _noise.Next();
                    alternate = !alternate;
                    if (alternate)
                        periodAmp *= MathF.Max(0f, 1f - style.Subharmonic);
                }

                // Sum of harmonics by the sine recurrence sin(k x) = 2 cos(x) sin((k-1) x) - sin((k-2) x).
                var x = MathF.Tau * phase;
                var cos2 = 2f * MathF.Cos(x);
                var current = MathF.Sin(x);
                var previous = 0f;
                var mix = (i % Block) / (float) Block;
                var harmonic = 0f;
                for (var k = 1; k <= harmonics; k++)
                {
                    harmonic += (ampFrom[k] + (ampTo[k] - ampFrom[k]) * mix) * current;
                    var next = cos2 * current - previous;
                    previous = current;
                    current = next;
                }

                voiceGain += (voiced - voiceGain) * gainGlide;
                fricationGain += (fric - fricationGain) * gainGlide;
                aspirationGain += (aspiration - aspirationGain) * gainGlide;

                var noise = _noise.Next();
                var boost = sound.NoiseFrequency >= 2500f ? style.FricationBoost : 1f;
                var breathNoise = breath.Process(noise);

                // Breath rides on the open phase of each glottal cycle (Klatt and Klatt 1990).
                var open = phase < 0.6f ? MathF.Sin(MathF.PI * phase / 0.6f) : 0.15f;
                var breathAmount = style.Breath;
                if (style.BreathGust > 0f)
                {
                    var gust = 0.5f + 0.5f * MathF.Sin(MathF.Tau * 2.5f * i / SampleRate);
                    breathAmount += 0.35f * style.BreathGust * gust;
                }

                var voicedPart = voiceGain * periodAmp
                                 * (harmonic * (1f - breathAmount) + breathNoise * open * breathAmount * BreathLevel);
                var noisePart = frication.Process(noise) * fricationGain * sound.NoiseGain * boost * FricationLevel
                                + breathNoise * aspirationGain * AspirationLevel;
                var sample = (voicedPart + noisePart) * envelope * (1f + 0.5f * swing);

                if (style.Bubble > 0f && t >= onsetEnd)
                {
                    var blub = 0.5f + 0.5f * MathF.Sin(MathF.Tau * 15f * i / SampleRate + wobblePhase);
                    sample *= 1f - 0.4f * style.Bubble * blub;
                }

                if (style.Growl > 0f)
                    sample *= 1f - 0.35f * style.Growl * (0.5f + 0.5f * growl);

                // Runs across word gaps, so a short letter still gets whole purr cycles.
                if (style.Purr > 0f)
                    sample *= 1f - 0.3f * style.Purr * (0.5f + 0.5f * MathF.Sin(MathF.Tau * 25f * i / SampleRate));

                for (var k = 0; k < popCount; k++)
                {
                    if (i < popStart[k] || i > popEnd[k])
                        continue;

                    var dt = (i - popStart[k]) / (float) SampleRate;
                    popAmp[k] = i == popStart[k] ? 1f : popAmp[k] * popFade[k];

                    var popPhase = MathF.Tau * popFrequency[k] * (dt + 0.05f * dt * dt / popDecay[k]);
                    sample += PopLevel * style.Pops * blipGain * popAmp[k] * MathF.Sin(popPhase);
                }

                buffer[i] = sample;
            }
        }
    }

    /// <summary>Tunes the hiss of a consonant. A hissing species widens s, z and sh and moves them higher.</summary>
    private static void TuneNoise(ref NoiseBand band, in Consonant sound, in Style style)
    {
        var frequency = sound.NoiseFrequency;
        var bandwidth = sound.NoiseBandwidth;
        if (style.HissWidth > 1f && sound.Kind == SoundKind.Fricative && bandwidth <= 2000f)
        {
            var amount = (style.HissWidth - 1f) / 2f;
            frequency = MathHelper.Lerp(frequency, MathF.Max(frequency, 5500f), amount);
            bandwidth *= style.HissWidth;
        }

        band.Tune(MathF.Min(frequency, MaxFrequency), bandwidth, SampleRate);
    }

    /// <summary>Fills harmonic amplitudes, normalised to unit power, and returns how many fit under Nyquist.</summary>
    // Amplitudes come from the glottal tilt and the formant curves at each harmonic, so every vowel is about as loud.
    private static int Harmonics(float[] amps, float[] tilt, float f0, Vector3 formants, float formantScale)
    {
        // Harmonics well above F4 add almost nothing, so stop there.
        var f4 = formants.Z + 1000f * formantScale;
        var top = MathF.Min(MaxFrequency, 1.4f * f4);
        var count = Math.Min(MaxHarmonics, (int) (top / MathF.Max(f0, 1f)));
        var power = 0f;

        // Wider formants at high pitch, so widely spaced harmonics still sound each vowel evenly.
        var widen = 0.2f * f0;

        for (var k = 1; k <= count; k++)
        {
            var f = k * f0;
            var a = tilt[k]
                    * Resonance(f, formants.X, 80f * formantScale + widen)
                    * Resonance(f, formants.Y, 100f * formantScale + widen)
                    * Resonance(f, formants.Z, 140f * formantScale + widen)
                    * Resonance(f, f4, 250f * formantScale + widen);
            amps[k] = a;
            power += a * a;
        }

        var norm = power > 0f ? 1f / MathF.Sqrt(power) : 0f;
        for (var k = 1; k <= count; k++)
        {
            amps[k] *= norm;
        }

        for (var k = count + 1; k <= MaxHarmonics; k++)
        {
            amps[k] = 0f;
        }

        return count;
    }

    /// <summary>Radiated LF glottal spectrum at harmonic k, from one Rd value (Fant 1995).</summary>
    // Low Rd is pressed and bright, high Rd soft and breathy. A peak near the glottal formant, then a roll-off above
    // the return-phase corner.
    private static float LfHarmonic(int k, float rd)
    {
        rd = Math.Clamp(rd, 0.3f, 4f);
        var ra = (-1f + 4.8f * rd) / 100f;
        var rk = (22.4f + 11.8f * rd) / 100f;
        var rg = 0.25f * rk / (0.11f * rd / (0.5f + 1.2f * rk) - ra);
        var glottal = k / rg;
        var corner = k * MathF.Tau * ra;
        return glottal / (1f + glottal * glottal) / MathF.Sqrt(1f + corner * corner);
    }

    /// <summary>Gain of a two-pole resonance with unity gain at 0 Hz, the analog form of a Klatt formant.</summary>
    private static float Resonance(float f, float centre, float bandwidth)
    {
        var ratio = f / centre;
        var a = 1f - ratio * ratio;
        var b = f * bandwidth / (centre * centre);
        return 1f / MathF.Sqrt(a * a + b * b);
    }

    /// <summary>Per-sample step of a one-pole glide with this time constant.</summary>
    private static float Smoothing(float seconds)
    {
        return 1f - MathF.Exp(-1f / (seconds * SampleRate));
    }

    /// <summary>Pitch through a phrase: a gentle fall, then a tune from the punctuation that ends it.</summary>
    private static float Contour(in Blip blip, float intonation)
    {
        var t = blip.PhrasePosition;
        var ending = Math.Max(0f, (t - 0.6f) / 0.4f);
        var tune = blip.Ending switch
        {
            '?' => 1f + intonation * 0.5f * ending,
            '!' => 1f + intonation * 0.6f * (1f - t),
            ',' => 1f + intonation * 0.2f * ending,
            _ => 1f - intonation * 0.5f * ending,
        };

        return (1f - PhraseDeclination * t) * tune;
    }

    /// <summary>16-bit samples, scaled so the loudest one sits at the same level for every voice.</summary>
    private static void ToPcm(ReadOnlySpan<float> buffer, short[] pcm)
    {
        var peak = 0f;
        foreach (var sample in buffer)
        {
            peak = Math.Max(peak, Math.Abs(sample));
        }

        var gain = peak > 0f ? TargetPeak / peak : 0f;

        for (var i = 0; i < buffer.Length; i++)
        {
            pcm[i] = (short) Math.Clamp(buffer[i] * gain * short.MaxValue, short.MinValue, short.MaxValue);
        }
    }
}
