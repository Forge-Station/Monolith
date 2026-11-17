using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Content.Shared._Goobstation.Research;
using Content.Shared.Research.Prototypes;
using System.Linq;
using System.Numerics;

namespace Content.Client._Goobstation.Research.UI;

/// <summary>
/// UI element for visualizing technologies prerequisites with configurable connection types
/// </summary>
public sealed partial class ResearchesContainerPanel : LayoutContainer
{
    public ResearchesContainerPanel()
    {

    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        // Lines sit under the technology cards so icons stay readable.
        DrawPrerequisiteLines(handle);
        base.Draw(handle);
    }

    private void DrawPrerequisiteLines(DrawingHandleScreen handle)
    {
        var technologies = Children.OfType<FancyResearchConsoleItem>().Where(item => !item.IsGhostPreview).ToList();
        var rects = technologies.Select(GetTechRect).ToList();

        foreach (var dependentItem in technologies)
        {
            if (dependentItem.Prototype.TechnologyPrerequisites.Count <= 0)
                continue;

            foreach (var prerequisiteItem in technologies)
            {
                if (!dependentItem.Prototype.TechnologyPrerequisites.Contains(prerequisiteItem.Prototype.ID))
                    continue;

                DrawChannelConnection(
                    handle,
                    prerequisiteItem,
                    dependentItem,
                    rects,
                    GetRefinedConnectionColor(prerequisiteItem, dependentItem),
                    prerequisiteItem.IsOnPath && dependentItem.IsOnPath);
            }
        }
    }

    /// <summary>
    /// Orthogonal link that leaves one card and enters only its real prerequisite target.
    /// The horizontal run stays in the gap between rows, so it does not skim neighboring cards.
    /// </summary>
    private void DrawChannelConnection(
        DrawingHandleScreen handle,
        FancyResearchConsoleItem prerequisite,
        FancyResearchConsoleItem dependent,
        List<UIBox2> rects,
        Color color,
        bool emphasis)
    {
        var from = GetTechRect(prerequisite);
        var to = GetTechRect(dependent);
        var sameRow = RangesOverlap(from.Top, from.Bottom, to.Top, to.Bottom, 8f);

        Vector2 start;
        Vector2 end;
        float channelY;

        if (sameRow)
        {
            start = new Vector2(from.Center.X, from.Top);
            end = new Vector2(to.Center.X, to.Top);
            channelY = Math.Min(from.Top, to.Top) - 18f;
            for (var i = 0; i < 6 && !IsChannelClear(channelY, start.X, end.X, rects); i++)
                channelY -= 10f;
        }
        else if (from.Center.Y <= to.Center.Y)
        {
            start = new Vector2(from.Center.X, from.Bottom);
            end = new Vector2(to.Center.X, to.Top);
            channelY = FindClearChannelY(start.Y, end.Y, start.X, end.X, rects);
        }
        else
        {
            start = new Vector2(from.Center.X, from.Top);
            end = new Vector2(to.Center.X, to.Bottom);
            channelY = FindClearChannelY(start.Y, end.Y, start.X, end.X, rects);
        }

        var bendA = new Vector2(start.X, channelY);
        var bendB = new Vector2(end.X, channelY);
        DrawVerticalRouted(handle, start.X, start.Y, channelY, rects, color, emphasis);
        DrawCleanLine(handle, bendA, bendB, color, emphasis);
        DrawVerticalRouted(handle, end.X, channelY, end.Y, rects, color, emphasis);
    }

