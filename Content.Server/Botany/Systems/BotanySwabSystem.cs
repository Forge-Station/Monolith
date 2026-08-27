using Content.Server.Botany;
using Content.Server.Botany.Components;
using Content.Server.Botany.Systems;
using Content.Server.Popups;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Swab;
using Robust.Shared.Timing;

namespace Content.Server.Botany.Systems;

public sealed partial class BotanySwabSystem : EntitySystem
{
    [Dependency] private SharedDoAfterSystem _doAfterSystem = default!;
    [Dependency] private PopupSystem _popupSystem = default!;
    [Dependency] private MutationSystem _mutationSystem = default!;
    [Dependency] private PlantHolderSystem _plantHolder = default!;
    [Dependency] private BotanySystem _botany = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BotanySwabComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<BotanySwabComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<BotanySwabComponent, BotanySwabDoAfterEvent>(OnDoAfter);
    }

    private void OnExamined(EntityUid uid, BotanySwabComponent swab, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        if (swab.SeedData == null)
        {
            args.PushMarkup(Loc.GetString("swab-unused"));
            return;
        }

        args.PushMarkup(Loc.GetString("swab-used-line", ("name", Loc.GetString(swab.SeedData.DisplayName))));
    }

    private void OnAfterInteract(EntityUid uid, BotanySwabComponent swab, AfterInteractEvent args)
    {
        if (args.Handled || args.Target == null || !args.CanReach)
            return;

        if (!HasComp<PlantHolderComponent>(args.Target) && !HasComp<SeedComponent>(args.Target))
            return;

        args.Handled = true;
        _doAfterSystem.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, swab.SwabDelay, new BotanySwabDoAfterEvent(), uid, target: args.Target, used: uid)
        {
            BreakOnMove = true,
            NeedHand = true,
        });
    }

    private void OnDoAfter(EntityUid uid, BotanySwabComponent swab, DoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Args.Target is not { } target)
            return;

        if (TryComp<SeedComponent>(target, out var packet))
        {
            SwabPacket(swab, target, packet, args.Args.User);
            args.Handled = true;
            return;
        }

        if (!TryComp<PlantHolderComponent>(target, out var plant))
            return;

        if (swab.SeedData == null)
        {
            CollectPollen(swab, target, plant, args.Args.User);
        }
        else if (plant.Seed == null)
        {
            PlantFromPollen(swab, target, plant, args.Args.User);
        }
        else
        {
            GraftPollen(swab, target, plant, args.Args.User);
        }

        args.Handled = true;
    }

    private void SwabPacket(BotanySwabComponent swab, EntityUid packet, SeedComponent seedComp, EntityUid user)
    {
        if (!_botany.TryGetSeed(seedComp, out var host))
        {
            _popupSystem.PopupEntity(Loc.GetString("botany-swab-nothing"), packet, user);
            return;
        }

        if (host.PreventSwabbing)
        {
            _popupSystem.PopupEntity(Loc.GetString("botany-cannot-be-swabbed-message"), packet, user);
            return;
        }

        if (swab.SeedData == null)
        {
            swab.SeedData = _mutationSystem.Duplicate(host);
            _popupSystem.PopupEntity(Loc.GetString("botany-swab-from-packet"), packet, user);
            return;
        }

        var grafted = _mutationSystem.Graft(swab.SeedData, host);
        _botany.ReplacePacketSeed(seedComp, grafted);
        _popupSystem.PopupEntity(Loc.GetString("botany-swab-graft-packet", ("name", Loc.GetString(grafted.DisplayName))), packet, user, PopupType.Medium);
    }

    private void CollectPollen(BotanySwabComponent swab, EntityUid tray, PlantHolderComponent plant, EntityUid user)
    {
        if (plant.Seed == null || plant.Dead)
        {
            _popupSystem.PopupEntity(Loc.GetString("botany-swab-nothing"), tray, user);
            return;
        }

        if (plant.Seed.PreventSwabbing)
        {
            _popupSystem.PopupEntity(Loc.GetString("botany-cannot-be-swabbed-message"), tray, user);
            return;
        }

        swab.SeedData = _mutationSystem.Duplicate(plant.Seed);
        _popupSystem.PopupEntity(Loc.GetString("botany-swab-from"), tray, user);
    }

    private void PlantFromPollen(BotanySwabComponent swab, EntityUid tray, PlantHolderComponent plant, EntityUid user)
    {
        if (swab.SeedData == null)
            return;

        plant.Seed = _mutationSystem.Duplicate(swab.SeedData);
        plant.Dead = false;
        plant.Age = 1;
        plant.Harvest = false;
        plant.Health = plant.Seed.Endurance;
        plant.LastCycle = _timing.CurTime;
        _plantHolder.CheckLevelSanity(tray, plant);
        _plantHolder.UpdateSprite(tray, plant);
        _popupSystem.PopupEntity(Loc.GetString("botany-swab-planted", ("name", Loc.GetString(plant.Seed.DisplayName))), tray, user, PopupType.Medium);
    }

    private void GraftPollen(BotanySwabComponent swab, EntityUid tray, PlantHolderComponent plant, EntityUid user)
    {
        if (swab.SeedData == null || plant.Seed == null)
            return;

        if (plant.Dead)
        {
            _popupSystem.PopupEntity(Loc.GetString("botany-swab-dead"), tray, user);
            return;
        }

        if (plant.Seed.PreventSwabbing)
        {
            _popupSystem.PopupEntity(Loc.GetString("botany-cannot-be-swabbed-message"), tray, user);
            return;
        }

        _plantHolder.EnsureUniqueSeed(tray, plant);
        if (plant.Seed == null)
            return;

        plant.Seed = _mutationSystem.Graft(swab.SeedData, plant.Seed);
        _plantHolder.UpdateSprite(tray, plant);
        _popupSystem.PopupEntity(Loc.GetString("botany-swab-graft", ("name", Loc.GetString(plant.Seed.DisplayName))), tray, user, PopupType.Medium);
    }
}
