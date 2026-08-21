using Content.Server._EinsteinEngines.Language;
using Content.Shared._Forge.Features.Components;
using Content.Shared.Examine;
using Robust.Shared.Utility;

namespace Content.Server._Forge.Features;

public sealed class InscriptionSystem : EntitySystem
{
    [Dependency] private readonly LanguageSystem _language = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<InscriptionComponent, ExaminedEvent>(OnExamined);
    }

    private void OnExamined(EntityUid uid, InscriptionComponent component, ExaminedEvent args)
    {
        if (string.IsNullOrWhiteSpace(component.Text))
        {
            args.PushMarkup(Loc.GetString("remnant-inscription-empty"));
            return;
        }

        var text = Loc.TryGetString(component.Text, out var localized) ? localized : component.Text;
        var escaped = FormattedMessage.EscapeText(text);
        var language = _language.GetLanguagePrototype(component.Language);
        if (language == null || _language.CanUnderstand(args.Examiner, component.Language))
        {
            args.PushMarkup(Loc.GetString("remnant-inscription-known", ("text", escaped)));
            return;
        }

        var obfuscated = FormattedMessage.EscapeText(_language.ObfuscateSpeech(text, language));
        args.PushMarkup(Loc.GetString("remnant-inscription-unknown", ("text", obfuscated)));
    }
}
