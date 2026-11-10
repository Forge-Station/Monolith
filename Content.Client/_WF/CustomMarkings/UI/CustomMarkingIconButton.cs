using System.Numerics;
using Content.Client._WF.Stylesheets;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._WF.CustomMarkings.UI;

/// <summary>A square button showing one of the module's icons, explained by its tooltip.</summary>
public sealed class CustomMarkingIconButton : ContainerButton
{
    public static readonly ResPath Icons = new("/Textures/_WF/CustomMarkings/editor.rsi");

    /// <summary>How many times its drawn size an icon is shown at. A whole number keeps the pixel art crisp.</summary>
    public const int IconScale = 2;

    private readonly TextureRect _icon;

    /// <param name="icon">A state of <see cref="Icons"/>.</param>
    public CustomMarkingIconButton(string icon, string tooltip)
    {
        AddStyleClass(StyleClassButton);
        AddStyleClass(StyleWolfgate.StyleClassCreatorIconButton);
        ToolTip = tooltip;

        var sprites = IoCManager.Resolve<IEntityManager>().System<SpriteSystem>();
        _icon = new TextureRect
        {
            Texture = sprites.Frame0(new SpriteSpecifier.Rsi(Icons, icon)),
            TextureScale = new Vector2(IconScale, IconScale),
            Stretch = TextureRect.StretchMode.KeepCentered,
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Center,
        };
        AddChild(_icon);
        Tint();
    }

    /// <summary>The icon is a child, so the button's state doesn't restyle it; it is retinted here instead.</summary>
    protected override void DrawModeChanged()
    {
        base.DrawModeChanged();
        Tint();
    }

    private void Tint()
    {
        // The base constructor gets here before the icon exists.
        if (_icon is null)
            return;

        var disabled = DrawMode == DrawModeEnum.Disabled;
        _icon.RemoveStyleClass(disabled ? StyleWolfgate.StyleClassCreatorIcon : StyleWolfgate.StyleClassCreatorIconDisabled);
        _icon.AddStyleClass(disabled ? StyleWolfgate.StyleClassCreatorIconDisabled : StyleWolfgate.StyleClassCreatorIcon);
    }
}
