using Robust.Shared.Serialization;

namespace Content.Shared._Forge.LivingNpc;

/// <summary>
/// Stable character traits. All values are 0..1.
/// </summary>
[DataDefinition, Serializable]
public sealed partial class LivingNpcPersonality
{
    /// <summary>Talks, greets, and seeks company.</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float Extraversion = 0.5f;

    /// <summary>Fights instead of fleeing when threatened.</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float Bravery = 0.45f;

    /// <summary>Approaches unfamiliar people and objects.</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float Curiosity = 0.5f;

    /// <summary>Helps others and stays polite.</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float Agreeableness = 0.55f;

    /// <summary>Sticks to work spots and routines.</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float Discipline = 0.5f;

    /// <summary>How quickly anger rises when hurt or insulted.</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float Temper = 0.4f;

    /// <summary>Jokes, laughs, and light idle chatter.</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float Humor = 0.4f;

    public LivingNpcPersonality Clone()
    {
        return new LivingNpcPersonality
        {
            Extraversion = Extraversion,
            Bravery = Bravery,
            Curiosity = Curiosity,
            Agreeableness = Agreeableness,
            Discipline = Discipline,
            Temper = Temper,
            Humor = Humor,
        };
    }

    public void Clamp()
    {
        Extraversion = Math.Clamp(Extraversion, 0f, 1f);
        Bravery = Math.Clamp(Bravery, 0f, 1f);
        Curiosity = Math.Clamp(Curiosity, 0f, 1f);
        Agreeableness = Math.Clamp(Agreeableness, 0f, 1f);
        Discipline = Math.Clamp(Discipline, 0f, 1f);
        Temper = Math.Clamp(Temper, 0f, 1f);
        Humor = Math.Clamp(Humor, 0f, 1f);
    }
}
