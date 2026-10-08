using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Random;

namespace Content.Server._Forge.Leviathans;

internal static class LeviathanVitals
{
    public static void RollHealth(MobThresholdSystem thresholds, IRobustRandom random, EntityUid uid, int min, int max)
    {
        min = Math.Max(1, min);
        max = Math.Max(min, max);
        var hp = random.Next(min, max + 1);
        var crit = Math.Max(1, (int)(hp * 0.92f));
        if (crit >= hp)
            crit = hp;
        thresholds.SetMobStateThreshold(uid, crit, MobState.Critical);
        thresholds.SetMobStateThreshold(uid, hp, MobState.Dead);
    }
}
