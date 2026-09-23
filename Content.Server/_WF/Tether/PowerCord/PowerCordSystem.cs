using Content.Server.NodeContainer;
using Content.Server.NodeContainer.EntitySystems;
using Content.Shared._WF.Tether;
using Content.Shared._WF.Tether.PowerCord;
using Content.Shared.Examine;
using Content.Shared.NodeContainer;
using Content.Shared.NodeContainer.NodeGroups;
using Content.Shared.Popups;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Tether.PowerCord;

/// <summary>
/// Power cords. A cord coil used on anything carrying the right voltage bolts a clamp to that
/// tile and runs the ordinary rope flow from it; two clamped ends merge the hulls' power nets
/// until the cord is untied, snapped or one hull leaves.
/// </summary>
public sealed class PowerCordSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _protos = default!;
    [Dependency] private readonly NodeContainerSystem _nodeContainer = default!;
    [Dependency] private readonly NodeGroupSystem _nodeGroup = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedMapSystem _maps = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private static readonly SoundSpecifier SnapSound = new SoundCollectionSpecifier("sparks");

    private const string SparkEffect = "EffectSparks";

    /// <summary>Cord rope type to the clamp it bolts down, built from the clamp prototypes.</summary>
    private Dictionary<string, (EntProtoId Clamp, NodeGroupID Voltage)>? _clamps;

    public override void Initialize()
    {
        base.Initialize();

        // Stage 1 pre-sets AttachPoint when the clicked entity is already an attach point, so this
        // handler vetoes; the node container one supplies a clamp for everything else.
        SubscribeLocalEvent<RopeAttachPointComponent, RopeCoilTargetAttemptEvent>(OnPointCoilAttempt);
        SubscribeLocalEvent<NodeContainerComponent, RopeCoilTargetAttemptEvent>(OnNodeCoilAttempt);
        SubscribeLocalEvent<PowerCordClampComponent, RopeAttachedEvent>(OnAttached);
        SubscribeLocalEvent<PowerCordClampComponent, RopeDetachedEvent>(OnDetached);
        SubscribeLocalEvent<PowerCordClampComponent, RopeCarryCancelledEvent>(OnCarryCancelled);
        SubscribeLocalEvent<PowerCordClampComponent, RopeBrokenEvent>(OnBroken);
        SubscribeLocalEvent<PowerCordClampComponent, AnchorStateChangedEvent>(OnAnchorChanged);
        SubscribeLocalEvent<PowerCordClampComponent, ComponentShutdown>(OnClampShutdown);
        SubscribeLocalEvent<PowerCordClampComponent, ExaminedEvent>(OnExamined);

        _protos.PrototypesReloaded += OnPrototypesReloaded;
    }

    public override void Shutdown()
    {
        _protos.PrototypesReloaded -= OnPrototypesReloaded;
        base.Shutdown();
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args) => _clamps = null;

    #region Coil interaction

    /// <summary>
    /// Refuses a cord on a plain attach point, plain rope on a clamp, and a cord of the wrong
    /// voltage. Clearing <c>AttachPoint</c> is what tells stage 1 the click is not allowed.
    /// </summary>
    private void OnPointCoilAttempt(Entity<RopeAttachPointComponent> ent, ref RopeCoilTargetAttemptEvent args)
    {
        if (args.AttachPoint != ent.Owner)
            return;

        var isCord = GetClamps().ContainsKey(args.RopeType.Id);
        if (!TryComp<PowerCordClampComponent>(ent, out var clamp))
        {
            if (isCord)
                Refuse(ref args, ent.Owner, "power-cord-popup-no-connection");

            return;
        }

        if (clamp.CordType == args.RopeType)
            return;

        Refuse(ref args, ent.Owner, isCord ? "power-cord-popup-wrong-voltage" : "power-cord-popup-cord-only");
    }

    /// <summary>
    /// Supplies the attach point for a cord coil used on a cable, generator, SMES, substation or
    /// APC: an existing clamp on that tile, or a fresh one bolted down there.
    /// </summary>
    private void OnNodeCoilAttempt(Entity<NodeContainerComponent> ent, ref RopeCoilTargetAttemptEvent args)
    {
        if (args.Handled || args.AttachPoint != null || !GetClamps().TryGetValue(args.RopeType.Id, out var cord))
            return;

        var xform = Transform(ent);
        if (!xform.Anchored || xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var gridComp))
        {
            Refuse(ref args, ent.Owner, "power-cord-popup-no-connection");
            return;
        }

        var grid = new Entity<MapGridComponent>(gridUid, gridComp);
        var tile = _maps.TileIndicesFor(grid, xform.Coordinates);
        EntityUid? reuse = null;
        var powered = false;
        foreach (var anchored in _maps.GetAnchoredEntities(grid, tile))
        {
            if (TryComp<PowerCordClampComponent>(anchored, out var existing))
            {
                if (reuse == null && existing.CordType == args.RopeType && HasRoom(anchored))
                    reuse = anchored;

                continue;
            }

            if (!TryComp<NodeContainerComponent>(anchored, out var container))
                continue;

            foreach (var node in container.Nodes.Values)
            {
                if (node.NodeGroupID != cord.Voltage)
                    continue;

                powered = true;
                break;
            }
        }

        if (reuse is { } existingClamp)
        {
            args.AttachPoint = existingClamp;
            args.Handled = true;
            return;
        }

        if (!powered)
        {
            Refuse(ref args, ent.Owner, "power-cord-popup-no-connection");
            return;
        }

        var clamp = Spawn(cord.Clamp, _maps.GridTileToLocal(gridUid, gridComp, tile));
        var clampXform = Transform(clamp);
        if (!clampXform.Anchored)
            _transform.AnchorEntity((clamp, clampXform), grid, tile);

        args.AttachPoint = clamp;
        args.Handled = true;
    }

    private void Refuse(ref RopeCoilTargetAttemptEvent args, EntityUid target, string message)
    {
        args.AttachPoint = null;
        args.Handled = true;
        _popup.PopupEntity(Loc.GetString(message), target, args.User);
    }

    private bool HasRoom(EntityUid point)
    {
        return TryComp<RopeAttachPointComponent>(point, out var attach) && attach.Ropes.Count < attach.MaxRopes;
    }

    #endregion

    #region Linking

    /// <summary>Only two clamps of one voltage conduct; anything else keeps the cord dead.</summary>
    private void OnAttached(Entity<PowerCordClampComponent> ent, ref RopeAttachedEvent args)
    {
        if (args.RopeType != ent.Comp.CordType ||
            !TryComp<PowerCordClampComponent>(args.Other, out var other) ||
            other.CordType != ent.Comp.CordType || other.Voltage != ent.Comp.Voltage)
            return;

        ent.Comp.Partner = GetNetEntity(args.Other);
        ent.Comp.Cord = GetNetEntity(args.Rope);
        Dirty(ent);
        // The far clamp only names this one back on its own copy of the event, so reflood both:
        // the queue is drained after both have run.
        Reflood(ent.Owner);
        Reflood(args.Other);
    }

    private void OnDetached(Entity<PowerCordClampComponent> ent, ref RopeDetachedEvent args)
    {
        ClearLink(ent);
        DeleteIfIdle(ent.Owner);
    }

    /// <summary>
    /// The first click of a cord bolts a clamp down before any rope exists, so a carry dropped
    /// before the second click would leave it behind as litter.
    /// </summary>
    private void OnCarryCancelled(Entity<PowerCordClampComponent> ent, ref RopeCarryCancelledEvent args)
    {
        DeleteIfIdle(ent.Owner);
    }

    /// <summary>A parting cord throws sparks at both clamps. Untying is quiet.</summary>
    private void OnBroken(Entity<PowerCordClampComponent> ent, ref RopeBrokenEvent args)
    {
        if (args.Refunded || args.RopeType != ent.Comp.CordType || Transform(ent).MapID == MapId.Nullspace)
            return;

        _audio.PlayPvs(SnapSound, ent.Owner);
        Spawn(SparkEffect, Transform(ent).Coordinates);
    }

    /// <summary>Unbolting a clamp cuts it out of the net even though the cord may still hang there.</summary>
    private void OnAnchorChanged(Entity<PowerCordClampComponent> ent, ref AnchorStateChangedEvent args)
    {
        if (args.Anchored)
            return;

        ClearLink(ent);
    }

    private void OnClampShutdown(Entity<PowerCordClampComponent> ent, ref ComponentShutdown args)
    {
        ClearLink(ent);
    }

    /// <summary>Drops the link from both sides and refloods both, so neither hull keeps the other's power.</summary>
    private void ClearLink(Entity<PowerCordClampComponent> ent)
    {
        var partnerNet = ent.Comp.Partner;
        if (ent.Comp.Partner != null || ent.Comp.Cord != null)
        {
            ent.Comp.Partner = null;
            ent.Comp.Cord = null;
            Dirty(ent);
            Reflood(ent.Owner);
        }

        if (partnerNet is not { } net || !TryGetEntity(net, out var partner) ||
            !TryComp<PowerCordClampComponent>(partner, out var other))
            return;

        if (other.Partner == GetNetEntity(ent.Owner))
        {
            other.Partner = null;
            other.Cord = null;
            Dirty(partner.Value, other);
        }

        Reflood(partner.Value);
        DeleteIfIdle(partner.Value);
    }

    /// <summary>A clamp with nothing tied to it is just litter on the cable.</summary>
    private void DeleteIfIdle(EntityUid clamp)
    {
        if (TerminatingOrDeleted(clamp) || !TryComp<RopeAttachPointComponent>(clamp, out var attach) ||
            attach.Ropes.Count > 0)
            return;

        QueueDel(clamp);
    }

    private void Reflood(EntityUid clamp)
    {
        if (!TryComp<PowerCordClampComponent>(clamp, out var comp) ||
            !_nodeContainer.TryGetNode<PowerCordNode>(clamp, comp.NodeName, out var node))
            return;

        _nodeGroup.QueueReflood(node);
    }

    #endregion

    private void OnExamined(Entity<PowerCordClampComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        args.PushMarkup(Loc.GetString("power-cord-examine-voltage",
            ("voltage", Loc.GetString($"power-cord-voltage-{ent.Comp.Voltage.ToString().ToLowerInvariant()}"))));

        if (ent.Comp.Partner is not { } net || !TryGetEntity(net, out var partner))
        {
            args.PushMarkup(Loc.GetString("power-cord-examine-unlinked"));
            return;
        }

        var grid = Transform(partner.Value).GridUid;
        args.PushMarkup(Loc.GetString("power-cord-examine-linked",
            ("grid", grid is { } uid ? Comp<MetaDataComponent>(uid).EntityName : Loc.GetString("power-cord-grid-unknown"))));
    }

    /// <summary>
    /// Built from the clamp prototypes so the cord types and their clamps stay in one place: the
    /// YAML. Rebuilt after a prototype reload.
    /// </summary>
    private Dictionary<string, (EntProtoId Clamp, NodeGroupID Voltage)> GetClamps()
    {
        if (_clamps != null)
            return _clamps;

        _clamps = new Dictionary<string, (EntProtoId, NodeGroupID)>();
        foreach (var proto in _protos.EnumeratePrototypes<EntityPrototype>())
        {
            if (proto.Abstract || !proto.TryGetComponent<PowerCordClampComponent>(out var clamp, EntityManager.ComponentFactory))
                continue;

            // The first clamp prototype for a cord type wins, so a mapper's variant cannot shadow it.
            _clamps.TryAdd(clamp.CordType.Id, (proto.ID, clamp.Voltage));
        }

        return _clamps;
    }
}