    /// <summary>
    /// Vertical run that steps around any technology card it would otherwise pass through.
    /// </summary>
    private void DrawVerticalRouted(
        DrawingHandleScreen handle,
        float x,
        float yFrom,
        float yTo,
        List<UIBox2> rects,
        Color color,
        bool emphasis,
        int depth = 0)
    {
        if (depth > 6 || Math.Abs(yTo - yFrom) < 1f)
        {
            DrawCleanLine(handle, new Vector2(x, yFrom), new Vector2(x, yTo), color, emphasis);
            return;
        }

        if (!TryFindVerticalBlocker(x, yFrom, yTo, rects, out var blocker))
        {
            DrawCleanLine(handle, new Vector2(x, yFrom), new Vector2(x, yTo), color, emphasis);
            return;
        }

        var goingDown = yTo > yFrom;
        var edgeY = goingDown ? blocker.Top - 8f : blocker.Bottom + 8f;
        var pastY = goingDown ? blocker.Bottom + 8f : blocker.Top - 8f;
        var leavesSpan = goingDown
            ? edgeY <= yFrom + 1f || edgeY >= yTo
            : edgeY >= yFrom - 1f || edgeY <= yTo;
        if (leavesSpan)
        {
            DrawCleanLine(handle, new Vector2(x, yFrom), new Vector2(x, yTo), color, emphasis);
            return;
        }

        var sideX = x >= blocker.Center.X ? blocker.Right + 14f : blocker.Left - 14f;
        DrawCleanLine(handle, new Vector2(x, yFrom), new Vector2(x, edgeY), color, emphasis);
        DrawCleanLine(handle, new Vector2(x, edgeY), new Vector2(sideX, edgeY), color, emphasis);
        DrawCleanLine(handle, new Vector2(sideX, edgeY), new Vector2(sideX, pastY), color, emphasis);
        DrawCleanLine(handle, new Vector2(sideX, pastY), new Vector2(x, pastY), color, emphasis);
        DrawVerticalRouted(handle, x, pastY, yTo, rects, color, emphasis, depth + 1);
    }

    private static bool TryFindVerticalBlocker(float x, float yFrom, float yTo, List<UIBox2> rects, out UIBox2 blocker)
    {
        var yMin = Math.Min(yFrom, yTo);
        var yMax = Math.Max(yFrom, yTo);
        foreach (var rect in rects)
        {
            if (x <= rect.Left + 2f || x >= rect.Right - 2f)
                continue;

            if (rect.Bottom <= yMin + 4f || rect.Top >= yMax - 4f)
                continue;

            blocker = rect;
            return true;
        }

        blocker = default;
        return false;
    }

    /// <summary>
    /// Picks a horizontal channel between the two anchors that does not cut through other technology cards.
    /// </summary>
    private static float FindClearChannelY(float fromY, float toY, float x1, float x2, List<UIBox2> rects)
    {
        var minY = Math.Min(fromY, toY);
        var maxY = Math.Max(fromY, toY);
        var span = maxY - minY;
        if (span < 12f)
            return (minY + maxY) / 2f;

        // Search from the target backwards so the last vertical segment is a short stub into that card only.
        var targetY = Math.Abs(toY - maxY) < 0.5f ? maxY : minY;
        var sourceY = targetY == maxY ? minY : maxY;
        for (var step = 1; step <= 8; step++)
        {
            var y = targetY + (sourceY - targetY) * (step / 9f);
            if (IsChannelClear(y, x1, x2, rects))
                return y;
        }

        return targetY + (sourceY - targetY) * 0.5f;
    }

    private static bool IsChannelClear(float y, float x1, float x2, List<UIBox2> rects)
    {
        var left = Math.Min(x1, x2);
        var right = Math.Max(x1, x2);

        foreach (var rect in rects)
        {
            if (y <= rect.Top + 3f || y >= rect.Bottom - 3f)
                continue;

            if (right < rect.Left || left > rect.Right)
                continue;

            return false;
        }

        return true;
    }

    private static bool RangesOverlap(float aMin, float aMax, float bMin, float bMax, float slack)
    {
        return aMin < bMax - slack && bMin < aMax - slack;
    }

