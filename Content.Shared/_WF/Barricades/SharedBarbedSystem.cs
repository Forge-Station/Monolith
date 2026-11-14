using Content.Shared.Climbing.Events;
using Content.Shared.Damage;
using Content.Shared.DoAfter;
using Content.Shared.Doors;
using Content.Shared.Doors.Components;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Content.Shared.Tools.Components;
using Content.Shared.Tools.Systems;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;

namespace Content.Shared._WF.Barricades;

/// <summary>Stringing and cutting barbed wire on barricades, and what the wire does.</summary>
public abstract partial class SharedBarbedSystem : EntitySystem
{
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedStackSystem _stacks = default!;
    [Dependency] private SharedToolSystem _tool = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<BarbedComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<BarbedComponent, AttackedEvent>(OnAttacked);
        SubscribeLocalEvent<BarbedComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<BarbedComponent, DoAfterAttemptEvent<BarbedDoAfterEvent>>(OnDoAfterAttempt);
        SubscribeLocalEvent<BarbedComponent, BarbedDoAfterEvent>(OnDoAfter);
        SubscribeLocalEvent<BarbedComponent, CutBarbedDoAfterEvent>(OnCutDoAfter);
        SubscribeLocalEvent<BarbedComponent, DoorStateChangedEvent>(OnDoorStateChanged);
        SubscribeLocalEvent<BarbedComponent, AttemptClimbEvent>(OnClimbAttempt);
        SubscribeLocalEvent<BarbedComponent, RMCConstructionUpgradedEvent>(OnConstructionUpgraded);
    }

    private void OnExamined(Entity<BarbedComponent> ent, ref ExaminedEvent args)
    {
        if (!ent.Comp.IsBarbed)
            return;

        using (args.PushGroup(nameof(SharedBarbedSystem)))
        {
            args.PushMarkup(Loc.GetString("rmc-barricade-examine-barbed"));
        }
    }

    private void OnAttacked(Entity<BarbedComponent> ent, ref AttackedEvent args)
    {
        if (!ent.Comp.IsBarbed)
            return;

        _damageable.TryChangeDamage(args.User, ent.Comp.ThornsDamage, origin: ent);
        _popup.PopupClient(Loc.GetString("barbed-wire-damage"), ent, args.User, PopupType.SmallCaution);
    }

    private void OnInteractUsing(Entity<BarbedComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (HasComp<BarbedWireComponent>(args.Used))
        {
            args.Handled = true;
            if (ent.Comp.IsBarbed)
            {
                _popup.PopupClient(Loc.GetString("barbed-wire-slot-insert-full"), ent, args.User);
                return;
            }

            var doAfter = new DoAfterArgs(EntityManager, args.User, ent.Comp.WireTime, new BarbedDoAfterEvent(), ent, ent, used: args.Used)
            {
                BreakOnMove = true,
                BreakOnDamage = true,
                NeedHand = true,
                AttemptFrequency = AttemptFrequency.EveryTick,
                CancelDuplicate = false,
                DuplicateCondition = DuplicateConditions.SameTarget,
            };

            if (_doAfter.TryStartDoAfter(doAfter))
                _popup.PopupClient(Loc.GetString("barbed-wire-slot-wiring"), ent, args.User);

            return;
        }

        if (!ent.Comp.IsBarbed ||
            !TryComp<ToolComponent>(args.Used, out var tool) ||
            !_tool.HasQuality(args.Used, ent.Comp.RemoveQuality, tool))
        {
            return;
        }

        args.Handled = true;
        _popup.PopupClient(Loc.GetString("barbed-wire-cutting-action-begin"), ent, args.User);
        var cutDoAfter = new DoAfterArgs(EntityManager, args.User, ent.Comp.CutTime, new CutBarbedDoAfterEvent(), ent, used: args.Used)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        };
        _doAfter.TryStartDoAfter(cutDoAfter);
    }

    private void OnDoAfterAttempt(Entity<BarbedComponent> ent, ref DoAfterAttemptEvent<BarbedDoAfterEvent> args)
    {
        if (ent.Comp.IsBarbed)
            args.Cancel();
    }

    private void OnDoAfter(Entity<BarbedComponent> ent, ref BarbedDoAfterEvent args)
    {
        if (args.Used == null || args.Cancelled || args.Handled || ent.Comp.IsBarbed)
            return;

        args.Handled = true;

        if (TryComp<StackComponent>(args.Used.Value, out var stack))
            _stacks.Use(args.Used.Value, 1, stack);

        ent.Comp.IsBarbed = true;
        Dirty(ent);
        UpdateBarricade(ent, true);

        _audio.PlayPredicted(ent.Comp.BarbSound, ent.Owner, args.User);
        _popup.PopupClient(Loc.GetString("barbed-wire-slot-insert-success"), ent.Owner, args.User);
    }

    private void OnCutDoAfter(Entity<BarbedComponent> ent, ref CutBarbedDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || !ent.Comp.IsBarbed)
            return;

        args.Handled = true;

        ent.Comp.IsBarbed = false;
        Dirty(ent);
        UpdateBarricade(ent, true);

        _audio.PlayPredicted(ent.Comp.CutSound, ent.Owner, args.User);
        _popup.PopupClient(Loc.GetString("barbed-wire-cutting-action-finish"), ent.Owner, args.User);

        if (_net.IsClient)
            return;

        Spawn(ent.Comp.Spawn, _transform.GetMoverCoordinates(ent));
    }

    private void OnDoorStateChanged(Entity<BarbedComponent> ent, ref DoorStateChangedEvent args)
    {
        UpdateBarricade(ent);
    }

    private void OnClimbAttempt(Entity<BarbedComponent> ent, ref AttemptClimbEvent args)
    {
        if (!ent.Comp.IsBarbed)
            return;

        args.Cancelled = true;
        _popup.PopupClient(Loc.GetString("barbed-wire-cant-climb"), ent.Owner, args.User);
    }

    private void OnConstructionUpgraded(Entity<BarbedComponent> ent, ref RMCConstructionUpgradedEvent args)
    {
        CopyBarbed(ent, args.New);
    }

    /// <summary>Carries the wire over to the entity a barricade turned into.</summary>
    protected void CopyBarbed(Entity<BarbedComponent> ent, EntityUid newUid)
    {
        if (!ent.Comp.IsBarbed)
            return;

        var newComp = EnsureComp<BarbedComponent>(newUid);
        newComp.IsBarbed = true;
        Dirty(newUid, newComp);
        UpdateBarricade((newUid, newComp), true);
    }

    protected void UpdateBarricade(Entity<BarbedComponent> ent, bool updateBarbed = false)
    {
        var open = TryComp(ent, out DoorComponent? door) && door.State == DoorState.Open;

        var visual = (ent.Comp.IsBarbed, open) switch
        {
            (true, true) => BarbedWireVisuals.WiredOpen,
            (true, false) => BarbedWireVisuals.WiredClosed,
            _ => BarbedWireVisuals.UnWired,
        };

        if (updateBarbed)
        {
            var ev = new BarbedStateChangedEvent();
            RaiseLocalEvent(ent, ref ev);
        }

        _appearance.SetData(ent, BarbedWireVisualLayers.Wire, visual);
    }
}
