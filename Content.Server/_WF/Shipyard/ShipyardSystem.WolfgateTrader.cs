using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Numerics;
using Content.Server._Mono.Shuttles.Components;
using Content.Server.Physics.Controllers;
using Content.Server.Shuttles.Components;
using Content.Server.StationEvents.Components;
using Content.Shared._Mono.Company;
using Content.Shared._Mono.Shipyard;
using Content.Shared._Mono.Ships.Components;
using Content.Shared._WF.ShipPa;
using Content.Shared._NF.Shipyard;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._NF.Shipyard.Events;
using Content.Shared.Access.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Shuttles.Components;
using Content.Shared.Station.Components;
using Content.Shared.UserInterface;
using Robust.Shared.EntitySerialization;
using Robust.Shared.Prototypes;

namespace Content.Server._NF.Shipyard.Systems;

/// <summary>
/// Wolfgate: lets an NPC stand in for a shipyard console, and lets a ship be copied out of the
/// world and back into it. Lives in the shipyard partial because the console and deed components
/// are access-restricted to it.
/// </summary>
public sealed partial class ShipyardSystem
{
    [Dependency] private IComponentFactory _wfComponentFactory = default!;
    [Dependency] private ItemSlotsSystem _wfItemSlots = default!;

    /// <summary>
    /// Everything a shipyard console prototype carries that the purchase and sale handlers read.
    /// Copied onto a host verbatim, so a host serves exactly the console's listing.
    /// </summary>
    private static readonly string[] HostedConsoleComponents =
    [
        "ShipyardConsole",
        "ShipyardListing",
        "AccessReader",
        "CompanyAccessReader",
    ];

    #region Hosted consoles

    /// <summary>
    /// Copies a shipyard console prototype's listing onto another entity and reports the UI key it
    /// is served on. The host must already declare that key in its UserInterface.
    /// </summary>
    public bool TryHostConsole(EntityUid host, EntProtoId consoleProto, [NotNullWhen(true)] out Enum? uiKey)
    {
        uiKey = null;

        if (!_prototypeManager.TryIndex(consoleProto, out var proto))
            return false;

        if (!proto.TryGetComponent<ActivatableUIComponent>(out var activatable, _wfComponentFactory)
            || activatable.Key is not ShipyardConsoleUiKey key)
        {
            Log.Error($"{consoleProto} is not a shipyard console: no ActivatableUI shipyard key.");
            return false;
        }

        if (!_ui.HasUi(host, key))
        {
            Log.Error($"{ToPrettyString(host)} cannot host {consoleProto}: no {key} interface declared.");
            return false;
        }

        ClearHostedConsole(host);

        var registry = new ComponentRegistry();
        foreach (var name in HostedConsoleComponents)
        {
            if (proto.Components.TryGetValue(name, out var entry))
                registry[name] = entry;
        }

        EntityManager.AddComponents(host, registry);

        uiKey = key;
        return true;
    }

    /// <summary>
    /// Takes a hosted listing back off an entity.
    /// </summary>
    public void ClearHostedConsole(EntityUid host)
    {
        RemComp<ShipyardConsoleComponent>(host);
        RemComp<ShipyardListingComponent>(host);
        RemComp<AccessReaderComponent>(host);
        RemComp<CompanyAccessReaderComponent>(host);
    }

    /// <summary>
    /// Puts a card in a hosted console's slot and locks it, so only the host can take it back out.
    /// </summary>
    public bool TryInsertHostedId(EntityUid host, EntityUid idCard)
    {
        if (!TryComp<ShipyardConsoleComponent>(host, out var console))
            return false;

        _wfItemSlots.SetLock(host, console.TargetIdSlot, false);

        if (!_wfItemSlots.TryInsert(host, console.TargetIdSlot, idCard, null))
            return false;

        _wfItemSlots.SetLock(host, console.TargetIdSlot, true);
        return true;
    }

    /// <summary>
    /// The last thing a console popped up at a customer; how a hosting trader learns why a sale was refused.
    /// </summary>
    public string? LastConsolePopup;

    /// <summary>
    /// Runs the console's own sell path with the customer as the actor. True when the deed left the card:
    /// the hull itself is only queued for deletion, so it is still around when this returns.
    /// </summary>
    public bool TryHostedSell(EntityUid host, EntityUid customer, Enum uiKey, EntityUid idCard, out string? refusal)
    {
        refusal = null;

        if (!TryComp<ShipyardConsoleComponent>(host, out var console))
            return false;

        LastConsolePopup = null;
        OnSellMessage(host, console, new ShipyardConsoleSellMessage { Actor = customer, UiKey = uiKey });

        if (!HasDeed(idCard))
            return true;

        refusal = LastConsolePopup;
        return false;
    }

    #endregion

    #region Pricing

    /// <summary>
    /// What a grid is worth before the sale rate and taxes.
    /// </summary>
    public int GetShipAppraisal(EntityUid shuttle)
    {
        return (int) _pricing.AppraiseGrid(shuttle, LacksPreserveOnSaleComp);
    }

    /// <summary>
    /// The bill a console books before it pays its taxes out of it.
    /// </summary>
    public int GetPostSaleRateBill(EntityUid console, int appraisal)
    {
        if (TryComp<ShipyardConsoleComponent>(console, out var comp) && comp.IgnoreBaseSaleRate)
            return appraisal;

        return (int) (appraisal * _baseSaleRate);
    }