    /// <summary>
    /// Draw tree-style connections where multiple prerequisites branch into a single trunk
    /// </summary>
    private void DrawTreeConnections(DrawingHandleScreen handle, List<FancyResearchConsoleItem> prerequisites, FancyResearchConsoleItem dependent, Color color)
    {
        if (prerequisites.Count == 0)
            return;

        var endCoords = GetTechCenter(dependent);

        if (prerequisites.Count == 1)
        {
            // Single prerequisite - draw as simple connection
            var startCoords = GetTechCenter(prerequisites[0]);
            DrawTreeConnection(handle, startCoords, endCoords, endCoords - startCoords, color);
            return;
        }

        // Multiple prerequisites - create clean tree structure
        var prerequisiteCoords = prerequisites.Select(GetTechCenter).ToList();

        // Sort prerequisites by their position for consistent branching
        var sortedPrereqs = prerequisiteCoords
            .Select((coord, index) => new { Coord = coord, Item = prerequisites[index] })
            .OrderBy(p => p.Coord.Y)
            .ThenBy(p => p.Coord.X)
            .ToList();

        // Calculate a clean trunk position
        var avgX = sortedPrereqs.Average(p => p.Coord.X);
        var avgY = sortedPrereqs.Average(p => p.Coord.Y);
        var avgPos = new Vector2(avgX, avgY);

        // Position trunk junction at a reasonable distance from dependent
        var toDependent = (endCoords - avgPos).Normalized();
        var trunkDistance = Math.Min(80f, (endCoords - avgPos).Length() * 0.6f);
        var trunkPoint = endCoords - toDependent * trunkDistance;

        // Draw clean trunk line from junction to dependent
        DrawCleanLine(handle, trunkPoint, endCoords, color);

        // Draw clean branches from each prerequisite to the trunk junction
        foreach (var prereq in sortedPrereqs)
        {
            DrawCleanTreeBranch(handle, prereq.Coord, trunkPoint, color);
        }

        // Draw a small, clean junction indicator
        DrawCleanJunctionIndicator(handle, trunkPoint, color);
    }

    /// <summary>
    /// Draw spread connections for multiple prerequisites with intelligent anti-overlap routing
    /// </summary>
    private void DrawSpreadConnections(DrawingHandleScreen handle, List<FancyResearchConsoleItem> prerequisites, FancyResearchConsoleItem dependent, Color color)
    {
        if (prerequisites.Count == 0)
            return;

        if (prerequisites.Count == 1)
        {
            // Single prerequisite - use regular spread connection with side connections
            var startCoords = GetTechSideConnection(prerequisites[0], dependent);
            var endCoords = GetTechSideConnection(dependent, prerequisites[0]);
            DrawSpreadConnection(handle, startCoords, endCoords, endCoords - startCoords, color);
            return;
        }

        // Multiple prerequisites - spread them out intelligently to avoid overlaps
        var prerequisiteConnections = prerequisites.Select(prereq => new
        {
            Item = prereq,
            StartCoord = GetTechSideConnection(prereq, dependent),      // Exit from prerequisite
            EndCoord = GetTechSideConnection(dependent, prereq)         // Entry to dependent
        }).ToList();

        // Sort prerequisites by angle relative to the dependent tech
        var sortedPrereqs = prerequisiteConnections
            .Select((conn, index) => new
            {
                StartCoord = conn.StartCoord,
                EndCoord = conn.EndCoord,
                Item = conn.Item,
                Angle = Math.Atan2(conn.StartCoord.Y - conn.EndCoord.Y, conn.StartCoord.X - conn.EndCoord.X)
            })
            .OrderBy(p => p.Angle)
            .ToList();

        // Create spread connections with increasing angular offsets
        for (int i = 0; i < sortedPrereqs.Count; i++)
        {
            var prereq = sortedPrereqs[i];
            var spreadIndex = i - (sortedPrereqs.Count - 1) / 2.0f; // Center the spread around 0

            DrawSpreadConnectionWithIndex(handle, prereq.StartCoord, prereq.EndCoord, color, spreadIndex, sortedPrereqs.Count);
        }
    }

