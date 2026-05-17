using Content.Server.Shuttles.Components;
using Content.Shared._Forge.CCVar;
using Content.Shared._Mono.Company;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared.Database;
using Content.Shared.NPC.Components;
using Content.Shared.Popups;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Events;
using Robust.Server.Player;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using System;
using System.Linq;

namespace Content.Server.Shuttles.Systems;

public sealed partial class ShuttleSystem
{
    private List<ForgeIffTransferListEntry>? _forgeTransferCompaniesCache;

    private void InitializeForgePoiCapture()
    {
        SubscribeLocalEvent<PoiCaptureConsoleComponent, PoiCaptureStartMessage>(OnPoiCaptureStart);
        SubscribeLocalEvent<PoiCaptureConsoleComponent, PoiCaptureInterruptMessage>(OnPoiCaptureInterrupt);
        SubscribeLocalEvent<PoiCaptureConsoleComponent, PoiCaptureTransferOwnershipMessage>(OnPoiCaptureTransferOwnership);
        SubscribeLocalEvent<PoiCaptureConsoleComponent, BoundUIOpenedEvent>(OnPoiCaptureConsoleOpen);
    }

    private TimeSpan GetForgePoiCaptureDuration()
    {
        var minutes = _cfg.GetCVar(ForgeCVars.PoiCaptureDurationMinutes);
        return TimeSpan.FromMinutes(Math.Max(0.1f, minutes));
    }

    private void UpdateForgePoiCapture()
    {
        var now = _gameTiming.CurTime;
        var query = EntityQueryEnumerator<PoiCaptureComponent>();

        while (query.MoveNext(out var gridUid, out var capture))
        {
            if (!capture.CaptureInProgress || now < capture.CaptureEndTime)
                continue;

            var previousOwner = capture.CurrentOwnerCompanyId;
            capture.CaptureInProgress = false;
            capture.CurrentOwnerCompanyId = capture.AttackerCompanyId;
            capture.CurrentOwnerFactionId = capture.AttackerFactionId;
            capture.AttackerCompanyId = "None";
            capture.AttackerFactionId = "None";
            capture.LastCapturedByName = NormalizeForgeOwner(capture.AttackerLeaderName);
            capture.AttackerLeaderName = "None";

            ApplyForgeOwnership(gridUid, capture.CurrentOwnerCompanyId, capture.CurrentOwnerFactionId, capture.CaptureLeaderUserId);

            _logger.Add(LogType.Action, LogImpact.Medium,
                $"POI capture completed on {ToPrettyString(gridUid)}: {previousOwner} -> {capture.CurrentOwnerCompanyId}, leader {capture.LastCapturedByName}");

            RefreshPoiCaptureConsolesForGrid(gridUid);
        }
    }

    private void OnPoiCaptureStart(EntityUid uid, PoiCaptureConsoleComponent component, PoiCaptureStartMessage args)
    {
        if (!TryGetForgeActorContext(uid, args.Actor, out var gridUid, out var actor, out var actorCompany, out var actorFaction, out var actorSession))
            return;

        var capture = EnsureComp<PoiCaptureComponent>(gridUid);
        SyncForgePoiCaptureOwnerFromGrid(gridUid, capture);
        var actorCompanyId = NormalizeForgeOwner(actorCompany);
        var actorFactionId = NormalizeForgeOwner(actorFaction);

        // Forge-Change:start validate-capture-company
        if (actorCompanyId == "None")
        {
            _popup.PopupEntity(Loc.GetString("iff-console-capture-company-required"), uid, actor, PopupType.Small);
            return;
        }
        // Forge-Change:end validate-capture-company

        // Forge-Change:start prevent-capture-own-poi
        if (capture.CurrentOwnerCompanyId == actorCompanyId)
        {
            _popup.PopupEntity(Loc.GetString("iff-console-capture-already-owned"), uid, actor, PopupType.Small);
            return;
        }
        // Forge-Change:end prevent-capture-own-poi

        if (capture.CaptureInProgress &&
            capture.AttackerCompanyId == actorCompanyId &&
            capture.AttackerFactionId == actorFactionId)
        {
            _popup.PopupEntity(Loc.GetString("iff-console-capture-already-running"), uid, actor, PopupType.Small);
            return;
        }

        var duration = GetForgePoiCaptureDuration();

        // Different faction/company can override an active capture.
        capture.CaptureInProgress = true;
        capture.CaptureStartTime = _gameTiming.CurTime;
        capture.CaptureEndTime = _gameTiming.CurTime + duration;
        capture.AttackerCompanyId = actorCompanyId;
        capture.AttackerFactionId = actorFactionId;
        capture.CaptureLeaderUserId = actorSession.UserId;
        capture.AttackerLeaderName = actorSession.Name;

        _logger.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(actor):player} started POI capture on {ToPrettyString(gridUid)} as {actorCompanyId}/{actorFactionId} for {(int) duration.TotalMinutes} minutes");

