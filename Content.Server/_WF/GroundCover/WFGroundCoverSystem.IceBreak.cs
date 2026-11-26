using Content.Shared._WF.GroundCover;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Tag;
using Content.Shared.Tools.Components;
using Content.Shared.Tools.Systems;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.GroundCover;

public sealed partial class WFGroundCoverSystem
{
    [Dependency] private SharedToolSystem _tool = default!;
    [Dependency] private TagSystem _tag = default!;

    private static readonly ProtoId<TagPrototype> PickaxeTag = "Pickaxe";
    private const string PryQuality = "Prying";

    private void InitializeIceBreak()
    {
        SubscribeLocalEvent<WFFreezableWaterComponent, InteractUsingEvent>(OnIceInteract);
        SubscribeLocalEvent<WFFreezableWaterComponent, WFIceBreakDoAfterEvent>(OnIceBroken);
    }

    private void OnIceInteract(Entity<WFFreezableWaterComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !ent.Comp.Frozen
            || !_tag.HasTag(args.Used, PickaxeTag) && !_tool.HasQuality(args.Used, PryQuality))
            return;

        var speed = TryComp<ToolComponent>(args.Used, out var tool) ? tool.SpeedModifier : 1f;
        var doAfter = new DoAfterArgs(EntityManager, args.User, TimeSpan.FromSeconds(ent.Comp.BreakSeconds / MathF.Max(speed, 0.05f)),
            new WFIceBreakDoAfterEvent(), ent, target: ent, used: args.Used)
        {
            BreakOnDamage = true,
            BreakOnMove = true,
            NeedHand = true,
        };

        if (!_doAfter.TryStartDoAfter(doAfter))
            return;

        _popup.PopupEntity(Loc.GetString("wf-ice-breaking"), args.User, args.User);
        args.Handled = true;
    }

    private void OnIceBroken(Entity<WFFreezableWaterComponent> ent, ref WFIceBreakDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;
        if (BreakHole(ent))
            _popup.PopupEntity(Loc.GetString("wf-ice-broken"), args.User, args.User);
    }

    /// <summary>Breaks a hole in frozen water back to open water, which freezes over again after a while.</summary>
    public bool BreakHole(Entity<WFFreezableWaterComponent> ent)
    {
        if (!ent.Comp.Frozen)
            return false;

        var xform = Transform(ent);
        if (xform.GridUid is { } gridUid && TryComp<MapGridComponent>(gridUid, out var grid))
        {
            var tile = _map.TileIndicesFor(gridUid, grid, xform.Coordinates);
            if (GetIceChunk(gridUid, tile) is { } chunk)
            {
                chunk.Holes ??= new Dictionary<int, float>();
                chunk.Holes[WFSnowCover.IndexIn(tile)] = (float) HoleTime.TotalSeconds;
            }

            SetDepth(gridUid, tile, 0);
        }

        SetFrozen(ent, false);
        return true;
    }
}