    /// <summary>
    /// Draw an individual spread connection with angled routing at midpoint for anti-overlap
    /// </summary>
    private void DrawSpreadConnectionWithIndex(DrawingHandleScreen handle, Vector2 start, Vector2 end, Color color, float spreadIndex, int totalConnections)
    {
        const float baseOffset = 20f; // Base offset for separation
        const float indexMultiplier = 10f; // Additional offset per connection index

        var delta = end - start;

        // Create perpendicular offset direction for each connection to avoid overlaps
        var mainAngle = Math.Atan2(delta.Y, delta.X);
        var offsetAngle = mainAngle + Math.PI / 2;
        var offsetDirection = new Vector2((float)Math.Cos(offsetAngle), (float)Math.Sin(offsetAngle));

        // Calculate unique offset for this connection based on its index
        var totalOffset = baseOffset + (Math.Abs(spreadIndex) * indexMultiplier);
        var indexOffset = offsetDirection * (totalOffset * Math.Sign(spreadIndex));

        // Create angled routing with bend exactly at midpoint
        var midPoint = Vector2.Lerp(start, end, 0.5f) + indexOffset;

        // Draw two-segment angled line: start -> midpoint -> end
        DrawCleanLine(handle, start, midPoint, color);
        DrawCleanLine(handle, midPoint, end, color);
    }

    /// <summary>
    /// Draw a clean tree branch from prerequisite to trunk junction
    /// </summary>
    private void DrawCleanTreeBranch(DrawingHandleScreen handle, Vector2 start, Vector2 trunkPoint, Color color)
    {
        var delta = trunkPoint - start;
        var distance = delta.Length();

        // Avoid very short or overlapping connections
        if (distance < 10f)
            return;

        // Use a simple two-segment path for clean appearance
        Vector2 intermediatePoint;

        // Determine the best routing based on the spatial relationship
        if (Math.Abs(delta.X) > Math.Abs(delta.Y))
        {
            // Horizontal-dominant: go horizontal first, then vertical
            var horizontalDistance = delta.X * 0.7f; // Don't go all the way
            intermediatePoint = new Vector2(start.X + horizontalDistance, start.Y);
        }
        else
        {
            // Vertical-dominant: go vertical first, then horizontal
            var verticalDistance = delta.Y * 0.7f; // Don't go all the way
            intermediatePoint = new Vector2(start.X, start.Y + verticalDistance);
        }

        // Draw the two-segment branch
        DrawCleanLine(handle, start, intermediatePoint, color);
        DrawCleanLine(handle, intermediatePoint, trunkPoint, color);
    }

    /// <summary>
    /// Draw a small, clean junction indicator at the trunk point
    /// </summary>
    private void DrawCleanJunctionIndicator(DrawingHandleScreen handle, Vector2 position, Color color)
    {
        const float size = 2.5f;

        // Draw a simple small square instead of complex shapes
        var rect = new UIBox2(
            position.X - size,
            position.Y - size,
            position.X + size,
            position.Y + size
        );

        handle.DrawRect(rect, color);
    }

    /// <summary>
    /// Draw tree-style connection for single prerequisite (fallback)
    /// </summary>
    private void DrawTreeConnection(DrawingHandleScreen handle, Vector2 start, Vector2 end, Vector2 delta, Color color)
    {
        // For single connections, tree style behaves like a clean L-shape
        const float straightLineThreshold = 15f;

        if (Math.Abs(delta.X) < straightLineThreshold || Math.Abs(delta.Y) < straightLineThreshold)
        {
            // Direct line for aligned connections
            DrawCleanLine(handle, start, end, color);
        }
        else
        {
            // Create a clean right-angle path
            Vector2 corner;

            // Always prefer the longer axis for the first segment
            if (Math.Abs(delta.X) > Math.Abs(delta.Y))
            {
                corner = new Vector2(end.X, start.Y);
            }
            else
            {
                corner = new Vector2(start.X, end.Y);
            }

            DrawCleanLine(handle, start, corner, color);
            DrawCleanLine(handle, corner, end, color);
        }
    }

