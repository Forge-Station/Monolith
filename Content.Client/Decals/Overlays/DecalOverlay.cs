using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Shared.Decals;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Enumerators;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Client.Decals.Overlays;

/// <summary>
///     Groups decals that are visually identical for drawing — keeps one entry (newest id wins).
/// </summary>
internal readonly record struct IdenticalDecalKey(Vector2 Pos, string Id, int ZIndex, Angle Angle, Color? Color);

public sealed class DecalOverlay : GridOverlay
    {
        private readonly SpriteSystem _sprites;
        private readonly IEntityManager _entManager;
        private readonly IPrototypeManager _prototypeManager;

        private readonly Dictionary<string, (Texture Texture, bool SnapCardinals)> _cachedTextures = new(64);

        private readonly List<(uint Id, Decal Decal)> _decals = new();

        /// <summary>
        ///     Maximum decals drawn per 1×1 tile (drops lowest Z-index first). 0 = unlimited.
        /// </summary>
        public int MaxPerTileDraw { get; set; } = 64;

        /// <summary>
        ///     When true, decals with identical tile-space appearance only draw once (newest id).
        /// </summary>
        public bool RemoveIdenticalDuplicates { get; set; } = true;

        public DecalOverlay(
            SpriteSystem sprites,
            IEntityManager entManager,
            IPrototypeManager prototypeManager)
        {
            _sprites = sprites;
            _entManager = entManager;
            _prototypeManager = prototypeManager;
        }

        protected override void Draw(in OverlayDrawArgs args)
        {
            if (args.MapId == MapId.Nullspace)
                return;

            var owner = Grid.Owner;

            if (!_entManager.TryGetComponent(owner, out DecalGridComponent? decalGrid) ||
                !_entManager.TryGetComponent(owner, out TransformComponent? xform))
            {
                return;
            }

            if (xform.MapID != args.MapId)
                return;

            // Shouldn't need to clear cached textures unless the prototypes get reloaded.
            var handle = args.WorldHandle;
            var xformSystem = _entManager.System<TransformSystem>();
            var eyeAngle = args.Viewport.Eye?.Rotation ?? Angle.Zero;

            var gridAABB = xformSystem.GetInvWorldMatrix(xform).TransformBox(args.WorldBounds.Enlarged(1f));
            var chunkEnumerator = new ChunkIndicesEnumerator(gridAABB, SharedDecalSystem.ChunkSize);
            _decals.Clear();

            while (chunkEnumerator.MoveNext(out var index))
            {
                if (!decalGrid.ChunkCollection.ChunkCollection.TryGetValue(index.Value, out var chunk))
                    continue;

                foreach (var (id, decal) in chunk.Decals)
                {
                    if (!gridAABB.Contains(decal.Coordinates))
                        continue;

                    _decals.Add((id, decal));
                }
            }

            if (_decals.Count == 0)
                return;

            if (RemoveIdenticalDuplicates)
                DeduplicateIdenticalDuplicates();

            if (MaxPerTileDraw > 0)
                CullDenseTiles();

            if (_decals.Count == 0)
                return;

            _decals.Sort((x, y) =>
            {
                var zComp = x.Decal.ZIndex.CompareTo(y.Decal.ZIndex);

                if (zComp != 0)
                    return zComp;

                return x.Id.CompareTo(y.Id);
            });

            var (_, worldRot, worldMatrix) = xformSystem.GetWorldPositionRotationMatrix(xform);
            handle.SetTransform(worldMatrix);

            foreach (var (_, decal) in _decals)
            {
                if (!_cachedTextures.TryGetValue(decal.Id, out var cache))
                {
                    // Nothing to cache someone messed up
                    if (!_prototypeManager.TryIndex<DecalPrototype>(decal.Id, out var decalProto))
                    {
                        continue;
                    }

                    cache = (_sprites.Frame0(decalProto.Sprite), decalProto.SnapCardinals);
                    _cachedTextures[decal.Id] = cache;
                }

                var cardinal = Angle.Zero;

                if (cache.SnapCardinals)
                {
                    var worldAngle = eyeAngle + worldRot;
                    cardinal = worldAngle.GetCardinalDir().ToAngle();
                }

                var angle = decal.Angle - cardinal;

                if (angle.Equals(Angle.Zero))
                    handle.DrawTexture(cache.Texture, decal.Coordinates, decal.Color);
                else
                    handle.DrawTexture(cache.Texture, decal.Coordinates, angle, decal.Color);
            }

            handle.SetTransform(Matrix3x2.Identity);
        }

        /// <summary>
        ///     Same position + prototype + transform + color + Z only need one draw; keep newest (highest id).
        /// </summary>
        private void DeduplicateIdenticalDuplicates()
        {
            if (_decals.Count < 2)
                return;

            _decals.Sort(static (a, b) => b.Id.CompareTo(a.Id));

            var seen = new HashSet<IdenticalDecalKey>();
            var write = 0;

            for (var read = 0; read < _decals.Count; read++)
            {
                var d = _decals[read].Decal;
                var key = new IdenticalDecalKey(d.Coordinates, d.Id, d.ZIndex, d.Angle, d.Color);

                if (!seen.Add(key))
                    continue;

                if (write != read)
                    _decals[write] = _decals[read];

                write++;
            }

            if (write < _decals.Count)
                _decals.RemoveRange(write, _decals.Count - write);
        }

        /// <summary>
        ///     Avoid drawing hundreds of stacked decals on the same tile (same GPU cost as full sort + overdraw).
        /// </summary>
        private void CullDenseTiles()
        {
            var buckets = new Dictionary<Vector2i, List<(uint Id, Decal Decal)>>();

            foreach (var item in _decals)
            {
                var tk = new Vector2i(
                    (int) Math.Floor(item.Decal.Coordinates.X),
                    (int) Math.Floor(item.Decal.Coordinates.Y));

                if (!buckets.TryGetValue(tk, out var list))
                {
                    list = new List<(uint Id, Decal Decal)>();
                    buckets[tk] = list;
                }

                list.Add(item);
            }

            _decals.Clear();

            foreach (var list in buckets.Values)
            {
                if (list.Count > MaxPerTileDraw)
                {
                    list.Sort(static (a, b) =>
                    {
                        var z = a.Decal.ZIndex.CompareTo(b.Decal.ZIndex);
                        if (z != 0)
                            return z;

                        return a.Id.CompareTo(b.Id);
                    });

                    list.RemoveRange(0, list.Count - MaxPerTileDraw);
                }

                _decals.AddRange(list);
            }
        }
}
