using Content.Server.Administration;
using Content.Shared._Forge.Paper;
using Content.Shared.Administration;
using Content.Shared.Paper;
using Robust.Shared.Console;
using Robust.Shared.Containers;
using Robust.Shared.Enums;

namespace Content.Server._Forge.Paper;

/// <summary>
/// Lists papers that currently contain uploaded pixel-art and opens them for
/// visual review. Automatic NSFW detection is not possible; staff must look.
/// </summary>
[AdminCommand(AdminFlags.Admin)]
public sealed partial class PaperImagesCommand : LocalizedCommands
{
    [Dependency] private IEntityManager _entities = default!;

    public override string Command => "paperimages";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length == 0)
        {
            ListPapers(shell);
            return;
        }

        if (args.Length != 1)
        {
            shell.WriteError(Help);
            return;
        }

        OpenPaper(shell, args[0]);
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length != 1)
            return CompletionResult.Empty;

        return CompletionResult.FromHintOptions(
            CompletionHelper.NetEntities(args[0], _entities),
            Loc.GetString("cmd-paperimages-hint"));
    }

    private void ListPapers(IConsoleShell shell)
    {
        var query = _entities.AllEntityQueryEnumerator<PaperComponent>();
        var found = 0;

        while (query.MoveNext(out var uid, out var paper))
        {
            var sizes = PaperPixelArtCodec.GetImageSizes(paper.Content);
            if (sizes.Count == 0)
                continue;

            found++;
            if (found == 1)
                shell.WriteLine(Loc.GetString("cmd-paperimages-header"));

            var preview = Truncate(PaperPixelArtCodec.SummarizeForLogs(paper.Content), 80);
            shell.WriteLine(Loc.GetString("cmd-paperimages-entry",
                ("entity", _entities.ToPrettyString(uid)),
                ("net", _entities.GetNetEntity(uid)),
                ("count", sizes.Count),
                ("sizes", PaperPixelArtCodec.FormatImageSizes(sizes)),
                ("location", FormatLocation(uid)),
                ("preview", preview)));
        }

        if (found == 0)
        {
            shell.WriteLine(Loc.GetString("cmd-paperimages-none"));
            return;
        }

        shell.WriteLine(Loc.GetString("cmd-paperimages-footer", ("count", found)));
    }

    private void OpenPaper(IConsoleShell shell, string rawId)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString("shell-cannot-run-command-from-server"));
            return;
        }

        if (player.Status != SessionStatus.InGame || player.AttachedEntity is not { Valid: true } actor)
        {
            shell.WriteError(Loc.GetString("shell-must-be-attached-to-entity"));
            return;
        }

        if (!NetEntity.TryParse(rawId, out var netId) || !_entities.TryGetEntity(netId, out var uid))
        {
            shell.WriteError(Loc.GetString("cmd-paperimages-invalid-entity", ("value", rawId)));
            return;
        }

        if (!_entities.TryGetComponent(uid.Value, out PaperComponent? paper))
        {
            shell.WriteError(Loc.GetString("cmd-paperimages-not-paper",
                ("entity", _entities.ToPrettyString(uid.Value))));
            return;
        }

        var sizes = PaperPixelArtCodec.GetImageSizes(paper.Content);
        if (sizes.Count == 0)
            shell.WriteLine(Loc.GetString("cmd-paperimages-no-images"));

        var paperSystem = _entities.System<PaperSystem>();
        if (!paperSystem.TryOpenReadUi(uid.Value, actor, paper))
        {
            shell.WriteError(Loc.GetString("cmd-paperimages-open-failed",
                ("entity", _entities.ToPrettyString(uid.Value))));
            return;
        }

        shell.WriteLine(Loc.GetString("cmd-paperimages-opened",
            ("entity", _entities.ToPrettyString(uid.Value)),
            ("count", sizes.Count),
            ("sizes", sizes.Count == 0 ? "-" : PaperPixelArtCodec.FormatImageSizes(sizes))));
    }

    private string FormatLocation(EntityUid uid)
    {
        var containers = _entities.System<SharedContainerSystem>();
        if (containers.TryGetContainingContainer(uid, out var container))
        {
            return Loc.GetString("cmd-paperimages-held-by",
                ("holder", _entities.ToPrettyString(container.Owner)));
        }

        var xform = _entities.GetComponent<TransformComponent>(uid);
        return Loc.GetString("cmd-paperimages-at", ("coords", xform.Coordinates));
    }

    private static string Truncate(string text, int maxChars)
    {
        text = text.Replace('\n', ' ').Replace('\r', ' ');
        if (text.Length <= maxChars)
            return text;

        return text[..Math.Max(0, maxChars - 1)] + "…";
    }
}
