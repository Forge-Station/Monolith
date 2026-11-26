using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.MappingTools;

/// <summary>
/// How a pipe draw lays its pipes: the layer, the colour (or <see cref="AutoColor"/> to match the first pipe it
/// joins) and whether it joins or rebuilds pipes already there. <see cref="Facing"/> is the side a lone half opens
/// to and the side a device's first port faces when nothing around it decides; None means north.
/// </summary>
[Serializable, NetSerializable]
public readonly record struct WFMappingPipeSpec(
    AtmosPipeLayer Layer,
    Color? Color,
    bool AutoColor,
    bool AutoJoin,
    bool Replace,
    PipeDirection Facing = PipeDirection.None);

/// <summary>The pipes already on a cell, on the layer being drawn: their open sides and whether they are one plain pipe.</summary>
public readonly record struct MappingExistingPipe(PipeDirection Directions, bool Plain);

/// <summary>A pipe a draw lays or rebuilds, with the sides it opens to.</summary>
public readonly record struct MappingPipeCell(Vector2i Cell, PipeDirection Directions);

/// <summary>What a pipe draw does, worked out from its path and the pipes around it.</summary>
public sealed class MappingPipePlan
{
    /// <summary>Path cells that get a pipe; a plain pipe already there is rebuilt with these sides.</summary>
    public readonly List<MappingPipeCell> Placed = new();

    /// <summary>Plain pipes off the path's ends that gain a side to join it.</summary>
    public readonly List<MappingPipeCell> Upgraded = new();

    /// <summary>Cells of pipes the path joins, in path order.</summary>
    public readonly List<Vector2i> Joined = new();

    /// <summary>Path cells holding one plain pipe that was kept because Replace is off.</summary>
    public int Skipped;

    /// <summary>Path cells holding a device or several pipes on the layer, which are never rebuilt.</summary>
    public int Blocked;
}

/// <summary>
/// Pure parts of the mapping screen's pipe mode: the dragged path, the pipe shape for a set of sides, and the plan
/// that joins a path to the pipes around it.
/// </summary>
public static class MappingToolsPipes
{
    /// <summary>The four sides, clockwise from north.</summary>
    public static readonly PipeDirection[] Sides =
    {
        PipeDirection.North,
        PipeDirection.East,
        PipeDirection.South,
        PipeDirection.West,
    };

    /// <summary>The primary-layer prototype of each plain pipe shape; other layers are its alternatives.</summary>
    public static string PrototypeFor(PipeShape shape)
    {
        return shape switch
        {
            PipeShape.Half => "GasPipeHalf",
            PipeShape.Straight => "GasPipeStraight",
            PipeShape.Bend => "GasPipeBend",
            PipeShape.TJunction => "GasPipeTJunction",
            _ => "GasPipeFourway",
        };
    }

    /// <summary>The side of <paramref name="from"/> that <paramref name="to"/> touches, or None when they don't share one.</summary>
    public static PipeDirection Side(Vector2i from, Vector2i to)
    {
        var delta = to - from;
        if (delta == new Vector2i(0, 1))
            return PipeDirection.North;
        if (delta == new Vector2i(0, -1))
            return PipeDirection.South;
        if (delta == new Vector2i(1, 0))
            return PipeDirection.East;
        if (delta == new Vector2i(-1, 0))
            return PipeDirection.West;

        return PipeDirection.None;
    }

    /// <summary>The cell next to <paramref name="cell"/> on one side.</summary>
    public static Vector2i Step(Vector2i cell, PipeDirection side)
    {
        return side switch
        {
            PipeDirection.North => cell + new Vector2i(0, 1),
            PipeDirection.South => cell + new Vector2i(0, -1),
            PipeDirection.East => cell + new Vector2i(1, 0),
            PipeDirection.West => cell + new Vector2i(-1, 0),
            _ => cell,
        };
    }

