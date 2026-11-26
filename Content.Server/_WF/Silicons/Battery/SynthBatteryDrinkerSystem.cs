using Content.Server._HL.Silicons.Synths.Battery;
using Content.Shared._HL.Silicons.Synths.Battery;
using Content.Shared._WF.Power;

namespace Content.Server._WF.Silicons.Battery;

/// <summary>
/// Points the battery drinker at a synth's internal cell, which lives in an organ
/// slot rather than the power cell slot the stock drinker looks in.
/// </summary>
public sealed class SynthBatteryDrinkerSystem : EntitySystem
{
    [Dependency] private readonly SynthBatterySystem _synthBattery = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SynthBatteryComponent, GetDrinkerBatteryEvent>(OnGetDrinkerBattery);
    }

    private void OnGetDrinkerBattery(Entity<SynthBatteryComponent> ent, ref GetDrinkerBatteryEvent args)
    {
        if (args.Battery != null)
            return;

        if (_synthBattery.TryGetBattery(ent.Owner, out var battery, ent.Comp))
            args.Battery = battery;
    }
}
