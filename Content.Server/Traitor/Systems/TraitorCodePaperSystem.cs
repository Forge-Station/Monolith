using Content.Shared.GameTicking.Rules.Components;
using System.Diagnostics.CodeAnalysis;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules;
using Content.Server.GameTicking.Rules.Components;
using Content.Server.Paper;
using Content.Server.Traitor.Components;
using Robust.Shared.Random;
using Robust.Shared.Utility;
using System.Linq;
using Content.Shared.Codewords;
using Content.Shared.Paper;

namespace Content.Server.Traitor.Systems;

public sealed partial class TraitorCodePaperSystem : EntitySystem
{
    [Dependency] private ServerGameTicker _gameTicker = default!;
    [Dependency] private CodewordSystem _codewords = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private PaperSystem _paper = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<TraitorCodePaperComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(EntityUid uid, TraitorCodePaperComponent component, MapInitEvent args)
    {
        SetupPaper(uid, component);
    }

    private void SetupPaper(EntityUid uid, TraitorCodePaperComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        if (TryComp(uid, out PaperComponent? paperComp))
        {
            if (TryGetTraitorCode(out var paperContent, component))
            {
                _paper.SetContent((uid, paperComp), paperContent);
            }
        }
    }

    private bool TryGetTraitorCode([NotNullWhen(true)] out string? traitorCode, TraitorCodePaperComponent component)
    {
        traitorCode = null;

        var codesMessage = new FormattedMessage();
        List<string> codeList = new();
        // Find the first nuke that matches the passed location.
        if (_gameTicker.IsGameRuleAdded<TraitorRuleComponent>())
        {
            var ruleEnts = _gameTicker.GetAddedGameRules();
            foreach (var ruleEnt in ruleEnts)
            {
                if (TryComp(ruleEnt, out TraitorRuleComponent? traitorComp))
                {
                    codeList.AddRange(_codewords.GetCodewords(traitorComp.CodewordFactionPrototypeId));
                }
            }
        }
        if (codeList.Count == 0)
        {
            if (component.FakeCodewords)
                codeList = _codewords.GetCodewords(new TraitorRuleComponent().CodewordFactionPrototypeId).ToList();
            else
                codeList = [Loc.GetString("traitor-codes-none")];
        }

        _random.Shuffle(codeList);

        int i = 0;
        foreach (var code in codeList)
        {
            i++;
            if (i > component.CodewordAmount && !component.CodewordShowAll)
                break;

            codesMessage.PushNewline();
            codesMessage.AddMarkupOrThrow(code);
        }

        if (!codesMessage.IsEmpty)
        {
            if (i == 1)
                traitorCode = Loc.GetString("traitor-codes-message-singular") + codesMessage;
            else
                traitorCode = Loc.GetString("traitor-codes-message-plural") + codesMessage;
        }
        return !codesMessage.IsEmpty;
    }
}
