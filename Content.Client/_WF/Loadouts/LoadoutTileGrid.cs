using System.Numerics;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.Loadouts;

/// <summary>
/// Virtualised grid of loadout tiles. It reports the height of the whole list so the scrollbar is honest, but
/// only holds the tiles for the rows on screen; the window re-points those as the scroll moves.
/// </summary>
public sealed class LoadoutTileGrid : Container
{
    public const float Separation = 4;

    private int _totalItemCount;
    private int _startRow;
    private float _cellHeight = LoadoutTile.TileHeight;

    /// <summary>How many tiles fit across, decided by the last measure.</summary>
    public int Columns { get; private set; } = 1;

    /// <summary>Width of one tile, stretched so a row fills the viewport.</summary>
    public float CellWidth { get; private set; } = LoadoutTile.TileWidth;

    /// <summary>Number of entries in the whole list, on screen or not.</summary>
    public int TotalItemCount
    {
        get => _totalItemCount;
        set
        {
            if (_totalItemCount == value)
                return;

            _totalItemCount = value;
            InvalidateMeasure();
        }
    }

    /// <summary>Height of a tile; taller when tiles carry a group caption.</summary>
    public float CellHeight
    {
        get => _cellHeight;
        set
        {
            if (MathHelper.CloseTo(_cellHeight, value))
                return;

            _cellHeight = value;
            InvalidateMeasure();
        }
    }

    /// <summary>Row the first pooled child sits on. A row rather than an index, so a column change can't skew it.</summary>
    public int StartRow
    {
        get => _startRow;
        set
        {
            if (_startRow == value)
                return;

            _startRow = value;

            // Often set from this grid's resize event, which is raised inside Arrange where an invalidation is
            // ignored; placing the tiles right away covers that.
            InvalidateArrange();
            ArrangeOverride(Size);
        }
    }

    public float RowHeight => CellHeight + Separation;

    public int RowCount => (TotalItemCount + Columns - 1) / Columns;

    /// <summary>Scroll offset that puts the row holding an entry at the top.</summary>
    public float OffsetOf(int index) => index / Columns * RowHeight;

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var width = float.IsFinite(availableSize.X) && availableSize.X > 0 ? availableSize.X : LoadoutTile.TileWidth;

        Columns = Math.Max(1, (int) ((width + Separation) / (LoadoutTile.TileWidth + Separation)));
        CellWidth = Math.Max(LoadoutTile.TileWidth, (width - (Columns - 1) * Separation) / Columns);

        var cell = new Vector2(CellWidth, CellHeight);
        foreach (var child in Children)
        {
            child.Measure(cell);
        }

        return new Vector2(width, Math.Max(0, RowCount * RowHeight - Separation));
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        var index = 0;

        foreach (var child in Children)
        {
            var row = StartRow + index / Columns;
            var column = index % Columns;

            child.Arrange(UIBox2.FromDimensions(
                column * (CellWidth + Separation),
                row * RowHeight,
                CellWidth,
                CellHeight));

            index++;
        }

        return finalSize;
    }
}
