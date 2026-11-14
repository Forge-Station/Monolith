using Content.Shared.Damage;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Content.Shared.Tools.Systems;
using Robust.Shared.GameStates;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Barricades;

/// <summary>An upgrade a barricade can take: an entity prototype that only carries this component.</summary>
[RegisterComponent, NetworkedComponent]
[Access(typeof(RMCUpgradeSystem))]
public sealed partial class RMCConstructionUpgradeComponent : Component
{
    /// <summary>Stack used up by the upgrade and refunded by a downgrade; null uses nothing.</summary>
    [DataField]
    public ProtoId<StackPrototype>? Material = "Steel";

    [DataField]
    public int Amount = 2;

    [DataField]
    public EntProtoId UpgradedEntity;

    [DataField]
    public EntProtoId BaseEntity;

    [DataField]
    public LocId UpgradedPopup;

    [DataField]
    public LocId FailurePopup = "rmc-construction-no-metal";
}

/// <summary>A barricade that a stack of the upgrade material opens an upgrade menu on, or a multitool downgrades.</summary>
[RegisterComponent, NetworkedComponent]
[Access(typeof(RMCUpgradeSystem))]
public sealed partial class RMCConstructionUpgradeTargetComponent : Component
{
    [DataField]
    public EntProtoId[]? Upgrades;

    [DataField]
    public EntProtoId? Downgrade;

    [DataField]
    public string DowngradeQuality = "Pulsing";
}

[Serializable, NetSerializable]
public enum RMCConstructionUpgradeUiKey
{
    Key,
}

[Serializable, NetSerializable]
public sealed class RMCConstructionUpgradeBuiMsg(EntProtoId upgrade) : BoundUserInterfaceMessage
{
    public readonly EntProtoId Upgrade = upgrade;
}

/// <summary>Raised on the old and new entity when a barricade is upgraded or downgraded.</summary>
public sealed class RMCConstructionUpgradedEvent(EntityUid newUid, EntityUid oldUid) : EntityEventArgs
{
    public readonly EntityUid New = newUid;
    public readonly EntityUid Old = oldUid;
}

/// <summary>Swaps barricades for their upgraded or downgraded versions, keeping damage and rotation.</summary>
public sealed partial class RMCUpgradeSystem : EntitySystem
{
    [Dependency] private IComponentFactory _compFactory = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedStackSystem _stack = default!;
    [Dependency] private SharedToolSystem _tool = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<RMCConstructionUpgradeTargetComponent, InteractUsingEvent>(OnInteractUsing);

        Subs.BuiEvents<RMCConstructionUpgradeTargetComponent>(RMCConstructionUpgradeUiKey.Key,
            subs => subs.Event<RMCConstructionUpgradeBuiMsg>(OnUpgradeBuiMsg));
    }

    private bool TryGetUpgrade(EntProtoId id, out RMCConstructionUpgradeComponent upgrade)
    {
        upgrade = default!;
        return _prototypes.TryIndex(id, out var proto) &&
               proto.TryComp(out upgrade!, _compFactory);
    }

    /// <summary>Whether the item is a stack one of the target's upgrades consumes.</summary>
    private bool IsUpgradeMaterial(Entity<RMCConstructionUpgradeTargetComponent> ent, EntityUid used)
    {
        if (ent.Comp.Upgrades == null || !TryComp(used, out StackComponent? stack))
            return false;

        foreach (var id in ent.Comp.Upgrades)
        {
            if (TryGetUpgrade(id, out var upgrade) && upgrade.Material == stack.StackTypeId)
                return true;
        }

        return false;
    }

    private void OnInteractUsing(Entity<RMCConstructionUpgradeTargetComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        var upgrading = IsUpgradeMaterial(ent, args.Used);
        var downgrading = ent.Comp.Downgrade != null && _tool.HasQuality(args.Used, ent.Comp.DowngradeQuality);
        if (!upgrading && !downgrading)
            return;

        args.Handled = true;

        if (!Transform(ent).Anchored)
        {
            _popup.PopupClient(Loc.GetString("rmc-construction-require-anchored", ("ent", ent)), ent, args.User, PopupType.SmallCaution);
            return;
        }

        if (downgrading)
        {
            RemoveUpgrade(ent, args.User);
            return;
        }

        _ui.OpenUi(ent.Owner, RMCConstructionUpgradeUiKey.Key, args.User);
    }

    private void RemoveUpgrade(Entity<RMCConstructionUpgradeTargetComponent> ent, EntityUid user)
    {
        if (_net.IsClient || ent.Comp.Downgrade is not { } downgrade || !TryGetUpgrade(downgrade, out var upgrade))
            return;

        if (upgrade.Material is { } material && upgrade.Amount > 0 && _prototypes.TryIndex(material, out var stackProto))
        {
            var refund = Spawn(stackProto.Spawn, _transform.GetMapCoordinates(ent));
            if (TryComp<StackComponent>(refund, out var stack))
                _stack.SetCount(refund, upgrade.Amount, stack);
        }

        ApplyEntityUpgrade(ent, upgrade.BaseEntity, user, Loc.GetString("rmc-construction-downgrade", ("ent", ent)));
    }

    private void OnUpgradeBuiMsg(Entity<RMCConstructionUpgradeTargetComponent> ent, ref RMCConstructionUpgradeBuiMsg args)
    {
        _ui.CloseUi(ent.Owner, RMCConstructionUpgradeUiKey.Key);

        var user = args.Actor;
        if (ent.Comp.Upgrades == null ||
            Array.IndexOf(ent.Comp.Upgrades, args.Upgrade) < 0 ||
            !TryGetUpgrade(args.Upgrade, out var upgrade))
        {
            return;
        }

        if (upgrade.Material is { } material)
        {
            EntityUid? used = null;
            foreach (var held in _hands.EnumerateHeld(user))
            {
                if (TryComp(held, out StackComponent? stack) && stack.StackTypeId == material)
                {
                    used = held;
                    break;
                }
            }

            if (used == null || !_stack.Use(used.Value, upgrade.Amount))
            {
                _popup.PopupClient(Loc.GetString(upgrade.FailurePopup, ("ent", ent)), ent, user, PopupType.SmallCaution);
                return;
            }
        }

        ApplyEntityUpgrade(ent, upgrade.UpgradedEntity, user, Loc.GetString(upgrade.UpgradedPopup));
    }

    private void ApplyEntityUpgrade(Entity<RMCConstructionUpgradeTargetComponent> ent, EntProtoId target, EntityUid user, string popup)
    {
        if (_net.IsClient)
            return;

        var rotation = _transform.GetWorldRotation(ent).GetCardinalDir().ToAngle();
        var spawned = Spawn(target, _transform.GetMapCoordinates(ent), rotation: rotation);
        _popup.PopupEntity(popup, spawned, user);

        if (TryComp<DamageableComponent>(ent, out var oldDamage) &&
            TryComp<DamageableComponent>(spawned, out var newDamage))
        {
            _damageable.SetDamage(spawned, newDamage, oldDamage.Damage);
        }

        var ev = new RMCConstructionUpgradedEvent(spawned, ent.Owner);
        RaiseLocalEvent(ent.Owner, ev);

        QueueDel(ent.Owner);
    }
}
