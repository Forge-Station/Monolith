using System.Linq;
using Content.Shared._Forge.KIAS;
using Content.Shared._Mono.Company;
using Content.Shared._Mono.Shipyard;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Robust.Shared.Player;
using Robust.Shared.Map;

namespace Content.Server._Forge.KIAS;

public sealed class KiasAccessSystem : EntitySystem
{
    [Dependency] private ShipAccessReaderSystem _ships = default!;
    [Dependency] private AccessReaderSystem _readers = default!;
    [Dependency] private SharedIdCardSystem _ids = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private CompanyAccessReaderSystem _companyReaders = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<KiasClaimableComponent, InteractUsingEvent>(OnSwipe);
        SubscribeLocalEvent<GridSplitEvent>(OnSplit);
    }

    private void OnSplit(ref GridSplitEvent args)
    {
        if (!TryComp<KiasClaimComponent>(args.Grid, out var claim) || claim.Owner == null) return;
        foreach (var grid in args.NewGrids) EnsureComp<KiasClaimComponent>(grid).Owner = claim.Owner;
    }

    public KiasAccessPolicy ResolvePolicy(EntityUid grid)
    {
        if (TryComp<ShuttleDeedComponent>(grid, out var deed) && deed.ShuttleUid != null)
            return KiasAccessPolicy.ShipDeed;
        if (TryComp<CompanyComponent>(grid, out var company) && company.CompanyName != "None")
            return KiasAccessPolicy.Company;
        if (HasComp<KiasPoiAccessComponent>(grid) || HasComp<CompanyAccessReaderComponent>(grid)
            || TryComp<AccessReaderComponent>(grid, out var reader) && reader.Enabled
            && (reader.AccessLists.Any(group => group.Count > 0) || reader.AccessKeys.Count > 0 || reader.DenyTags.Count > 0
                || reader.RequiredCompany != null || reader.ContainerAccessProvider != null))
            return KiasAccessPolicy.Poi;
        return TryComp<KiasClaimComponent>(grid, out var claim) && claim.Owner != null
            ? KiasAccessPolicy.Claimed : KiasAccessPolicy.OpenUnclaimed;
    }

    public EntityUid? Core(EntityUid grid)
    {
        return TryComp<KiasGridComponent>(grid, out var runtime) && runtime.Core is { } core
               && !TerminatingOrDeleted(core) && Transform(core).GridUid == grid ? core : null;
    }

    public bool CanConfigure(EntityUid grid, EntityUid actor, bool security = false)
    {
        if (TerminatingOrDeleted(grid) || TerminatingOrDeleted(actor) || Core(grid) is not { } core)
            return false;
        if (!security && TryComp<AccessReaderComponent>(core, out var wireAccess) && !wireAccess.Enabled)
            return true;
        var policy = ResolvePolicy(grid);
        switch (policy)
        {
            case KiasAccessPolicy.ShipDeed:
                if (_ships.HasDeedAccess(grid, actor))
                    return true;
                if (security || !TryComp<ShipGuestAccessComponent>(grid, out var guests))
                    return false;
                return guests.GuestCyborgs.Contains(actor) || _ships.FindAccessibleIdCards(actor).Any(guests.GuestIdCards.Contains);
            case KiasAccessPolicy.Company:
                var company = Comp<CompanyComponent>(grid).CompanyName;
                return _ships.FindAccessibleIdCards(actor).Any(card => Comp<IdCardComponent>(card).CompanyName == company);
            case KiasAccessPolicy.Poi:
                return (HasComp<AccessReaderComponent>(grid) || HasComp<CompanyAccessReaderComponent>(grid))
                    && (!HasComp<AccessReaderComponent>(grid) || _readers.IsAllowed(actor, grid)) && _companyReaders.IsAllowed(grid, actor);
            case KiasAccessPolicy.Claimed:
                return TryComp<ActorComponent>(actor, out var player)
                    && Comp<KiasClaimComponent>(grid).Owner == player.PlayerSession.UserId
                    && _ships.FindAccessibleIdCards(actor).Count > 0;
            default:
                return !security;
        }
    }

    public bool CanRegister(EntityUid grid, EntityUid actor, bool locked)
    {
        return !locked && !TerminatingOrDeleted(actor) && Core(grid) != null;
    }

    public bool CanReceiveWireless(EntityUid grid, EntityUid transmitter, KiasWirelessComponent receiver) =>
        ResolvePolicy(grid) == KiasAccessPolicy.OpenUnclaimed || receiver.TrustedTransmitters.Contains(transmitter);

    public bool SetWirelessTrust(EntityUid receiver, EntityUid transmitter, EntityUid actor)
    {
        var kias = EntityManager.System<KiasSystem>();
        if (!kias.IsOnline(receiver) || !kias.IsOnline(transmitter)
            || !TryComp<KiasWirelessComponent>(receiver, out var config) || !HasComp<KiasWirelessComponent>(transmitter)
            || Transform(receiver).GridUid is not { } grid || Transform(transmitter).GridUid is not { } sourceGrid
            || !CanConfigure(sourceGrid, actor) || !CanConfigure(grid, actor)
            || ResolvePolicy(grid) != KiasAccessPolicy.OpenUnclaimed && !CanConfigure(grid, actor, security: true)) return false;
        config.TrustedTransmitters.RemoveAll(uid => TerminatingOrDeleted(uid));
        if (!config.TrustedTransmitters.Remove(transmitter))
        {
            if (config.TrustedTransmitters.Count >= 16) return false;
            config.TrustedTransmitters.Add(transmitter);
        }
        return true;
    }

    public bool Claim(EntityUid grid, EntityUid actor, EntityUid id)
    {
        if (ResolvePolicy(grid) != KiasAccessPolicy.OpenUnclaimed || Core(grid) is not { } core
            || !HasComp<IdCardComponent>(id) || !TryComp<ActorComponent>(actor, out var player))
            return false;
        EnsureComp<KiasClaimComponent>(grid).Owner = player.PlayerSession.UserId;
        return true;
    }

    private void OnSwipe(Entity<KiasClaimableComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || Transform(ent).GridUid is not { } grid || !_ids.TryGetIdCard(args.Used, out var id))
            return;
        args.Handled = true;
        if (Claim(grid, args.User, id.Owner))
            _popup.PopupEntity(Loc.GetString("kias-claimed"), ent, args.User);
        else
            _popup.PopupEntity(Loc.GetString("kias-claim-protected"), ent, args.User);
    }
}
