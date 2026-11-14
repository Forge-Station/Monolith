using Content.Shared._WF.ChatFont;
using Content.Client._WF.ChatFont;
using Robust.Shared.Utility;

namespace Content.Client.Lobby.UI;

/// <summary>The Chat font dropdown in the Body card, with a line of sample speech in the picked font.</summary>
public sealed partial class HumanoidProfileEditor
{
    private List<WFChatFontPrototype> _wfChatFonts = new();

    private void InitializeChatFont()
    {
        _wfChatFonts = WFChatFontRules.Ordered(_prototypeManager);
        for (var i = 0; i < _wfChatFonts.Count; i++)
        {
            ChatFontButton.AddItem(Loc.GetString(_wfChatFonts[i].Name), i);
        }

        ChatFontButton.OnItemSelected += args =>
        {
            ChatFontButton.SelectId(args.Id);
            SetChatFont(_wfChatFonts[args.Id]);
        };
    }

    /// <summary>Shows the profile's chat font, the default when it names none that exists.</summary>
    private void UpdateChatFont()
    {
        if (Profile == null)
            return;

        var index = _wfChatFonts.FindIndex(f => f.ID == Profile.ChatFont.Id);
        if (index < 0)
            index = _wfChatFonts.FindIndex(f => f.ID == WFChatFontRules.Default.Id);

        if (index < 0)
            return;

        ChatFontButton.SelectId(index);
        UpdateChatFontPreview(_wfChatFonts[index]);
    }

    private void SetChatFont(WFChatFontPrototype font)
    {
        UpdateChatFontPreview(font);
        if (Profile == null || Profile.ChatFont == font.ID)
            return;

        Profile = Profile.WithChatFont(font.ID);
        SetDirty();
    }

    private void UpdateChatFontPreview(WFChatFontPrototype font)
    {
        var sample = FormattedMessage.EscapeText(Loc.GetString("wf-chat-font-preview"));
        // The default tag set leaves out font, so the sample would draw in the label's own.
        ChatFontPreview.SetMessage(FormattedMessage.FromMarkupOrThrow($"[font=\"{font.Font}\"]{sample}[/font]"),
            WFChatFontTags.Allowed);
    }
}
