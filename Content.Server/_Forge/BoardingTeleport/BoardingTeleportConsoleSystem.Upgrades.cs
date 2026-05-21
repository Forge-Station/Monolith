using Content.Server.Power.EntitySystems;
using Content.Shared._Forge.BoardingTeleport;
using Content.Shared._Forge.BoardingTeleport.Components;
using Robust.Shared.Map;

namespace Content.Server._Forge.BoardingTeleport;

public sealed partial class BoardingTeleportConsoleSystem
{
    private void OnSelectPlatformSlot(Entity<BoardingTeleportConsoleComponent> ent, ref BoardingTeleportSelectPlatformSlotMessage args)
    {
        ent.Comp.SelectedPlatformSlot = Math.Clamp(args.SlotIndex, 0, BoardingTeleportConstants.MaxPlatformLandingSlots - 1);
        Dirty(ent);
        UpdateUi(ent);
    }

    private void OnToggleSharedLanding(Entity<BoardingTeleportConsoleComponent> ent, ref BoardingTeleportToggleSharedLandingMessage args)
    {
        ent.Comp.UseSharedLandingZone = !ent.Comp.UseSharedLandingZone;

        if (ent.Comp.UseSharedLandingZone && ent.Comp.LandingCoordinates is { } shared)
        {
            _lock.EnsurePlatformLandings(ent.Comp, Math.Max(1, _lock.GetOrderedPlatforms(ent.Owner).Count));
            var netCoords = GetNetCoordinates(shared);
            for (var i = 0; i < ent.Comp.PlatformLandings.Count; i++)
                ent.Comp.PlatformLandings[i] = netCoords;
        }

        Dirty(ent);
        UpdateUi(ent);
    }

    private void OnSyncVolley(Entity<BoardingTeleportConsoleComponent> ent, ref BoardingTeleportSyncVolleyMessage args)
    {
        RaiseLocalEvent(new BoardingTeleportSyncVolleyEvent(ent.Owner));
        UpdateUi(ent);
    }

    public EntityCoordinates? GetLandingForPlatform(EntityUid consoleUid, BoardingTeleportConsoleComponent console, EntityUid platformUid)
    {
        var slot = _lock.GetPlatformSlotIndex(consoleUid, platformUid);
        return _lock.GetLandingForPlatform(console, slot);
    }

    private NetCoordinates? GetSelectedLandingNet(BoardingTeleportConsoleComponent console)
    {
        if (console.UseSharedLandingZone)
            return console.LandingCoordinates is { } shared ? GetNetCoordinates(shared) : null;

        var slot = Math.Clamp(console.SelectedPlatformSlot, 0, BoardingTeleportConstants.MaxPlatformLandingSlots - 1);
        if (slot >= 0 && slot < console.PlatformLandings.Count && console.PlatformLandings[slot] is { } perSlot)
            return perSlot;

        return console.LandingCoordinates is { } fallback ? GetNetCoordinates(fallback) : null;
    }

    public void ApplyLockAndScramblerToBalance(
        BoardingTeleportConsoleComponent console,
        EntityUid? targetGrid,
        out float scatterPenalty,
        out float riskPenalty,
        out float scramblerScatter,
        out float scramblerRisk,
        out bool lockExpired)
    {
        _lock.GetLockPenalties(console, out scatterPenalty, out riskPenalty, out lockExpired);

        scramblerScatter = 0f;
        scramblerRisk = 0f;
        if (targetGrid is { } grid && _lock.TryGetScramblerEffect(grid, out var effect))
        {
            scramblerScatter = effect.ScatterBonus;
            scramblerRisk = effect.RiskBonus;
        }
    }

    private List<BoardingTeleportPlatformUiEntry> BuildPlatformUiEntries(EntityUid consoleUid, BoardingTeleportConsoleComponent console)
    {
        var entries = new List<BoardingTeleportPlatformUiEntry>();
        var slot = 0;

        foreach (var platformUid in _lock.GetOrderedPlatforms(consoleUid))
        {
            if (!TryComp<BoardingTeleportPlatformComponent>(platformUid, out var platform))
                continue;

            float? cooldown = null;
            if (_timing.CurTime < platform.NextUse)
                cooldown = (float) (platform.NextUse - _timing.CurTime).TotalSeconds;

            var landing = _lock.GetLandingForPlatform(console, slot);
            var hasLandingResolved = landing != null || console.LandingCoordinates != null;
            var name = MetaData(platformUid).EntityName;

            entries.Add(new BoardingTeleportPlatformUiEntry
            {
                Platform = GetNetEntity(platformUid),
                Name = name,
                CooldownSeconds = cooldown,
                IsReady = cooldown is not > 0.05f && !platform.DeparturePending && this.IsPowered(platformUid, EntityManager),
                HasLanding = hasLandingResolved,
                SlotIndex = slot,
                IsSelected = slot == console.SelectedPlatformSlot,
            });

            slot++;
            if (slot >= BoardingTeleportConstants.MaxPlatformLandingSlots)
                break;
        }

        return entries;
    }

    public bool TryValidateEngine(EntityUid consoleUid, BoardingTeleportConsoleComponent console, out BoardingTeleportStatus status)
    {
        status = BoardingTeleportStatus.None;
        _engine.TryLinkConsole((consoleUid, console));

        if (!_engine.TryGetLinkedEngine(consoleUid, console, out var engineUid, out var engine))
        {
            status = BoardingTeleportStatus.NoEngine;
            return false;
        }

        if (!_lock.IsEngineReady(engineUid, engine, out status))
            return false;

        return true;
    }

    public void StartEngineCooldown(EntityUid consoleUid, BoardingTeleportConsoleComponent console)
    {
        if (!_engine.TryGetLinkedEngine(consoleUid, console, out var engineUid, out var engine))
            return;

        if (engine.JumpCooldown <= 0f)
            return;

        engine.NextJump = _timing.CurTime + TimeSpan.FromSeconds(engine.JumpCooldown);
        Dirty(engineUid, engine);
    }
}
