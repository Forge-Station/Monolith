using System.Numerics;
using Content.Shared._Shitmed.Targeting;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._WF.Wolfmed.Medical;

/// <summary>
/// The health analyzer's targeting doll as a standalone control: the same 32 px status art drawn at 3x in a
/// 96x96 frame, with the analyzer's own per-limb button rectangles. Clicking a limb raises
/// <see cref="OnPartSelected"/>.
/// </summary>
public sealed class WolfmedBodyDoll : PanelContainer
{
    /// <summary>Limb, button size and offset, in the analyzer's 96x96 frame.</summary>
    private static readonly (TargetBodyPart Part, string Style, Vector2 Size, Vector2 Offset)[] Layout =
    {
        (TargetBodyPart.Head, "TargetDollButtonHead", new Vector2(27, 27), new Vector2(33, 6)),
        (TargetBodyPart.Torso, "TargetDollButtonChest", new Vector2(33, 27), new Vector2(30, 27)),
        (TargetBodyPart.Groin, "TargetDollButtonGroin", new Vector2(33, 21), new Vector2(30, 48)),
        (TargetBodyPart.RightArm, "TargetDollButtonRightArm", new Vector2(21, 24), new Vector2(18, 27)),
        (TargetBodyPart.RightHand, "TargetDollButtonRightHand", new Vector2(18, 18), new Vector2(18, 45)),
        (TargetBodyPart.LeftArm, "TargetDollButtonLeftArm", new Vector2(21, 24), new Vector2(54, 27)),
        (TargetBodyPart.LeftHand, "TargetDollButtonLeftHand", new Vector2(18, 18), new Vector2(57, 45)),
        (TargetBodyPart.RightLeg, "TargetDollButtonRightLeg", new Vector2(18, 24), new Vector2(30, 57)),
        (TargetBodyPart.RightFoot, "TargetDollButtonRightFoot", new Vector2(24, 15), new Vector2(24, 75)),
        (TargetBodyPart.LeftLeg, "TargetDollButtonLeftLeg", new Vector2(18, 24), new Vector2(45, 57)),
        (TargetBodyPart.LeftFoot, "TargetDollButtonLeftFoot", new Vector2(24, 15), new Vector2(45, 75)),
    };

    private readonly IEntityManager _entities;
    private readonly SpriteSystem _sprites;
    private readonly SpriteView _view;
    private readonly Dictionary<TargetBodyPart, TextureRect> _overlays = new();
    private EntityUid _dollEntity;

    public event Action<TargetBodyPart>? OnPartSelected;

    public WolfmedBodyDoll()
    {
        _entities = IoCManager.Resolve<IEntityManager>();
        _sprites = _entities.System<SpriteSystem>();

        SetSize = new Vector2(96, 96);
        HorizontalAlignment = HAlignment.Center;
        VerticalAlignment = VAlignment.Center;

        _view = new SpriteView
        {
            OverrideDirection = Direction.South,
            SetSize = new Vector2(96, 96),
        };
        AddChild(_view);

        var buttons = new PanelContainer { SetSize = new Vector2(96, 96) };
        AddChild(buttons);

        foreach (var (part, style, size, offset) in Layout)
        {
            var name = part.ToString().ToLowerInvariant();
            var overlay = new TextureRect
            {
                Texture = _sprites.Frame0(new SpriteSpecifier.Rsi(
                    new ResPath($"/Textures/_Shitmed/Interface/Targeting/Status/{name}.rsi"), $"{name}_0")),
                Stretch = TextureRect.StretchMode.Scale,
                SetSize = new Vector2(96, 96),
                Visible = false,
                MouseFilter = MouseFilterMode.Ignore,
                Modulate = Color.FromHex("#ffcf6b"),
            };
            AddChild(overlay);
            _overlays[part] = overlay;

            var button = new TextureButton
            {
                SetSize = size,
                Margin = new Thickness(offset.X, offset.Y, 0, 0),
                HorizontalAlignment = HAlignment.Left,
                VerticalAlignment = VAlignment.Top,
                MouseFilter = MouseFilterMode.Stop,
            };
            button.AddStyleClass(style);
            var captured = part;
            button.OnPressed += _ => OnPartSelected?.Invoke(captured);
            buttons.AddChild(button);
        }
    }

    /// <summary>Redraws the doll from one scan's per-limb integrity.</summary>
    public void SetBody(Dictionary<TargetBodyPart, TargetIntegrity>? body)
    {
        if (body == null)
        {
            _view.Visible = false;
            return;
        }

        _view.Visible = true;

        // One preview entity for the life of the control: a scan lands about once a second, and spawning a
        // fresh doll for each one churned entities for nothing.
        if (_entities.Deleted(_dollEntity))
        {
            _dollEntity = _entities.Spawn("AlertSpriteView");
            _view.SetEntity(_dollEntity);
        }

        if (!_entities.TryGetComponent(_dollEntity, out SpriteComponent? sprite))
            return;

        var layer = 0;
        foreach (var (part, integrity) in body)
        {
            var name = part.ToString().ToLowerInvariant();
            var rsi = new SpriteSpecifier.Rsi(
                new ResPath($"/Textures/_Shitmed/Interface/Targeting/Status/{name}.rsi"), $"{name}_{(int) integrity}");

            if (!sprite.TryGetLayer(layer, out _))
                sprite.AddLayer(_sprites.Frame0(rsi));
            else
                sprite.LayerSetTexture(layer, _sprites.Frame0(rsi));

            sprite.LayerSetScale(layer, new Vector2(3f, 3f));
            sprite.LayerSetVisible(layer, true);
            layer++;
        }

        // A shorter scan than the last one leaves layers behind; hide them rather than redraw stale limbs.
        while (sprite.TryGetLayer(layer, out _))
        {
            sprite.LayerSetVisible(layer, false);
            layer++;
        }
    }

    /// <summary>Highlights one limb, or none.</summary>
    public void SetSelected(TargetBodyPart? part)
    {
        foreach (var (key, overlay) in _overlays)
            overlay.Visible = key == part;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_entities.Deleted(_dollEntity))
            _entities.QueueDeleteEntity(_dollEntity);

        base.Dispose(disposing);
    }
}
