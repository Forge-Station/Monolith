using Content.Shared.ActionBlocker;
using Content.Shared.Damage;
using Content.Shared.DoAfter;
using Content.Shared.DragDrop;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Content.Shared.Tools.Systems;
using Content.Shared.Verbs;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared._WF.Barricades;

/// <summary>Deploying, collapsing, merging and welding MB-6 folding barricades.</summary>
public sealed partial class RMCFoldingBarricadeSystem : EntitySystem
{
    [Dependency] private ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private BarricadeSystem _barricade = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedStackSystem _stack = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedToolSystem _tool = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private const string PryingQuality = "Prying";
    private const string WeldingQuality = "Welding";
    private const string DeployedDamageType = "Blunt";

    public override void Initialize()
    {
        SubscribeLocalEvent<RMCFoldingBarricadeStackComponent, ComponentStartup>(OnStackStartup);
        SubscribeLocalEvent<RMCFoldingBarricadeStackComponent, StackCountChangedEvent>(OnStackCountChanged);
        SubscribeLocalEvent<RMCFoldingBarricadeStackComponent, StackSplitEvent>(OnStackSplit);
        SubscribeLocalEvent<RMCFoldingBarricadeStackComponent, UseInHandEvent>(OnStackUseInHand);
        SubscribeLocalEvent<RMCFoldingBarricadeStackComponent, InteractUsingEvent>(OnStackInteractUsing, before: [typeof(SharedStackSystem)]);
        SubscribeLocalEvent<RMCFoldingBarricadeStackComponent, RMCFoldingBarricadeDeployDoAfterEvent>(OnStackDeployDoAfter);
        SubscribeLocalEvent<RMCFoldingBarricadeStackComponent, RMCFoldingBarricadeRepairDoAfterEvent>(OnStackRepairDoAfter);
        SubscribeLocalEvent<RMCFoldingBarricadeStackComponent, ExaminedEvent>(OnStackExamined);

        SubscribeLocalEvent<RMCFoldingBarricadeComponent, InteractUsingEvent>(OnBarricadeInteractUsing, before: [typeof(SharedStackSystem)]);
        SubscribeLocalEvent<RMCFoldingBarricadeComponent, GetVerbsEvent<AlternativeVerb>>(OnBarricadeGetAlternativeVerbs);
        SubscribeLocalEvent<RMCFoldingBarricadeComponent, CanDragEvent>(OnBarricadeCanDrag);
        SubscribeLocalEvent<RMCFoldingBarricadeComponent, CanDropDraggedEvent>(OnBarricadeCanDropDragged);
        SubscribeLocalEvent<RMCFoldingBarricadeComponent, DragDropDraggedEvent>(OnBarricadeDragDropDragged);
        SubscribeLocalEvent<RMCFoldingBarricadeComponent, RMCFoldingBarricadeCollapseDoAfterEvent>(OnBarricadeCollapseDoAfter);
        SubscribeLocalEvent<RMCFoldingBarricadeComponent, ExaminedEvent>(OnBarricadeExamined);
    }

    private void OnStackStartup(Entity<RMCFoldingBarricadeStackComponent> ent, ref ComponentStartup args)
    {
        NormalizeStoredDamage(ent);
    }

    private void OnStackUseInHand(Entity<RMCFoldingBarricadeStackComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        TryStartDeploy(ent, args.User);
    }

    private void OnStackInteractUsing(Entity<RMCFoldingBarricadeStackComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (TryComp(args.Used, out RMCFoldingBarricadeStackComponent? usedFolding))
        {
            args.Handled = true;
            TryMergeStacks((args.Used, usedFolding), ent, args.ClickLocation);
            return;
        }

        if (!_tool.HasQuality(args.Used, WeldingQuality))
            return;

        args.Handled = true;
        TryStartStackRepair(ent, args.User, args.Used);
    }