    /// <summary>
    /// Get the center point of a tech item
    /// </summary>
    private Vector2 GetTechCenter(FancyResearchConsoleItem tech)
    {
        var techRect = GetTechRect(tech);
        return techRect.Center;
    }

    /// <summary>
    /// Get connection point on the side-middle of a tech item facing toward another tech
    /// </summary>
    private Vector2 GetTechSideConnection(FancyResearchConsoleItem fromTech, FancyResearchConsoleItem toTech)
    {
        var fromRect = GetTechRect(fromTech);
        var toRect = GetTechRect(toTech);

        var fromCenter = fromRect.Center;
        var toCenter = toRect.Center;
        var direction = (toCenter - fromCenter).Normalized();

        // Calculate which side of the FROM tech box to exit from (toward the TO tech)
        var absX = Math.Abs(direction.X);
        var absY = Math.Abs(direction.Y);

        if (absX > absY)
        {
            // Horizontal-dominant connection - exit from the side facing the target
            if (direction.X > 0)
                return new Vector2(fromRect.Right, fromCenter.Y); // Exit right side to go right
            else
                return new Vector2(fromRect.Left, fromCenter.Y); // Exit left side to go left
        }
        else
        {
            // Vertical-dominant connection - exit from the side facing the target
            if (direction.Y > 0)
                return new Vector2(fromCenter.X, fromRect.Bottom); // Exit bottom side to go down
            else
                return new Vector2(fromCenter.X, fromRect.Top); // Exit top side to go up
        }
    }

    /// <summary>
    /// Draw connection based on the configured line type
    /// </summary>
    private void DrawConfigurableConnection(
        DrawingHandleScreen handle,
        Vector2 start,
        Vector2 end,
        Color color,
        PrerequisiteLineType lineType,
        bool emphasis = false)
    {
        var delta = end - start;
        var distance = delta.Length();

        // Early exit for very short connections
        if (distance < 1f)
            return;

        switch (lineType)
        {
            case PrerequisiteLineType.LShape:
                DrawLShapeConnection(handle, start, end, delta, color, emphasis);
                break;

            case PrerequisiteLineType.Diagonal:
                DrawCleanLine(handle, start, end, color, emphasis);
                break;

            case PrerequisiteLineType.Tree:
                DrawTreeConnection(handle, start, end, delta, color);
                break;

            case PrerequisiteLineType.Spread:
                DrawSpreadConnection(handle, start, end, delta, color);
                break;

            default:
                DrawLShapeConnection(handle, start, end, delta, color, emphasis);
                break;
        }
    }

    /// <summary>
    /// Draw L-shaped connection (default clean style)
    /// </summary>
    private void DrawLShapeConnection(DrawingHandleScreen handle, Vector2 start, Vector2 end, Vector2 delta, Color color, bool emphasis = false)
    {
        const float straightLineThreshold = 15f;

        if (Math.Abs(delta.X) < straightLineThreshold || Math.Abs(delta.Y) < straightLineThreshold)
            DrawCleanLine(handle, start, end, color, emphasis);
        else
            DrawSimpleLShape(handle, start, end, delta, color, emphasis);
    }

    /// <summary>
    /// Draw simple L-shaped path
    /// </summary>
    private void DrawSimpleLShape(DrawingHandleScreen handle, Vector2 start, Vector2 end, Vector2 delta, Color color, bool emphasis = false)
    {
        // Vertical trees read better when the bend stays on the child's column.
        var corner = Math.Abs(delta.Y) >= Math.Abs(delta.X)
            ? new Vector2(start.X, end.Y)
            : new Vector2(end.X, start.Y);

        DrawCleanLine(handle, start, corner, color, emphasis);
        DrawCleanLine(handle, corner, end, color, emphasis);
    }

