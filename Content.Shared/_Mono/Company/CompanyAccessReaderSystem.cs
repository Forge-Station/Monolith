using Content.Shared.Popups;
using Content.Shared.UserInterface;

namespace Content.Shared._Mono.Company;

/// <summary>
/// This system handles checking if a user belongs to the required company
/// before granting access to an entity.
/// </summary>
public sealed partial class CompanyAccessReaderSystem : EntitySystem
{
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CompanyAccessReaderComponent, ActivatableUIOpenAttemptEvent>(OnUIOpenAttempt);
    }

    private void OnUIOpenAttempt(Entity<CompanyAccessReaderComponent> entity, ref ActivatableUIOpenAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        // #Forge-change START (KIAS): Адаптер использует штатную проверку company без копии правил доступа.
        if (!IsAllowed(entity, args.User))
        {
        // #Forge-change END (KIAS)
            args.Cancel();
            if (entity.Comp.PopupMessage != null)
                // #Forge-change START (KIAS): Адаптер использует штатную проверку company без копии правил доступа.
                _popup.PopupClient(Loc.GetString(entity.Comp.PopupMessage), entity, args.User);
                // #Forge-change END (KIAS)
        }
    // #Forge-change START (KIAS): Адаптер использует штатную проверку company без копии правил доступа.
    }
    // #Forge-change END (KIAS)

    // #Forge-change START (KIAS): Адаптер использует штатную проверку company без копии правил доступа.
    public bool IsAllowed(EntityUid target, EntityUid user)
    {
        if (!TryComp<CompanyAccessReaderComponent>(target, out var reader)) return true;
        if (!TryComp<CompanyComponent>(user, out var company)) return reader.Inverted;
        return reader.RequiredCompanies.Contains(company.CompanyName) != reader.Inverted;
    // #Forge-change END (KIAS)
    }
}
