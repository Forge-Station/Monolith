using System.Diagnostics.CodeAnalysis;
using Content.Shared._Mono.Company;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.StationRecords;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.ShipAccess;

/// <summary>
/// Shared reads for ship access, used by the console tab on the client and by the server: who holds the deed,
/// which record key a card carries, and the ship's faction. The doors themselves decide through their normal
/// airlock access readers, which the server keeps in step.
/// </summary>
public sealed class WFShipAccessSystem : EntitySystem
{
    /// <summary>Tiles from the console within which a person's card can be added to the allow list.</summary>
    public const float AddRange = 3f;

    /// <summary>Digits in a ship or door code.</summary>
    public const int CodeLength = 4;

    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedIdCardSystem _idCard = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private ISharedPlayerManager _player = default!;

    /// <summary>Whether the rule lets a code open the door.</summary>
    public static bool TakesCode(WFDoorAccessRule rule)
    {
        return rule is WFDoorAccessRule.Code or WFDoorAccessRule.PlayersOrCode;
    }

    /// <summary>Whether the rule has a per-door card list.</summary>
    public static bool TakesPlayers(WFDoorAccessRule rule)
    {
        return rule is WFDoorAccessRule.Players or WFDoorAccessRule.PlayersOrCode;
    }

    /// <summary>Whether a code is exactly four digits.</summary>
    public static bool IsValidCode(string code)
    {
        if (code.Length != CodeLength)
            return false;

        foreach (var c in code)
        {
            if (c < '0' || c > '9')
                return false;
        }

        return true;
    }

    /// <summary>A door's rule, Default when it has no rule component.</summary>
    public WFDoorAccessRule GetRule(EntityUid door)
    {
        return TryComp<WFDoorAccessRuleComponent>(door, out var rule) ? rule.Rule : WFDoorAccessRule.Default;
    }

    /// <summary>The card a person would swipe: the one in their active hand, else the one they wear. False when they carry none.</summary>
    public bool TryGetCard(EntityUid user, out EntityUid card)
    {
        if (_idCard.TryFindIdCard(user, out var idCard))
        {
            card = idCard.Owner;
            return true;
        }

        card = default;
        return false;
    }

    /// <summary>The crew record key a card carries. False for a card without a crew record, which no reader can be given.</summary>
    public bool TryGetKey(EntityUid card, out WFShipAccessKey key)
    {
        if (TryComp<StationRecordKeyStorageComponent>(card, out var storage) && storage.Key is { } recordKey)
        {
            key = ToKey(recordKey);
            return true;
        }

        key = default;
        return false;
    }

    public WFShipAccessKey ToKey(StationRecordKey key)
    {
        return new WFShipAccessKey(GetNetEntity(key.OriginStation), key.Id);
    }

    public StationRecordKey ToRecordKey(WFShipAccessKey key)
    {
        return new StationRecordKey(key.Id, GetEntity(key.Station));
    }

    /// <summary>Finds the allow list entry for a record key.</summary>
    public bool TryGetEntry(WFShipAccessComponent comp, WFShipAccessKey key, [NotNullWhen(true)] out WFShipAccessEntry? entry)
    {
        foreach (var e in comp.AllowList)
        {
            if (e.Key != key)
                continue;

            entry = e;
            return true;
        }

        entry = null;
        return false;
    }

    /// <summary>
    /// A grid belongs to a faction when its CompanyComponent names a company with an access group or level of the
    /// same id (USSP, TSF, PDV): any of those access levels on a card is what the door readers can check. "None",
    /// empty and companies without access mean no faction.
    /// </summary>
    public bool IsFactionGrid(EntityUid grid, out IReadOnlyCollection<ProtoId<AccessLevelPrototype>> access)
    {
        access = Array.Empty<ProtoId<AccessLevelPrototype>>();
        if (!TryComp<CompanyComponent>(grid, out var comp) || string.IsNullOrEmpty(comp.CompanyName.Id) || comp.CompanyName == "None")
            return false;

        var id = comp.CompanyName.Id;
        if (_proto.TryIndex<AccessGroupPrototype>(id, out var group))
            access = group.Tags;
        else if (_proto.HasIndex<AccessLevelPrototype>(id))
            access = new[] { new ProtoId<AccessLevelPrototype>(id) };

        return access.Count > 0;
    }

    /// <summary>Whether the item holds the deed for this grid: an ID card, or the voucher the ship was bought with.</summary>
    public bool IsDeedFor(EntityUid item, EntityUid grid)
    {
        return TryComp<ShuttleDeedComponent>(item, out var deed) && deed.ShuttleUid == grid;
    }

    /// <summary>
    /// Whether the user holds this grid's deed: on an ID card in their hands or id slot, directly or in a PDA, or on a
    /// voucher in their hands, as Mono's own deed checks count it.
    /// </summary>
    public bool HasDeedFor(EntityUid user, EntityUid grid)
    {
        foreach (var item in _hands.EnumerateHeld(user))
        {
            if (HoldsDeedFor(item, grid))
                return true;
        }

        return _inventory.TryGetSlotEntity(user, "id", out var worn) && HoldsDeedFor(worn.Value, grid);
    }

    private bool HoldsDeedFor(EntityUid item, EntityUid grid)
    {
        return IsDeedFor(item, grid) || _idCard.TryGetIdCard(item, out var card) && IsDeedFor(card.Owner, grid);
    }

    /// <summary>Whether the user's player is one the ship is registered to, whatever body they are in.</summary>
    public bool IsRegisteredTo(EntityUid user, WFShipAccessComponent comp)
    {
        return comp.OwnerUsers.Count > 0
               && _player.TryGetSessionByEntity(user, out var session)
               && comp.OwnerUsers.Contains(session.UserId);
    }

    /// <summary>Who may edit the ship's access: the deed holder, or a player an admin tool registered the ship to.</summary>
    public bool IsOwner(EntityUid user, EntityUid grid)
    {
        return HasDeedFor(user, grid) || TryComp<WFShipAccessComponent>(grid, out var comp) && IsRegisteredTo(user, comp);
    }

    /// <summary>The ID card in the user's id slot, directly or in a PDA: their own, as opposed to one they happen to hold.</summary>
    public bool TryGetWornCard(EntityUid user, out EntityUid card)
    {
        if (_inventory.TryGetSlotEntity(user, "id", out var worn) && _idCard.TryGetIdCard(worn.Value, out var idCard))
        {
            card = idCard.Owner;
            return true;
        }

        card = default;
        return false;
    }
}
