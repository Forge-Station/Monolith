using System.Numerics;
using Content.Client.Stylesheets;
using Content.Shared._WF.Headshot;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client._WF.Headshot;

/// <summary>A headshot thumbnail that smoothly grows to full size while hovered.</summary>
public sealed class HeadshotPortrait : TextureRect
{
    private const float ZoomSeconds = 0.15f;

    private PanelContainer? _zoom;
    private TextureRect? _zoomImage;
    private bool _hovered;

    /// <summary>0 at thumbnail size, 1 at full size.</summary>
    private float _progress;

    public HeadshotPortrait()
    {
        SetSize = new Vector2(HeadshotRules.ThumbnailSize, HeadshotRules.ThumbnailSize);
        Stretch = StretchMode.KeepAspectCentered;
        MouseFilter = MouseFilterMode.Pass;
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

    protected override void ExitedTree()
    {
        base.ExitedTree();
        _hovered = false;
        CloseZoom();
    }

    // Closing the examine tooltip only hides it, which also stops our frame updates.
    protected override void VisibilityChanged(bool newVisible)
    {
        base.VisibilityChanged(newVisible);
        if (newVisible)
            return;

        _hovered = false;
        CloseZoom();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        // The texture may be swapped or disposed under us, so never draw a stale one.
        if (Texture is not { } texture || Root is not { } root)
        {
            CloseZoom();
            return;
        }

        var step = args.DeltaSeconds / ZoomSeconds;
        _progress = Math.Clamp(_progress + (_hovered ? step : -step), 0f, 1f);
        if (_progress <= 0f)
        {
            CloseZoom();
            return;
        }

        if (_zoom == null)
        {
            _zoomImage = new TextureRect { Stretch = StretchMode.Scale };
            _zoom = new PanelContainer { MouseFilter = MouseFilterMode.Ignore, Children = { _zoomImage } };
            _zoom.SetOnlyStyleClass(StyleNano.StyleClassTooltipPanel);
            root.PopupRoot.AddChild(_zoom);
        }

        var eased = _progress * _progress * (3f - 2f * _progress);
        _zoomImage!.Texture = texture;
        _zoomImage.SetSize = Vector2.Lerp(Fit(texture.Size, Size), Fit(texture.Size, new Vector2(HeadshotRules.ImageSize)), eased);
        _zoom.Modulate = Color.White.WithAlpha(MathF.Min(1f, eased * 4f));

        // Grow from the thumbnail's centre, kept on screen.
        _zoom.Measure(Vector2Helpers.Infinity);
        var size = _zoom.DesiredSize;
        var position = GlobalPosition + (Size - size) / 2;
        position = Vector2.Clamp(position, Vector2.Zero, Vector2.Max(Vector2.Zero, root.Size - size));
        LayoutContainer.SetPosition(_zoom, position);
    }

    private void CloseZoom()
    {
        _progress = 0f;
        _zoom?.Orphan();
        _zoom = null;
        _zoomImage = null;
    }

    /// <summary>The size of an image scaled to fit a box, keeping its aspect ratio.</summary>
    private static Vector2 Fit(Vector2 image, Vector2 box)
    {
        return image * MathF.Min(box.X / image.X, box.Y / image.Y);
    }
}
