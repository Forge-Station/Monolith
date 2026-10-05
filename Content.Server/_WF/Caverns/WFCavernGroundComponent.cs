using System.Linq;
using System.Numerics;
using Content.Shared._WF.Caverns;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Caverns;

/// <summary>Links a planet's ground map to the cavern below it, and records every way down.</summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class WFCavernGroundComponent : Component
{
    /// <summary>The cavern map below this ground.</summary>
    [ViewVariables]
    public EntityUid Cavern;

    /// <summary>The cavern prototype it was built from.</summary>
    [ViewVariables]
    public ProtoId<WFCavernPrototype> Prototype;

    /// <summary>The planet centre on the ground grid; the gate cell tries centre + gate offset first.</summary>
    [ViewVariables]
    public Vector2 Centre;

    /// <summary>Every mouth cell looked at so far, by cell index.</summary>
    [ViewVariables]
    public Dictionary<Vector2i, WFCavernCell> Cells = new();

    /// <summary>Every mouth stamped on this ground, in the order they were cut.</summary>
    [ViewVariables]
    public List<WFCavernMouth> Mouths = new();

    /// <summary>The shade over each hole tile, by ground tile index.</summary>
    [ViewVariables]
    public Dictionary<Vector2i, EntityUid> Shades = new();

    /// <summary>The climb point under each lip, by tile index (the same on both maps).</summary>
    [ViewVariables]
    public Dictionary<Vector2i, EntityUid> ClimbPoints = new();

    /// <summary>Ground tiles emptied since the hole queue last ran: holes, or an unload's.</summary>
    [ViewVariables]
    public HashSet<Vector2i> Opened = new();

    /// <summary>Hole tiles filled since the hole queue last ran, whose shades go.</summary>
    [ViewVariables]
    public HashSet<Vector2i> Closed = new();

    /// <summary>Cavern tiles emptied since the hole queue last ran, which it fills again.</summary>
    [ViewVariables]
    public HashSet<Vector2i> FloorOpened = new();

    /// <summary>Holes whose stairs went since the hole queue last ran, which it gives a landing and a climb point.</summary>
    [ViewVariables]
    public HashSet<Vector2i> Refit = new();
}

/// <summary>What became of a mouth cell: claimed, waiting, or without a site.</summary>
public enum WFCavernClaim : byte
{
    /// <summary>Never looked at.</summary>
    Unclaimed,

    /// <summary>The site is stamped.</summary>
    Claimed,

    /// <summary>A site exists but can't be stamped yet: loaded terrain, a grid or something built is in the way.</summary>
    Deferred,

    /// <summary>No candidate passed the pure checks, or the site's footprint or pad is pinned: this cell never gets a mouth.</summary>
    Empty,
}

/// <summary>Why a mouth exists.</summary>
public enum WFCavernMouthKind : byte
{
    /// <summary>The world's mouth near its centre, claimed at build.</summary>
    Gate,

    /// <summary>A cell's mouth, claimed ahead of the terrain streaming in.</summary>
    Cell,

    /// <summary>Carved by an admin.</summary>
    Admin,
}

/// <summary>One mouth cell's cached site and claim state.</summary>
public sealed class WFCavernCell
{
    /// <summary>Where the cell stands.</summary>
    [ViewVariables]
    public WFCavernClaim State = WFCavernClaim.Unclaimed;

    /// <summary>Whether the pure checks have run; the site never changes once they have.</summary>
    [ViewVariables]
    public bool Evaluated;

    /// <summary>The next candidate to check, so an evaluation can span ticks.</summary>
    [ViewVariables]
    public int Cursor;

    /// <summary>The site's anchor and shape, or null when no candidate passed.</summary>
    [ViewVariables]
    public WFCavernSite? Site;
}

/// <summary>Where a cell's mouth goes: the anchor tile and the shape grown around it.</summary>
public readonly record struct WFCavernSite(Vector2i Origin, WFCavernMouthShape Shape);

/// <summary>A stamped way down: a shaped hole, the lip around it and the climb tile on that lip.</summary>
public sealed class WFCavernMouth
{
    /// <summary>The anchor: the hole tile nearest the hole's centroid.</summary>
    public readonly Vector2i Origin;

    /// <summary>The lip tile over the climb point.</summary>
    public readonly Vector2i ClimbTile;

    /// <summary>Why the mouth exists.</summary>
    public readonly WFCavernMouthKind Kind;

    /// <summary>The shape it was cut from, as offsets from <see cref="Origin"/>.</summary>
    public readonly WFCavernMouthShape Shape;

    /// <summary>The hole's ground tiles.</summary>
    public readonly HashSet<Vector2i> Hole = new();

    /// <summary>The lip: every ground tile touching the hole, diagonals included, that is not hole.</summary>
    public readonly HashSet<Vector2i> Ring = new();

    /// <summary>Places a grown shape at its anchor tile.</summary>
    public WFCavernMouth(Vector2i origin, WFCavernMouthShape shape, WFCavernMouthKind kind)
    {
        Origin = origin;
        Shape = shape;
        Kind = kind;
        ClimbTile = origin + shape.Climb;

        foreach (var tile in shape.Hole)
        {
            Hole.Add(origin + tile);
        }

        foreach (var tile in shape.Ring)
        {
            Ring.Add(origin + tile);
        }
    }

    /// <summary>How many tiles the hole has.</summary>
    public int Size => Hole.Count;

    /// <summary>The hole's centroid in ground-local coordinates.</summary>
    public Vector2 Centre => new Vector2(Origin.X, Origin.Y) + Shape.Centroid;

    /// <summary>The hole's lowest corner tile.</summary>
    public Vector2i Min => Origin + Shape.Min;

    /// <summary>The hole's highest corner tile.</summary>
    public Vector2i Max => Origin + Shape.Max;

    /// <summary>The hole and its lip.</summary>
    public IEnumerable<Vector2i> Footprint => Hole.Concat(Ring);

    /// <summary>Whether a ground tile lies inside the hole.</summary>
    public bool Contains(Vector2i tile)
    {
        return Hole.Contains(tile);
    }

    /// <summary>The cavern pad: every tile within <paramref name="radius"/> of a hole tile in either axis.</summary>
    public HashSet<Vector2i> Pad(int radius)
    {
        var pad = new HashSet<Vector2i>();
        foreach (var tile in Shape.Pad(radius))
        {
            pad.Add(Origin + tile);
        }

        return pad;
    }
}
