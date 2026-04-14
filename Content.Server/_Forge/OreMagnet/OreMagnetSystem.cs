using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._Forge.OreMagnet.Components;
using Content.Server.Construction;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Item;
using Content.Shared.Popups;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Tag;
using Robust.Server.Audio;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.Server._Forge.OreMagnet;

public sealed class OreMagnetSystem : EntitySystem
{
    private const int MaxOreBoxesToUse = 16;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly TagSystem _tag = default!;
    [Dependency] private readonly SharedStorageSystem _storage = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedItemSystem _item = default!;

    private EntityQuery<PhysicsComponent> _physicsQuery;

    public override void Initialize()
    {
        base.Initialize();
        _physicsQuery = GetEntityQuery<PhysicsComponent>();

        SubscribeLocalEvent<OreMagnetComponent, ComponentInit>(OnComponentInit);
        SubscribeLocalEvent<OreMagnetComponent, ActivateInWorldEvent>(OnActivateInWorld);
        SubscribeLocalEvent<OreMagnetComponent, RefreshPartsEvent>(OnRefreshParts);
        SubscribeLocalEvent<OreMagnetComponent, UpgradeExamineEvent>(OnUpgradeExamine);
        SubscribeLocalEvent<OreMagnetComponent, ExaminedEvent>(OnExamined);
    }

    private void OnComponentInit(Entity<OreMagnetComponent> ent, ref ComponentInit args)
    {
        ent.Comp.Cooldown = ent.Comp.BaseCooldown;
    }

    private void OnActivateInWorld(Entity<OreMagnetComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex)
            return;

        var now = _timing.CurTime;
        var xform = Transform(ent);

        if (ent.Comp.Processing)
        {
            _popup.PopupEntity(Loc.GetString("ore-magnet-cycle-busy"), ent, args.User, PopupType.SmallCaution);
            args.Handled = true;
            return;
        }

        if (xform.GridUid is not { Valid: true } currentGrid)
        {
            _popup.PopupEntity(Loc.GetString("ore-magnet-requires-grid"), ent, args.User, PopupType.SmallCaution);
            args.Handled = true;
            return;
        }

        if (ent.Comp.BoundGridUid is { Valid: true } boundGrid &&
            boundGrid != currentGrid &&
            now < ent.Comp.BoundUntil)
        {
            _popup.PopupEntity(Loc.GetString("ore-magnet-bound-to-grid"), ent, args.User, PopupType.SmallCaution);
            args.Handled = true;
            return;
        }

        var gridCooldown = EnsureComp<OreMagnetGridCooldownComponent>(currentGrid);
        if (now < gridCooldown.NextUseTime)
        {
            var remainingMinutes = Math.Ceiling((gridCooldown.NextUseTime - now).TotalMinutes);
            _popup.PopupEntity(
                Loc.GetString("ore-magnet-cooldown-active", ("minutes", remainingMinutes)),
                ent,
                args.User,
                PopupType.SmallCaution);
            args.Handled = true;
            return;
        }

        var oreBoxes = GetOreBoxesOnGrid(currentGrid, xform);
        if (oreBoxes.Count == 0)
        {
            _popup.PopupEntity(Loc.GetString("ore-magnet-no-orebox-on-grid"), ent, args.User, PopupType.SmallCaution);
            args.Handled = true;
            return;
        }

        var range = MathF.Min(ent.Comp.Range, ent.Comp.MaxRange);
        ent.Comp.PendingOres.Clear();
        foreach (var near in _lookup.GetEntitiesInRange(ent, range, LookupFlags.Dynamic | LookupFlags.Sundries | LookupFlags.Approximate))
        {
            if (ent.Comp.MaxPendingOres > 0 &&
                ent.Comp.PendingOres.Count >= ent.Comp.MaxPendingOres)
                break;

            if (near == ent.Owner)
                continue;

            if (!_tag.HasTag(near, "Ore"))
                continue;

            if (!_physicsQuery.TryGetComponent(near, out var physics) || physics.BodyStatus != BodyStatus.OnGround)
                continue;

            ent.Comp.PendingOres.Add(near);
        }

        gridCooldown.NextUseTime = now + ent.Comp.Cooldown;
        ent.Comp.BoundGridUid = currentGrid;
        ent.Comp.BoundUntil = gridCooldown.NextUseTime;
        ent.Comp.Processing = ent.Comp.PendingOres.Count > 0;
        ent.Comp.NextBatchTime = now;

        _audio.PlayPvs(ent.Comp.ActivateSound, ent);

        var popup = ent.Comp.PendingOres.Count > 0
            ? Loc.GetString("ore-magnet-cycle-started", ("count", ent.Comp.PendingOres.Count))
            : Loc.GetString("ore-magnet-pull-empty");
        _popup.PopupEntity(popup, ent, args.User, PopupType.Medium);

