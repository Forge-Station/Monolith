using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;

namespace Content.Client._WF.ExamineCard;

/// <summary>A tilted ink stamp drawn over the card, like a border inspector's.</summary>
public sealed class ExamineCardStampControl : Control
{
    private static readonly Angle Tilt = Angle.FromDegrees(-12);

    private readonly Font _font;
    private readonly string _text;
    private readonly Color _ink;

    public ExamineCardStampControl(Font font, string text, Color ink)
    {
        _font = font;
        _text = text;
        _ink = ink;
        MouseFilter = MouseFilterMode.Ignore;
        SetSize = new Vector2(124, 44);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var scale = UIScale;
        var text = handle.GetDimensions(_font, _text, scale);
        var half = new Vector2(text.X / 2 + 7 * scale, text.Y / 2 + 4 * scale);

        var old = handle.GetTransform();
        handle.SetTransform(Matrix3x2.Multiply(Matrix3Helpers.CreateTransform((Vector2) PixelSize / 2f, Tilt), old));

        // A doubled border reads as a rubber stamp rather than a box.
        var outer = new UIBox2(-half, half);
        var inner = new UIBox2(-half + new Vector2(3 * scale), half - new Vector2(3 * scale));
        for (var i = 0; i < 2; i++)
        {
            var offset = new Vector2(i);
            handle.DrawRect(new UIBox2(outer.TopLeft + offset, outer.BottomRight - offset), _ink, false);
        }
        handle.DrawRect(inner, _ink.WithAlpha(_ink.A * 0.7f), false);
        handle.DrawString(_font, -text / 2, _text, scale, _ink);

        handle.SetTransform(old);
    }
}
