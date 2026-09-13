using System.Numerics;
using Content.Client._WF.Humanoid;
using Content.Client._WF.Stylesheets;
using Content.Shared._WF.Genitals;
using Content.Shared._WF.Genitals.Prototypes;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._WF.Genitals.UI;

/// <summary>Creator tile for one genital shape: its front art, cropped to its body region and tinted, over its name.</summary>
/// <remarks>Styled like WolfgateMarkingTile and lit while the shape is selected.</remarks>
public sealed class WolfgateGenitalShapeTile : ContainerButton
{
    public const float TileWidth = 96f;

    /// <summary>Logical step drawn on the tile (penis size 3, cup C); shapes without it resolve to their nearest step.</summary>
    private const int PreviewStep = 3;

    private readonly RegionIcon _icon;

    /// <summary>The shape this tile selects.</summary>
    public ProtoId<GenitalShapePrototype> Shape { get; }

    /// <param name="shape">The shape the tile selects and names.</param>
    /// <param name="art">The shape whose art is drawn: the shape itself, or the one its Preview names.</param>
    /// <param name="direction">Direction the art faces.</param>
    public WolfgateGenitalShapeTile(GenitalShapePrototype shape, GenitalShapePrototype art, Direction direction)
    {
        Shape = shape.ID;
        AddStyleClass(WolfgateMarkingTile.StyleClassTile);
        ToggleMode = true;
        MinSize = new Vector2(TileWidth - 4, 88);

        var name = Loc.GetString(shape.Name);
        ToolTip = name;

        var sprites = new List<SpriteSpecifier>();
        if (GenitalSpriteResolver.TryGetState(art, PreviewStep, false, false, out var state))
            sprites.Add(new SpriteSpecifier.Rsi(art.Sprite, state));

        _icon = new RegionIcon(sprites, art.Region);
        AddChild(new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 2,
            Margin = new Thickness(4, 4, 4, 2),
            Children =
            {
                _icon,
                new Label
                {
                    Text = name,
                    ClipText = true,
                    Align = Label.AlignMode.Center,
                    HorizontalAlignment = HAlignment.Stretch,
                    StyleClasses = { StyleWolfgate.StyleClassCreatorFieldLabel },
                },
            },
        });

        SetDirection(direction);
    }

    /// <summary>Colour the art is drawn in: the organ's current colour.</summary>
    public Color Tint
    {
        set => _icon.Tint = value;
    }

    /// <summary>Faces the art like the preview doll, or the nearest direction the art is drawn in.</summary>
    public void SetDirection(Direction direction)
    {
        _icon.SetDirection(direction);
    }

    /// <summary>The chest or groin part of a sprite frame, scaled up, so small anatomy art stays legible.</summary>
    private sealed class RegionIcon : Control
    {
        private const float Scale = 4f;

        /// <summary>Frame size the crops are measured against.</summary>
        private const float FrameSize = 32f;

        /// <summary>Crops of a 32 px frame, in pixels from the top left, centred on the body's middle column.</summary>
        private static readonly UIBox2 ChestCrop = new(8, 6, 24, 22);

        private static readonly UIBox2 GroinCrop = new(8, 14, 24, 30);

        private readonly List<SpriteSpecifier> _sprites;
        private readonly UIBox2 _crop;
        private Texture? _texture;
        private Color _tint = Color.White;

        public RegionIcon(List<SpriteSpecifier> sprites, GenitalRegion region)
        {
            _sprites = sprites;
            _crop = region == GenitalRegion.Chest ? ChestCrop : GroinCrop;
            MinSize = new Vector2(_crop.Width * Scale, _crop.Height * Scale);
            HorizontalAlignment = HAlignment.Center;
        }

        public Color Tint
        {
            set => _tint = value;
        }

        public void SetDirection(Direction direction)
        {
            _texture = _sprites.Count == 0
                ? null
                : WolfgateMarkingIcon.FrameFor(_sprites[0], WolfgateMarkingIcon.VisibleDirection(_sprites, direction));
        }

        protected override void Draw(DrawingHandleScreen handle)
        {
            if (_texture == null)
                return;

            // The crop follows the frame size, in case an RSI is not 32 px.
            var sx = _texture.Width / FrameSize;
            var sy = _texture.Height / FrameSize;
            var region = new UIBox2(_crop.Left * sx, _crop.Top * sy, _crop.Right * sx, _crop.Bottom * sy);
            handle.DrawTextureRectRegion(_texture, PixelSizeBox, region, _tint);
        }
    }
}
