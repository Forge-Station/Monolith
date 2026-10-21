using Content.Shared._Shitmed.Humanoid.Events;
using Content.Shared._WF.ChatFont;
using Content.Shared.Speech;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.ChatFont;

/// <summary>Gives a character the chat font picked in their profile; chat, radio and telephone wraps read it.</summary>
public sealed partial class WFChatFontSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _proto = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SpeechComponent, ProfileLoadFinishedEvent>(OnProfileLoadFinished);
    }

    private void OnProfileLoadFinished(Entity<SpeechComponent> ent, ref ProfileLoadFinishedEvent args)
    {
        if (args.Profile != null)
            SetFont(ent, WFChatFontRules.ClientFont(args.Profile.ChatFont, _proto));
    }

    /// <summary>Draws the entity's speech in the client font, or in the speech verb's own when null.</summary>
    public void SetFont(EntityUid uid, string? font)
    {
        if (font == null)
        {
            RemComp<WFChatFontComponent>(uid);
            return;
        }

        var comp = EnsureComp<WFChatFontComponent>(uid);
        comp.Font = font;
        Dirty(uid, comp);
    }

    /// <summary>The font the entity's speech is drawn in, or null for the speech verb's own.</summary>
    public string? GetFont(EntityUid uid)
    {
        return CompOrNull<WFChatFontComponent>(uid)?.Font;
    }
}