    private void OnStackDeployDoAfter(Entity<RMCFoldingBarricadeStackComponent> ent, ref RMCFoldingBarricadeDeployDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;

        if (!_net.IsServer)
            return;

        if (!TryComp(ent, out StackComponent? stack) || stack.Count <= 0)
            return;

        if (!CanDeploy(ent, args.User, out var direction, out var popup))
        {
            if (popup != null)
                _popup.PopupEntity(popup, ent, args.User, PopupType.SmallCaution);

            return;
        }

        var damage = PopStoredDamage(ent, stack);
        SetCountPreserving(ent, stack.Count - 1, stack);

        var barricade = SpawnAtPosition(ent.Comp.DeployedPrototype, _transform.GetMoverCoordinates(args.User));
        var barricadeXform = Transform(barricade);
        _transform.SetLocalRotation(barricade, direction.ToAngle(), barricadeXform);

        if (!barricadeXform.Anchored)
            _transform.AnchorEntity((barricade, barricadeXform));

        ApplyStoredDamage(barricade, damage);

        _popup.PopupEntity(Loc.GetString("rmc-folding-barricade-deploy-finish", ("barricade", barricade)), barricade, args.User);
    }

    private void OnStackRepairDoAfter(Entity<RMCFoldingBarricadeStackComponent> ent, ref RMCFoldingBarricadeRepairDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;

        if (!_net.IsServer)
            return;

        NormalizeStoredDamage(ent);

        var repaired = false;
        for (var i = 0; i < ent.Comp.StoredDamage.Count; i++)
        {
            if (ent.Comp.StoredDamage[i] <= 0)
                continue;

            ent.Comp.StoredDamage[i] = MathHelper.Clamp(ent.Comp.StoredDamage[i] - ent.Comp.RepairAmount, 0, ent.Comp.MaxDamage);
            repaired = true;
        }

        if (!repaired)
            return;

        Dirty(ent);

        _popup.PopupEntity(Loc.GetString("rmc-repairable-finish-self", ("target", ent)), args.User, args.User);
        _audio.PlayPvs(ent.Comp.RepairSound, ent);
    }

    private void OnStackExamined(Entity<RMCFoldingBarricadeStackComponent> ent, ref ExaminedEvent args)
    {
        using (args.PushGroup(nameof(RMCFoldingBarricadeStackComponent)))
        {
            args.PushMarkup(Loc.GetString("rmc-folding-barricade-stack-examine-deploy"));

            NormalizeStoredDamage(ent);
            if (IsStackDamaged(ent))
                args.PushMarkup(Loc.GetString("rmc-folding-barricade-stack-examine-damaged"));
        }
    }

    private void OnStackCountChanged(Entity<RMCFoldingBarricadeStackComponent> ent, ref StackCountChangedEvent args)
    {
        if (ent.Comp.SuppressCountChange)
            return;

        if (args.NewCount < args.OldCount)
        {
            var removed = args.OldCount - args.NewCount;
            NormalizeStoredDamage(ent, args.OldCount);

            for (var i = 0; i < removed; i++)
            {
                if (ent.Comp.StoredDamage.Count == 0)
                {
                    ent.Comp.PendingSplitDamage.Insert(0, 0);
                    continue;
                }

                var last = ent.Comp.StoredDamage[^1];
                ent.Comp.StoredDamage.RemoveAt(ent.Comp.StoredDamage.Count - 1);
                ent.Comp.PendingSplitDamage.Insert(0, last);
            }
        }
        else if (args.NewCount > args.OldCount)
        {
            for (var i = 0; i < args.NewCount - args.OldCount; i++)
                ent.Comp.StoredDamage.Add(0);
        }

        NormalizeStoredDamage(ent, args.NewCount);
        Dirty(ent);
    }

    private void OnStackSplit(Entity<RMCFoldingBarricadeStackComponent> ent, ref StackSplitEvent args)
    {
        if (!TryComp(args.NewId, out RMCFoldingBarricadeStackComponent? splitFolding))
            return;

        var newCount = TryComp(args.NewId, out StackComponent? splitStack) ? splitStack.Count : 1;

        var damage = new List<float>(newCount);
        for (var i = 0; i < newCount; i++)
        {
            if (ent.Comp.PendingSplitDamage.Count > 0)
            {
                damage.Add(ClampDamage(ent.Comp.PendingSplitDamage[0], ent.Comp.MaxDamage));
                ent.Comp.PendingSplitDamage.RemoveAt(0);
                continue;
            }

            damage.Add(0);
        }

        splitFolding.StoredDamage = damage;
        NormalizeStoredDamage((args.NewId, splitFolding), splitStack);
        Dirty(args.NewId, splitFolding);

        NormalizeStoredDamage(ent);
        Dirty(ent);
    }

