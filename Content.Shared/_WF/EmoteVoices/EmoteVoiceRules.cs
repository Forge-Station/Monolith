using System.Diagnostics.CodeAnalysis;
using Content.Shared.Chat.Prototypes;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Preferences;
using Content.Shared.Speech.Components;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.EmoteVoices;

/// <summary>Emote voice checks and lookups shared by the creator and the server.</summary>
public static class EmoteVoiceRules
{
    /// <summary>The scream emote.</summary>
    public static readonly ProtoId<EmotePrototype> Scream = "Scream";
    /// <summary>The laugh emote.</summary>
    public static readonly ProtoId<EmotePrototype> Laugh = "Laugh";

    /// <summary>The voice if it exists and belongs to the emote, otherwise null (the species' own).</summary>
    public static ProtoId<EmoteVoicePrototype>? Sanitize(ProtoId<EmoteVoicePrototype>? voice,
        ProtoId<EmotePrototype> emote,
        IPrototypeManager protoManager)
    {
        if (voice is not { } id || !protoManager.TryIndex(id, out var proto) || proto.Emote != emote)
            return null;

        return id;
    }

    /// <summary>A voice as stored in the database, where an empty string keeps the species' own.</summary>
    public static ProtoId<EmoteVoicePrototype>? FromStored(string? stored)
    {
        if (string.IsNullOrEmpty(stored))
            return null;

        return new ProtoId<EmoteVoicePrototype>(stored);
    }

    /// <summary>The database form of a voice; see <see cref="FromStored"/>.</summary>
    public static string ToStored(ProtoId<EmoteVoicePrototype>? voice)
    {
        return voice?.Id ?? string.Empty;
    }

    /// <summary>The voices a profile chose, skipping the ones left to the species.</summary>
    public static IEnumerable<ProtoId<EmoteVoicePrototype>> Chosen(HumanoidCharacterProfile profile)
    {
        if (profile.ScreamVoice is { } scream)
            yield return scream;

        if (profile.LaughVoice is { } laugh)
            yield return laugh;
    }

    /// <summary>Every voice for an emote, sorted by name.</summary>
    public static List<EmoteVoicePrototype> VoicesFor(ProtoId<EmotePrototype> emote, IPrototypeManager protoManager)
    {
        var voices = new List<EmoteVoicePrototype>();
        foreach (var voice in protoManager.EnumeratePrototypes<EmoteVoicePrototype>())
        {
            if (voice.Emote == emote)
                voices.Add(voice);
        }

        voices.Sort((a, b) => string.Compare(Loc.GetString(a.Name), Loc.GetString(b.Name), StringComparison.CurrentCultureIgnoreCase));
        return voices;
    }

    /// <summary>The emote sounds a species' body loads for a sex, the way VocalSystem picks them.</summary>
    public static bool TryGetSpeciesSounds(ProtoId<SpeciesPrototype> species,
        Sex sex,
        IPrototypeManager protoManager,
        IComponentFactory factory,
        [NotNullWhen(true)] out EmoteSoundsPrototype? sounds)
    {
        sounds = null;
        if (!protoManager.TryIndex(species, out var speciesProto)
            || !protoManager.TryIndex(speciesProto.Prototype, out var body)
            || !body.TryComp<VocalComponent>(out var vocal, factory)
            || vocal.Sounds == null)
            return false;

        // Same fallback as VocalSystem.LoadSounds: most species list only Male and Female.
        if (!vocal.Sounds.TryGetValue(sex, out var id)
            && !vocal.Sounds.TryGetValue(Sex.Unsexed, out id)
            && !vocal.Sounds.TryGetValue(Sex.Male, out id)
            && !vocal.Sounds.TryGetValue(Sex.Female, out id))
            return false;

        return protoManager.TryIndex(id, out sounds);
    }

    /// <summary>The sound and params an emote plays for a character, or false when it is silent.</summary>
    public static bool TryGetEmoteSound(ProtoId<SpeciesPrototype> species,
        Sex sex,
        ProtoId<EmotePrototype> emote,
        ProtoId<EmoteVoicePrototype>? voice,
        IPrototypeManager protoManager,
        IComponentFactory factory,
        [NotNullWhen(true)] out SoundSpecifier? sound,
        out AudioParams audioParams)
    {
        sound = null;
        audioParams = AudioParams.Default;

        // A voice for another emote counts as unset, as in Sanitize.
        if (voice is { } voiceId && protoManager.TryIndex(voiceId, out var voiceProto) && voiceProto.Emote == emote)
        {
            sound = voiceProto.GetSound(sex);
            audioParams = voiceProto.Params;
            return sound != null;
        }

        if (!TryGetSpeciesSounds(species, sex, protoManager, factory, out var sounds))
            return false;

        if (!sounds.Sounds.TryGetValue(emote, out sound))
            sound = sounds.FallbackSound;

        if (sound == null)
            return false;

        audioParams = sounds.GeneralParams ?? sound.Params;
        return true;
    }
}