        RefreshPoiCaptureConsolesForGrid(gridUid);
        _popup.PopupEntity(Loc.GetString("iff-console-capture-started",
            ("minutes", (int) duration.TotalMinutes)), uid, actor, PopupType.Medium);
    }

    private void OnPoiCaptureInterrupt(EntityUid uid, PoiCaptureConsoleComponent component, PoiCaptureInterruptMessage args)
    {
        if (!TryGetForgeActorContext(uid, args.Actor, out var gridUid, out var actor, out var actorCompany, out var actorFaction, out _))
            return;

        if (!TryComp<PoiCaptureComponent>(gridUid, out var capture) || !capture.CaptureInProgress)
            return;

        var actorCompanyId = NormalizeForgeOwner(actorCompany);
        var actorFactionId = NormalizeForgeOwner(actorFaction);
        var sameCompany = capture.AttackerCompanyId == actorCompanyId;
        var sameFaction = capture.AttackerFactionId == actorFactionId;

        if (sameCompany && sameFaction)
        {
            _popup.PopupEntity(Loc.GetString("iff-console-capture-cannot-interrupt-own"), uid, actor, PopupType.Small);
            return;
        }

        var attackerCompany = capture.AttackerCompanyId;
        var attackerFaction = capture.AttackerFactionId;

        capture.CaptureInProgress = false;
        capture.AttackerCompanyId = "None";
        capture.AttackerFactionId = "None";
        capture.CaptureLeaderUserId = null;
        capture.AttackerLeaderName = "None";

        _logger.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(actor):player} interrupted POI capture on {ToPrettyString(gridUid)} ({attackerCompany}/{attackerFaction}) as {actorCompanyId}/{actorFactionId}");

        RefreshPoiCaptureConsolesForGrid(gridUid);
        _popup.PopupEntity(Loc.GetString("iff-console-capture-interrupted"), uid, actor, PopupType.Medium);
    }

    private void OnPoiCaptureTransferOwnership(EntityUid uid, PoiCaptureConsoleComponent component, PoiCaptureTransferOwnershipMessage args)
    {
        if (!TryGetForgeActorContext(uid, args.Actor, out var gridUid, out var actor, out _, out _, out var actorSession))
            return;

        if (!TryComp<PoiCaptureComponent>(gridUid, out var capture))
            return;

        if (capture.CaptureLeaderUserId == null || capture.CaptureLeaderUserId != actorSession.UserId)
        {
            _popup.PopupEntity(Loc.GetString("iff-console-transfer-not-leader"), uid, actor, PopupType.Small);
            return;
        }

        if (capture.CaptureInProgress)
        {
            _popup.PopupEntity(Loc.GetString("iff-console-transfer-denied-active-capture"), uid, actor, PopupType.Small);
            return;
        }

        var companyId = NormalizeForgeOwner(args.CompanyId);
        var factionId = capture.CurrentOwnerFactionId;

        if (companyId == "None" || string.IsNullOrWhiteSpace(args.CompanyId))
        {
            _popup.PopupEntity(Loc.GetString("iff-console-transfer-must-pick-company"), uid, actor, PopupType.Small);
            return;
        }

        if (!_protoManager.TryIndex<CompanyPrototype>(companyId, out _))
        {
            _popup.PopupEntity(Loc.GetString("iff-console-transfer-invalid-company"), uid, actor, PopupType.Small);
            return;
        }

        // Forge-Change:start prevent-noop-transfer
        if (capture.CurrentOwnerCompanyId == companyId)
        {
            _popup.PopupEntity(Loc.GetString("iff-console-transfer-same-company"), uid, actor, PopupType.Small);
            return;
        }
        // Forge-Change:end prevent-noop-transfer

        var previousOwner = capture.CurrentOwnerCompanyId;
        capture.CurrentOwnerCompanyId = companyId;
        capture.CurrentOwnerFactionId = factionId;
        capture.CaptureLeaderUserId = actorSession.UserId;
        ApplyForgeOwnership(gridUid, companyId, factionId, actorSession.UserId);

        _logger.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(actor):player} transferred POI {ToPrettyString(gridUid)} ownership: {previousOwner} -> {companyId}");

        RefreshPoiCaptureConsolesForGrid(gridUid);
        _popup.PopupEntity(Loc.GetString("iff-console-transfer-success"), uid, actor, PopupType.Medium);
    }

    private bool TryGetForgeActorContext(
        EntityUid consoleUid,
        EntityUid? actorUid,
        out EntityUid gridUid,
        out EntityUid actor,
        out string companyId,
        out string factionId,
        out ICommonSession session)
    {
        gridUid = default;
        actor = default;
        companyId = "None";
        factionId = "None";
        session = default!;

        if (actorUid is not { Valid: true } validActor)
            return false;

        if (!TryComp(validActor, out ActorComponent? actorComp))
            return false;

        if (!TryComp(consoleUid, out TransformComponent? xform) || xform.GridUid is not { Valid: true } ownedGrid)
            return false;

        gridUid = ownedGrid;
        actor = validActor;
        session = actorComp.PlayerSession;
        companyId = TryComp<CompanyComponent>(actor, out var companyComp) ? companyComp.CompanyName : "None";

        if (TryComp<NpcFactionMemberComponent>(actor, out var factionComp) && factionComp.Factions.Count > 0)
            factionId = factionComp.Factions.First().ToString();

        return true;
    }

    private void ApplyForgeOwnership(EntityUid gridUid, string companyId, string factionId, NetUserId? leaderUserId)
    {
        // Forge-Change:start safe-network-component-updates
        // Avoid dynamically adding networked ownership components at runtime on arbitrary grids.
        // We only mutate existing components to reduce state replication edge-cases.
        if (TryComp<CompanyComponent>(gridUid, out var companyComp))
        {
            companyComp.CompanyName = companyId;
            Dirty(gridUid, companyComp);
        }

        if (TryComp<ShuttleFactionComponent>(gridUid, out var factionComp))
        {
            factionComp.Faction = factionId;
            Dirty(gridUid, factionComp);
        }
        // Forge-Change:end safe-network-component-updates

        if (leaderUserId != null && TryComp<ShipOwnershipComponent>(gridUid, out var ownership))
        {
            ownership.OwnerUserId = leaderUserId.Value;
            ownership.IsOwnerOnline = true;
            ownership.LastStatusChangeTime = _gameTiming.CurTime;
            ownership.LastPlayerActivityTime = _gameTiming.CurTime;
            Dirty(gridUid, ownership);
        }

        if (_protoManager.TryIndex<CompanyPrototype>(companyId, out var companyProto))
            SetIFFColor(gridUid, companyProto.Color);

        SetIFFReadOnly(gridUid, false);
    }

    private PoiCaptureConsoleBoundUserInterfaceState BuildForgePoiCaptureConsoleState(EntityUid? gridUid, NetUserId? viewerUserId = null)
    {
        var duration = GetForgePoiCaptureDuration();
        var state = new PoiCaptureConsoleBoundUserInterfaceState
        {
            CaptureDurationSeconds = (int) duration.TotalSeconds,
        };

        if (gridUid is not { Valid: true } grid)
        {
            FillForgeCaptureTransferLists(state);
            return state;
        }

        if (!TryComp<PoiCaptureComponent>(grid, out var capture))
        {
            GetForgeGridOwner(grid, out var ownerCompanyId, out var ownerFactionId);
            state.CurrentOwnerCompanyId = ownerCompanyId;
            state.CurrentOwnerFactionId = ownerFactionId;
            FillForgeCaptureTransferLists(state);
            return state;
        }

        if (!capture.CaptureInProgress)
            SyncForgePoiCaptureOwnerFromGrid(grid, capture);

        state.CaptureInProgress = capture.CaptureInProgress;
        state.CaptureStartTime = capture.CaptureStartTime;
        state.CaptureEndTime = capture.CaptureEndTime;
        state.CurrentOwnerCompanyId = capture.CurrentOwnerCompanyId;
        state.CurrentOwnerFactionId = capture.CurrentOwnerFactionId;
        state.AttackerCompanyId = capture.AttackerCompanyId;
        state.AttackerFactionId = capture.AttackerFactionId;
        state.LastCapturedByName = capture.LastCapturedByName;
        state.CaptureLeaderUserId = capture.CaptureLeaderUserId;
        state.CanTransfer = viewerUserId != null
            && capture.CaptureLeaderUserId == viewerUserId
            && !capture.CaptureInProgress;
        FillForgeCaptureTransferLists(state);
        return state;
    }

    // Forge-Change:start capture-transfer-lists
    private void FillForgeCaptureTransferLists(PoiCaptureConsoleBoundUserInterfaceState state)
    {
        state.TransferCompanies.Clear();
        // Forge-Change:start cache-transfer-companies
        _forgeTransferCompaniesCache ??= BuildForgeTransferCompanyCache();
        state.TransferCompanies.AddRange(_forgeTransferCompaniesCache);
        // Forge-Change:end cache-transfer-companies

    }

    private List<ForgeIffTransferListEntry> BuildForgeTransferCompanyCache()
    {
        var companies = new List<ForgeIffTransferListEntry>();
        foreach (var proto in _protoManager.EnumeratePrototypes<CompanyPrototype>())
        {
            if (proto.Hidden || proto.ID == "None")
                continue;

            companies.Add(new ForgeIffTransferListEntry
            {
                Id = proto.ID,
                Label = proto.Name,
            });
        }

        companies.Sort((a, b) => string.Compare(a.Label, b.Label, StringComparison.CurrentCultureIgnoreCase));
        return companies;
    }
    // Forge-Change:end capture-transfer-lists

    private void RefreshPoiCaptureConsolesForGrid(EntityUid gridUid)
    {
        var query = AllEntityQuery<PoiCaptureConsoleComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid != gridUid)
                continue;

            _uiSystem.SetUiState(uid, PoiCaptureConsoleUiKey.Key, BuildForgePoiCaptureConsoleState(gridUid));
        }
    }

    private static string NormalizeForgeOwner(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "None";

        return value.Trim();
    }

    private void GetForgeGridOwner(EntityUid gridUid, out string companyId, out string factionId)
    {
        companyId = "None";
        factionId = "None";

        if (TryComp<CompanyComponent>(gridUid, out var companyComp))
            companyId = NormalizeForgeOwner(companyComp.CompanyName);

        if (TryComp<ShuttleFactionComponent>(gridUid, out var factionComp))
            factionId = NormalizeForgeOwner(factionComp.Faction);
    }

    private void SyncForgePoiCaptureOwnerFromGrid(EntityUid gridUid, PoiCaptureComponent capture)
    {
        GetForgeGridOwner(gridUid, out var companyId, out var factionId);

        if (companyId != "None")
            capture.CurrentOwnerCompanyId = companyId;

        if (factionId != "None")
            capture.CurrentOwnerFactionId = factionId;
    }

    private void OnPoiCaptureConsoleOpen(EntityUid uid, PoiCaptureConsoleComponent component, ref BoundUIOpenedEvent args)
    {
        NetUserId? viewer = null;
        if (TryComp(args.Actor, out ActorComponent? actor))
            viewer = actor.PlayerSession.UserId;

        if (!TryComp(uid, out TransformComponent? xform) || xform.GridUid is not { Valid: true } gridUid)
        {
            _uiSystem.SetUiState(uid, PoiCaptureConsoleUiKey.Key, BuildForgePoiCaptureConsoleState(null, viewer));
            return;
        }

        _uiSystem.SetUiState(uid, PoiCaptureConsoleUiKey.Key, BuildForgePoiCaptureConsoleState(gridUid, viewer));
    }
}
