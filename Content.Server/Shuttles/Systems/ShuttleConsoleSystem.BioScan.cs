// Forge-Change-full - BioScan
using System.Numerics;
using Content.Server.Shuttles.Components;
using Content.Shared._Mono.Company;
using Content.Shared._Mono.Ships.Components;
using Content.Shared._Mono.Shuttles;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared.Humanoid;
using Content.Shared.NPC.Systems;
using Content.Shared.Popups;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Timing;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server.Shuttles.Systems;

public sealed partial class ShuttleConsoleSystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly NpcFactionSystem _npcFaction = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;

    private void InitializeBioScan()
    {
    }

    private void OnBioScanPositionMessage(Entity<ShuttleConsoleComponent> ent, ref ShuttleConsoleBioScanPositionMessage args)
    {
        if (!TryGetScannerGrid(ent, out var scannerGrid))
        {
            SetBioScanStatus(ent, ShuttleBioScanStatus.InvalidTarget);
            return;
        }

        if (!CanUseBioScan(ent, scannerGrid))
        {
            SetBioScanStatus(ent, ShuttleBioScanStatus.NoAccess);
            return;
        }

        if (ent.Comp.BioScanActive)
        {
            return;
        }

        if (!TryValidateScanTarget(ent, scannerGrid, args.Coordinates, out var targetGrid, out var failStatus))
        {
            SetBioScanStatus(ent, failStatus);
            return;
        }

        StartBioScan(ent, targetGrid);
    }

    private bool TryGetScannerGrid(Entity<ShuttleConsoleComponent> ent, out EntityUid scannerGrid)
    {
        var consoleXform = Transform(ent);
        if (consoleXform.GridUid == null)
        {
            scannerGrid = default;
            return false;
        }

        scannerGrid = consoleXform.GridUid.Value;
        return true;
    }

    private bool TryValidateScanTarget(
        Entity<ShuttleConsoleComponent> ent,
        EntityUid scannerGrid,
        MapCoordinates mapCoordinates,
        out EntityUid targetGrid,
        out ShuttleBioScanStatus failStatus)
    {
        if (!_mapManager.TryFindGridAt(mapCoordinates, out targetGrid, out _))
        {
            failStatus = ShuttleBioScanStatus.InvalidTarget;
            return false;
        }

        if (targetGrid == scannerGrid)
        {
            failStatus = ShuttleBioScanStatus.InvalidTarget;
            return false;
        }

        var scannerPos = _transform.GetWorldPosition(scannerGrid);
        var targetPos = _transform.GetWorldPosition(targetGrid);
        var distance = Vector2.Distance(scannerPos, targetPos);
        if (distance > ent.Comp.BioScanRange)
        {
            failStatus = ShuttleBioScanStatus.TargetTooFar;
            return false;
        }

        if (IsGridMoving(targetGrid, ent.Comp))
        {
            failStatus = ShuttleBioScanStatus.TargetMoving;
            return false;
        }

        failStatus = ShuttleBioScanStatus.None;
        return true;
    }

    private void StartBioScan(Entity<ShuttleConsoleComponent> ent, EntityUid targetGrid)
    {
        var now = _timing.CurTime;
        ent.Comp.BioScanActive = true;
        ent.Comp.BioScanTarget = targetGrid;
        ent.Comp.BioScanTime = new StartEndTime(now, now + TimeSpan.FromSeconds(ent.Comp.BioScanDuration));
        ent.Comp.BioScanStatus = ShuttleBioScanStatus.InProgress;
        Dirty(ent, ent.Comp);
        RefreshBioScanState(ent);
    }

    private bool CanUseBioScan(Entity<ShuttleConsoleComponent> console, EntityUid scannerGrid)
    {
        if (!TryComp<ShuttleBioScanAccessComponent>(console, out var access))
            return false;

        if (TryComp<VesselComponent>(scannerGrid, out var vesselComp) &&
            _prototype.TryIndex<VesselPrototype>(vesselComp.VesselId, out var vesselProto))
        {
            foreach (var vesselClass in access.AllowedClasses)
            {
                if (vesselProto.Classes.Contains(vesselClass))
                    return true;
            }
        }

        if (TryComp<CompanyComponent>(scannerGrid, out var company))
        {
            foreach (var allowedCompany in access.AllowedCompanies)
            {
                if (string.Equals(company.CompanyName, allowedCompany, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        var humanoidQuery = EntityQueryEnumerator<HumanoidAppearanceComponent, TransformComponent>();
        while (humanoidQuery.MoveNext(out var _, out var humanoid, out var xform))
        {
            if (xform.GridUid != scannerGrid)
                continue;

            foreach (var allowedSpecies in access.AllowedSpecies)
            {
                if (humanoid.Species == allowedSpecies)
                    return true;
            }
        }

        foreach (var tag in access.AllowedTags)
        {
            if (!string.IsNullOrWhiteSpace(tag) &&
                (_tags.HasTag(console.Owner, tag) || _tags.HasTag(scannerGrid, tag)))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsGridMoving(EntityUid gridUid, ShuttleConsoleComponent component)
    {
        if (!TryComp<PhysicsComponent>(gridUid, out var body))
            return false;

        return body.LinearVelocity.LengthSquared() > component.BioScanMaxVelocity * component.BioScanMaxVelocity ||
               MathF.Abs(body.AngularVelocity) > component.BioScanMaxAngularVelocity;
    }

    private void UpdateBioScans()
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<ShuttleConsoleComponent>();
        while (query.MoveNext(out var uid, out var console))
        {
            if (!console.BioScanActive || now < console.BioScanTime.End)
            {
                // Cancel the scan if the target starts moving during the process.
                if (console.BioScanActive &&
                    console.BioScanTarget is { } currentTarget &&
                    Exists(currentTarget) &&
                    IsGridMoving(currentTarget, console))
                {
                    SetBioScanStatus((uid, console), ShuttleBioScanStatus.TargetMoving);
                }

                continue;
            }

            CompleteBioScan((uid, console));
        }
    }

    private void CompleteBioScan(Entity<ShuttleConsoleComponent> ent)
    {
        ent.Comp.BioScanActive = false;
        var target = ent.Comp.BioScanTarget;
        ent.Comp.BioScanTarget = null;

        if (target == null || !Exists(target.Value))
        {
            ent.Comp.BioScanStatus = ShuttleBioScanStatus.InvalidTarget;
            Dirty(ent, ent.Comp);
            RefreshBioScanState(ent);
            return;
        }

        var hasThreat = HasBioThreat(target.Value, ent);
        if (hasThreat)
        {
            ent.Comp.BioScanStatus = ShuttleBioScanStatus.ThreatDetected;
            ApplyInfectedMarker(target.Value);

            var shuttleName = MetaData(target.Value).EntityName;
            var message = Loc.GetString("shuttle-console-bioscan-radio-warning", ("shuttle", shuttleName));
            _radioSystem.SendRadioMessage(ent, message, "Common", ent);
            _popup.PopupEntity(Loc.GetString("shuttle-console-bioscan-complete-threat"), ent, PopupType.Medium);
        }
        else
        {
            ent.Comp.BioScanStatus = ShuttleBioScanStatus.Clean;
            RemoveInfectedMarker(target.Value);
            _popup.PopupEntity(Loc.GetString("shuttle-console-bioscan-complete-clean"), ent, PopupType.Medium);
        }

        Dirty(ent, ent.Comp);
        RefreshBioScanState(ent);
    }

    private bool HasBioThreat(EntityUid targetGrid, Entity<ShuttleConsoleComponent> console)
    {
        if (!TryComp<ShuttleBioScanAccessComponent>(console, out var access))
            return false;

        var xformQuery = EntityQueryEnumerator<TransformComponent>();
        while (xformQuery.MoveNext(out var uid, out var xform))
        {
            if (xform.GridUid != targetGrid)
                continue;

            foreach (var threatTag in access.ThreatTags)
            {
                if (_tags.HasTag(uid, threatTag))
                    return true;
            }

            foreach (var threatFaction in access.ThreatFactions)
            {
                if (_npcFaction.IsMember((uid, null), threatFaction))
                    return true;
            }

            if (TryComp<HumanoidAppearanceComponent>(uid, out var humanoid))
            {
                foreach (var threatSpecies in access.ThreatSpecies)
                {
                    if (humanoid.Species == threatSpecies)
                        return true;
                }
            }

            if (MetaData(uid).EntityPrototype?.ID is { } protoId)
            {
                foreach (var threatProto in access.ThreatEntityPrototypes)
                {
                    if (string.Equals(protoId, threatProto, StringComparison.Ordinal))
                        return true;
                }
            }
        }

        return false;
    }

    private void ApplyInfectedMarker(EntityUid targetGrid)
    {
        var marker = EnsureComp<ShuttleBioThreatMarkerComponent>(targetGrid);
        if (!marker.Marked)
        {
            marker.OriginalIFFColor = _shuttle.GetIFFColor(targetGrid);
            marker.OriginalName = MetaData(targetGrid).EntityName;
            marker.Marked = true;
        }

        _shuttle.SetIFFColor(targetGrid, Color.Red);
        var infectedPrefix = Loc.GetString("shuttle-console-bioscan-infected-prefix");
        var baseName = marker.OriginalName;
        var infectedName = baseName.StartsWith(infectedPrefix)
            ? baseName
            : $"{infectedPrefix} {baseName}";
        _metaData.SetEntityName(targetGrid, infectedName);
    }

    private void RemoveInfectedMarker(EntityUid targetGrid)
    {
        if (!TryComp<ShuttleBioThreatMarkerComponent>(targetGrid, out var marker) || !marker.Marked)
            return;

        _shuttle.SetIFFColor(targetGrid, marker.OriginalIFFColor);
        _metaData.SetEntityName(targetGrid, marker.OriginalName);
        marker.Marked = false;
    }

    private void SetBioScanStatus(Entity<ShuttleConsoleComponent> ent, ShuttleBioScanStatus status)
    {
        ent.Comp.BioScanStatus = status;
        ent.Comp.BioScanActive = false;
        ent.Comp.BioScanTarget = null;
        ent.Comp.BioScanTime = default;
        Dirty(ent, ent.Comp);
        RefreshBioScanState(ent);
    }

    private void RefreshBioScanState(Entity<ShuttleConsoleComponent> ent)
    {
        DockingInterfaceState? dockState = null;
        UpdateState(ent, ref dockState);
    }
}
