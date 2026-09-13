using System.Diagnostics.CodeAnalysis;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.DoAfter;
using Content.Shared.Power.Components;
using Content.Shared._EinsteinEngines.Silicon;
using Content.Shared._EinsteinEngines.Silicon.Components; // WOLFGATE
using Content.Shared._WF.Power; // WOLFGATE
using Content.Shared.Verbs;
using Robust.Shared.Utility;
using Content.Server._EinsteinEngines.Silicon.Charge;
using Content.Server.Power.EntitySystems;
using Content.Server.Popups;
using Content.Server.PowerCell;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Content.Server._EinsteinEngines.Power.Components;
using Content.Server._EinsteinEngines.Silicon;

namespace Content.Server._EinsteinEngines.Power;

public sealed partial class BatteryDrinkerSystem : EntitySystem
{
    [Dependency] private ItemSlotsSystem _slots = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private BatterySystem _battery = default!;
    [Dependency] private SiliconChargeSystem _silicon = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private PowerCellSystem _powerCell = default!;
    [Dependency] private SharedContainerSystem _container = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BatteryComponent, GetVerbsEvent<AlternativeVerb>>(AddAltVerb);

        SubscribeLocalEvent<BatteryDrinkerComponent, BatteryDrinkerDoAfterEvent>(OnDoAfter);
    }

    private void AddAltVerb(EntityUid uid, BatteryComponent batteryComponent, GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        if (!TryComp<BatteryDrinkerComponent>(args.User, out var drinkerComp) ||
            !TestDrinkableBattery(uid, drinkerComp) ||
            !TryGetDrinkerBattery(args.User, out var drinkerBattery)) // WOLFGATE: was silicon-only
            return;

        // WOLFGATE: don't offer to drain your own cell.
        if (drinkerBattery.Value.Owner == uid)
            return;

        AlternativeVerb verb = new()
        {
            Act = () => DrinkBattery(uid, args.User, drinkerComp),
            Text = Loc.GetString("battery-drinker-verb-drink"),
            Icon = new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/VerbIcons/smite.svg.192dpi.png")),
        };

        args.Verbs.Add(verb);
    }

    /// <summary>
    /// WOLFGATE: finds the battery a drinker stores its charge in. Synths keep their cell in an
    /// organ slot, so the event gives their system first refusal before the stock silicon lookup.
    /// </summary>
    private bool TryGetDrinkerBattery(EntityUid drinker, [NotNullWhen(true)] out Entity<BatteryComponent>? battery)
    {
        battery = null;

        var ev = new GetDrinkerBatteryEvent();
        RaiseLocalEvent(drinker, ref ev);

        if (ev.Battery is { } answered)
        {
            battery = answered;
            return true;
        }

        if (!HasComp<SiliconComponent>(drinker))
            return false;

        if (TryComp<BatteryComponent>(drinker, out var ownBattery))
        {
            battery = (drinker, ownBattery);
            return true;
        }

        if (_powerCell.TryGetBatteryFromSlot(drinker, out var cell, out var cellBattery))
        {
            battery = (cell.Value, cellBattery);
            return true;
        }

        return false;
    }

    private bool TestDrinkableBattery(EntityUid target, BatteryDrinkerComponent drinkerComp)
    {
        if (!drinkerComp.DrinkAll && !HasComp<BatteryDrinkerSourceComponent>(target))
            return false;

        return true;
    }

    private void DrinkBattery(EntityUid target, EntityUid user, BatteryDrinkerComponent drinkerComp)
    {
        var doAfterTime = drinkerComp.DrinkSpeed;

        if (TryComp<BatteryDrinkerSourceComponent>(target, out var sourceComp))
            doAfterTime *= sourceComp.DrinkSpeedMulti;
        else
            doAfterTime *= drinkerComp.DrinkAllMultiplier;

        var args = new DoAfterArgs(EntityManager, user, doAfterTime, new BatteryDrinkerDoAfterEvent(), user, target) // TODO: Make this doafter loop, once we merge Upstream.
        {
            BreakOnDamage = true,
            BreakOnMove = true,
            Broadcast = false,
            DistanceThreshold = 1.35f,
            RequireCanInteract = true,
            CancelDuplicate = false,
            MultiplyDelay = false, // Goobstation
        };

        _doAfter.TryStartDoAfter(args);
    }

    private void OnDoAfter(EntityUid uid, BatteryDrinkerComponent drinkerComp, DoAfterEvent args)
    {
        if (args.Cancelled || args.Target == null)
            return;

        var source = args.Target.Value;
        var drinker = uid;

        if (!TryComp<BatteryComponent>(source, out var sourceBattery))
            return;

        // WOLFGATE: resolve the drinker's own store, wherever it lives.
        if (!TryGetDrinkerBattery(drinker, out var drinkerBattery))
            return;

        var drinkerBatteryComponent = drinkerBattery.Value.Comp;

        TryComp<BatteryDrinkerSourceComponent>(source, out var sourceComp);

        var amountToDrink = drinkerComp.DrinkMultiplier * 1000;

        amountToDrink = MathF.Min(amountToDrink, sourceBattery.CurrentCharge);
        amountToDrink = MathF.Min(amountToDrink, drinkerBatteryComponent.MaxCharge - drinkerBatteryComponent.CurrentCharge);

        if (sourceComp != null && sourceComp.MaxAmount > 0)
            amountToDrink = MathF.Min(amountToDrink, (float) sourceComp.MaxAmount);

        if (amountToDrink <= 0)
        {
            // WOLFGATE: say which side is the problem instead of always blaming the source.
            var message = sourceBattery.CurrentCharge <= 0
                ? Loc.GetString("battery-drinker-empty", ("target", source))
                : Loc.GetString("battery-drinker-full");

            _popup.PopupEntity(message, drinker, drinker);
            return;
        }

        if (_battery.TryUseCharge(source, amountToDrink))
            _battery.SetCharge(drinkerBattery.Value.Owner, drinkerBatteryComponent.CurrentCharge + amountToDrink, drinkerBatteryComponent);
        else
        {
            _battery.SetCharge(drinkerBattery.Value.Owner, sourceBattery.CurrentCharge + drinkerBatteryComponent.CurrentCharge, drinkerBatteryComponent);
            _battery.SetCharge(source, 0);
        }

        if (sourceComp != null && sourceComp.DrinkSound != null){
            _popup.PopupEntity(Loc.GetString("ipc-recharge-tip"), drinker, drinker, PopupType.SmallCaution);
            _audio.PlayPvs(sourceComp.DrinkSound, source);
            Spawn("EffectSparks", Transform(source).Coordinates);
        }
    }
}
