using Content.Shared._Forge.Hive;
using Content.Shared._Forge.Hive.Components;
using Content.Shared.Interaction.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._Forge.Hive.Abilities;


public sealed partial class HiveAbilitySystem
{
    private void InitializeBodyParts()
    {
        SubscribeLocalEvent<HiveMemberComponent, HiveToggleHandItemEvent>(OnToggleHandItem);
        SubscribeLocalEvent<HiveMemberComponent, HiveToggleArmorEvent>(OnToggleArmor);
    }

    #region Предметы в руке

    private void OnToggleHandItem(Entity<HiveMemberComponent> ent, ref HiveToggleHandItemEvent args)
    {
        if (args.Handled)
            return;


        foreach (var held in _hands.EnumerateHeld(ent))
        {
            if (!IsOwnBodyPart(held, args.Item))
                continue;

            QueueDel(held);
            _audio.PlayPvs(args.Sound, ent);
            if (args.PopupOff is { } off)
                _popup.PopupEntity(Loc.GetString(off), ent, ent);

            _actions.SetToggled(args.Action.Owner, false);
            args.Handled = true;
            return;
        }

        var item = SpawnBodyPart(ent, args.Item);

        if (!_hands.TryForcePickupAnyHand(ent, item))
        {
            QueueDel(item);
            _popup.PopupEntity(Loc.GetString("hive-ability-no-free-hand"), ent, ent);
            return;
        }

        _audio.PlayPvs(args.Sound, ent);
        if (args.PopupOn is { } on)
            _popup.PopupEntity(Loc.GetString(on), ent, ent);

        _actions.SetToggled(args.Action.Owner, true);
        args.Handled = true;
    }

    #endregion

    #region Броня

    private void OnToggleArmor(Entity<HiveMemberComponent> ent, ref HiveToggleArmorEvent args)
    {
        if (args.Handled)
            return;


        var worn = new List<(string Slot, EntityUid Item)>();
        foreach (var (slot, proto) in args.Items)
        {
            if (_inventory.TryGetSlotEntity(ent, slot, out var item) && IsOwnBodyPart(item.Value, proto))
                worn.Add((slot, item.Value));
        }

        if (worn.Count > 0)
        {
            foreach (var (slot, item) in worn)
            {
                _inventory.TryUnequip(ent, slot, silent: true, force: true);
                if (!TerminatingOrDeleted(item))
                    QueueDel(item);
            }

            _audio.PlayPvs(args.Sound, ent);
            if (args.PopupOff is { } off)
                _popup.PopupEntity(Loc.GetString(off), ent, ent);

            _actions.SetToggled(args.Action.Owner, false);
            args.Handled = true;
            return;
        }

        var equippedAny = false;
        foreach (var (slot, proto) in args.Items)
        {
            if (!_inventory.HasSlot(ent, slot))
                continue;


            _inventory.TryUnequip(ent, slot, silent: true, force: true);

            var item = SpawnBodyPart(ent, proto);
            if (_inventory.TryEquip(ent, item, slot, silent: true, force: true))
                equippedAny = true;
            else
                QueueDel(item);
        }

        if (!equippedAny)
            return;

        _audio.PlayPvs(args.Sound, ent);
        if (args.PopupOn is { } on)
            _popup.PopupEntity(Loc.GetString(on), ent, ent);

        _actions.SetToggled(args.Action.Owner, true);
        args.Handled = true;
    }

    #endregion

    private EntityUid SpawnBodyPart(EntityUid owner, EntProtoId proto)
    {
        var item = Spawn(proto, Transform(owner).Coordinates);

        var part = EnsureComp<HiveBodyPartComponent>(item);
        part.SourcePrototype = proto;
        part.Owner = owner;
        Dirty(item, part);


        EnsureComp<UnremoveableComponent>(item);
        return item;
    }

    private bool IsOwnBodyPart(EntityUid item, EntProtoId proto)
    {
        return TryComp<HiveBodyPartComponent>(item, out var part) && part.SourcePrototype == proto;
    }
}
