using Robust.Shared.Serialization;

namespace Content.Shared._Forge.LivingNpc;

/// <summary>
/// Short-term emotional state. All values are 0..1.
/// </summary>
[DataDefinition, Serializable]
public sealed partial class LivingNpcMood
{
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float Happiness = 0.55f;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float Fear;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float Anger;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float Energy = 0.75f;

    /// <summary>
    /// How much they currently want company. High means lonely.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float SocialNeed = 0.4f;

    public void Clamp()
    {
        Happiness = Math.Clamp(Happiness, 0f, 1f);
        Fear = Math.Clamp(Fear, 0f, 1f);
        Anger = Math.Clamp(Anger, 0f, 1f);
        Energy = Math.Clamp(Energy, 0f, 1f);
        SocialNeed = Math.Clamp(SocialNeed, 0f, 1f);
    }

    public string DominantKey()
    {
        if (Fear >= 0.55f && Fear >= Anger && Fear >= (1f - Happiness))
            return "fear";
        if (Anger >= 0.5f && Anger >= Fear)
            return "anger";
        if (Happiness >= 0.7f)
            return "happy";
        if (Happiness <= 0.3f)
            return "sad";
        if (Energy <= 0.3f)
            return "tired";
        return "calm";
    }
}