    /// <summary>How many of the four sides are set.</summary>
    public static int Count(PipeDirection directions)
    {
        var count = 0;
        foreach (var side in Sides)
        {
            if (directions.HasDirection(side))
                count++;
        }

        return count;
    }

    /// <summary>
    /// Adds the cell the cursor reached to a path. A jump is bridged with an L, along the last step's axis first;
    /// reaching a cell already on the path cuts the path back to it. False when the path would pass
    /// <paramref name="max"/> cells; it then holds what fitted.
    /// </summary>
    public static bool Extend(List<Vector2i> path, Vector2i cell, int max)
    {
        if (path.Count == 0)
        {
            if (max < 1)
                return false;

            path.Add(cell);
            return true;
        }

        var current = path[^1];
        var alongX = path.Count < 2 || path[^1].X != path[^2].X;
        while (current != cell)
        {
            var moveX = alongX ? current.X != cell.X : current.Y == cell.Y;
            var next = moveX
                ? current + new Vector2i(Math.Sign(cell.X - current.X), 0)
                : current + new Vector2i(0, Math.Sign(cell.Y - current.Y));

            var index = path.IndexOf(next);
            if (index >= 0)
            {
                path.RemoveRange(index + 1, path.Count - index - 1);
            }
            else
            {
                if (path.Count >= max)
                    return false;

                path.Add(next);
            }

            current = next;
        }

        return true;
    }

    /// <summary>
    /// The path through <paramref name="cells"/> in order, every step to a side neighbour; null when it would pass
    /// <paramref name="max"/> cells or the jumps between the cells are that long.
    /// </summary>
    public static List<Vector2i>? Normalise(IReadOnlyList<Vector2i> cells, int max)
    {
        var walk = 0L;
        for (var i = 1; i < cells.Count; i++)
        {
            var delta = cells[i] - cells[i - 1];
            walk += Math.Abs((long) delta.X) + Math.Abs((long) delta.Y);
        }

        if (walk > 4L * max)
            return null;

        var path = new List<Vector2i>();
        foreach (var cell in cells)
        {
            if (!Extend(path, cell, max))
                return null;
        }

        return path;
    }

    /// <summary>
    /// The plain pipe shape with exactly these open sides and the rotation that turns it there: half for one, straight
    /// for two opposite, bend for two adjacent, T for three and four-way for four.
    /// </summary>
    public static bool TryShape(PipeDirection directions, out PipeShape shape, out Angle rotation)
    {
        rotation = Angle.Zero;
        directions &= PipeDirection.Fourway;
        switch (Count(directions))
        {
            case 1:
                shape = PipeShape.Half;
                break;
            case 2:
                shape = directions is PipeDirection.Longitudinal or PipeDirection.Lateral
                    ? PipeShape.Straight
                    : PipeShape.Bend;
                break;
            case 3:
                shape = PipeShape.TJunction;
                break;
            case 4:
                shape = PipeShape.Fourway;
                return true;
            default:
                shape = default;
                return false;
        }

        var baseDirection = shape.ToBaseDirection();
        for (var turn = 0; turn < 4; turn++)
        {
            var angle = Angle.FromDegrees(90 * turn);
            if (baseDirection.RotatePipeDirection(angle.Theta) != directions)
                continue;

            rotation = angle;
            return true;
        }

        return false;
    }

    /// <summary>Whether exactly one of the four sides is set.</summary>
    public static bool IsSide(PipeDirection direction)
    {
        return Array.IndexOf(Sides, direction) >= 0;
    }

    /// <summary>Sides turned by quarter turns, as an entity rotated by <c>90 * turns</c> degrees turns its pipe nodes.</summary>
    public static PipeDirection Turn(PipeDirection directions, int turns)
    {
        return directions.RotatePipeDirection(QuarterTurns(turns).Theta);
    }

    /// <summary>An angle of whole quarter turns, 0 to 3.</summary>
    public static Angle QuarterTurns(int turns)
    {
        return Angle.FromDegrees(90 * (((turns % 4) + 4) % 4));
    }

