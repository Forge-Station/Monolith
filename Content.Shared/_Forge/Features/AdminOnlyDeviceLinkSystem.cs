using Content.Shared._Forge.Features.Components;
using Content.Shared.Administration.Managers;
using Content.Shared.DeviceLinking.Events;

namespace Content.Shared._Forge.Features;

public sealed class AdminOnlyDeviceLinkSystem : EntitySystem
{
    [Dependency] private readonly ISharedAdminManager _admin = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<AdminOnlyDeviceLinkComponent, LinkAttemptEvent>(OnLinkAttempt);
    }

    private void OnLinkAttempt(Entity<AdminOnlyDeviceLinkComponent> ent, ref LinkAttemptEvent args)
    {
        if (args.User == null)
            return;

        if (!_admin.IsAdmin(args.User.Value, includeDeAdmin: true))
            args.Cancel();
    }
}
