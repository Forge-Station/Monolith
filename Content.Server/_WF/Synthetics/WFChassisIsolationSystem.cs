using Content.Server._WF.Wolfmed.Consciousness;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Body.Events;
using Content.Shared._WF.Synthetics;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Movement.Systems;
using Robust.Shared.Timing;

namespace Content.Server._WF.Synthetics;

/// <summary>
/// Cuts a chassis limb's servos and sensors from the panel, and gives them back. An isolated part is switched off
/// and cannot be switched back on by repairs, reads Disabled, and puts no pain on the body
/// (<see cref="PainSystem.CanFeelPain"/> and <see cref="BodyPartFunctionalitySystem.GetState"/> check for it).
/// </summary>
public sealed class WFChassisIsolationSystem : EntitySystem
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(1);

    [Dependency] private BodyPartFunctionalitySystem _functionality = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MovementSpeedModifierSystem _speed = default!;
    [Dependency] private PainSystem _pain = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private WolfmedConsciousnessSystem _consciousness = default!;

    private TimeSpan _nextSweep;

    /// <summary>A limb the panel may isolate: an arm, hand, leg or foot. The head and torso run the chassis.</summary>
    public static bool CanIsolate(BodyPartComponent part) =>
        part.PartType is BodyPartType.Arm or BodyPartType.Hand or BodyPartType.Leg or BodyPartType.Foot;

    /// <summary>Isolates an attached limb. False if it is not a limb, not attached or already isolated.</summary>
    public bool TryIsolate(EntityUid part)
    {
        if (!TryComp(part, out BodyPartComponent? bodyPart) || bodyPart.Body is not { } body ||
            !CanIsolate(bodyPart) || HasComp<WFIsolatedPartComponent>(part))
            return false;

        var isolated = EnsureComp<WFIsolatedPartComponent>(part);
        isolated.CouldEnable = bodyPart.CanEnable;
        Dirty(part, isolated);

        // Remembered so a restore gives the sensors their reading back instead of a free reset.
        EnsureComp<WFIsolatedPainComponent>(part).Pain = _pain.GetRawPain(part);

        bodyPart.CanEnable = false;
        var ev = new BodyPartEnableChangedEvent(false);
        RaiseLocalEvent(part, ref ev);

        Refresh(body, part);
        return true;
    }

    /// <summary>Restores an isolated part, attached or not. False if it was not isolated.</summary>
    public bool TryRestore(EntityUid part)
    {
        if (!TryComp(part, out WFIsolatedPartComponent? isolated))
            return false;

        var remembered = CompOrNull<WFIsolatedPainComponent>(part)?.Pain ?? FixedPoint2.Zero;
        var couldEnable = isolated.CouldEnable;
        RemComp<WFIsolatedPartComponent>(part);
        RemComp<WFIsolatedPainComponent>(part);
        RemComp<WFLowPowerPartComponent>(part);

        if (!TryComp(part, out BodyPartComponent? bodyPart))
            return true;

        bodyPart.CanEnable = couldEnable;
        if (couldEnable)
        {
            // The damage check switches it back on only if the limb is whole enough to work, as a repair would.
            if (TryComp(part, out DamageableComponent? damageable))
                _body.CheckBodyPart((part, bodyPart), null, false, damageable);
            else
            {
                var ev = new BodyPartEnableChangedEvent(true);
                RaiseLocalEvent(part, ref ev);
            }
        }

        if (bodyPart.Body is not { } body)
            return true;

        _pain.RefreshWoundPain(part);
        if (TryComp(part, out PainComponent? pain))
            _pain.SetPain((part, pain), FixedPoint2.Max(remembered, pain.WoundPain));

        Refresh(body, part);
        return true;
    }

    /// <summary>Isolated by the panel's user, as opposed to cut by low power alone.</summary>
    public bool IsUserIsolated(EntityUid part) =>
        HasComp<WFIsolatedPartComponent>(part) &&
        (!TryComp(part, out WFLowPowerPartComponent? lowPower) || lowPower.UserIsolated);

    /// <summary>
    /// The panel's isolate or restore. On a limb low power has cut it only records the choice, which applies once
    /// power returns; restoring a limb that is only unpowered is refused. Deferred is true when only recorded.
    /// </summary>
    public bool TrySetUserIsolated(EntityUid part, bool isolate, out bool deferred)
    {
        deferred = false;
        if (!TryComp(part, out WFLowPowerPartComponent? lowPower))
            return isolate ? TryIsolate(part) : TryRestore(part);

        if (lowPower.UserIsolated == isolate)
            return false;

        lowPower.UserIsolated = isolate;
        deferred = true;
        return true;
    }

    /// <summary>Cuts a limb for low power, as isolation does, remembering whether the user had isolated it already.</summary>
    public bool TryCutPower(EntityUid part)
    {
        if (HasComp<WFLowPowerPartComponent>(part))
            return false;

        var user = HasComp<WFIsolatedPartComponent>(part);
        if (!user && !TryIsolate(part))
            return false;

        EnsureComp<WFLowPowerPartComponent>(part).UserIsolated = user;
        return true;
    }

    /// <summary>Gives a limb its power back; one the user had isolated stays isolated.</summary>
    public bool TryRestorePower(EntityUid part)
    {
        if (!TryComp(part, out WFLowPowerPartComponent? lowPower))
            return false;

        var user = lowPower.UserIsolated;
        RemComp<WFLowPowerPartComponent>(part);
        if (!user)
            TryRestore(part);

        return true;
    }

    /// <summary>Forgets the pain an isolated part would come back with; a reboot clears it like the rest.</summary>
    public void ClearRememberedPain(EntityUid part)
    {
        if (TryComp(part, out WFIsolatedPainComponent? memory))
            memory.Pain = FixedPoint2.Zero;
    }

    private void Refresh(EntityUid body, EntityUid part)
    {
        _functionality.RefreshPart(body, part);
        _pain.RefreshWoundPain(part);
        _speed.RefreshMovementSpeedModifiers(body);
        _consciousness.Refresh(body);
    }

    /// <summary>A part that left its body isolated (blown off, pulled) is restored, so nothing carries it elsewhere.</summary>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _nextSweep)
            return;

        _nextSweep = _timing.CurTime + SweepInterval;
        var stale = new List<EntityUid>();
        var query = EntityQueryEnumerator<WFIsolatedPartComponent, BodyPartComponent>();
        while (query.MoveNext(out var uid, out _, out var part))
        {
            if (part.Body == null)
                stale.Add(uid);
        }

        foreach (var uid in stale)
            TryRestore(uid);
    }
}

/// <summary>A limb cut by low power rather than by the panel; isolated the same way while it lasts.</summary>
[RegisterComponent]
public sealed partial class WFLowPowerPartComponent : Component
{
    /// <summary>The user isolated it too: it stays isolated when power returns.</summary>
    [DataField]
    public bool UserIsolated;
}

/// <summary>The pain an isolated part carried when its sensors were cut, given back on restore.</summary>
[RegisterComponent]
public sealed partial class WFIsolatedPainComponent : Component
{
    [DataField]
    public FixedPoint2 Pain;
}
