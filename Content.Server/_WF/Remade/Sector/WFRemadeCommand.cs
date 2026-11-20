using System.Globalization;
using System.Linq;
using Content.Server.Administration;
using Content.Server._WF.Remade.Director;
using Content.Server._WF.Remade.Monolith;
using Content.Shared._WF.Remade;
using Content.Shared._WF.Remade.Sector;
using Content.Shared.Administration;
using Robust.Server.Player;
using Robust.Shared.Console;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.Remade.Sector;

/// <summary>The Remade from the console: place a site, set a Scion's resonance, or list the Scions in the sector.</summary>
[AdminCommand(AdminFlags.Admin)]
public sealed partial class WFRemadeCommand : LocalizedEntityCommands
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFRemadeSiteSystem _sites = default!;
    [Dependency] private WFRemadeClearSystem _clear = default!;
    [Dependency] private WFRemadeDirectorSystem _director = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _players = default!;

    /// <summary>How far from the admin a site placed with "here" lies, in metres.</summary>
    private const float HereOffset = 400f;

    private static readonly string[] Subcommands = { "spawn", "resonance", "status", "clear", "outbreak", "lockdown" };
    private const float LockdownDefaultSeconds = 60f;

    public override string Command => "wf_remade";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        switch (args.Length > 0 ? args[0] : string.Empty)
        {
            case "spawn" when args.Length is >= 1 and <= 3:
                Spawn(shell, args);
                break;
            case "resonance" when args.Length is 2 or 3:
                Resonance(shell, args);
                break;
            case "status" when args.Length == 1:
                Status(shell);
                break;
            case "clear" when args.Length == 1:
                shell.WriteLine(Loc.GetString("cmd-wf_remade-cleared", ("count", _clear.Clear())));
                break;
            case "outbreak" when args.Length is 1 or 2:
                Outbreak(shell, args);
                break;
            case "lockdown" when args.Length is >= 2 and <= 4:
                Lockdown(shell, args);
                break;
            default:
                shell.WriteLine(Help);
                break;
        }
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHintOptions(Subcommands, Loc.GetString("cmd-wf_remade-hint-sub")),
            2 when args[0] == "spawn" => CompletionResult.FromHintOptions(
                _proto.EnumeratePrototypes<WFRemadeSitePrototype>().Select(site => site.ID).Append("here"),
                Loc.GetString("cmd-wf_remade-hint-site")),
            3 when args[0] == "spawn" => CompletionResult.FromHintOptions(new[] { "here" }, Loc.GetString("cmd-wf_remade-hint-here")),
            2 when args[0] == "resonance" => CompletionResult.FromHint(Loc.GetString("cmd-wf_remade-hint-value")),
            3 when args[0] == "resonance" => CompletionResult.FromHint(Loc.GetString("cmd-wf_remade-hint-scion")),
            2 when args[0] == "outbreak" => CompletionResult.FromHint(Loc.GetString("cmd-wf_remade-hint-scion")),
            2 when args[0] == "lockdown" => CompletionResult.FromHintOptions(CompletionHelper.SessionNames(players: _players), Loc.GetString("cmd-wf_remade-hint-target")),
            3 when args[0] == "lockdown" => CompletionResult.FromHint(Loc.GetString("cmd-wf_remade-hint-seconds")),
            4 when args[0] == "lockdown" => CompletionResult.FromHint(Loc.GetString("cmd-wf_remade-hint-range")),
            _ => CompletionResult.Empty,
        };
    }

    /// <summary>spawn [site] [here]: starts a site now, ignoring the rule's limits. Without a site, a discovery site is picked.</summary>
    private void Spawn(IConsoleShell shell, string[] args)
    {
        var here = args.Skip(1).Contains("here");
        var named = args.Skip(1).FirstOrDefault(arg => arg != "here");
        WFRemadeSitePrototype? site;
        if (named != null)
        {
            if (!_proto.TryIndex(named, out site))
            {
                shell.WriteError(Loc.GetString("cmd-wf_remade-no-site", ("site", named)));
                return;
            }
        }
        else
            site = _sites.PickDiscovery();

        if (site == null)
        {
            shell.WriteError(Loc.GetString("cmd-wf_remade-no-sites"));
            return;
        }

        var admin = shell.Player?.AttachedEntity;
        MapCoordinates? at = null;
        if (here)
        {
            if (admin is not { } body || EntityManager.GetComponent<TransformComponent>(body).MapID == MapId.Nullspace)
            {
                shell.WriteError(Loc.GetString("cmd-wf_remade-no-body"));
                return;
            }

            at = _transform.GetMapCoordinates(body).Offset(new System.Numerics.Vector2(HereOffset, 0f));
        }

        if (!_sites.TryStartSite(site, at, announce: !here, out var encounter, admin))
        {
            shell.WriteError(Loc.GetString("cmd-wf_remade-spawn-failed", ("site", site.ID)));
            return;
        }

        shell.WriteLine(Loc.GetString("cmd-wf_remade-spawned", ("site", site.ID), ("encounter", EntityManager.GetNetEntity(encounter).ToString())));
    }

    /// <summary>lockdown target [seconds] [range]: bolts the powered doors round a player or entity and sounds the biohazard alarm.</summary>
    private void Lockdown(IConsoleShell shell, string[] args)
    {
        EntityUid? target = null;
        if (NetEntity.TryParse(args[1], out var net) && EntityManager.TryGetEntity(net, out var parsed))
            target = parsed;
        else if (_players.TryGetSessionByUsername(args[1], out var session))
            target = session.AttachedEntity;

        if (target is not { } uid || EntityManager.Deleted(uid))
        {
            shell.WriteError(Loc.GetString("cmd-wf_remade-no-target", ("target", args[1])));
            return;
        }

        var seconds = LockdownDefaultSeconds;
        var range = WFRemadeDirectorSystem.LockdownRange;
        if (args.Length >= 3 && (!float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out seconds) || seconds <= 0f)
            || args.Length >= 4 && (!float.TryParse(args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out range) || range <= 0f))
        {
            shell.WriteError(Loc.GetString("cmd-wf_remade-bad-lockdown"));
            return;
        }

        var bolted = _director.Lockdown(uid, seconds, range);
        shell.WriteLine(Loc.GetString(bolted > 0 ? "cmd-wf_remade-locked-down" : "cmd-wf_remade-lockdown-none",
            ("count", bolted), ("seconds", (int) seconds)));
    }

    /// <summary>outbreak [scion]: breaks out on another crewed ship now, as a stone at the Crescendo does.</summary>
    private void Outbreak(IConsoleShell shell, string[] args)
    {
        EntityUid? stone = null;
        if (args.Length == 2)
        {
            if (NetEntity.TryParse(args[1], out var net) && EntityManager.TryGetEntity(net, out var parsed)
                && EntityManager.HasComponent<WFMonolithComponent>(parsed))
            {
                stone = parsed;
            }
            else
            {
                shell.WriteError(Loc.GetString("cmd-wf_remade-no-scion", ("scion", args[1])));
                return;
            }
        }
        else
        {
            var query = EntityManager.EntityQueryEnumerator<WFMonolithComponent, WFRemadeDirectorComponent>();
            if (query.MoveNext(out var first, out _, out _))
                stone = first;
        }

        if (stone is not { } uid || !EntityManager.TryGetComponent<WFRemadeDirectorComponent>(uid, out var director))
        {
            shell.WriteError(Loc.GetString("cmd-wf_remade-none"));
            return;
        }

        var ok = _director.TryOutbreak((uid, director, EntityManager.GetComponent<WFMonolithComponent>(uid)), _timing.CurTime);
        shell.WriteLine(Loc.GetString(ok ? "cmd-wf_remade-outbreak" : "cmd-wf_remade-outbreak-failed"));
    }

    /// <summary>resonance value [scion]: sets a Scion's resonance. Its stage follows within a second.</summary>
    private void Resonance(IConsoleShell shell, string[] args)
    {
        if (!float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            || !float.IsFinite(value))
        {
            shell.WriteError(Loc.GetString("cmd-wf_remade-bad-value", ("value", args[1])));
            return;
        }

        value = Math.Clamp(value, 0f, WFMonolithMath.MaxResonance);
        EntityUid? scion = null;
        if (args.Length == 3)
        {
            if (!int.TryParse(args[2], out var id) || !EntityManager.TryGetEntity(new NetEntity(id), out scion)
                || !EntityManager.HasComponent<WFMonolithComponent>(scion))
            {
                shell.WriteError(Loc.GetString("cmd-wf_remade-no-scion", ("scion", args[2])));
                return;
            }
        }
        else
            scion = Pick(shell);

        if (scion is not { } target)
        {
            shell.WriteError(Loc.GetString("cmd-wf_remade-none"));
            return;
        }

        EntityManager.GetComponent<WFMonolithComponent>(target).Resonance = value;
        shell.WriteLine(Loc.GetString("cmd-wf_remade-resonance", ("scion", EntityManager.GetNetEntity(target).ToString()),
            ("value", value.ToString("0.#", CultureInfo.InvariantCulture))));
    }

    /// <summary>The wild Scion if there is one, else the one nearest the admin, else any.</summary>
    private EntityUid? Pick(IConsoleShell shell)
    {
        EntityUid? best = null;
        var bestWild = false;
        var bestDistance = float.MaxValue;
        var admin = shell.Player?.AttachedEntity;
        var query = EntityManager.EntityQueryEnumerator<WFMonolithComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            var wild = _sites.IsWild(uid);
            var distance = admin is { } body && EntityManager.GetComponent<TransformComponent>(body).MapID == xform.MapID
                ? (_transform.GetWorldPosition(xform) - _transform.GetWorldPosition(body)).Length()
                : float.MaxValue;
            if (best == null || wild && !bestWild || wild == bestWild && distance < bestDistance)
            {
                best = uid;
                bestWild = wild;
                bestDistance = distance;
            }
        }

        return best;
    }

    private void Status(IConsoleShell shell)
    {
        shell.WriteLine(Loc.GetString("cmd-wf_remade-status-header",
            ("scions", _sites.ScionCount()),
            ("wild", _sites.WildCount()),
            ("can", _sites.CanStartWild(WFRemadeSiteSystem.DefaultMinGap, WFRemadeSiteSystem.DefaultGoneCooldown, out var reason)
                ? Loc.GetString("cmd-wf_remade-status-yes")
                : reason)));

        var query = EntityManager.EntityQueryEnumerator<WFMonolithComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var monolith, out var xform))
        {
            var signal = EntityManager.GetComponentOrNull<WFScionSignalComponent>(uid);
            shell.WriteLine(Loc.GetString("cmd-wf_remade-status-scion",
                ("scion", EntityManager.GetNetEntity(uid).ToString()),
                ("stage", monolith.Stage.ToString()),
                ("resonance", monolith.Resonance.ToString("0.0", CultureInfo.InvariantCulture)),
                ("state", Loc.GetString(signal == null ? "cmd-wf_remade-state-none"
                    : _sites.IsWild(uid, signal) ? "cmd-wf_remade-state-wild"
                    : signal.Disturbed ? "cmd-wf_remade-state-disturbed" : "cmd-wf_remade-state-quiet")),
                ("grid", xform.GridUid is { } grid ? EntityManager.GetNetEntity(grid).ToString() : "-")));
        }
    }
}
