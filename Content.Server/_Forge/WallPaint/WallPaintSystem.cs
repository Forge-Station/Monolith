using Content.Server.Administration.Managers;
using Content.Shared._Forge.WallPaint;
using Content.Shared.Administration;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._Forge.WallPaint;

public sealed partial class WallPaintSystem : EntitySystem
{
    [Dependency] private IAdminManager _admin = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private IPrototypeManager _prototype = default!;

    private static readonly HashSet<string> PaintablePrototypeIds = new()
    {
        "WallShuttle",
        "WallShuttleInterior",
        "WallShuttleDiagonal",
        "ShuttleWindow",
        "ShuttleWindowDiagonal",
        "WallReinforced",
        "WallReinforcedDiagonal",
        "ReinforcedWindow",
        "ReinforcedWindowDiagonal",
        "WindowReinforcedDirectional",
        "WallPlastitanium",
        "WallPlastitaniumIndestructible",
        "WallPlastitaniumDiagonal",
        "WallPlastitaniumDiagonalIndestructible",
        "PlastitaniumWindowBase",
        "PlastitaniumWindowSquareBase",
        "PlastitaniumWindow",
        "PlastitaniumWindowIndestructible",
        "PlastitaniumWindowDiagonalBase",
        "PlastitaniumWindowDiagonal",
        "PlastitaniumWindowDiagonalIndestructible",
    };

    private static readonly HashSet<string> ProtectTransparentPrototypeIds = new()
    {
        "ShuttleWindow",
        "ShuttleWindowDiagonal",
        "ReinforcedWindow",
        "ReinforcedWindowDiagonal",
        "WindowReinforcedDirectional",
        "PlastitaniumWindowBase",
        "PlastitaniumWindowSquareBase",
        "PlastitaniumWindow",
        "PlastitaniumWindowIndestructible",
        "PlastitaniumWindowDiagonalBase",
        "PlastitaniumWindowDiagonal",
        "PlastitaniumWindowDiagonalIndestructible",
    };

    public override void Initialize()
    {
        SubscribeNetworkEvent<WallPaintRequestEvent>(OnPaintRequest);
    }

    public bool TrySetPaint(EntityUid uid, Color color, bool remove)
    {
        if (!TryGetPaintSettings(uid, out var protectTransparent))
            return false;

        if (remove)
            return RemComp<WallPaintComponent>(uid);

        var paint = EnsureComp<WallPaintComponent>(uid);
        paint.Color = color;
        paint.ProtectTransparent = protectTransparent;
        Dirty(uid, paint);
        return true;
    }

    public int PaintGrid(EntityUid gridUid, Color color, bool remove)
    {
        var count = 0;
        var query = EntityQueryEnumerator<TransformComponent>();

        while (query.MoveNext(out var uid, out var transform))
        {
            if (transform.GridUid != gridUid)
                continue;

            if (TrySetPaint(uid, color, remove))
                count++;
        }

        return count;
    }

    public bool CanUseMappingPaint(ICommonSession session, EntityUid target)
    {
        if (!_admin.IsAdmin(session, true) ||
            !_admin.HasAdminFlag(session, AdminFlags.Mapping) ||
            !TryGetPaintSettings(target, out _))
        {
            return false;
        }

        if (session.AttachedEntity is not { } attached ||
            !TryComp(attached, out TransformComponent? actorTransform) ||
            !TryComp(target, out TransformComponent? targetTransform))
        {
            return false;
        }

        var targetMap = targetTransform.MapID;
        if (targetMap == MapId.Nullspace ||
            actorTransform.MapID != targetMap ||
            !_mapSystem.IsPaused(targetMap))
        {
            return false;
        }

        return true;
    }

    private bool TryGetPaintSettings(EntityUid uid, out bool protectTransparent)
    {
        if (TryComp(uid, out PaintableWallComponent? paintable))
        {
            protectTransparent = paintable.ProtectTransparent;
            return true;
        }

        protectTransparent = false;

        if (!TryPrototype(uid, out var prototype) || prototype == null)
            return false;

        var isPaintable = false;
        foreach (var (id, _) in _prototype.EnumerateAllParents<EntityPrototype>(prototype.ID, includeSelf: true))
        {
            isPaintable |= PaintablePrototypeIds.Contains(id);
            protectTransparent |= ProtectTransparentPrototypeIds.Contains(id);

            if (isPaintable && protectTransparent)
                return true;
        }

        return isPaintable;
    }

    private void OnPaintRequest(WallPaintRequestEvent ev, EntitySessionEventArgs args)
    {
        if (args.SenderSession is not { } session ||
            !TryGetEntity(ev.Target, out var target) ||
            !CanUseMappingPaint(session, target.Value))
        {
            return;
        }

        TrySetPaint(target.Value, ev.Color, ev.Remove);
    }
}