    /// <summary>
    /// Draw direct diagonal connection
    /// </summary>
    private void DrawDiagonalConnection(DrawingHandleScreen handle, Vector2 start, Vector2 end, Color color)
    {
        DrawCleanLine(handle, start, end, color);
    }

    /// <summary>
    /// Draw spread connection that uses angled routing at midpoint to avoid tech boxes
    /// </summary>
    private void DrawSpreadConnection(DrawingHandleScreen handle, Vector2 start, Vector2 end, Vector2 delta, Color color)
    {
        const float straightLineThreshold = 15f;

        // For very aligned connections, just draw straight
        if (Math.Abs(delta.X) < straightLineThreshold || Math.Abs(delta.Y) < straightLineThreshold)
        {
            DrawCleanLine(handle, start, end, color);
            return;
        }

        // Create angled routing with the bend exactly at the midpoint
        const float avoidanceDistance = 30f; // Distance to offset the midpoint for collision avoidance

        // Calculate perpendicular direction for avoidance at midpoint
        var mainAngle = Math.Atan2(delta.Y, delta.X);
        var offsetAngle = mainAngle + Math.PI / 2;
        var avoidanceDirection = new Vector2((float)Math.Cos(offsetAngle), (float)Math.Sin(offsetAngle));

        // Determine avoidance direction based on connection direction for consistency
        // Use the direction of the connection to determine which side to offset
        // This ensures connections going in opposite directions offset to opposite sides
        var avoidanceMultiplier = delta.Y >= 0 ? 1f : -1f; // Consistent direction based on Y-direction
        var avoidanceOffset = avoidanceDirection * avoidanceDistance * avoidanceMultiplier;

        // Create angled path with bend exactly at midpoint: start -> midpoint+offset -> end
        var midPoint = Vector2.Lerp(start, end, 0.5f) + avoidanceOffset;

        // Draw two-segment angled line: start -> midpoint -> end
        DrawCleanLine(handle, start, midPoint, color);
        DrawCleanLine(handle, midPoint, end, color);
    }

    /// <summary>
    /// Get the visual rectangle of a tech item
    /// </summary>
    private UIBox2 GetTechRect(FancyResearchConsoleItem tech)
    {
        var position = new Vector2(tech.PixelPosition.X, tech.PixelPosition.Y);
        var size = new Vector2(tech.PixelWidth, tech.PixelHeight);

        var padding = 6f;
        return new UIBox2(
            position.X + padding,
            position.Y + padding,
            position.X + size.X - padding,
            position.Y + size.Y - padding
        );
    }

    /// <summary>
    /// Determine connection color based on availability
    /// </summary>
    private Color GetRefinedConnectionColor(FancyResearchConsoleItem prerequisite, FancyResearchConsoleItem dependent)
    {
        if (prerequisite.IsOnPath && dependent.IsOnPath)
            return Color.FromHex("#7AD7FF");

        if (prerequisite.IsDimmed || dependent.IsDimmed)
            return new Color(0.55f, 0.2f, 0.2f, 0.22f);

        return ResearchColorScheme.GetConnectionColor(dependent.Availability);
    }

    /// <summary>
    /// Draw a clean line with subtle thickness
    /// </summary>
    private void DrawCleanLine(DrawingHandleScreen handle, Vector2 start, Vector2 end, Color color, bool emphasis = false)
    {
        handle.DrawLine(start, end, color);

        var direction = (end - start).Normalized();
        if (direction.LengthSquared() < 0.001f)
            return;

        var spread = emphasis ? 1.35f : 0.5f;
        var perpendicular = new Vector2(-direction.Y, direction.X) * spread;
        var thicknessColor = new Color(color.R, color.G, color.B, color.A * (emphasis ? 0.85f : 0.6f));
        handle.DrawLine(start + perpendicular, end + perpendicular, thicknessColor);
        handle.DrawLine(start - perpendicular, end - perpendicular, thicknessColor);
    }
}