    /// <summary>
    /// The quarter turns that point a device's first port, south at rest as prototypes are drawn, to
    /// <paramref name="facing"/>; north when it isn't one side.
    /// </summary>
    public static int TurnsToFace(PipeDirection facing)
    {
        if (!IsSide(facing))
            facing = PipeDirection.North;

        for (var turn = 0; turn < 4; turn++)
        {
            if (Turn(PipeDirection.South, turn) == facing)
                return turn;
        }

        return 0;
    }

    /// <summary>
    /// The quarter turns for a device with <paramref name="ports"/> (its sides at rest) on a cell: the turn whose
    /// ports meet the most pipes pointing at the cell, then the most plain pipes beside it that can gain a side, and
    /// of those the nearest to <paramref name="requested"/>.
    /// </summary>
    public static int DeviceTurns(PipeDirection ports, PipeDirection pointing, PipeDirection joinable, int requested)
    {
        var best = ((requested % 4) + 4) % 4;
        var bestScore = -1;
        foreach (var offset in new[] { 0, 1, 3, 2 })
        {
            var turn = ((requested + offset) % 4 + 4) % 4;
            var turned = Turn(ports & PipeDirection.Fourway, turn);
            var score = 8 * Count(turned & pointing) + Count(turned & joinable);
            if (score <= bestScore)
                continue;

            best = turn;
            bestScore = score;
        }

        return best;
    }

    /// <summary>
    /// The message a pipe draw ends with, from what it changed and the path cells it left alone: a refusal when it
    /// changed nothing, a note when it skipped cells, else null.
    /// </summary>
    public static string? ReportKey(int changed, int skipped, int blocked)
    {
        if (changed == 0)
        {
            return (skipped, blocked) switch
            {
                (> 0, > 0) => "wf-mapping-tools-pipes-all-skipped-blocked",
                (> 0, _) => "wf-mapping-tools-pipes-all-skipped",
                (_, > 0) => "wf-mapping-tools-pipes-all-blocked",
                _ => "wf-mapping-tools-pipes-nothing",
            };
        }

        return (skipped, blocked) switch
        {
            (> 0, > 0) => "wf-mapping-tools-pipes-laid-skipped-blocked",
            (> 0, _) => "wf-mapping-tools-pipes-laid-skipped",
            (_, > 0) => "wf-mapping-tools-pipes-laid-blocked",
            _ => null,
        };
    }