        args.Handled = true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<OreMagnetComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out var xform))
        {
            if (!comp.Processing || comp.PendingOres.Count == 0 || comp.NextBatchTime > now)
                continue;

            if (xform.GridUid is not { Valid: true } currentGrid)
            {
                comp.Processing = false;
                comp.PendingOres.Clear();
                continue;
            }

            var oreBoxes = GetOreBoxesOnGrid(currentGrid, xform);
            var droppedNearMagnet = 0;

            var batch = Math.Max(1, comp.BatchSize);
            for (var i = 0; i < batch && comp.PendingOres.Count > 0; i++)
            {
                var index = comp.PendingOres.Count - 1;
                var oreUid = comp.PendingOres[index];
                comp.PendingOres.RemoveAt(index);

                if (!Exists(oreUid) ||
                    !_tag.HasTag(oreUid, "Ore") ||
                    !_physicsQuery.TryGetComponent(oreUid, out var physics) ||
                    physics.BodyStatus != BodyStatus.OnGround)
                {
                    continue;
                }

                if (!TryComp<ItemComponent>(oreUid, out var item))
                    continue;

                var itemArea = _item.GetItemShape((oreUid, item)).GetArea();

                if (!TryInsertIntoAnyOreBox(oreUid, itemArea, oreBoxes))
                {
                    MoveOreNearMagnet((uid, comp), oreUid, droppedNearMagnet);
                    droppedNearMagnet++;
                }
            }

            if (comp.PendingOres.Count == 0)
                comp.Processing = false;

            comp.NextBatchTime = now + comp.BatchDelay;
        }
    }

    private void OnRefreshParts(Entity<OreMagnetComponent> ent, ref RefreshPartsEvent args)
    {
        var rating = args.PartRatings[ent.Comp.MachinePartCooldown] - 1;
        var newSeconds = ent.Comp.BaseCooldown.TotalSeconds * MathF.Pow(ent.Comp.PartRatingCooldownMultiplier, rating);
        var minSeconds = ent.Comp.MinCooldown.TotalSeconds;
        ent.Comp.Cooldown = TimeSpan.FromSeconds(Math.Max(newSeconds, minSeconds));
    }

    private void OnUpgradeExamine(Entity<OreMagnetComponent> ent, ref UpgradeExamineEvent args)
    {
        args.AddPercentageUpgrade("ore-magnet-cooldown-upgrade", (float) (ent.Comp.Cooldown / ent.Comp.BaseCooldown));
    }

    private void OnExamined(Entity<OreMagnetComponent> ent, ref ExaminedEvent args)
    {
        var linkedState = Loc.GetString("ore-magnet-linked-state-auto");

        args.PushMarkup(Loc.GetString("ore-magnet-examine-linked", ("state", linkedState)));
    }

    private bool TryInsertIntoAnyOreBox(EntityUid oreUid, int itemArea, List<OreBoxState> oreBoxes)
    {
        foreach (var box in oreBoxes)
        {
            if (box.FreeArea < itemArea)
                continue;

            if (_storage.Insert(box.Uid, oreUid, out _, storageComp: box.Storage, playSound: false))
            {
                box.FreeArea -= itemArea;
                return true;
            }
        }

        return false;
    }

    private void MoveOreNearMagnet(Entity<OreMagnetComponent> magnet, EntityUid oreUid, int index)
    {
        var magnetXform = Transform(magnet);
        var baseCoords = magnetXform.Coordinates;
        var ring = 1f + (index / 8) * 0.35f;
        var angle = (float) (index % 8) / 8f * MathF.Tau;
        var offset = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * ring;
        _transform.SetCoordinates(oreUid, baseCoords.Offset(offset));
    }

    private List<OreBoxState> GetOreBoxesOnGrid(EntityUid gridUid, TransformComponent magnetXform)
    {
        var boxes = new List<(OreBoxState Box, float Distance)>();
        var query = EntityQueryEnumerator<StorageComponent, TransformComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out var storage, out var xform, out var meta))
        {
            if (xform.GridUid != gridUid)
                continue;

            if (meta.EntityPrototype?.ID is not { } id || !id.StartsWith("OreBox", StringComparison.Ordinal))
                continue;

            var totalArea = storage.Grid.GetArea();
            var filledArea = _storage.GetCumulativeItemAreas((uid, storage));
            var freeArea = totalArea - filledArea;
            if (freeArea <= 0)
                continue;

            var distance = (xform.LocalPosition - magnetXform.LocalPosition).LengthSquared();
            boxes.Add((new OreBoxState(uid, storage, freeArea), distance));
        }

        boxes.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        return boxes
            .Take(MaxOreBoxesToUse)
            .Select(entry => entry.Box)
            .ToList();
    }

    private sealed class OreBoxState
    {
        public EntityUid Uid { get; }
        public StorageComponent Storage { get; }
        public int FreeArea { get; set; }

        public OreBoxState(EntityUid uid, StorageComponent storage, int freeArea)
        {
            Uid = uid;
            Storage = storage;
            FreeArea = freeArea;
        }
    }
}
