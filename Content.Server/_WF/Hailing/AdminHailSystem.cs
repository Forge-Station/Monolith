using Content.Server.Administration.Managers;
using Content.Server.EUI;
using Content.Server.Shuttles.Components;
using Content.Shared._WF.Hailing;
using Content.Shared.Administration;
using Content.Shared.Database;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server._WF.Hailing;

/// <summary>
/// The admin verb on shuttle consoles that hails a ship as God. The admin needs no ship of their own: a
/// stand-in entity takes their end of the hail, and they talk through the ordinary hail window.
/// </summary>
public sealed class AdminHailSystem : EntitySystem
{
    [Dependency] private IAdminManager _admins = default!;
    [Dependency] private EuiManager _eui = default!;
    [Dependency] private HailingSystem _hailing = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        // Broadcast, so no other system's verbs on shuttle consoles are in the way.
        SubscribeLocalEvent<GetVerbsEvent<Verb>>(OnGetVerbs);
        SubscribeLocalEvent<AdminHailEndpointComponent, HailEndedEvent>(OnEnded);
    }

    private void OnGetVerbs(GetVerbsEvent<Verb> args)
    {
        if (!HasComp<ShuttleConsoleComponent>(args.Target) || !TryComp<ActorComponent>(args.User, out var actor) ||
            !_admins.HasAdminFlag(actor.PlayerSession, AdminFlags.Fun))
        {
            return;
        }

        var player = actor.PlayerSession;
        var console = args.Target;

        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("hail-admin-verb"),
            Message = Loc.GetString("hail-admin-verb-description"),
            Category = VerbCategory.Admin,
            Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/bubbles.svg.192dpi.png")),
            Act = () =>
            {
                if (Start(player, console) == null)
                    _popup.PopupCursor(Loc.GetString("hail-admin-no-console"), player, PopupType.MediumCaution);
            },
            Impact = LogImpact.Medium,
        });
    }

    /// <summary>
    /// Hails the ship a console is on as God and gives the admin their window. Null if nothing could ring.
    /// </summary>
    public AdminHailEui? Start(ICommonSession admin, EntityUid console)
    {
        if (admin.AttachedEntity is not { } user || TerminatingOrDeleted(console) ||
            Transform(console).GridUid is not { } ship ||
            _hailing.HailAsGod(user, admin, ship) is not { } endpoint)
        {
            return null;
        }

        var eui = new AdminHailEui(endpoint, this);
        AddComp<AdminHailEndpointComponent>(endpoint).Eui = eui;
        _eui.OpenEui(eui, admin);
        return eui;
    }

    /// <summary>Sends a line from God.</summary>
    public void Say(EntityUid endpoint, string text)
    {
        // The stand-in speaks for whoever opened it, whatever body they have moved to since.
        if (TryComp<HailConsoleComponent>(endpoint, out var hail) && hail.OperatorUid is { } user)
            _hailing.Say(endpoint, user, text);
    }

    /// <summary>Cancels the hail or closes the channel, leaving the admin their window and its log.</summary>
    public void End(EntityUid endpoint)
    {
        _hailing.End(endpoint);
    }

    /// <summary>The admin's window is gone: end whatever is left and remove the stand-in.</summary>
    public void Close(EntityUid endpoint)
    {
        if (TerminatingOrDeleted(endpoint))
            return;

        if (TryComp<AdminHailEndpointComponent>(endpoint, out var comp))
            comp.Eui = null;

        _hailing.End(endpoint);
        QueueDel(endpoint);
    }

    private void OnEnded(Entity<AdminHailEndpointComponent> ent, ref HailEndedEvent args)
    {
        if (args.Reason is { } reason)
            ent.Comp.Eui?.SendEnded(reason, args.Ship);
    }
}
