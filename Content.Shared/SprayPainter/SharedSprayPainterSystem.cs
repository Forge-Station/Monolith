/// Forge-Chane-Start
using Content.Shared.Administration.Logs;
using Content.Shared.Charges.Components;
using Content.Shared.Charges.Systems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.SprayPainter.Components;
using Content.Shared.SprayPainter.Prototypes;
using Content.Shared.Verbs;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using System.Linq;

namespace Content.Shared.SprayPainter;

/// <summary>
/// System for painting paintable objects using a spray painter.
/// Pipes are handled serverside since AtmosPipeColorSystem is server only.
/// </summary>
public abstract partial class SharedSprayPainterSystem : EntitySystem
{
    private static readonly ProtoId<ReagentPrototype>[] WashReagents =
    [
        "Water",
        "SpaceCleaner",
    ];

    private static readonly FixedPoint2 WashCost = 5;
    private static readonly SoundSpecifier WashSound = new SoundPathSpecifier("/Audio/Effects/Fluids/watersplash.ogg");

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] protected ISharedAdminLogManager AdminLogger = default!;
    [Dependency] protected SharedAppearanceSystem Appearance = default!;
    [Dependency] protected SharedAudioSystem Audio = default!;
    [Dependency] protected SharedChargesSystem Charges = default!;
    [Dependency] protected SharedDoAfterSystem DoAfter = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IPrototypeManager _protoMan = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SprayPainterComponent, MapInitEvent>(OnMapInit);

        SubscribeLocalEvent<SprayPainterComponent, SprayPainterDoAfterEvent>(OnPainterDoAfter);
        SubscribeLocalEvent<SprayPainterComponent, GetVerbsEvent<AlternativeVerb>>(OnPainterGetAltVerbs);
        SubscribeLocalEvent<PaintableComponent, InteractUsingEvent>(OnPaintableInteract);
        SubscribeLocalEvent<PaintedComponent, InteractUsingEvent>(OnPaintedInteract);
        SubscribeLocalEvent<PaintedComponent, ExaminedEvent>(OnPainedExamined);

        Subs.BuiEvents<SprayPainterComponent>(SprayPainterUiKey.Key,
            subs =>
            {
                subs.Event<SprayPainterSetPaintableStyleMessage>(OnSetPaintable);
                subs.Event<SprayPainterSetPipeColorMessage>(OnSetPipeColor);
                subs.Event<SprayPainterTabChangedMessage>(OnTabChanged);
                subs.Event<SprayPainterSetDecalMessage>(OnSetDecal);
                subs.Event<SprayPainterSetDecalColorMessage>(OnSetDecalColor);
                subs.Event<SprayPainterSetDecalAngleMessage>(OnSetDecalAngle);
                subs.Event<SprayPainterSetDecalSnapMessage>(OnSetDecalSnap);
                subs.Event<SprayPainterSetDecalColorPickerMessage>(OnSetDecalColorPicker);
            });
    }

    private void OnMapInit(Entity<SprayPainterComponent> ent, ref MapInitEvent args)
    {
        bool stylesByGroupPopulated = false;
        foreach (var groupProto in _protoMan.EnumeratePrototypes<PaintableGroupPrototype>())
        {
            ent.Comp.StylesByGroup[groupProto.ID] = groupProto.DefaultStyle;
            stylesByGroupPopulated = true;
        }
        if (stylesByGroupPopulated)
            Dirty(ent);

        if (ent.Comp.ColorPalette.Count > 0)
            SetPipeColor(ent, ent.Comp.ColorPalette.First().Key);
    }

    private void SetPipeColor(Entity<SprayPainterComponent> ent, string? paletteKey)
    {
        if (paletteKey == null || paletteKey == ent.Comp.PickedColor)
            return;

        if (!ent.Comp.ColorPalette.ContainsKey(paletteKey))
            return;

        ent.Comp.PickedColor = paletteKey;
        Dirty(ent);
    }

    #region Interaction

    private void OnPainterDoAfter(Entity<SprayPainterComponent> ent, ref SprayPainterDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled)
            return;

        if (args.Args.Target is not { } target)
            return;

        if (!HasComp<PaintableComponent>(target))
            return;

        if (!TryComp<LimitedChargesComponent>(ent, out var charges)
            || Charges.IsEmpty(ent, charges)
            || Charges.HasInsufficientCharges(ent, args.Cost, charges))
            return;

        Appearance.SetData(target, PaintableVisuals.Prototype, args.Prototype);
        Audio.PlayPredicted(ent.Comp.SpraySound, ent, args.Args.User);
        Charges.UseCharges(ent, args.Cost, charges);

        var paintedComponent = EnsureComp<PaintedComponent>(target);
        paintedComponent.DryTime = _timing.CurTime + ent.Comp.FreshPaintDuration;

        var ev = new EntityPaintedEvent(
            User: args.User,
            Tool: ent,
            Prototype: args.Prototype,
            Group: args.Group);
        RaiseLocalEvent(target, ref ev);

        AdminLogger.Add(LogType.Action,
            LogImpact.Low,
            $"{ToPrettyString(args.Args.User):user} painted {ToPrettyString(args.Args.Target.Value):target}");

        args.Handled = true;
        Dirty(target, paintedComponent);
    }

    private void OnPainterGetAltVerbs(Entity<SprayPainterComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !args.Using.HasValue)
            return;

        var user = args.User;

        AlternativeVerb verb = new()
        {
            Text = Loc.GetString("spray-painter-verb-toggle-decals"),
            Icon = new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/VerbIcons/settings.svg.192dpi.png")),
            Act = () => TogglePaintDecals(ent, user),
            Impact = LogImpact.Low
        };
        args.Verbs.Add(verb);
    }

    /// <summary>
    /// Toggles whether clicking on the floor paints a decal or not.
    /// </summary>
    private void TogglePaintDecals(Entity<SprayPainterComponent> ent, EntityUid user)
    {
        if (!_timing.IsFirstTimePredicted)
            return;

        var pitch = 1.0f;
        switch (ent.Comp.DecalMode)
        {
            case DecalPaintMode.Off:
            default:
                ent.Comp.DecalMode = DecalPaintMode.Add;
                pitch = 1.0f;
                break;
            case DecalPaintMode.Add:
                ent.Comp.DecalMode = DecalPaintMode.Remove;
                pitch = 1.2f;
                break;
            case DecalPaintMode.Remove:
                ent.Comp.DecalMode = DecalPaintMode.Off;
                pitch = 0.8f;
                break;
        }
        Dirty(ent);

        // Make the machine beep.
        Audio.PlayPredicted(ent.Comp.SoundSwitchDecalMode, ent, user, ent.Comp.SoundSwitchDecalMode.Params.WithPitchScale(pitch));

        // Decal painting mode and the eyedropper fight over floor clicks — turn the picker off.
        if (ent.Comp.ColorPickerEnabled)
        {
            ent.Comp.ColorPickerEnabled = false;
            Dirty(ent);
        }
    }

    /// <summary>
    /// Handles spray paint interactions with an object.
    /// An object must belong to a spray paintable group to be painted, and the painter must have sufficient ammo to paint it.
    /// </summary>
    private void OnPaintableInteract(Entity<PaintableComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (!TryComp<SprayPainterComponent>(args.Used, out var painter))
            return;

        if (ent.Comp.Group is not { } group
            || !painter.StylesByGroup.TryGetValue(group, out var selectedStyle)
            || !_protoMan.Resolve(group, out PaintableGroupPrototype? targetGroup))
            return;

        // Valid paint target.
        args.Handled = true;

        if (!TryComp<LimitedChargesComponent>(args.Used, out var charges))
            return;

        if (Charges.IsEmpty(args.Used, charges) || Charges.HasInsufficientCharges(args.Used, targetGroup.Cost, charges))
        {
            var msg = Loc.GetString("spray-painter-interact-no-charges");
            _popup.PopupEntity(msg, args.User, args.User);
            return;
        }

        if (!targetGroup.Styles.TryGetValue(selectedStyle, out var proto))
        {
            var msg = Loc.GetString("spray-painter-style-not-available");
            _popup.PopupEntity(msg, args.User, args.User);
            return;
        }

        var doAfterEventArgs = new DoAfterArgs(EntityManager,
            args.User,
            targetGroup.Time,
            new SprayPainterDoAfterEvent(proto, group, targetGroup.Cost),
            args.Used,
            target: ent,
            used: args.Used)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        };

        if (!DoAfter.TryStartDoAfter(doAfterEventArgs, out _))
            return;

        // Log the attempt
        AdminLogger.Add(LogType.Action,
            LogImpact.Low,
            $"{ToPrettyString(args.User):user} is painting {ToPrettyString(ent):target} to '{selectedStyle}' at {Transform(ent).Coordinates:targetlocation}");
    }

    /// <summary>
    /// Prints out if an object has been painted recently.
    /// </summary>
    private void OnPainedExamined(Entity<PaintedComponent> ent, ref ExaminedEvent args)
    {
        // If the paint's dried, it isn't detectable.
        if (_timing.CurTime > ent.Comp.DryTime)
            return;

        args.PushText(Loc.GetString("spray-painter-on-examined-painted-message"));
    }

    /// <summary>
    /// Allows washing fresh paint with water or space cleaner before it dries.
    /// </summary>
    private void OnPaintedInteract(Entity<PaintedComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (!_solutions.TryGetDrainableSolution(args.Used, out var soln, out var solution))
            return;

        string? washReagent = null;
        foreach (var reagent in WashReagents)
        {
            if (solution.GetTotalPrototypeQuantity(reagent) >= WashCost)
            {
                washReagent = reagent;
                break;
            }
        }

        if (washReagent == null)
            return;

        // Valid wash attempt — claim the interaction so spray bottles don't also fire.
        args.Handled = true;

        if (_timing.CurTime > ent.Comp.DryTime)
        {
            _popup.PopupClient(Loc.GetString("spray-painter-wash-paint-dried"), ent, args.User);
            return;
        }

        if (!_solutions.RemoveReagent(soln.Value, washReagent, WashCost))
            return;

        // Force visualizers to re-apply the entity's own prototype so the painted RSI is undone.
        // Leaving Prototype set to the entity itself is intentional: RemoveData alone would leave the
        // already-swapped sprite untouched.
        if (Prototype(ent)?.ID is { } protoId)
            Appearance.SetData(ent, PaintableVisuals.Prototype, protoId);

        RemCompDeferred<PaintedComponent>(ent);

        Audio.PlayPredicted(WashSound, ent, args.User);
        _popup.PopupClient(Loc.GetString("spray-painter-wash-paint-success"), ent, args.User);

        AdminLogger.Add(LogType.Action,
            LogImpact.Low,
            $"{ToPrettyString(args.User):user} washed paint off {ToPrettyString(ent):target}");
    }

    #endregion Interaction

    #region UI

    /// <summary>
    /// Sets the style that a particular type of paintable object (e.g. lockers) should be painted in.
    /// </summary>
    private void OnSetPaintable(Entity<SprayPainterComponent> ent, ref SprayPainterSetPaintableStyleMessage args)
    {
        if (!ent.Comp.StylesByGroup.ContainsKey(args.Group))
            return;

        ent.Comp.StylesByGroup[args.Group] = args.Style;
        Dirty(ent);
    }

    /// <summary>
    /// Changes the color to paint pipes in.
    /// </summary>
    private void OnSetPipeColor(Entity<SprayPainterComponent> ent, ref SprayPainterSetPipeColorMessage args)
    {
        SetPipeColor(ent, args.Key);
    }

    /// <summary>
    /// Tracks the tab the spray painter was on.
    /// </summary>
    private void OnTabChanged(Entity<SprayPainterComponent> ent, ref SprayPainterTabChangedMessage args)
    {
        ent.Comp.SelectedTab = args.Index;
        Dirty(ent);
    }

    /// <summary>
    /// Sets the decal prototype to paint.
    /// </summary>
    private void OnSetDecal(Entity<SprayPainterComponent> ent, ref SprayPainterSetDecalMessage args)
    {
        ent.Comp.SelectedDecal = args.DecalPrototype;
        Dirty(ent);
    }

    /// <summary>
    /// Sets the angle to paint decals at.
    /// </summary>
    private void OnSetDecalAngle(Entity<SprayPainterComponent> ent, ref SprayPainterSetDecalAngleMessage args)
    {
        ent.Comp.SelectedDecalAngle = args.Angle;
        Dirty(ent);
    }

    /// <summary>
    /// Enables or disables snap-to-grid when painting decals.
    /// </summary>
    private void OnSetDecalSnap(Entity<SprayPainterComponent> ent, ref SprayPainterSetDecalSnapMessage args)
    {
        ent.Comp.SnapDecals = args.Snap;
        Dirty(ent);
    }

    /// <summary>
    /// Enables or disables the decal colour picker.
    /// </summary>
    private void OnSetDecalColorPicker(Entity<SprayPainterComponent> ent, ref SprayPainterSetDecalColorPickerMessage args)
    {
        ent.Comp.ColorPickerEnabled = args.Toggle;
        Dirty(ent);
    }

    /// <summary>
    /// Sets the decal to paint on the ground.
    /// </summary>
    private void OnSetDecalColor(Entity<SprayPainterComponent> ent, ref SprayPainterSetDecalColorMessage args)
    {
        ent.Comp.SelectedDecalColor = args.Color;
        Dirty(ent);
    }

    protected virtual void UpdateUi(Entity<SprayPainterComponent> ent)
    {
    }

    #endregion
}
/// Forge-Chane-End
///
