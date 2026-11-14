using Content.Shared._HL.Silicons.Synths.Battery;
using Content.Server.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Power.Components; // WOLFGATE - battery moved to shared here
using Content.Server.Power.EntitySystems;
using Content.Shared.Power; // WOLFGATE
using Content.Shared.Rejuvenate; // WOLFGATE
using Robust.Server.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Containers;
using Robust.Shared.Timing;

namespace Content.Server._HL.Silicons.Synths.Battery;

public sealed partial class SynthBatteryPowerSystem : EntitySystem
{
    private static readonly TimeSpan UpdateRate = TimeSpan.FromSeconds(1);

    [Dependency] private SharedSynthBatteryAlertSystem _alerts = default!;
    [Dependency] private SynthBatteryEffectsSystem _effects = default!;
    [Dependency] private SynthBatterySystem _synthBattery = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private BatterySystem _battery = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IGameTiming _timing = default!;

    private TimeSpan _nextUpdate;

    public override void Initialize()
    {
        SubscribeLocalEvent<BatteryComponent, ChargeChangedEvent>(OnBatteryChargeChanged);
        SubscribeLocalEvent<SynthBatteryComponent, BeingGibbedEvent>(OnBeingGibbed);
        SubscribeLocalEvent<SynthBatteryComponent, RejuvenateEvent>(OnRejuvenate); // WOLFGATE
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextUpdate)
            return;

        _nextUpdate = _timing.CurTime + UpdateRate;

