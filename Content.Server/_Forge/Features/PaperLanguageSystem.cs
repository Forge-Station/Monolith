using System.Text;
using Content.Server._EinsteinEngines.Language;
using Content.Shared._EinsteinEngines.Language;
using Content.Shared._EinsteinEngines.Language.Systems;
using Content.Shared._Forge.Features;
using Content.Shared._Forge.Features.Components;
using Content.Shared.Examine;
using Content.Shared.Ghost;
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
        if (string.IsNullOrEmpty(args.Language))
            return;

        ProtoId<LanguagePrototype> language = args.Language;
        if (language == SharedLanguageSystem.UniversalPrototype || _language.GetLanguagePrototype(language) == null)
            return;

        var canWrite = HasComp<GhostComponent>(args.Actor)
                       || TryComp<UniversalLanguageSpeakerComponent>(args.Actor, out var uni) && uni.Enabled
                       || _language.CanSpeak(args.Actor, language);
        if (!canWrite)
            return;

        TryComp<PaperComponent>(paper.Owner, out var paperComp);
        var currentContent = paperComp?.Content ?? string.Empty;
        var fragment = PaperLanguageFormatting.GetAppendedFragment(
            PaperLanguageFormatting.Join(paper.Comp.Segments),
            args.Text);

        if (paper.Comp.Segments.Count == 0)
        {
            var prior = currentContent;
            if (!string.IsNullOrEmpty(fragment) && currentContent.EndsWith(fragment, StringComparison.Ordinal))
                prior = currentContent[..^fragment.Length].TrimEnd('\r', '\n');

            if (!string.IsNullOrEmpty(prior) && prior != fragment)
                paper.Comp.Segments.Add(new PaperLanguageSegment { Text = prior, Language = paper.Comp.Language });
        }

        if (string.IsNullOrWhiteSpace(fragment))
        {
            paper.Comp.Language = language;
            Dirty(paper);
            return;
        }

        if (paper.Comp.Segments.Count > 0 && paper.Comp.Segments[^1].Language == language)
        {
            var last = paper.Comp.Segments[^1];
            var separator = last.Text.Length == 0 || last.Text.EndsWith('\n') ? string.Empty : "\n";
            last.Text += separator + fragment;
        }
        else
        {
            paper.Comp.Segments.Add(new PaperLanguageSegment { Text = fragment, Language = language });
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
