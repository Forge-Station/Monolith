using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Input;

namespace Content.Client._WF.ExamineCard;

/// <summary>A small heart button that opens the character's adult description beside the card.</summary>
public sealed class ExamineCardHeartToggle : Control
{
    public static readonly Color Rose = Color.FromHex("#E0537E");

    private bool _pressed;
    private bool _hovered;

    public event Action<bool>? Toggled;

    public ExamineCardHeartToggle(bool pressed)
    {
        _pressed = pressed;
        MouseFilter = MouseFilterMode.Stop;
        DefaultCursorShape = CursorShape.Hand;
        SetSize = new Vector2(22, 16);
        Margin = new Thickness(0, 0, 4, 0);
        ToolTip = Loc.GetString("wf-examine-card-nsfw-tooltip");
    }

    public void Toggle()
    {
        _pressed = !_pressed;
        Toggled?.Invoke(_pressed);
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if (args.Function != EngineKeyFunctions.UIClick)
            return;

        Toggle();
        args.Handle();
    }

    protected override void MouseEntered()
    {
        base.MouseEntered();
        _hovered = true;
    }

    protected override void MouseExited()
    {
        base.MouseExited();
        _hovered = false;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var box = PixelSizeBox;
        var lit = _pressed || _hovered;
        handle.DrawRect(box, lit ? Rose.WithAlpha(0.25f) : ExamineCardControl.PaperShade.WithAlpha(ExamineCardControl.FillAlpha));
        handle.DrawRect(box, ExamineCardControl.Ink, false);

        // Two lobes and a point, scaled to the button.
        var centre = box.Center;
        var r = MathF.Min(box.Width, box.Height) * 0.2f;
        var colour = _pressed ? Rose : Rose.WithAlpha(0.75f);
        var left = centre + new Vector2(-r * 0.95f, -r * 0.45f);
        var right = centre + new Vector2(r * 0.95f, -r * 0.45f);
        Disc(handle, left, r, colour);
        Disc(handle, right, r, colour);
        var point = new[]
        {
            left + new Vector2(-r * 0.97f, r * 0.25f),
            right + new Vector2(r * 0.97f, r * 0.25f),
            centre + new Vector2(0, r * 2.1f),
        };
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, point, colour);
    }

    /// <summary>A filled circle as a triangle fan; the screen handle has no circle of its own.</summary>
    private static void Disc(DrawingHandleScreen handle, Vector2 centre, float radius, Color colour)
    {
        const int segments = 16;
        var fan = new Vector2[segments + 2];
        fan[0] = centre;
        for (var i = 0; i <= segments; i++)
        {
            var angle = MathF.Tau * i / segments;
            fan[i + 1] = centre + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
        }
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, fan, colour);
    }
}
