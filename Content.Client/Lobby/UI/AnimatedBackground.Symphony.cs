using System.Numerics;
using System.Text.RegularExpressions;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Client.Lobby.UI;

/// <summary>
/// A lobby background that moves: a sprite sheet the SSymphony panel made from a GIF, played one cell at a time and
/// cropped to cover the screen the way the still background is. The sheet's frames, columns and frame time are in its
/// file name, <c>hash.anim-24x6-80.png</c>, since the client sandbox cannot read a GIF's frames itself.
/// </summary>
public sealed class SymphonyAnimatedBackground : Control
{
    private static readonly Regex Tag = new(@"\.anim-(\d{1,3})x(\d{1,3})-(\d{2,4})\.png$");

    private Texture? _sheet;
    private int _frames;
    private int _cols;
    private float _interval;
    private float _time;

    public SymphonyAnimatedBackground()
    {
        MouseFilter = MouseFilterMode.Ignore;
        RectClipContent = true;
        Visible = false;
    }

    /// <summary>Whether a background path is one of these sheets.</summary>
    public static bool IsAnimated(string? path) => path != null && Tag.IsMatch(path);

    /// <summary>Play this sheet, or stop and hide with null.</summary>
    public void Play(Texture? sheet, string? path)
    {
        var m = path == null ? null : Tag.Match(path);
        if (sheet == null || m == null || !m.Success)
        {
            _sheet = null;
            Visible = false;
            return;
        }
        _sheet = sheet;
        _frames = Math.Max(1, int.Parse(m.Groups[1].Value));
        _cols = Math.Max(1, int.Parse(m.Groups[2].Value));
        _interval = Math.Max(0.02f, int.Parse(m.Groups[3].Value) / 1000f);
        _time = 0;
        Visible = true;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (_sheet != null && VisibleInTree)
            _time = (_time + args.DeltaSeconds) % (_frames * _interval);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        if (_sheet == null)
            return;
        var rows = (_frames + _cols - 1) / _cols;
        var cellW = _sheet.Width / (float) _cols;
        var cellH = _sheet.Height / (float) rows;
        var frame = Math.Min(_frames - 1, (int) (_time / _interval));
        var region = UIBox2.FromDimensions(new Vector2(frame % _cols * cellW, frame / _cols * cellH), new Vector2(cellW, cellH));
        // Cover the whole control, as KeepAspectCovered does for the still background: scale up, centre, crop.
        var scale = Math.Max(PixelSize.X / cellW, PixelSize.Y / cellH);
        var size = new Vector2(cellW, cellH) * scale;
        var at = (PixelSize - size) / 2;
        handle.DrawTextureRectRegion(_sheet, UIBox2.FromDimensions(at, size), region);
    }
}
