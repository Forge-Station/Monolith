using Robust.Shared.Configuration;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Remade.Betrayal;

/// <summary>Settings for the betrayal effects: false sounds, music, mirrored crewmates and a breaking mind.</summary>
[CVarDefs]
public sealed class WFBetrayalCVars
{
    /// <summary>Multiplies how often betrayal effects come. Zero silences them.</summary>
    public static readonly CVarDef<float> Rate =
        CVarDef.Create("wf.remade.betrayal.rate", 1f, CVar.SERVERONLY);

    /// <summary>Scales the stingers, the score and the silence that clears the music on this client. Zero turns them off.</summary>
    public static readonly CVarDef<float> Music =
        CVarDef.Create("wf.remade.betrayal.music", 1f, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>Scales the knocking, scraping, breathing and footsteps on this client. Zero turns them off.</summary>
    public static readonly CVarDef<float> Sounds =
        CVarDef.Create("wf.remade.betrayal.sounds", 1f, CVar.CLIENTONLY | CVar.ARCHIVE);
}

/// <summary>The false sounds played to one player, positioned by their client.</summary>
[Serializable, NetSerializable]
public enum WFBetrayalSoundKind : byte
{
    Knock,
    Scrape,
    Breath,
    Footsteps,
}

/// <summary>Fades the ambient sound and music to nothing on one client, for a while or until a stinger.</summary>
[Serializable, NetSerializable]
public sealed class WFBetrayalSilenceEvent(float seconds) : EntityEventArgs
{
    public readonly float Seconds = seconds;
}

/// <summary>Plays one client a stinger and ends any silence.</summary>
[Serializable, NetSerializable]
public sealed class WFBetrayalStingerEvent(int stinger) : EntityEventArgs
{
    /// <summary>Index into <see cref="WFBetrayalSounds.Stingers"/>.</summary>
    public readonly int Stinger = stinger;
}

/// <summary>Sets how many layers of the score one client plays, from 0, for a few seconds.</summary>
[Serializable, NetSerializable]
public sealed class WFBetrayalScoreEvent(int layers, float gain, float seconds) : EntityEventArgs
{
    public readonly int Layers = layers;

    /// <summary>How loud it is, from 0 to 1.</summary>
    public readonly float Gain = gain;

    public readonly float Seconds = seconds;
}

/// <summary>A false sound for one client to place where it will unsettle: behind a door, in a wall, at the player's back.</summary>
[Serializable, NetSerializable]
public sealed class WFBetrayalSoundEvent(WFBetrayalSoundKind kind, float intensity, float seconds) : EntityEventArgs
{
    public readonly WFBetrayalSoundKind Kind = kind;

    /// <summary>From 0 to 1: louder and closer as it climbs.</summary>
    public readonly float Intensity = intensity;

    /// <summary>How long a lasting sound (footsteps) goes on for.</summary>
    public readonly float Seconds = seconds;
}

/// <summary>Makes another player look like a Remade to one client, for a moment.</summary>
[Serializable, NetSerializable]
public sealed class WFBetrayalFlickerEvent(NetEntity target, int sprite, float seconds) : EntityEventArgs
{
    public readonly NetEntity Target = target;

    /// <summary>Index into <see cref="Dread.WFDreadGlimpses.All"/>.</summary>
    public readonly int Sprite = sprite;

    public readonly float Seconds = seconds;
}

/// <summary>The music the betrayal effects play. Stingers are cut from the tracks, the score layers are the tracks themselves.</summary>
public static class WFBetrayalSounds
{
    private const string Dir = "/Audio/_WF/Remade/betrayal/";

    public static readonly string[] Stingers =
    {
        Dir + "stinger_1.ogg", Dir + "stinger_2.ogg", Dir + "stinger_3.ogg",
    };

    /// <summary>The score from its lowest layer to its highest.</summary>
    public static readonly string[] ScoreLayers =
    {
        Dir + "music_pensive.ogg", Dir + "music_nicole.ogg",
        Dir + "music_unitology.ogg", Dir + "music_credits_violin.ogg",
    };

    public const string Knocks = "WFBetrayalKnocks";
    public const string Scrapes = "WFBetrayalScrapes";
    public const string Breaths = "WFBetrayalBreaths";
    public const string Steps = "WFBetrayalSteps";
    public const string HeavySteps = "WFBetrayalHeavySteps";
}

/// <summary>The numbers behind the betrayal effects. All rates are per minute at a Monolith's side, before stress.</summary>
public static class WFBetrayalMath
{
    /// <summary>Resonance at which each layer of the score comes in.</summary>
    private static readonly float[] LayerAt = { 35f, 55f, 75f, 95f };

    /// <summary>The most layers of the score there are.</summary>
    public static int MaxLayers => LayerAt.Length;

    /// <summary>How many layers of the score play at a resonance: none below Growth, all four at the Crescendo.</summary>
    public static int ScoreLayers(float resonance)
    {
        var layers = 0;
        foreach (var at in LayerAt)
        {
            if (resonance >= at)
                layers++;
        }

        return layers;
    }

    /// <summary>How hard the mind breaks, from 0 at the stress where the Director backs off to 1 at breaking point.</summary>
    public static float Breaking(float stress, float from = 40f)
    {
        return Math.Clamp((stress - from) / (100f - from), 0f, 1f);
    }

    /// <summary>The share of the stone's dread-rate felt by each kind of mind effect, per minute at full breaking and Crescendo.</summary>
    public static float MindPerMinute(MindEffect effect)
    {
        return effect switch
        {
            MindEffect.Drop => 2.5f,
            MindEffect.Nosebleed => 1.5f,
            MindEffect.Twitch => 3f,
            MindEffect.Announcement => 1f,
            MindEffect.Message => 1.5f,
            _ => 0f,
        };
    }

    /// <summary>How much more a stage breaks a mind: a young stone barely does, the Crescendo does fully.</summary>
    public static float MindStage(WFMonolithStage stage)
    {
        return stage switch
        {
            WFMonolithStage.Hymn => 0.2f,
            WFMonolithStage.Growth => 0.4f,
            WFMonolithStage.Harvest => 0.65f,
            WFMonolithStage.Rapture => 0.85f,
            WFMonolithStage.Crescendo => 1f,
            _ => 0f,
        };
    }

    /// <summary>The gun spread multiplier for a shaking hand at a breaking level: 1 is steady.</summary>
    public static float AimSpread(float breaking) => 1f + 5f * Math.Clamp(breaking, 0f, 1f);

    /// <summary>
    /// Garbles text the way a failing radio does: a few letters are swapped for static or dropped. <paramref name="roll"/>
    /// gives numbers from 0 to 1.
    /// </summary>
    public static string Corrupt(string text, float rate, Func<float> roll)
    {
        const string Static = "#%&?*~";
        var builder = new System.Text.StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (!char.IsLetter(c) || roll() >= rate)
            {
                builder.Append(c);
                continue;
            }

            if (roll() < 0.35f)
                continue;

            builder.Append(Static[Math.Min((int) (roll() * Static.Length), Static.Length - 1)]);
        }

        return builder.ToString();
    }
}

/// <summary>The body and mind effects a breaking player suffers.</summary>
public enum MindEffect : byte
{
    Drop,
    Nosebleed,
    Twitch,
    Announcement,
    Message,
}