    /// <summary>
    /// Works out a pipe draw. Each path cell opens to its neighbours along the path. A cell holding pipes on the
    /// layer is rebuilt with its old sides added when <paramref name="replace"/> is on and it is one plain pipe, else
    /// kept. With <paramref name="autoJoin"/> a cell also opens to pipes pointing at it, kept path cells included,
    /// and each end of the path runs into the plain pipe straight past it, which gains the side it needs. A cell
    /// left with no sides becomes a half: towards its path neighbour, else (a lone click) towards a plain pipe beside
    /// it, which gains the side, else towards <paramref name="facing"/>.
    /// </summary>
    public static MappingPipePlan Plan(IReadOnlyList<Vector2i> path, Func<Vector2i, MappingExistingPipe?> existing,
        bool autoJoin, bool replace, PipeDirection facing = PipeDirection.None)
    {
        var plan = new MappingPipePlan();
        var count = path.Count;
        var index = new Dictionary<Vector2i, int>(count);
        var placed = new bool[count];
        var found = new MappingExistingPipe?[count];
        var directions = new PipeDirection[count];
        var upgrades = new Dictionary<Vector2i, PipeDirection>();
        var joined = new HashSet<Vector2i>();

        void Join(Vector2i cell)
        {
            if (joined.Add(cell))
                plan.Joined.Add(cell);
        }

        for (var i = 0; i < count; i++)
        {
            index[path[i]] = i;
            found[i] = existing(path[i]);
            if (found[i] is not { } pipe)
            {
                placed[i] = true;
            }
            else if (pipe.Plain && replace)
            {
                placed[i] = true;
                directions[i] = pipe.Directions & PipeDirection.Fourway;
                Join(path[i]);
            }
            else if (pipe.Plain)
            {
                plan.Skipped++;
            }
            else
            {
                plan.Blocked++;
            }
        }

        void Upgrade(Vector2i cell, PipeDirection plainDirections, PipeDirection side)
        {
            var current = upgrades.TryGetValue(cell, out var upgraded) ? upgraded : plainDirections & PipeDirection.Fourway;
            if (!upgrades.ContainsKey(cell))
                plan.Upgraded.Add(new MappingPipeCell(cell, PipeDirection.None));

            upgrades[cell] = current | side;
            Join(cell);
        }

        MappingExistingPipe? PipeAt(Vector2i cell)
        {
            return index.TryGetValue(cell, out var at) ? found[at] : existing(cell);
        }

        for (var i = 0; i < count; i++)
        {
            if (!placed[i])
                continue;

            var cell = path[i];
            for (var j = i - 1; j <= i + 1; j += 2)
            {
                if (j < 0 || j >= count)
                    continue;

                var side = Side(cell, path[j]);
                if (placed[j])
                {
                    directions[i] |= side;
                }
                else if (autoJoin && found[j]!.Value.Directions.HasDirection(side.GetOpposite()))
                {
                    directions[i] |= side;
                    Join(path[j]);
                }
            }

            if (!autoJoin)
                continue;

            foreach (var side in Sides)
            {
                if (directions[i].HasDirection(side))
                    continue;

                var next = Step(cell, side);
                if (index.TryGetValue(next, out var at) && placed[at])
                    continue;

                if (PipeAt(next) is { } other && other.Directions.HasDirection(side.GetOpposite()))
                {
                    directions[i] |= side;
                    Join(next);
                }
            }

            if (count < 2 || i != 0 && i != count - 1)
                continue;

            var ahead = Side(i == 0 ? path[1] : path[count - 2], cell);
            var beyond = Step(cell, ahead);
            if (directions[i].HasDirection(ahead) ||
                index.ContainsKey(beyond) ||
                existing(beyond) is not { Plain: true } plain)
            {
                continue;
            }

            directions[i] |= ahead;
            Upgrade(beyond, plain.Directions, ahead.GetOpposite());
        }

        for (var i = 0; i < count; i++)
        {
            if (!placed[i])
                continue;

            if (directions[i] == PipeDirection.None)
                directions[i] = LoneSide(i);

            plan.Placed.Add(new MappingPipeCell(path[i], directions[i]));
        }

        // The side a cell that joins nothing opens to: along the path towards a kept pipe, else a plain pipe beside
        // a lone click (which gains the side), else the facing.
        PipeDirection LoneSide(int i)
        {
            if (i > 0)
                return Side(path[i], path[i - 1]);

            if (i < count - 1)
                return Side(path[i], path[i + 1]);

            var preferred = IsSide(facing) ? facing : PipeDirection.North;
            if (autoJoin)
            {
                var start = Array.IndexOf(Sides, preferred);
                for (var turn = 0; turn < Sides.Length; turn++)
                {
                    var side = Sides[(start + turn) % Sides.Length];
                    var next = Step(path[i], side);
                    if (index.ContainsKey(next) || existing(next) is not { Plain: true } plain)
                        continue;

                    Upgrade(next, plain.Directions, side.GetOpposite());
                    return side;
                }
            }

            return preferred;
        }

        for (var i = 0; i < plan.Upgraded.Count; i++)
        {
            var cell = plan.Upgraded[i].Cell;
            plan.Upgraded[i] = new MappingPipeCell(cell, upgrades[cell]);
        }

        return plan;
    }
}
