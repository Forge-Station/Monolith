using Content.Shared._WF.NpcCrew;

namespace Content.Server._WF.NpcCrew;

/// <summary>
/// What a crew skill level means in numbers. NPC crew are meant to lose to a player of equal kit: at every level
/// they take more damage than they should, do less, and are slow on the trigger.
/// </summary>
public static class WFCrewSkills
{
    /// <param name="AimError">Degrees a hand weapon shot may stray to either side.</param>
    /// <param name="ShootDelay">Seconds with a target in sight before the first shot.</param>
    /// <param name="GunneryError">Metres ship guns may be laid off the target, re-rolled every few seconds.</param>
    /// <param name="Leading">How well ship guns lead a moving target, 0 to 1.</param>
    /// <param name="Handling">Multiplier on thrust and turn rate at the helm.</param>
    /// <param name="Evasion">Multiplier on how far ahead the pilot looks for collisions and incoming fire.</param>
    /// <param name="DodgesFire">Whether the pilot steers out of the way of ship weapon fire.</param>
    /// <param name="DamageTaken">Multiplier on damage the crewman takes. NPC crew go down faster than players.</param>
    /// <param name="DamageDealt">Multiplier on damage the crewman does to anyone who is not NPC crew.</param>
    public readonly record struct Profile(float AimError, float ShootDelay, float GunneryError, float Leading, float Handling,
        float Evasion, bool DodgesFire, float DamageTaken, float DamageDealt);

    public static Profile Of(WFCrewSkill skill)
    {
        return skill switch
        {
            WFCrewSkill.Green => new Profile(18f, 1.4f, 30f, 0.3f, 0.7f, 0.4f, false, 2.6f, 0.35f),
            WFCrewSkill.Regular => new Profile(9f, 0.9f, 14f, 0.7f, 0.9f, 0.75f, true, 2.1f, 0.46f),
            WFCrewSkill.Elite => new Profile(0f, 0.25f, 0f, 1f, 1.1f, 1.25f, true, 1.13f, 0.86f),
            _ => new Profile(4f, 0.5f, 5f, 1f, 1f, 1f, true, 1.55f, 0.63f),
        };
    }
}
