using Content.Server.DoAfter;
using Content.Server.Popups;
using Content.Shared._Forge.Bioscanner;
using Content.Shared._Mono.CorticalBorer;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Robust.Server.Audio;

namespace Content.Server._Forge.Bioscanner;

public sealed partial class BioscannerSystem : EntitySystem
{
    [Dependency] private DoAfterSystem _doafter = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private AudioSystem _audio = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BioscannerComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<BioscannerComponent, BioscannerDoAfter>(OnDoAfter);
        SubscribeLocalEvent<BioscannerComponent, ExaminedEvent>(OnExamined);
    }

    private void OnAfterInteract(EntityUid uid, BioscannerComponent component, ref AfterInteractEvent args)
    {
        if (!args.CanReach || args.Target is null)
            return;

        var doAfterArgs = new DoAfterArgs(EntityManager,
            args.User,
            component.DoAfterDuration,
            new BioscannerDoAfter(),
            uid,
            args.Target,
            args.Used)
        {
            BreakOnMove = true,
            NeedHand = true,
            BlockDuplicate = true,
            BreakOnHandChange = true,
        };

        _doafter.TryStartDoAfter(doAfterArgs);

        var popup = Loc.GetString("check-borer-start", ("user", args.User), ("target", args.Target.Value));
        _popup.PopupEntity(popup, uid, PopupType.SmallCaution);
    }

    private void OnDoAfter(EntityUid uid, BioscannerComponent component, ref BioscannerDoAfter args)
    {
        if (args.Cancelled
        || args.Handled
        || args.Target is not { } target)
        return;

        _audio.PlayPvs(component.ScanningEndSound, uid);
        component.LastTarget = target;

        if (!HasComp<CorticalBorerInfestedComponent>(target))
        {
            var clearString = Loc.GetString("check-borer-clear");
            _popup.PopupEntity(clearString, uid, PopupType.Medium);
            component.WasInfested = false;

            return;
        }

        var infestedString = Loc.GetString("check-borer-infested");
        _popup.PopupEntity(infestedString, uid, PopupType.MediumCaution);
        component.WasInfested = true;
    }

    private void OnExamined(EntityUid uid, BioscannerComponent component, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange
        || component.LastTarget is not { } lastTarget)
            return;

        var target = Loc.GetString("check-borer-examined-target", ("target", lastTarget));
        var infectionStatus = Loc.GetString("check-borer-examined-infection-status", 
            ("status", component.WasInfested 
                ? Loc.GetString("check-borer-examined-status-infested") 
                : Loc.GetString("check-borer-examined-status-clean")));

        args.PushMarkup(target, 1);
        args.PushMarkup(infectionStatus);
    }
}