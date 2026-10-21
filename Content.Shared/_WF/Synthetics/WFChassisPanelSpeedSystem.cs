using Content.Shared._EinsteinEngines.Silicon.Components;
using Content.Shared._HL.Silicons.Synths.Battery;
using Content.Shared.Movement.Systems;

namespace Content.Shared._WF.Synthetics;

/// <summary>
/// The chassis's speed modifiers: overclocked servos, an open bay and low power. Shared so the client predicts the
/// same speed. The panel, the bay and the low-power system set the state and refresh; this is the one place any of
/// it is applied.
/// </summary>
public sealed class WFChassisPanelSpeedSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFChassisComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
    }

    private void OnRefreshSpeed(Entity<WFChassisComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (ent.Comp.Overclocked)
            args.ModifySpeed(ent.Comp.OverclockSpeed, ent.Comp.OverclockSpeed);

        if (ent.Comp.BayOpen)
            args.ModifySpeed(ent.Comp.BayOpenSpeed, ent.Comp.BayOpenSpeed);

        if (TryComp(ent, out WFLowPowerComponent? lowPower))
        {
            var (walk, sprint) = OwnUnpoweredSlowdown(ent);
            args.ModifySpeed(lowPower.Speed / walk, lowPower.Speed / sprint);
        }
    }

    /// <summary>
    /// The slowdown the chassis's own battery system already applies on an empty cell, so low power can fold it in:
    /// an IPC's charge-state threshold (as SharedSiliconChargeSystem reads it) and a synth's unpowered modifier.
    /// </summary>
    private (float Walk, float Sprint) OwnUnpoweredSlowdown(EntityUid body)
    {
        var walk = 1f;
        var sprint = 1f;

        if (TryComp(body, out SiliconComponent? silicon) && silicon.BatteryPowered)
        {
            var closest = 1;
            foreach (var key in silicon.SpeedModifierThresholds.Keys)
            {
                if (silicon.ChargeState >= key && key > closest)
                    closest = key;
            }

            if (silicon.SpeedModifierThresholds.TryGetValue(closest, out var modifier) && modifier > 0f)
            {
                walk *= modifier;
                sprint *= modifier;
            }
        }

        if (TryComp(body, out SynthBatteryComponent? synth) && synth.Unpowered)
        {
            if (synth.UnpoweredWalkSpeedModifier > 0f)
                walk *= synth.UnpoweredWalkSpeedModifier;

            if (synth.UnpoweredSprintSpeedModifier > 0f)
                sprint *= synth.UnpoweredSprintSpeedModifier;
        }

        return (walk, sprint);
    }
}
