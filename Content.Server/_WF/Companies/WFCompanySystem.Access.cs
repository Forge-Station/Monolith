using System.Linq;
using Content.Server._WF.ShipAccess;
using Content.Shared._Mono.Company;
using Content.Shared._WF.Companies;
using Content.Shared._WF.ShipAccess;
using Content.Shared.Access.Components;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Content.Shared.StationRecords;
using Robust.Shared.Player;

namespace Content.Server._WF.Companies;

public sealed partial class WFCompanySystem
{
    [Dependency] private WFShipAccessServerSystem _shipAccess = default!;
    [Dependency] private WFShipAccessSystem _shipKeys = default!;
    [Dependency] private SharedShuttleSystem _shuttles = default!;

    /// <summary>The ID card key each member spawned with this round, for Company ship access.</summary>
    private readonly Dictionary<int, WFShipAccessKey> _memberKeys = new();
    private TimeSpan _nextAccessSync;

    private void InitializeAccess()
    {
        SubscribeLocalEvent<WFShipAccessComponent, WFShipCompanyLinkEvent>(OnLinkCompany);
        SubscribeLocalEvent<WFCompanyShipComponent, ComponentShutdown>(OnCompanyShipRemoved);
    }

    /// <summary>Remembers the key of a member's issued ID card.</summary>
    private void CaptureMemberKey(int profile, EntityUid card)
    {
        if (_shipKeys.TryGetKey(card, out var key))
            _memberKeys[profile] = key;
    }

    /// <summary>Copies the rank on a member's ID into their station record, for screens that show its job icon and title.</summary>
    private void SyncRecordRank(EntityUid card)
    {
        if (!TryComp<StationRecordKeyStorageComponent>(card, out var storage) || storage.Key is not { } key ||
            !_records.TryGetRecord<GeneralStationRecord>(key, out var record))
            return;
        var rank = CompOrNull<WFCompanyRankIconComponent>(card)?.RankId ?? 0;
        // A card retitled since spawning, by an ID console or a crew role, keeps that title.
        var title = CompOrNull<WFCompanyIdComponent>(card)?.Title;
        var retitled = !string.IsNullOrEmpty(title) && CompOrNull<IdCardComponent>(card)?.LocalizedJobTitle == title && record.JobTitle != title;
        if (record.WFCompanyRank == rank && !retitled)
            return;
        record.WFCompanyRank = rank;
        if (retitled)
            record.JobTitle = title!;
        _records.Synchronize(key);
    }

    /// <summary>Links a ship to the company of the player setting Company access.</summary>
    private void OnLinkCompany(Entity<WFShipAccessComponent> ship, ref WFShipCompanyLinkEvent args)
    {
        if (!_players.TryGetSessionByEntity(args.Actor, out var session) || CompanyIdOf(session) is not { } id ||
            !_companies.TryGetValue(id, out var company))
            return;
        _shipAccess.SetCompany(ship, id, company.Name, CompanyKeys(company));
    }

    private IEnumerable<WFShipAccessKey> CompanyKeys(Company company) =>
        company.Members.Keys.Where(_memberKeys.ContainsKey).Select(profile => _memberKeys[profile]);

    /// <summary>Keeps linked ships' member keys current, links company ships, and paints company ships' IFF.</summary>
    private void SyncCompanyAccess()
    {
        if (_timing.CurTime < _nextAccessSync)
            return;
        _nextAccessSync = _timing.CurTime + TimeSpan.FromSeconds(2);
        // ID record keys are issued after spawning finishes, so pick them up from members' worn cards here.
        foreach (var session in _players.Sessions)
        {
            if (MemberBody(session) is not { } found || !_shipKeys.TryGetWornCard(found.Body, out var card) ||
                !_spawnedSlots.TryGetValue(session.UserId, out var slot) || !_profileIds.TryGetValue(session.UserId, out var ids) ||
                !ids.TryGetValue(slot, out var profile))
                continue;
            CaptureMemberKey(profile, card);
            SyncRecordRank(card);
        }
        var ships = EntityQueryEnumerator<WFShipAccessComponent>();
        while (ships.MoveNext(out var uid, out var access))
        {
            var id = access.CompanyId ?? CompOrNull<WFCompanyShipComponent>(uid)?.CompanyId;
            if (id == null)
                continue;
            if (_companies.TryGetValue(id.Value, out var company))
                _shipAccess.SetCompany((uid, access), company.Id, company.Name, CompanyKeys(company));
            else
                _shipAccess.SetCompany((uid, access), null, null, Array.Empty<WFShipAccessKey>());
        }
        var disbanded = new List<EntityUid>();
        var flown = EntityQueryEnumerator<WFCompanyShipComponent, IFFComponent>();
        while (flown.MoveNext(out var uid, out var ship, out var iff))
        {
            if (!_companies.TryGetValue(ship.CompanyId, out var company))
            {
                disbanded.Add(uid);
                continue;
            }
            if (ship.Name != company.Name || ship.Tag != company.Tag || ship.Colour != company.Colour)
            {
                ship.Name = company.Name;
                ship.Tag = company.Tag;
                ship.Colour = company.Colour;
                Dirty(uid, ship);
            }
            // The faction company on a ship overrides the colour of its radar label.
            if (TryComp<CompanyComponent>(uid, out var faction) && !string.IsNullOrEmpty(faction.CompanyName))
            {
                ship.PreviousFaction ??= faction.CompanyName;
                faction.CompanyName = string.Empty;
                Dirty(uid, faction);
            }
            if (iff.Color == ship.Colour)
                continue;
            ship.PreviousIffColour ??= iff.Color;
            _shuttles.SetIFFColor(uid, ship.Colour, iff);
        }
        foreach (var uid in disbanded)
            RemComp<WFCompanyShipComponent>(uid);
    }

    /// <summary>Flies a ship bought with a member's ID under their company.</summary>
    public void MarkBoughtShip(EntityUid ship, EntityUid card)
    {
        if (!TryComp<WFCompanyIdComponent>(card, out var marker) || !_companies.TryGetValue(marker.CompanyId, out var company))
            return;
        var flown = EnsureComp<WFCompanyShipComponent>(ship);
        flown.CompanyId = company.Id;
        flown.Name = company.Name;
        flown.Tag = company.Tag;
        flown.Colour = company.Colour;
        Dirty(ship, flown);
    }

    /// <summary>A ship that stops flying for a company gets its own IFF colour back.</summary>
    private void OnCompanyShipRemoved(Entity<WFCompanyShipComponent> ship, ref ComponentShutdown args)
    {
        if (TerminatingOrDeleted(ship))
            return;
        if (ship.Comp.PreviousIffColour is { } colour)
            _shuttles.SetIFFColor(ship, colour);
        if (ship.Comp.PreviousFaction is { } previous && TryComp<CompanyComponent>(ship, out var faction))
        {
            faction.CompanyName = previous;
            Dirty(ship, faction);
        }
    }
}
