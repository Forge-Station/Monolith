using System.Numerics;
using Robust.Client.UserInterface;

namespace Content.Client._WF.Cockpit;

/// <summary>Clips full-screen speech bubbles to the cockpit's world view while keeping their screen coordinates.</summary>
public sealed class WFCockpitSpeechClip : Control
{
    private Vector2 _screen;
    private Vector2 _origin;

    public WFCockpitSpeechClip()
    {
        RectClipContent = true;
        MouseFilter = MouseFilterMode.Ignore;
    }

    /// <summary>Children are measured at the size of the whole screen, which this control is given as its available size.</summary>
    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        if (_screen != availableSize)
        {
            _screen = availableSize;
            InvalidateArrange();
        }
        foreach (var child in Children)
            child.Measure(availableSize);
        return Vector2.Zero;
    }

    protected override void ArrangeCore(UIBox2 finalRect)
    {
        _origin = finalRect.TopLeft;
        base.ArrangeCore(finalRect);
    }

    /// <summary>Places children over the whole screen, so this control's own position cancels out.</summary>
    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        foreach (var child in Children)
            child.Arrange(UIBox2.FromDimensions(-_origin, _screen));
        return finalSize;
    }
}
