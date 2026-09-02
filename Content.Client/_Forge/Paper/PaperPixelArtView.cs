using System.Numerics;
using Content.Shared._Forge.Paper;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Graphics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Client._Forge.Paper;

/// <summary>
/// Turns decoded paper drawings into a <see cref="TextureRect"/> that lives
/// next to <see cref="RichTextLabel"/>, not inside it.
/// </summary>
public static class PaperPixelArtView
{
    public static Control Create(PaperPixelArtCodec.PaperPixelArt art, float maxWidth)
    {
        using var image = new Image<Rgba32>(art.Width, art.Height);
        for (var y = 0; y < art.Height; y++)
        {
            for (var x = 0; x < art.Width; x++)
            {
                var color = art.Pixels[y * art.Width + x];
                image[x, y] = new Rgba32(color.RByte, color.GByte, color.BByte, color.AByte);
            }
        }

        var texture = Texture.LoadFromImage(image, "paper-px", new TextureLoadParameters
        {
            SampleParameters = TextureSampleParameters.Default,
            Srgb = true,
            Preload = false
        });

        var scale = (float)art.Scale;
        if (maxWidth > 0 && art.Width * scale > maxWidth)
            scale = maxWidth / art.Width;

        return new TextureRect
        {
            Texture = texture,
            Stretch = TextureRect.StretchMode.Keep,
            TextureScale = new Vector2(scale, scale),
            HorizontalAlignment = Control.HAlignment.Left,
            MouseFilter = Control.MouseFilterMode.Ignore
        };
    }
}