        var query = EntityQueryEnumerator<SynthBatteryComponent, MobStateComponent>();
        while (query.MoveNext(out var uid, out var synthBattery, out var mobState))
        {
            if (mobState.CurrentState == MobState.Dead)
                continue;

            UpdatePower((uid, synthBattery, mobState), (float) UpdateRate.TotalSeconds);
        }
    }

    private void UpdatePower(Entity<SynthBatteryComponent, MobStateComponent> ent, float delta)
    {
        EnsureStartingBattery(ent);

        if (!_synthBattery.TryGetBattery(ent.Owner, out var battery, ent.Comp1))
        {
            _alerts.ShowNoBatteryAlert((ent.Owner, ent.Comp1));
            SetUnpowered(ent, true);
            return;
        }

        var charge = battery.Value.Comp.CurrentCharge;
        if (charge <= 0f)
        {
            _alerts.ShowBatteryAlert((ent.Owner, ent.Comp1), charge, battery.Value.Comp.MaxCharge);
            SetUnpowered(ent, true);
            return;
        }

        SetUnpowered(ent, false);

        if (ent.Comp1.DrawRate <= 0f)
        {
            _alerts.ShowBatteryAlert((ent.Owner, ent.Comp1), charge, battery.Value.Comp.MaxCharge);
            return;
        }

        var oldPercent = GetChargeLevel(battery.Value) * 100f;
        var changed = _battery.ChangeCharge(battery.Value.Owner, -ent.Comp1.DrawRate * delta, battery.Value.Comp);

        if (changed < 0f)
        {
            var newPercent = GetChargeLevel(battery.Value) * 100f;
            UpdateLowPowerWarning(ent, oldPercent, newPercent);
        }
    }

    private void EnsureStartingBattery(Entity<SynthBatteryComponent, MobStateComponent> ent)
    {
        if (ent.Comp1.StartingBatteryInserted)
            return;

        if (_synthBattery.TryGetBattery(ent.Owner, out _, ent.Comp1))
        {
            ent.Comp1.StartingBatteryInserted = true;
            return;
        }

        if (ent.Comp1.StartingBattery is not { } prototype)
        {
            ent.Comp1.StartingBatteryInserted = true;
            return;
        }

        if (!_synthBattery.TryGetBatteryContainer(ent.Owner, ent.Comp1.OrganSlot, out _, out var container))
            return;

        var battery = Spawn(prototype, Transform(ent.Owner).Coordinates);
        if (!HasComp<BatteryComponent>(battery) ||
            !_container.Insert(battery, container))
        {
            QueueDel(battery);
            return;
        }

        ent.Comp1.StartingBatteryInserted = true;
    }

    private void UpdateLowPowerWarning(Entity<SynthBatteryComponent> ent, float oldPercent, float newPercent)
    {
        var warn = false;
        foreach (var threshold in ent.Comp.WarningPercentages)
        {
            if (oldPercent > threshold && newPercent <= threshold)
            {
                warn = true;
                break;
            }
        }

        if (!warn)
            return;

        if (ent.Comp.BatteryLowText != null)
            _popup.PopupEntity(Loc.GetString(ent.Comp.BatteryLowText), ent, PopupType.LargeCaution);

        PlaySound(ent, ent.Comp.BatteryLowSound);
    }

    private static float GetChargeLevel(Entity<BatteryComponent> battery)
    {
        if (battery.Comp.MaxCharge <= 0f)
            return 0f;

        return battery.Comp.CurrentCharge / battery.Comp.MaxCharge;
    }

    private void PlaySound(Entity<SynthBatteryComponent> ent, SoundSpecifier? sound)
    {
        if (sound == null)
            return;

        _audio.PlayPvs(sound, ent);
    }

    /// <summary>
    /// WOLFGATE: a synth's charge lives on a cell inside its battery organ slot, and RejuvenateEvent only reaches the
    /// mob, so rejuvenating left the synth on an empty battery. Fill the cell too; the charge change clears the
    /// unpowered state and refreshes the alert through OnBatteryChargeChanged.
    /// </summary>
    private void OnRejuvenate(Entity<SynthBatteryComponent> ent, ref RejuvenateEvent args)
    {
        if (!_synthBattery.TryGetBattery(ent.Owner, out var battery, ent.Comp))
            return;

        _battery.SetCharge(battery.Value.Owner, battery.Value.Comp.MaxCharge, battery.Value.Comp);
    }

    public void SetUnpowered(Entity<SynthBatteryComponent> ent, bool unpowered)
    {
        if (ent.Comp.Unpowered == unpowered)
            return;

        ent.Comp.Unpowered = unpowered;
        Dirty(ent);
        _effects.RefreshUnpoweredEffects(ent);

        if (!unpowered)
            return;

        if (ent.Comp.BatteryDeadText != null)
            _popup.PopupEntity(Loc.GetString(ent.Comp.BatteryDeadText), ent, PopupType.LargeCaution);

        PlaySound(ent, ent.Comp.BatteryDeadSound);
    }

    private void OnBatteryChargeChanged(Entity<BatteryComponent> ent, ref ChargeChangedEvent args)
    {
        if (!TryGetSynthFromBattery(ent.Owner, out var synth) ||
            TryComp(synth.Owner, out MobStateComponent? mobState) && mobState.CurrentState == MobState.Dead)
            return;

        SetUnpowered(synth, args.Charge <= 0f);
        _alerts.ShowBatteryAlert(synth, args.Charge, args.MaxCharge);
    }

    private bool TryGetSynthFromBattery(EntityUid battery, out Entity<SynthBatteryComponent> synth)
    {
        synth = default;

        if (!_container.TryGetContainingContainer(battery, out var container) ||
            !TryComp(container.Owner, out BodyPartComponent? part) ||
            part.Body is not { } body ||
            !TryComp(body, out SynthBatteryComponent? synthBattery))
            return false;

        synth = (body, synthBattery);
        return true;
    }

    private void OnBeingGibbed(Entity<SynthBatteryComponent> ent, ref BeingGibbedEvent args)
    {
        if (!_synthBattery.TryGetBatteryContainer(ent.Owner, ent.Comp.OrganSlot, out _, out var container))
            return;

        for (var i = container.ContainedEntities.Count - 1; i >= 0; i--)
        {
            var contained = container.ContainedEntities[i];

            if (!HasComp<BatteryComponent>(contained))
                continue;

            if (_container.Remove(contained, container, destination: Transform(ent).Coordinates))
                args.GibbedParts.Add(contained);
        }
    }
}
