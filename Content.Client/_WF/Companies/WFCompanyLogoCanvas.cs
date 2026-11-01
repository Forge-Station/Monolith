using System.Numerics;
using Content.Shared._WF.Companies;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Input;

namespace Content.Client._WF.Companies;

/// <summary>A square pixel canvas: left click paints, right click erases, with fill and colour-pick tools.</summary>
public sealed class WFCompanyLogoCanvas : Control
{
    public enum Tool : byte { Pencil, Fill, Picker }

    /// <summary>Raw RGBA pixels, row by row.</summary>
    public byte[] Pixels;

    public Color Brush = Color.White;
    public Tool Mode = Tool.Pencil;

    /// <summary>Raised when the picker tool reads a colour off the canvas.</summary>
    public event Action<Color>? ColourPicked;

    private bool? _painting;

    private readonly int Size;

    public WFCompanyLogoCanvas(int size)
    {
        Size = size;
        Pixels = new byte[size * size * 4];
        MinSize = new Vector2(384, 384);
        MouseFilter = MouseFilterMode.Stop;
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        var erase = args.Function == EngineKeyFunctions.UIRightClick;
        if (args.Function != EngineKeyFunctions.UIClick && !erase)
            return;
        args.Handle();
        if (Cell(args.RelativePosition) is not { } cell)
            return;
        switch (Mode)
        {
            case Tool.Picker when !erase:
                ColourPicked?.Invoke(Get(cell.X, cell.Y));
                break;
            case Tool.Fill:
                Fill(cell.X, cell.Y, erase ? Color.Transparent : Brush);
                break;
            default:
                _painting = erase;
                Set(cell.X, cell.Y, erase ? Color.Transparent : Brush);
                break;
        }
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);
        if (args.Function == EngineKeyFunctions.UIClick || args.Function == EngineKeyFunctions.UIRightClick)
            _painting = null;
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);
        if (_painting is { } erase && Cell(args.RelativePosition) is { } cell)
            Set(cell.X, cell.Y, erase ? Color.Transparent : Brush);
    }

    protected override void MouseExited()
    {
        base.MouseExited();
        _painting = null;
    }

    private (int X, int Y)? Cell(Vector2 position)
    {
        var x = (int) (position.X / Width * Size);
        var y = (int) (position.Y / Height * Size);
        return x >= 0 && x < Size && y >= 0 && y < Size ? (x, y) : null;
    }

    public Color Get(int x, int y)
    {
        var i = (y * Size + x) * 4;
        return new Color(Pixels[i], Pixels[i + 1], Pixels[i + 2], Pixels[i + 3]);
    }

    private void Set(int x, int y, Color colour)
    {
        var i = (y * Size + x) * 4;
        Pixels[i] = (byte) (colour.R * 255);
        Pixels[i + 1] = (byte) (colour.G * 255);
        Pixels[i + 2] = (byte) (colour.B * 255);
        Pixels[i + 3] = (byte) (colour.A * 255);
    }

    /// <summary>Flood-fills the touching area of the clicked pixel's colour.</summary>
    private void Fill(int x, int y, Color colour)
    {
        var target = Get(x, y);
        if (target == colour)
            return;
        var stack = new Stack<(int X, int Y)>();
        stack.Push((x, y));
        while (stack.Count > 0)
        {
            var (cx, cy) = stack.Pop();
            if (cx < 0 || cx >= Size || cy < 0 || cy >= Size || Get(cx, cy) != target)
                continue;
            Set(cx, cy, colour);
            stack.Push((cx + 1, cy));
            stack.Push((cx - 1, cy));
            stack.Push((cx, cy + 1));
            stack.Push((cx, cy - 1));
        }
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var cell = new Vector2(PixelSize.X / (float) Size, PixelSize.Y / (float) Size);
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var box = UIBox2.FromDimensions(new Vector2(x * cell.X, y * cell.Y), cell);
                // A checkerboard shows through transparent pixels.
                handle.DrawRect(box, (x + y) % 2 == 0 ? Color.FromHex("#3A3A3A") : Color.FromHex("#2A2A2A"));
                var colour = Get(x, y);
                if (colour.A > 0)
                    handle.DrawRect(box, colour);
            }
        }
    }
}
