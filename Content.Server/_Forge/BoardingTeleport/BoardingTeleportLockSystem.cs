using Content.Server.DeviceNetwork.Systems;
using Content.Server.Power.EntitySystems;
using Content.Server.Shuttles.Systems;
using Content.Shared._Forge.BoardingTeleport;
using Content.Shared._Forge.BoardingTeleport.Components;
using Content.Shared.DeviceNetwork.Components;
using Content.Shared.Power;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server._Forge.BoardingTeleport;

public sealed class BoardingTeleportLockSystem : EntitySystem
{
    private readonly HashSet<Entity<BoardingBluespaceScramblerComponent>> _scramblerScratch = new();

    [Dependency] private readonly DeviceListSystem _deviceList = default!;
    [Dependency] private readonly DockingSystem _docking = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public void GetLockPenalties(
        BoardingTeleportConsoleComponent console,
        out float scatterPenalty,
        out float riskPenalty,
        out bool expired)
    {
        var age = BoardingTeleportLockDegrade.GetLockAgeSeconds(console.LockEstablishedAt, _timing);
        var steps = BoardingTeleportLockDegrade.GetDegradeSteps(age);
        scatterPenalty = BoardingTeleportLockDegrade.GetScatterPenalty(steps);
        riskPenalty = BoardingTeleportLockDegrade.GetRiskPenalty(steps);
        expired = BoardingTeleportLockDegrade.IsLockExpired(age);
    }

    public bool TryGetScramblerEffect(EntityUid targetGrid, out BoardingTeleportScramblerEffect effect)
    {
        effect = default;
        var blocks = false;
        var scatter = 0f;
        var risk = 0f;

        _scramblerScratch.Clear();
        _lookup.GetChildEntities(targetGrid, _scramblerScratch);
        foreach (var (uid, scrambler) in _scramblerScratch)
        {
            if (!this.IsPowered(uid, EntityManager))
                continue;

            if (scrambler.BlockLocks)
                blocks = true;

            scatter = MathF.Max(scatter, scrambler.ScatterBonus);
            risk = MathF.Max(risk, scrambler.RiskBonus);
        }

        if (!blocks && scatter <= 0f && risk <= 0f)
            return false;

        effect = new BoardingTeleportScramblerEffect
        {
            BlocksLock = blocks,
            ScatterBonus = scatter,
            RiskBonus = risk,
        };
        return true;
    }

    public bool IsFriendlyTarget(EntityUid scannerGrid, EntityUid targetGrid, bool blockFriendly)
    {
        if (!blockFriendly)
            return false;

        if (scannerGrid == targetGrid)
            return true;

        return AreGridsDocked(scannerGrid, targetGrid);
    }

    public bool IsEngineReady(
        EntityUid engineUid,
        BoardingTeleportEngineComponent engine,
        out BoardingTeleportStatus status)
    {
        status = BoardingTeleportStatus.None;

        if (!this.IsPowered(engineUid, EntityManager))
        {
            status = BoardingTeleportStatus.NoEnginePower;
            return false;
        }

        if (_timing.CurTime < engine.NextJump)
        {
            status = BoardingTeleportStatus.EngineRecharging;
            return false;
        }

        return true;
    }

    public EntityCoordinates? GetLandingForPlatform(BoardingTeleportConsoleComponent console, int slotIndex)
    {
        if (console.UseSharedLandingZone)
            return console.LandingCoordinates;

        if (slotIndex >= 0 && slotIndex < console.PlatformLandings.Count &&
            console.PlatformLandings[slotIndex] is { } netLanding)
        {
            return EntityManager.GetCoordinates(netLanding);
        }

        return console.LandingCoordinates;
    }

    public int GetPlatformSlotIndex(EntityUid consoleUid, EntityUid platformUid)
    {
        if (!TryComp<DeviceListComponent>(consoleUid, out var deviceList))
            return 0;

        var index = 0;
        foreach (var device in _deviceList.GetAllDevices(consoleUid, deviceList))
        {
            if (device == platformUid)
                return index;

            if (HasComp<BoardingTeleportPlatformComponent>(device))
                index++;
        }

        return 0;
    }

    public List<EntityUid> GetOrderedPlatforms(EntityUid consoleUid)
    {
        var list = new List<EntityUid>();
        if (!TryComp<DeviceListComponent>(consoleUid, out var deviceList))
            return list;

        foreach (var device in _deviceList.GetAllDevices(consoleUid, deviceList))
        {
            if (HasComp<BoardingTeleportPlatformComponent>(device))
                list.Add(device);
        }

        return list;
    }

    public void EnsurePlatformLandings(BoardingTeleportConsoleComponent console, int slotCount)
    {
        while (console.PlatformLandings.Count < slotCount)
            console.PlatformLandings.Add(null);

        if (console.PlatformLandings.Count > BoardingTeleportConstants.MaxPlatformLandingSlots)
            console.PlatformLandings.RemoveRange(BoardingTeleportConstants.MaxPlatformLandingSlots, console.PlatformLandings.Count - BoardingTeleportConstants.MaxPlatformLandingSlots);
    }

    public void MarkLockEstablished(BoardingTeleportConsoleComponent console)
    {
        console.LockEstablishedAt = _timing.CurTime;
    }

    private bool AreGridsDocked(EntityUid gridA, EntityUid gridB) =>
        _docking.AreGridsDocked(gridA, gridB);
}
