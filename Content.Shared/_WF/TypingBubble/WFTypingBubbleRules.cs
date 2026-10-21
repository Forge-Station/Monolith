using System.Linq;
using Content.Shared.Chat.TypingIndicator;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.TypingBubble;

/// <summary>Which typing bubbles a character can pick, and how a profile's pick is validated and stored.</summary>
public static class WFTypingBubbleRules
{
    /// <summary>Bubbles not offered: no art, or another bubble's art with a species' offset.</summary>
    private static readonly HashSet<string> Hidden = new() { "empty", "thaven" };

    /// <summary>Whether a character can pick this bubble.</summary>
    public static bool IsOffered(ProtoId<TypingIndicatorPrototype> indicator, IPrototypeManager proto)
    {
        return !Hidden.Contains(indicator.Id) && proto.HasIndex(indicator);
    }

    /// <summary>The bubble if it can be picked, otherwise null for the species' own.</summary>
    public static ProtoId<TypingIndicatorPrototype>? Validate(ProtoId<TypingIndicatorPrototype>? indicator, IPrototypeManager proto)
    {
        if (indicator is { } id && IsOffered(id, proto))
            return id;

        return null;
    }

    /// <summary>The bubble as stored in the database.</summary>
    public static string? ToStored(ProtoId<TypingIndicatorPrototype>? indicator)
    {
        return indicator?.Id;
    }

    /// <summary>The bubble read back from the database; an empty value is the species' own.</summary>
    public static ProtoId<TypingIndicatorPrototype>? FromStored(string? stored)
    {
        if (string.IsNullOrEmpty(stored))
            return null;

        return new ProtoId<TypingIndicatorPrototype>(stored);
    }

    /// <summary>Every bubble a character can pick, the plain one first.</summary>
    public static List<TypingIndicatorPrototype> Offered(IPrototypeManager proto)
    {
        return proto.EnumeratePrototypes<TypingIndicatorPrototype>()
            .Where(p => !Hidden.Contains(p.ID))
            .OrderBy(p => p.ID != SharedTypingIndicatorSystem.InitialIndicatorId.Id)
            .ThenBy(p => p.ID, StringComparer.Ordinal)
            .ToList();
    }
}
