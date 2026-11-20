using Content.Shared.Stunnable;

namespace Content.Shared._WF.Wolfmed.Compat;

/// <summary>Onyx-shaped paralyse call mapped onto Wolfgate's TryParalyze.</summary>
public static class StunSystemOnyxCompat
{
    /// <summary>Extends a paralysis by the given duration; false when the duration is null or non-positive.</summary>
    public static bool TryUpdateParalyzeDuration(this SharedStunSystem stun, EntityUid uid, TimeSpan? duration, bool visualized = false)
        => duration is { } d && d > TimeSpan.Zero && stun.TryParalyze(uid, d, refresh: false);
}
