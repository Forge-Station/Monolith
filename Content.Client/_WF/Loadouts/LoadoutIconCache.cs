using System.Numerics;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Client._WF.Loadouts;

/// <summary>
/// Preview entities for loadout icons. An entity's sprite draws every layer in its own colour, which a
/// prototype's base icon doesn't, so tiles need a live entity each. The cache shares one per prototype between
/// the controls showing it and keeps a few released ones around, so scrolling back doesn't respawn them.
/// </summary>
public sealed class LoadoutIconCache
{
    /// <summary>How many unused entities are kept before the oldest are deleted.</summary>
    public const int SpareLimit = 96;

    private readonly IEntityManager _entManager;
    private readonly Dictionary<EntProtoId, Slot> _slots = new();
    private readonly Queue<(EntProtoId Id, int Stamp)> _released = new();
    private int _idle;
    private int _stamp;

    public LoadoutIconCache(IEntityManager entManager)
    {
        _entManager = entManager;
    }

    /// <summary>Entities currently alive, in use or spare.</summary>
    public int Count => _slots.Count;

    /// <summary>Takes a reference to the preview entity of a prototype, spawning it if needed.</summary>
    public EntityUid Acquire(EntProtoId id, out Vector2 pixelSize)
    {
        if (!_slots.TryGetValue(id, out var slot))
        {
            var uid = _entManager.SpawnEntity(id, MapCoordinates.Nullspace);
            slot = new Slot(uid);

            if (_entManager.TryGetComponent(uid, out SpriteComponent? sprite))
                slot.PixelSize = _entManager.System<SpriteSystem>().GetLocalBounds((uid, sprite)).Size * EyeManager.PixelsPerMeter;

            _slots[id] = slot;
        }
        else if (slot.Refs == 0)
        {
            _idle--;
        }

        slot.Refs++;
        pixelSize = slot.PixelSize;
        return slot.Uid;
    }

    /// <summary>Gives a reference back. The entity stays alive as a spare until enough newer ones push it out.</summary>
    public void Release(EntProtoId id)
    {
        if (!_slots.TryGetValue(id, out var slot) || slot.Refs == 0)
            return;

        if (--slot.Refs > 0)
            return;

        slot.Stamp = ++_stamp;
        _released.Enqueue((id, slot.Stamp));
        _idle++;

        while (_idle > SpareLimit && _released.TryDequeue(out var old))
        {
            // An entry is stale if its entity was taken again since; a later entry stands for it then.
            if (!_slots.TryGetValue(old.Id, out var spare) || spare.Refs > 0 || spare.Stamp != old.Stamp)
                continue;

            _entManager.DeleteEntity(spare.Uid);
            _slots.Remove(old.Id);
            _idle--;
        }

        // Re-taken entities leave stale entries behind; drop them before they pile up.
        if (_released.Count > SpareLimit * 4)
        {
            var live = new List<(EntProtoId Id, int Stamp)>();
            foreach (var item in _released)
            {
                if (_slots.TryGetValue(item.Id, out var spare) && spare.Refs == 0 && spare.Stamp == item.Stamp)
                    live.Add(item);
            }

            _released.Clear();
            foreach (var item in live)
            {
                _released.Enqueue(item);
            }
        }
    }

    /// <summary>Deletes every entity. Views still pointing at one draw nothing afterwards.</summary>
    public void Clear()
    {
        foreach (var slot in _slots.Values)
        {
            _entManager.DeleteEntity(slot.Uid);
        }

        _slots.Clear();
        _released.Clear();
        _idle = 0;
    }

    private sealed class Slot
    {
        public readonly EntityUid Uid;
        public Vector2 PixelSize;
        public int Refs;
        public int Stamp;

        public Slot(EntityUid uid)
        {
            Uid = uid;
        }
    }
}

/// <summary>Sprite view of a loadout's preview entity, drawn pixel-perfect when it fits its box and shrunk when not.</summary>
public sealed class LoadoutIconView : SpriteView
{
    private readonly LoadoutIconCache _cache;
    private readonly float _box;
    private EntProtoId? _prototype;

    public LoadoutIconView(LoadoutIconCache cache, float box, float scale)
    {
        _cache = cache;
        _box = box;
        Scale = new Vector2(scale, scale);
        SetSize = new Vector2(box, box);
        OverrideDirection = Direction.South;
        MouseFilter = MouseFilterMode.Ignore;
    }

    /// <summary>Shows the preview entity of a prototype, or nothing for null.</summary>
    public void SetPrototype(EntProtoId? prototype)
    {
        if (_prototype == prototype)
            return;

        if (_prototype is { } old)
            _cache.Release(old);

        _prototype = prototype;

        if (prototype is not { } id)
        {
            SetEntity((EntityUid?) null);
            return;
        }

        var uid = _cache.Acquire(id, out var size);

        // Fit measures a sprite by its rotated bounds and so draws it at about 0.7 of the box; only fall back to
        // it for sprites that would spill out at the wanted scale.
        Stretch = Math.Max(size.X, size.Y) * Scale.X <= _box + 0.5f ? StretchMode.None : StretchMode.Fit;
        SetEntity(uid);
    }
}