    /// <summary>
    /// What the seller actually walks away with: the appraisal after the sale rate and taxes.
    /// </summary>
    public int GetShipResaleValue(EntityUid console, EntityUid shuttle)
    {
        return CalculateShipResaleValue(new Entity<ShipyardConsoleComponent?>(console, null), GetShipAppraisal(shuttle));
    }

    #endregion

    #region Copying ships

    /// <summary>
    /// Strips everything bound to the old owner, so the saved copy comes back as an unclaimed hull.
    /// Everything taken off here is put back by the system that owns it or by the buyer's deed; what
    /// survives a sale on purpose, such as the repair record, is left alone.
    /// </summary>
    public void StripForResale(EntityUid grid)
    {
        RemComp<ShuttleDeedComponent>(grid);
        RemComp<ShipOwnershipComponent>(grid);
        RemComp<LinkedLifecycleGridParentComponent>(grid);
        RemComp<StationMemberComponent>(grid);
        RemComp<CompanyComponent>(grid);
        RemComp<FTLComponent>(grid);

        // Guests the seller waved aboard, by card and by borg.
        RemComp<ShipGuestAccessComponent>(grid);

        // Job slots and the station they were counted against; the console saves them again on power loss.
        RemComp<ShuttleConsoleJobSlotsComponent>(grid);

        // Whoever was at the helm, and the consoles a crewed hull was counting: both are dead uids.
        RemComp<PilotedShuttleComponent>(grid);
        RemComp<CrewedShuttleComponent>(grid);

        // The deed sets the lock again for the buyer, at the console's own default.
        RemComp<FTLLockComponent>(grid);

        // The PA timeline, which points at audio fetched for the old crew.
        RemComp<ShipPaBroadcastComponent>(grid);

        // A hull on the lot is not at general quarters; the PA ensures this again with its defaults.
        RemComp<ShipAlertComponent>(grid);
    }

    /// <summary>
    /// Serialises a grid and everything on it to YAML in memory.
    /// </summary>
    public bool TrySaveShip(EntityUid grid, [NotNullWhen(true)] out string? data)
    {
        data = null;

        using var writer = new StringWriter();

        // Ignore: a docked ship points at the station's docks, and none of that comes with it.
        var options = SerializationOptions.Default;
        options.MissingEntityBehaviour = MissingEntityBehaviour.Ignore;
        options.LogAutoInclude = null;

        try
        {
            if (!_mapLoader.TrySaveGrid(grid, writer, options))
                return false;
        }
        catch (Exception e)
        {
            Log.Error($"Failed to copy {ToPrettyString(grid)} for resale: {e}");
            return false;
        }

        data = writer.ToString();
        return true;
    }

    /// <summary>
    /// Loads a saved ship back onto the shipyard's staging map, the way a purchase loads a vessel.
    /// </summary>
    public bool TryAddSavedShip(string data, [NotNullWhen(true)] out EntityUid? shuttleGrid)
    {
        shuttleGrid = null;

        SetupShipyardIfNeeded();
        if (ShipyardMap == null)
            return false;

        try
        {
            using var reader = new StringReader(data);
            if (!_mapLoader.TryLoadGrid(ShipyardMap.Value, reader, "used ship", out var grid,
                    offset: new Vector2(500f + _shuttleIndex, 1f)))
            {
                return false;
            }

            _shuttleIndex += grid.Value.Comp.LocalAABB.Width + ShuttleSpawnBuffer;
            shuttleGrid = grid.Value.Owner;
            return true;
        }
        catch (Exception e)
        {
            Log.Error($"Failed to load a used ship: {e}");
            return false;
        }
    }

    /// <summary>
    /// FTL-docks an already loaded grid to a station, the tail half of a shipyard purchase.
    /// </summary>
    public bool TryDockLoadedShuttle(EntityUid stationUid, EntityUid shuttleGrid)
    {
        if (!TryComp<StationDataComponent>(stationUid, out var stationData)
            || !TryComp<ShuttleComponent>(shuttleGrid, out var shuttleComponent))
        {
            return false;
        }

        var targetGrid = _station.GetLargestGrid((stationUid, stationData));
        if (targetGrid == null)
            return false;

        var ev = new ShipBoughtEvent();
        RaiseLocalEvent(shuttleGrid, ev);
        _shuttle.TryFTLDock(shuttleGrid, shuttleComponent, targetGrid.Value);
        return true;
    }

    #endregion

    #region Deeds

    /// <summary>
    /// The ship a deed card points at, if it still exists.
    /// </summary>
    public bool TryGetDeedShip(EntityUid idCard, out EntityUid shuttle)
    {
        shuttle = default;

        if (!TryComp<ShuttleDeedComponent>(idCard, out var deed)
            || deed.ShuttleUid is not { } uid
            || TerminatingOrDeleted(uid))
        {
            return false;
        }

        shuttle = uid;
        return true;
    }

    /// <summary>
    /// Reads a ship's name and owner off its own deed.
    /// </summary>
    public bool TryGetShipDeedInfo(EntityUid uid, out string name, out string owner)
    {
        name = string.Empty;
        owner = string.Empty;

        if (!TryComp<ShuttleDeedComponent>(uid, out var deed))
            return false;

        name = GetFullName(deed);
        owner = deed.ShuttleOwner ?? string.Empty;
        return true;
    }

    /// <summary>
    /// Whether a card already carries a deed, the rule that stops a second purchase.
    /// </summary>
    public bool HasDeed(EntityUid idCard)
    {
        return HasComp<ShuttleDeedComponent>(idCard);
    }

    #endregion
}
