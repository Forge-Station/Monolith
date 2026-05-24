using Content.Shared._Forge.BoardingTeleport.Components;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;

namespace Content.Server._Forge.BoardingTeleport;

public sealed class BoardingTeleportEngineSystem : EntitySystem
{
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedRadarConsoleSystem _radar = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BoardingTeleportEngineComponent, ComponentStartup>(OnEngineStartup);
        SubscribeLocalEvent<BoardingTeleportEngineComponent, ComponentShutdown>(OnEngineShutdown);
    }

    private void OnEngineStartup(Entity<BoardingTeleportEngineComponent> ent, ref ComponentStartup args)
    {
        TryLinkEngine(ent);
    }

    private void OnEngineShutdown(Entity<BoardingTeleportEngineComponent> ent, ref ComponentShutdown args)
    {
        UnlinkEngine(ent.Owner, ent.Comp);
    }

    public void UnlinkConsole(Entity<BoardingTeleportConsoleComponent> ent)
    {
        UnlinkConsole(ent.Owner, ent.Comp);
    }

    public void TryLinkEngine(Entity<BoardingTeleportEngineComponent> ent)
    {
        if (!TryGetGridUid(ent.Owner, out var gridUid))
            return;

        if (ent.Comp.LinkedConsole is { } linkedConsole &&
            Exists(linkedConsole) &&
            Transform(linkedConsole).GridUid == gridUid)
        {
            SyncRadarRange(linkedConsole, ent.Comp);
            return;
        }

        UnlinkEngine(ent.Owner, ent.Comp);

        var console = FindBestConsoleOnGrid(gridUid, ent.Owner);
        if (console == null)
            return;

        Link(ent.Owner, ent.Comp, console.Value);
    }

    public void TryLinkConsole(Entity<BoardingTeleportConsoleComponent> ent)
    {
        if (!TryGetGridUid(ent.Owner, out var gridUid))
            return;

        if (ent.Comp.LinkedEngine is { } linkedEngine &&
            Exists(linkedEngine) &&
            Transform(linkedEngine).GridUid == gridUid)
        {
            if (TryComp<BoardingTeleportEngineComponent>(linkedEngine, out var linkedEngineComp))
                SyncRadarRange(ent.Owner, linkedEngineComp);
            return;
        }

        UnlinkConsole(ent.Owner, ent.Comp);

        var engine = FindBestEngineOnGrid(gridUid, ent.Owner);
        if (engine == null)
            return;

        if (!TryComp<BoardingTeleportEngineComponent>(engine.Value, out var engineComp))
            return;

        Link(engine.Value, engineComp, ent.Owner);
    }

    public bool TryGetLinkedEngine(
        EntityUid consoleUid,
        BoardingTeleportConsoleComponent console,
        out EntityUid engineUid,
        out BoardingTeleportEngineComponent engine)
    {
        engineUid = default;
        engine = null!;

        if (console.LinkedEngine is not { } linked || !Exists(linked))
            return false;

        if (!TryComp<BoardingTeleportEngineComponent>(linked, out var engineComp))
            return false;

        if (!TryGetGridUid(consoleUid, out var consoleGrid) ||
            Transform(linked).GridUid != consoleGrid)
        {
            return false;
        }

        engineUid = linked;
        engine = engineComp;
        return true;
    }

    private void Link(EntityUid engineUid, BoardingTeleportEngineComponent engine, EntityUid consoleUid)
    {
        if (engine.LinkedConsole is { } oldConsole && oldConsole != consoleUid)
            ClearConsoleLink(oldConsole);

        if (TryComp<BoardingTeleportConsoleComponent>(consoleUid, out var console) &&
            console.LinkedEngine is { } oldEngine &&
            oldEngine != engineUid)
        {
            if (TryComp<BoardingTeleportEngineComponent>(oldEngine, out var oldEngineComp))
                UnlinkEngine(oldEngine, oldEngineComp);
        }

        engine.LinkedConsole = consoleUid;
        Dirty(engineUid, engine);

        if (TryComp<BoardingTeleportConsoleComponent>(consoleUid, out console))
        {
            console.LinkedEngine = engineUid;
            Dirty(consoleUid, console);
        }

        SyncRadarRange(consoleUid, engine);
    }

    private void UnlinkEngine(EntityUid engineUid, BoardingTeleportEngineComponent engine)
    {
        if (engine.LinkedConsole is { } consoleUid)
            ClearConsoleLink(consoleUid);

        engine.LinkedConsole = null;
        Dirty(engineUid, engine);
    }

    private void UnlinkConsole(EntityUid consoleUid, BoardingTeleportConsoleComponent console)
    {
        if (console.LinkedEngine is { } engineUid &&
            TryComp<BoardingTeleportEngineComponent>(engineUid, out var engine) &&
            engine.LinkedConsole == consoleUid)
        {
            engine.LinkedConsole = null;
            Dirty(engineUid, engine);
        }

        console.LinkedEngine = null;
        Dirty(consoleUid, console);
    }

    private void ClearConsoleLink(EntityUid consoleUid)
    {
        if (!TryComp<BoardingTeleportConsoleComponent>(consoleUid, out var console))
            return;

        console.LinkedEngine = null;
        Dirty(consoleUid, console);
    }

    private EntityUid? FindBestConsoleOnGrid(EntityUid gridUid, EntityUid engineUid)
    {
        var enginePos = _transform.GetWorldPosition(engineUid);
        EntityUid? best = null;
        var bestDist = float.MaxValue;

        var query = EntityQueryEnumerator<BoardingTeleportConsoleComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid != gridUid)
                continue;

            if (!TryComp<BoardingTeleportConsoleComponent>(uid, out var console))
                continue;

            if (console.LinkedEngine is { } linkedEngine &&
                linkedEngine != engineUid &&
                Exists(linkedEngine))
                continue;

            var dist = (_transform.GetWorldPosition(uid) - enginePos).LengthSquared();
            if (dist >= bestDist)
                continue;

            bestDist = dist;
            best = uid;
        }

        return best;
    }

    private EntityUid? FindBestEngineOnGrid(EntityUid gridUid, EntityUid consoleUid)
    {
        var consolePos = _transform.GetWorldPosition(consoleUid);
        EntityUid? best = null;
        var bestDist = float.MaxValue;

        var query = EntityQueryEnumerator<BoardingTeleportEngineComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var engine, out var xform))
        {
            if (xform.GridUid != gridUid)
                continue;

            if (engine.LinkedConsole is { } linkedConsole &&
                linkedConsole != consoleUid &&
                Exists(linkedConsole))
                continue;

            var dist = (_transform.GetWorldPosition(uid) - consolePos).LengthSquared();
            if (dist >= bestDist)
                continue;

            bestDist = dist;
            best = uid;
        }

        return best;
    }

    private void SyncRadarRange(EntityUid consoleUid, BoardingTeleportEngineComponent engine)
    {
        if (!TryComp<RadarConsoleComponent>(consoleUid, out var radar))
            return;

        if (MathF.Abs(radar.MaxRange - engine.Range) < 0.01f)
            return;

        _radar.SetRange(consoleUid, engine.Range, radar);
    }

    private bool TryGetGridUid(EntityUid uid, out EntityUid gridUid)
    {
        var grid = Transform(uid).GridUid;
        if (grid == null)
        {
            gridUid = default;
            return false;
        }

        gridUid = grid.Value;
        return true;
    }
}
