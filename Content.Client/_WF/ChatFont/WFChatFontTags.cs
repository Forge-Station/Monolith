using Content.Shared._WF.ChatFont;
using Robust.Client.UserInterface.RichText;

namespace Content.Client._WF.ChatFont;

/// <summary>The rich-text tags that let a label draw a picked chat font.</summary>
public static class WFChatFontTags
{
    /// <summary>A label's default tags (the engine's RichTextEntry.DefaultTags, which is internal).</summary>
    private static readonly Type[] Default =
    [
        typeof(BoldItalicTag), typeof(BoldTag), typeof(BulletTag), typeof(ColorTag), typeof(HeadingTag), typeof(ItalicTag),
    ];

    /// <summary>A label's default tags plus font.</summary>
    public static readonly Type[] Allowed = [..Default, typeof(FontTag)];

    /// <summary>Tags for a speech bubble's text: with font when the speaker picked a chat font, otherwise the defaults.</summary>
    public static Type[] ForSpeaker(IEntityManager entMan, EntityUid speaker)
    {
        return entMan.HasComponent<WFChatFontComponent>(speaker) ? Allowed : Default;
    }
}
