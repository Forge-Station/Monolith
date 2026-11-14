using Content.Server._WF.Caverns;
using Content.Shared._WF.GroundCover;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Tools.Components;
using Content.Shared.Tools.Systems;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.GroundCover;

public sealed partial class WFGroundCoverSystem
{
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    private void InitializeShovel()
    {
        // Snow comes off before the ground under it is dug.
        SubscribeLocalEvent<WFSnowShovelComponent, AfterInteractEvent>(OnShovelInteract,
            before: new[] { typeof(SharedToolSystem), typeof(WFCavernDigSystem) });
        SubscribeLocalEvent<WFSnowShovelComponent, WFSnowShovelDoAfterEvent>(OnShoveled);
    }

    private void OnShovelInteract(Entity<WFSnowShovelComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target != null)
            return;

        var click = _transform.ToMapCoordinates(args.ClickLocation);
        if (!_map.TryFindGridAt(click, out var gridUid, out var grid))
            return;

        var tile = _map.TileIndicesFor(gridUid, grid, click);
        if (GetDepth(gridUid, tile) == 0
            || !_interaction.InRangeUnobstructed(args.User, _map.GridTileToLocal(gridUid, grid, tile), popup: false))
            return;

        var speed = TryComp<ToolComponent>(ent, out var tool) ? tool.SpeedModifier : 1f;
        var clear = new WFSnowShovelDoAfterEvent(GetNetEntity(gridUid), tile);
        var doAfter = new DoAfterArgs(EntityManager, args.User, TimeSpan.FromSeconds(ent.Comp.Seconds / MathF.Max(speed, 0.05f)),
            tool != null ? new SharedToolSystem.ToolDoAfterEvent(0f, clear, GetNetEntity(ent)) : clear, ent, target: ent, used: ent)
        {
            BreakOnDamage = true,
            BreakOnMove = true,
            NeedHand = true,
        };

        if (!_doAfter.TryStartDoAfter(doAfter))
            return;

        args.Handled = true;
    }

    private void OnShoveled(Entity<WFSnowShovelComponent> ent, ref WFSnowShovelDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;
        var grid = GetEntity(args.Grid);
        if (!HasComp<MapGridComponent>(grid) || GetDepth(grid, args.Tile) == 0)
            return;

        SetDepth(grid, args.Tile, 0);
        _popup.PopupEntity(Loc.GetString("wf-snow-cover-shoveled"), args.User, args.User);
    }
}
