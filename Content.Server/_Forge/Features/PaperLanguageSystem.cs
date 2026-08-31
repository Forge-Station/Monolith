using Content.Server._EinsteinEngines.Language;
using Content.Shared._EinsteinEngines.Language;
using Content.Shared._EinsteinEngines.Language.Systems;
using Content.Shared._Forge.Features.Components;
using Content.Shared.Examine;
using Content.Shared.Ghost;
using Content.Shared._Forge.Paper;
using Content.Shared.Paper;
using Robust.Shared.Prototypes;

namespace Content.Server._Forge.Features;

public sealed partial class PaperLanguageSystem : EntitySystem
{
    [Dependency] private readonly LanguageSystem _language = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PaperLanguageComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<PaperLanguageComponent, PaperComponent.PaperInputTextMessage>(OnInputText);
    }

    private void OnExamined(EntityUid uid, PaperLanguageComponent component, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        var hasWriting = TryComp<PaperComponent>(uid, out var paper) && !string.IsNullOrWhiteSpace(paper.Content);
        if (!hasWriting)
            return;

        var names = new List<string>();
        var seen = new HashSet<string>();
        foreach (var segment in GetEffectiveSegments(component, paper?.Content))
        {
            if (!seen.Add(segment.Language.Id))
                continue;

            var proto = _language.GetLanguagePrototype(segment.Language);
            names.Add(proto?.Name ?? segment.Language.Id);
        }

        if (names.Count == 0)
            return;

        if (names.Count == 1)
        {
            args.PushMarkup(Loc.GetString("paper-language-examine", ("language", names[0])));
            return;
        }

        args.PushMarkup(Loc.GetString("paper-language-examine-multiple",
            ("languages", string.Join(", ", names))));
    }

    private void OnInputText(Entity<PaperLanguageComponent> paper, ref PaperComponent.PaperInputTextMessage args)
    {
        var submitted = PaperPixelArtCodec.Compress(args.Text);
        if (TryComp<PaperComponent>(paper.Owner, out var paperComp) &&
            submitted.Length > paperComp.ContentSize)
            return;

        ProtoId<LanguagePrototype> language = args.Language;
        if (string.IsNullOrEmpty(args.Language) ||
            language == SharedLanguageSystem.UniversalPrototype ||
            _language.GetLanguagePrototype(language) == null)
        {
            language = paper.Comp.Language;
        }
        else
        {
            var canWrite = HasComp<GhostComponent>(args.Actor)
                           || TryComp<UniversalLanguageSpeakerComponent>(args.Actor, out var uni) && uni.Enabled
                           || _language.CanSpeak(args.Actor, language);
            if (!canWrite)
                language = paper.Comp.Language;
        }

        paper.Comp.Segments.Clear();
        if (!string.IsNullOrWhiteSpace(submitted))
        {
            paper.Comp.Segments.Add(new PaperLanguageSegment
            {
                Text = submitted,
                Language = language
            });
        }

        paper.Comp.Language = language;
        Dirty(paper);
    }

    private static List<PaperLanguageSegment> GetEffectiveSegments(PaperLanguageComponent component, string? fallbackContent)
    {
        if (component.Segments.Count > 0)
            return component.Segments;

        if (string.IsNullOrWhiteSpace(fallbackContent))
            return [];

        return
        [
            new PaperLanguageSegment
            {
                Text = fallbackContent,
                Language = component.Language
            }
        ];
    }
}
