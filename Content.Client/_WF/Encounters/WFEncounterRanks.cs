using System.Numerics;
using Content.Shared._WF.Encounters;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Utility;

namespace Content.Client._WF.Encounters;

/// <summary>Draws an encounter's threat rank: one to three chevrons, then one to three stars, grey through amber to red.</summary>
public sealed class WFEncounterRanks
{
    private static readonly ResPath Glyphs = new("/Textures/_WF/Encounters/ranks.rsi");

    private readonly Texture _chevron;
    private readonly Texture _star;

    public WFEncounterRanks(SpriteSystem sprites)
    {
        _chevron = sprites.Frame0(new SpriteSpecifier.Rsi(Glyphs, "chevron"));
        _star = sprites.Frame0(new SpriteSpecifier.Rsi(Glyphs, "star"));
    }

    /// <summary>How many glyphs a rank draws; none for rank 0 or an unranked encounter.</summary>
    public static int Count(int? threat)
    {
        return threat switch
        {
            null or <= 0 => 0,
            <= 3 => threat.Value,
            _ => Math.Min(threat.Value - 3, 3),
        };
    }

    public static Color Tint(int threat)
    {
        return threat switch
        {
            <= 2 => Color.FromHex("#c8ccd4"),
            3 => Color.FromHex("#ffc94a"),
            4 => Color.FromHex("#ff8a3d"),
            _ => Color.FromHex("#ff4b4b"),
        };
    }

    /// <summary>The width a rank takes at a glyph size.</summary>
    public static float Width(int? threat, float size)
    {
        var count = Count(threat);
        return count == 0 ? 0f : count * size * 0.8f + size * 0.2f;
    }

    /// <summary>Draws a rank in a row from its top-left corner.</summary>
    public void Draw(DrawingHandleScreen handle, Vector2 topLeft, int? threat, float size)
    {
        var count = Count(threat);
        if (count == 0)
            return;

        var texture = threat > 3 ? _star : _chevron;
        var color = Tint(threat!.Value);
        for (var i = 0; i < count; i++)
        {
            handle.DrawTextureRect(texture, UIBox2.FromDimensions(topLeft + new Vector2(i * size * 0.8f, 0f), new Vector2(size)), color);
        }
    }
}
