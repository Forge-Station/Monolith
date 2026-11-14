using System.Linq;
using Content.Shared._WF.ShipAccess;

namespace Content.Server._WF.ShipAccess;

public sealed partial class WFShipAccessServerSystem
{
    /// <summary>Replaces automatic crew credentials without changing manually granted access.</summary>
    public void SetCrew(Entity<WFShipAccessComponent> ship, int? id, string? name, IEnumerable<WFShipAccessKey> keys)
    {
        var current = keys.Distinct().ToList();
        if (ship.Comp.CrewId == id && ship.Comp.CrewName == name && ship.Comp.CrewKeys.SequenceEqual(current))
            return;
        ship.Comp.CrewId = id;
        ship.Comp.CrewName = name;
        ship.Comp.CrewKeys = current;
        Dirty(ship);
        RefreshShip(ship);
    }

    /// <summary>Replaces the linked company's credentials without changing manually granted access.</summary>
    public void SetCompany(Entity<WFShipAccessComponent> ship, int? id, string? name, IEnumerable<WFShipAccessKey> keys)
    {
        var current = keys.Distinct().ToList();
        if (ship.Comp.CompanyId == id && ship.Comp.CompanyName == name && ship.Comp.CompanyKeys.SequenceEqual(current))
            return;
        ship.Comp.CompanyId = id;
        ship.Comp.CompanyName = name;
        ship.Comp.CompanyKeys = current;
        if (id == null && ship.Comp.Mode == WFShipAccessMode.Company)
            ship.Comp.Mode = WFShipAccessMode.Private;
        Dirty(ship);
        RefreshShip(ship);
    }

    /// <summary>Links the ship to the actor's player company through the company system.</summary>
    public bool TryLinkCompany(Entity<WFShipAccessComponent> ship, EntityUid actor)
    {
        var ev = new WFShipCompanyLinkEvent(actor);
        RaiseLocalEvent(ship.Owner, ref ev);
        return ship.Comp.CompanyId != null;
    }

    /// <summary>Links an unbound ship to the actor's crew through its membership system.</summary>
    public bool TryLinkCrew(Entity<WFShipAccessComponent> ship, EntityUid actor)
    {
        if (ship.Comp.CrewId != null)
            return true;
        var ev = new WFShipCrewLinkEvent(actor);
        RaiseLocalEvent(ship.Owner, ref ev);
        return ship.Comp.CrewId != null;
    }

    /// <summary>Changes ship mode only when its required crew or company exists.</summary>
    public bool SetMode(Entity<WFShipAccessComponent> ship, WFShipAccessMode mode)
    {
        if (!Enum.IsDefined(mode) || mode == WFShipAccessMode.Crew && ship.Comp.CrewId == null ||
            mode == WFShipAccessMode.Company && ship.Comp.CompanyId == null ||
            mode == WFShipAccessMode.Faction && !_access.IsFactionGrid(ship, out _))
            return false;
        if (ship.Comp.Mode == mode)
            return true;
        ship.Comp.Mode = mode;
        Dirty(ship);
        RefreshShip(ship);
        return true;
    }
}

/// <summary>Asks the company system to bind a ship to the actor's player company.</summary>
[ByRefEvent]
public readonly record struct WFShipCompanyLinkEvent(EntityUid Actor);

/// <summary>Asks the crew membership system to bind a ship to the actor's current crew.</summary>
[ByRefEvent]
public readonly record struct WFShipCrewLinkEvent(EntityUid Actor);
