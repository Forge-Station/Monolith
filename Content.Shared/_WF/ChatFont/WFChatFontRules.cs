using System.Linq;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.ChatFont;

/// <summary>Validation, storage and lookup of a profile's chat font.</summary>
public static class WFChatFontRules
{
    /// <summary>The font every profile starts with: the speech verb's own.</summary>
    public static readonly ProtoId<WFChatFontPrototype> Default = "WFChatFontDefault";

    /// <summary>The font if it exists, otherwise <see cref="Default"/>.</summary>
    public static ProtoId<WFChatFontPrototype> Validate(ProtoId<WFChatFontPrototype> font, IPrototypeManager proto)
    {
        return proto.HasIndex(font) ? font : Default;
    }

    /// <summary>The font as stored in the database.</summary>
    public static string ToStored(ProtoId<WFChatFontPrototype> font)
    {
        return font.Id;
    }

    /// <summary>The font read back from the database; an empty value is the default.</summary>
    public static ProtoId<WFChatFontPrototype> FromStored(string? stored)
    {
        return string.IsNullOrEmpty(stored) ? Default : new ProtoId<WFChatFontPrototype>(stored);
    }

    /// <summary>The client font a picked chat font draws speech in, or null for the speech verb's own.</summary>
    public static string? ClientFont(ProtoId<WFChatFontPrototype> font, IPrototypeManager proto)
    {
        if (font == Default || !proto.TryIndex(font, out var prototype))
            return null;

        return prototype.Font;
    }

    /// <summary>Every chat font in dropdown order.</summary>
    public static List<WFChatFontPrototype> Ordered(IPrototypeManager proto)
    {
        return proto.EnumeratePrototypes<WFChatFontPrototype>()
            .OrderBy(p => p.Order)
            .ThenBy(p => p.ID)
            .ToList();
    }
}
