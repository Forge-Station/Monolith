using System.Numerics;

namespace Content.Client._WF.Cockpit;

/// <summary>Places the cockpit's large panels: world view and MFD side by side, swapped, or the MFD alone.</summary>
public static class WFCockpitPanelLayout
{
    /// <summary>Distance from the screen edge to the outermost panels.</summary>
    public const float Margin = 8;

    /// <summary>Distance between neighbouring panels in the centre column.</summary>
    public const float Gap = 6;

    /// <summary>The screen and the fixed panels around the two big areas; Side is the width of the full-height column (see <see cref="SideWidth"/>), Strip and Votes only matter when maximised.</summary>
    public readonly record struct Metrics(Vector2 Size, float Top, float Left, float Side, float Bottom, float Motion,
        float Strip = 0, float Votes = 0);

    /// <summary>The rectangle of every panel an arrangement moves; the world, votes and alerts boxes are empty when they do not apply.</summary>
    public readonly record struct Frame(UIBox2 Motion, UIBox2 World, UIBox2 Mfd, UIBox2 Votes, UIBox2 Alerts, UIBox2 Row,
        bool WorldShown);

    /// <summary>Room taken by the margins and the gaps around the left, centre and side columns.</summary>
    private const float ColumnSpacing = 32;

    /// <summary>The narrowest the centre column is allowed to get when the side column grows.</summary>
    private const float MinCenter = 500;

    /// <summary>The width of the centre column, between the left column and the side column.</summary>
    public static float Center(Vector2 size, float left, float side) => Math.Max(160, size.X - left - side - ColumnSpacing);

    /// <summary>
    /// The width of the full-height side column: its normal share of the screen or, when <paramref name="expanded"/>, half
    /// of the room it shares with the centre column, so the two big areas are equal, while the centre keeps its minimum.
    /// </summary>
    public static float SideWidth(Vector2 size, float left, bool expanded)
    {
        var limit = Math.Max(300, size.X - left - MinCenter - ColumnSpacing);
        var normal = Math.Min(Math.Clamp(size.X * 0.29f, 340, 520), limit);
        if (!expanded)
            return normal;
        var half = (size.X - left - ColumnSpacing) / 2;
        return Math.Max(normal, Math.Min(half, limit));
    }

    /// <summary>Whether expanding would make the side column any wider at this window size.</summary>
    public static bool CanExpand(Vector2 size, float left) =>
        SideWidth(size, left, true) - SideWidth(size, left, false) >= 1;

    /// <summary>The height of the flight panel at the foot of the left column.</summary>
    public static float FlightHeight(Vector2 size) => Math.Clamp(size.Y * 0.27f, 218, 250);

    /// <summary>The most the communications and shield row takes from the world view above it.</summary>
    public const float RowExtra = 40;

    /// <summary>
    /// The height of the communications and shield row. On a tall enough screen it stands a little above the flight
    /// panel beside it, the difference coming off the world view, because chat and the shield controls need the room
    /// more. A short screen has none to give: the maximised MFD already runs out of height there.
    /// </summary>
    public static float RowHeight(Vector2 size) =>
        FlightHeight(size) + Math.Clamp((size.Y - 720) * 0.12f, 0, RowExtra);

    /// <summary>The height of the centre and side columns from <paramref name="top"/> to the bottom margin.</summary>
    public static float Height(Vector2 size, float top) => Math.Max(320, size.Y - top - Margin);

    /// <summary>The width of the motion gauges and, when maximised, of the MFD: the centre column, or the centre and side columns less the alert strip.</summary>
    public static float MainWidth(Vector2 size, float left, float side, float strip, bool maximised)
    {
        if (!maximised)
            return Center(size, left, side);
        return Math.Max(160, size.X - Margin - (left + 16) - strip - (strip > 0 ? Gap : 0));
    }

    /// <summary>Lays out the centre and side columns for the chosen arrangement.</summary>
    public static Frame Compute(Metrics metrics, bool swapped, bool maximised)
    {
        var (size, top, left, side, bottom, motion, strip, votes) = metrics;
        var centerX = left + 16;
        var main = MainWidth(size, left, side, strip, maximised);
        var height = Height(size, top);
        var rowY = size.Y - bottom - Margin;
        if (!maximised)
        {
            var big = UIBox2.FromDimensions(centerX, top + motion + Gap, main,
                Math.Max(80, height - bottom - motion - 2 * Gap));
            var sideBox = UIBox2.FromDimensions(size.X - side - Margin, top, side, height);
            return new Frame(UIBox2.FromDimensions(centerX, top, main, motion), swapped ? sideBox : big,
                swapped ? big : sideBox, default, default, UIBox2.FromDimensions(centerX, rowY, main, bottom), true);
        }

        // The MFD takes over the world view and the side column. The motion gauges stay above it and the lower row
        // below, the row widened to the full deck, and the crew alerts get a strip along the right edge.
        var below = top + motion + Gap;
        var mfdTop = votes > 0 ? below + votes + Gap : below;
        var mfdBottom = rowY - Gap;
        return new Frame(UIBox2.FromDimensions(centerX, top, main, motion), default,
            UIBox2.FromDimensions(centerX, mfdTop, main, Math.Max(80, mfdBottom - mfdTop)),
            UIBox2.FromDimensions(centerX, below, main, votes),
            UIBox2.FromDimensions(centerX + main + (strip > 0 ? Gap : 0), top, strip, Math.Max(1, mfdBottom - top)),
            UIBox2.FromDimensions(centerX, rowY, size.X - Margin - centerX, bottom), false);
    }

    /// <summary>Gives the shields what their controls need, up to <paramref name="maxShare"/> of the row, and the rest to communications.</summary>
    public static (float Comms, float Shields) SplitRow(float rowWidth, float shieldsNeed, float maxShare = 0.57f)
    {
        var usable = Math.Max(0, rowWidth - Gap);
        var shields = Math.Clamp(shieldsNeed, 0, usable * maxShare);
        return (usable - shields, shields);
    }
}
