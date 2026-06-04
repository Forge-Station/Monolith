using Robust.Shared.Serialization;
using Robust.Shared.Timing;

namespace Content.Shared._Forge.BoardingTeleport;

public static class BoardingTeleportLockDegrade
{
    public static float GetLockAgeSeconds(TimeSpan? lockEstablishedAt, IGameTiming timing)
    {
        if (lockEstablishedAt is not { } established)
            return 0f;

        return MathF.Max(0f, (float) (timing.CurTime - established).TotalSeconds);
    }

    public static int GetDegradeSteps(float lockAgeSeconds)
    {
        if (lockAgeSeconds <= 0f)
            return 0;

        return (int) (lockAgeSeconds / BoardingTeleportConstants.LockDegradeIntervalSeconds);
    }

    public static bool IsLockExpired(float lockAgeSeconds)
    {
        return lockAgeSeconds > BoardingTeleportConstants.LockMaxAgeSeconds;
    }

    public static float GetScatterPenalty(int degradeSteps)
    {
        return degradeSteps * BoardingTeleportConstants.LockDegradeScatterBonus;
    }

    public static float GetRiskPenalty(int degradeSteps)
    {
        return degradeSteps * BoardingTeleportConstants.LockDegradeRiskBonus;
    }
}

[Serializable, NetSerializable]
public struct BoardingTeleportPlatformUiEntry
{
    public NetEntity Platform;
    public string Name;
    public float? CooldownSeconds;
    public bool IsReady;
    public bool HasLanding;
    public int SlotIndex;
    public bool IsSelected;
}

[Serializable, NetSerializable]
public struct BoardingTeleportScramblerEffect
{
    public bool BlocksLock;
    public float ScatterBonus;
    public float RiskBonus;
}
