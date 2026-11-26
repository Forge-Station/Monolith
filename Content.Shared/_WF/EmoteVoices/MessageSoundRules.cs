using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Speech;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.EmoteVoices;

/// <summary>Message sound checks and lookups shared by the creator and the server.</summary>
public static class MessageSoundRules
{
    /// <summary>The message sound if it exists, otherwise null (the species' own).</summary>
    public static ProtoId<MessageSoundPrototype>? Sanitize(ProtoId<MessageSoundPrototype>? sound, IPrototypeManager protoManager)
    {
        if (sound is { } id && protoManager.HasIndex(id))
            return id;

        return null;
    }

    /// <summary>A message sound as stored in the database, where an empty string keeps the species' own.</summary>
    public static ProtoId<MessageSoundPrototype>? FromStored(string? stored)
    {
        if (string.IsNullOrEmpty(stored))
            return null;

        return new ProtoId<MessageSoundPrototype>(stored);
    }

    /// <summary>The database form of a message sound; see <see cref="FromStored"/>.</summary>
    public static string ToStored(ProtoId<MessageSoundPrototype>? sound)
    {
        return sound?.Id ?? string.Empty;
    }

    /// <summary>Every message sound, the silent one first, the rest sorted by name.</summary>
    public static List<MessageSoundPrototype> All(IPrototypeManager protoManager)
    {
        var sounds = new List<MessageSoundPrototype>(protoManager.EnumeratePrototypes<MessageSoundPrototype>());
        sounds.Sort((a, b) =>
        {
            if ((a.Sounds == null) != (b.Sounds == null))
                return a.Sounds == null ? -1 : 1;

            return string.Compare(Loc.GetString(a.Name), Loc.GetString(b.Name), StringComparison.CurrentCultureIgnoreCase);
        });
        return sounds;
    }

    /// <summary>The speech sounds a species' body has, or null when it speaks silently.</summary>
    public static ProtoId<SpeechSoundsPrototype>? SpeciesSounds(ProtoId<SpeciesPrototype> species,
        IPrototypeManager protoManager,
        IComponentFactory factory)
    {
        if (!protoManager.TryIndex(species, out var speciesProto)
            || !protoManager.TryIndex(speciesProto.Prototype, out var body)
            || !body.TryComp<SpeechComponent>(out var speech, factory))
            return null;

        return speech.SpeechSounds;
    }

    /// <summary>The speech sounds a character speaks with: their pick, or their species' own.</summary>
    public static ProtoId<SpeechSoundsPrototype>? Resolve(ProtoId<SpeciesPrototype> species,
        ProtoId<MessageSoundPrototype>? sound,
        IPrototypeManager protoManager,
        IComponentFactory factory)
    {
        if (sound is { } id && protoManager.TryIndex(id, out var proto))
            return proto.Sounds;

        return SpeciesSounds(species, protoManager, factory);
    }
}
