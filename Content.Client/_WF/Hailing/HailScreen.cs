using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client._WF.Hailing;

/// <summary>
/// The hail window's viewscreen: the person on the other end from the waist up behind scanlines, or
/// static with a caption while there is nobody to show.
/// </summary>
public sealed class HailScreen : Control
{
    /// <summary>Big enough that the screen only has room for the head and chest.</summary>
    private const float SpriteScale = 8f;

    private static readonly Color BackColor = Color.FromHex("#06140f");
    private static readonly Color BorderColor = Color.FromHex("#1f5c3a");
    private static readonly Color TextColor = Color.FromHex("#00ff2a");
    private static readonly Color FeedTint = Color.FromHex("#d8ffe6");

    private readonly SpriteView _view;
    private readonly Noise _noise;
    private readonly Label _shipLabel;
    private readonly Label _nameLabel;
    private readonly Label _captionLabel;

    private bool _feed;
    private string _noVisual = string.Empty;

    public HailScreen()
    {
        RectClipContent = true;
        HorizontalExpand = true;

        AddChild(new PanelContainer
        {
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = BackColor,
                BorderColor = BorderColor,
                BorderThickness = new Thickness(2),
            },
        });

        // Taller than the screen on purpose, so the legs run off the bottom edge.
        AddChild(_view = new SpriteView
        {
            OverrideDirection = Direction.South,
            Scale = new Vector2(SpriteScale, SpriteScale),
            Stretch = SpriteView.StretchMode.None,
            SetSize = new Vector2(32f * SpriteScale, 32f * SpriteScale),
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Top,
            Margin = new Thickness(0, 4, 0, 0),
            Modulate = FeedTint,
            Visible = false,
        });

        AddChild(_noise = new Noise { MouseFilter = MouseFilterMode.Ignore });

        AddChild(_shipLabel = new Label
        {
            HorizontalAlignment = HAlignment.Left,
            VerticalAlignment = VAlignment.Top,
            Margin = new Thickness(8, 5),
            FontColorOverride = TextColor,
            ClipText = true,
        });

        AddChild(_nameLabel = new Label
        {
            HorizontalAlignment = HAlignment.Left,
            VerticalAlignment = VAlignment.Bottom,
            Margin = new Thickness(8, 5),
            FontColorOverride = TextColor,
            ClipText = true,
        });

        AddChild(_captionLabel = new Label
        {
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Center,
            StyleClasses = { "LabelHeading" },
            FontColorOverride = TextColor,
        });
    }

    /// <summary>
    /// Puts a person on the screen. They may not have reached this client yet, or ever, so the caption
    /// stands in until they are drawn.
    /// </summary>
    public void ShowFeed(NetEntity? person, string ship, string name, string noVisual)
    {
        _feed = true;
        _noise.Glow = false;
        _noVisual = noVisual;
        _shipLabel.Text = ship;
        _nameLabel.Text = name;

        if (person is { } target)
            _view.SetEntity(target);
        else
            _view.SetEntity(null);

        _view.Visible = true;
    }

    /// <summary>
    /// Shows the other end as God: no picture and no static, only a slow glow behind the name.
    /// </summary>
    public void ShowDivine(string name)
    {
        _feed = false;
        _view.Visible = false;
        _view.SetEntity(null);
        _shipLabel.Text = string.Empty;
        _nameLabel.Text = string.Empty;
        _captionLabel.Text = name;
        _noise.Static = false;
        _noise.Glow = true;
    }

    /// <summary>
    /// Blanks the screen to static under a caption.
    /// </summary>
    public void ShowStatic(string caption, string ship = "")
    {
        _feed = false;
        _view.Visible = false;
        _view.SetEntity(null);
        _shipLabel.Text = ship;
        _nameLabel.Text = string.Empty;
        _captionLabel.Text = caption;
        _noise.Static = true;
        _noise.Glow = false;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        _noise.Time += args.DeltaSeconds;

        if (!_feed)
            return;

        var live = _view.Entity != null;
        _noise.Static = !live;
        _captionLabel.Text = live ? string.Empty : _noVisual;
    }

    // The sprite view asks for more height than the screen has; the screen's size is its own.
    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        base.MeasureOverride(availableSize);
        return Vector2.Zero;
    }

    /// <summary>Scanlines over the picture, and snow when there is no picture.</summary>
    private sealed class Noise : Control
    {
        private const int Flecks = 140;

        private static readonly Color ScanColor = Color.Black.WithAlpha(0.28f);
        private static readonly Color BandColor = Color.White.WithAlpha(0.04f);
        private static readonly Color GlowColor = Color.FromHex("#ffe9a8");

        private readonly Random _random = new();

        public bool Static = true;
        public bool Glow;
        public float Time;

        protected override void Draw(DrawingHandleScreen handle)
        {
            var size = PixelSize;
            var line = MathF.Max(1f, MathF.Round(UIScale));

            if (Glow)
            {
                // Brightest across the middle and breathing slowly.
                var breath = 0.5f + 0.5f * MathF.Sin(Time * 1.4f);
                handle.DrawRect(new UIBox2(0f, 0f, size.X, size.Y), GlowColor.WithAlpha(0.10f + 0.06f * breath));
                handle.DrawRect(new UIBox2(0f, size.Y * 0.25f, size.X, size.Y * 0.75f), GlowColor.WithAlpha(0.10f + 0.08f * breath));
                handle.DrawRect(new UIBox2(0f, size.Y * 0.4f, size.X, size.Y * 0.6f), GlowColor.WithAlpha(0.12f + 0.10f * breath));
            }

            if (Static)
            {
                for (var i = 0; i < Flecks; i++)
                {
                    var x = _random.NextSingle() * size.X;
                    var y = _random.NextSingle() * size.Y;
                    var width = (4f + _random.NextSingle() * 22f) * UIScale;
                    var shade = Color.White.WithAlpha(0.04f + _random.NextSingle() * 0.22f);
                    handle.DrawRect(new UIBox2(x, y, MathF.Min(x + width, size.X), y + line * 2f), shade);
                }
            }

            for (var y = 0f; y < size.Y; y += line * 3f)
            {
                handle.DrawRect(new UIBox2(0f, y, size.X, y + line), ScanColor);
            }

            // A slow bright bar rolling down the tube.
            var height = 30f * UIScale;
            var top = Time * 45f * UIScale % (size.Y + height) - height;
            handle.DrawRect(new UIBox2(0f, MathF.Max(top, 0f), size.X, MathF.Min(top + height, size.Y)), BandColor);
        }
    }
}