    private void OnBarricadeInteractUsing(Entity<RMCFoldingBarricadeComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !_tool.HasQuality(args.Used, PryingQuality))
            return;

        args.Handled = true;
        TryStartCollapse(ent, args.User, ent.Comp.CrowbarCollapseDelay, args.Used, true);
    }

    private void OnBarricadeGetAlternativeVerbs(Entity<RMCFoldingBarricadeComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("rmc-folding-barricade-collapse-verb"),
            Act = () => TryStartCollapse(ent, user, ent.Comp.CollapseDelay),
            Priority = 1,
        });
    }

    private void OnBarricadeCanDrag(Entity<RMCFoldingBarricadeComponent> ent, ref CanDragEvent args)
    {
        args.Handled = true;
    }

    private void OnBarricadeCanDropDragged(Entity<RMCFoldingBarricadeComponent> ent, ref CanDropDraggedEvent args)
    {
        if (args.User != args.Target)
            return;

        args.CanDrop = true;
        args.Handled = true;
    }

    private void OnBarricadeDragDropDragged(Entity<RMCFoldingBarricadeComponent> ent, ref DragDropDraggedEvent args)
    {
        if (args.User != args.Target)
            return;

        args.Handled = true;
        TryStartCollapse(ent, args.User, ent.Comp.CollapseDelay);
    }

    private void OnBarricadeCollapseDoAfter(Entity<RMCFoldingBarricadeComponent> ent, ref RMCFoldingBarricadeCollapseDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;

        if (!_net.IsServer)
            return;

        var damage = 0f;
        if (TryComp(ent, out DamageableComponent? damageable))
            damage = damageable.TotalDamage.Float();

        ReturnFolded(ent, args.User, ClampDamage(damage, ent.Comp.MaxDamage));

        _popup.PopupEntity(Loc.GetString("rmc-folding-barricade-collapse-finish", ("barricade", ent)), args.User, args.User);
        QueueDel(ent);
    }

    private void OnBarricadeExamined(Entity<RMCFoldingBarricadeComponent> ent, ref ExaminedEvent args)
    {
        using (args.PushGroup(nameof(RMCFoldingBarricadeComponent)))
        {
            args.PushMarkup(Loc.GetString("rmc-folding-barricade-deployed-examine"));
        }
    }

    private void TryStartDeploy(Entity<RMCFoldingBarricadeStackComponent> ent, EntityUid user)
    {
        if (!CanDeploy(ent, user, out _, out var popup))
        {
            if (popup != null && _timing.IsFirstTimePredicted)
                _popup.PopupEntity(popup, ent, user, PopupType.SmallCaution);

            return;
        }

        var doAfter = new DoAfterArgs(EntityManager, user, ent.Comp.DeployDelay, new RMCFoldingBarricadeDeployDoAfterEvent(), ent, ent)
        {
            NeedHand = true,
            BreakOnMove = true,
            BreakOnDamage = true,
            BreakOnHandChange = true,
            BlockDuplicate = true,
            DuplicateCondition = DuplicateConditions.SameEvent,
        };

        if (!_doAfter.TryStartDoAfter(doAfter))
            return;

        _popup.PopupPredicted(
            Loc.GetString("rmc-folding-barricade-deploy-start", ("barricade", ent)),
            Loc.GetString("rmc-folding-barricade-deploy-start-others", ("user", user), ("barricade", ent)),
            user,
            user);
    }

    private bool CanDeploy(Entity<RMCFoldingBarricadeStackComponent> ent, EntityUid user, out Direction direction, out string? popup)
    {
        popup = null;
        var userXform = Transform(user);
        direction = userXform.LocalRotation.GetCardinalDir();

        if (!TryComp(ent, out StackComponent? stack) || stack.Count <= 0)
            return false;

        return _barricade.CanPlaceBarricade(userXform.Coordinates, direction, out popup, CollisionGroup.Impassable);
    }

    private void TryStartStackRepair(Entity<RMCFoldingBarricadeStackComponent> ent, EntityUid user, EntityUid used)
    {
        NormalizeStoredDamage(ent);
        var damaged = GetDamagedCount(ent);
        if (damaged == 0)
        {
            if (_timing.IsFirstTimePredicted)
                _popup.PopupClient(Loc.GetString("rmc-repairable-not-damaged", ("target", ent)), user, user, PopupType.SmallCaution);

            return;
        }

        var delay = ent.Comp.RepairDelayPerDamaged * damaged;
        _tool.UseTool(used, user, ent, delay, WeldingQuality, new RMCFoldingBarricadeRepairDoAfterEvent(), ent.Comp.RepairFuel);
    }

    private void TryStartCollapse(
        Entity<RMCFoldingBarricadeComponent> ent,
        EntityUid user,
        TimeSpan delay,
        EntityUid? used = null,
        bool needHand = false)
    {
        if (!_actionBlocker.CanInteract(user, ent))
            return;

        var doAfter = new DoAfterArgs(EntityManager, user, delay, new RMCFoldingBarricadeCollapseDoAfterEvent(), ent, ent, used: used)
        {
            NeedHand = needHand,
            BreakOnMove = true,
            BreakOnDamage = true,
            BreakOnHandChange = used != null,
            BlockDuplicate = true,
            DuplicateCondition = DuplicateConditions.SameTarget,
        };

        if (!_doAfter.TryStartDoAfter(doAfter))
            return;

        _popup.PopupPredicted(
            Loc.GetString("rmc-folding-barricade-collapse-start", ("barricade", ent)),
            Loc.GetString("rmc-folding-barricade-collapse-start-others", ("user", user), ("barricade", ent)),
            user,
            user);
    }

    /// <summary>Adds the collapsed barricade to a held stack, or puts a new one in the user's hands.</summary>
    private void ReturnFolded(Entity<RMCFoldingBarricadeComponent> ent, EntityUid user, float damage)
    {
        if (TryComp(user, out HandsComponent? hands))
        {
            foreach (var held in _hands.EnumerateHeld(user, hands))
            {
                if (TryComp(held, out RMCFoldingBarricadeStackComponent? heldFolding) &&
                    TryComp(held, out StackComponent? heldStack) &&
                    AddStoredDamageToStack((held, heldFolding), damage, heldStack))
                {
                    return;
                }
            }
        }

        var folded = SpawnAtPosition(ent.Comp.FoldedPrototype, Transform(ent).Coordinates);
        if (TryComp(folded, out RMCFoldingBarricadeStackComponent? folding) &&
            TryComp(folded, out StackComponent? stack))
        {
            folding.StoredDamage.Clear();
            folding.StoredDamage.Add(ClampDamage(damage, folding.MaxDamage));
            SetCountPreserving((folded, folding), 1, stack);
            Dirty(folded, folding);
        }

        _hands.TryPickupAnyHand(user, folded);
    }

    private void TryMergeStacks(
        Entity<RMCFoldingBarricadeStackComponent> donor,
        Entity<RMCFoldingBarricadeStackComponent> recipient,
        EntityCoordinates popupLocation)
    {
        if (donor.Owner == recipient.Owner ||
            !TryComp(donor, out StackComponent? donorStack) ||
            !TryComp(recipient, out StackComponent? recipientStack) ||
            donorStack.StackTypeId != recipientStack.StackTypeId)
        {
            return;
        }

        NormalizeStoredDamage(donor, donorStack);
        NormalizeStoredDamage(recipient, recipientStack);

        var transfer = Math.Min(donorStack.Count, _stack.GetAvailableSpace(recipientStack));
        if (transfer <= 0)
        {
            if (_timing.IsFirstTimePredicted)
                _popup.PopupCoordinates(Loc.GetString("comp-stack-already-full"), popupLocation, PopupType.Small);

            return;
        }

        for (var i = 0; i < transfer; i++)
        {
            var damage = 0f;
            if (donor.Comp.StoredDamage.Count > 0)
            {
                damage = donor.Comp.StoredDamage[^1];
                donor.Comp.StoredDamage.RemoveAt(donor.Comp.StoredDamage.Count - 1);
            }

            recipient.Comp.StoredDamage.Add(damage);
        }

        SetCountPreserving(donor, donorStack.Count - transfer, donorStack);
        SetCountPreserving(recipient, recipientStack.Count + transfer, recipientStack);
        NormalizeStoredDamage(donor, donorStack);
        NormalizeStoredDamage(recipient, recipientStack);

        if (_timing.IsFirstTimePredicted)
            _popup.PopupCoordinates($"+{transfer}", popupLocation, PopupType.Small);
    }

    private bool AddStoredDamageToStack(Entity<RMCFoldingBarricadeStackComponent> ent, float damage, StackComponent stack)
    {
        NormalizeStoredDamage(ent, stack);

        if (_stack.GetAvailableSpace(stack) <= 0)
            return false;

        ent.Comp.StoredDamage.Add(ClampDamage(damage, ent.Comp.MaxDamage));
        SetCountPreserving(ent, stack.Count + 1, stack);
        NormalizeStoredDamage(ent, stack);
        return true;
    }

    private float PopStoredDamage(Entity<RMCFoldingBarricadeStackComponent> ent, StackComponent stack)
    {
        NormalizeStoredDamage(ent, stack);

        var damage = 0f;
        if (ent.Comp.StoredDamage.Count > 0)
        {
            damage = ent.Comp.StoredDamage[^1];
            ent.Comp.StoredDamage.RemoveAt(ent.Comp.StoredDamage.Count - 1);
        }

        Dirty(ent);
        return ClampDamage(damage, ent.Comp.MaxDamage);
    }

    private void NormalizeStoredDamage(Entity<RMCFoldingBarricadeStackComponent> ent)
    {
        NormalizeStoredDamage(ent, TryComp(ent, out StackComponent? stack) ? stack.Count : ent.Comp.StoredDamage.Count);
    }

    private void NormalizeStoredDamage(Entity<RMCFoldingBarricadeStackComponent> ent, StackComponent? stack)
    {
        NormalizeStoredDamage(ent, stack?.Count ?? ent.Comp.StoredDamage.Count);
    }

    /// <summary>Keeps one clamped damage entry per barricade in the stack.</summary>
    private void NormalizeStoredDamage(Entity<RMCFoldingBarricadeStackComponent> ent, int count)
    {
        count = Math.Max(0, count);

        for (var i = 0; i < ent.Comp.StoredDamage.Count; i++)
            ent.Comp.StoredDamage[i] = ClampDamage(ent.Comp.StoredDamage[i], ent.Comp.MaxDamage);

        while (ent.Comp.StoredDamage.Count < count)
            ent.Comp.StoredDamage.Add(0);

        while (ent.Comp.StoredDamage.Count > count)
            ent.Comp.StoredDamage.RemoveAt(ent.Comp.StoredDamage.Count - 1);

        Dirty(ent);
    }

    private void SetCountPreserving(Entity<RMCFoldingBarricadeStackComponent> ent, int count, StackComponent stack)
    {
        ent.Comp.SuppressCountChange = true;
        try
        {
            _stack.SetCount(ent, count, stack);
        }
        finally
        {
            ent.Comp.SuppressCountChange = false;
        }
    }

    private void ApplyStoredDamage(EntityUid target, float damage)
    {
        if (damage <= 0 || !TryComp(target, out DamageableComponent? damageable))
            return;

        var spec = new DamageSpecifier();
        spec.DamageDict[DeployedDamageType] = FixedPoint2.New(damage);
        _damageable.TryChangeDamage(target, spec, true, false, damageable);
    }

    private bool IsStackDamaged(Entity<RMCFoldingBarricadeStackComponent> ent)
    {
        var threshold = ent.Comp.MaxDamage * 0.25f;
        foreach (var damage in ent.Comp.StoredDamage)
        {
            if (damage >= threshold)
                return true;
        }

        return false;
    }

    private int GetDamagedCount(Entity<RMCFoldingBarricadeStackComponent> ent)
    {
        var count = 0;
        foreach (var damage in ent.Comp.StoredDamage)
        {
            if (damage > 0)
                count++;
        }

        return count;
    }

    private static float ClampDamage(float damage, float maxDamage)
    {
        return float.IsNaN(damage) ? 0 : MathHelper.Clamp(damage, 0, maxDamage);
    }
}
