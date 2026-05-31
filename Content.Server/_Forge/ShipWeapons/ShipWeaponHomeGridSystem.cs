using Content.Shared._Forge.ShipWeapons.Components;
using Content.Shared.Examine;

namespace Content.Server._Forge.ShipWeapons;

public sealed class ShipWeaponHomeGridSystem : EntitySystem
{
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShipWeaponHomeGridComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ShipWeaponHomeGridComponent, EntParentChangedMessage>(OnParentChanged);
        SubscribeLocalEvent<ShipWeaponHomeGridComponent, ExaminedEvent>(OnExamined);
    }

    private void OnMapInit(EntityUid uid, ShipWeaponHomeGridComponent component, MapInitEvent args)
    {
        component.HomeGrid ??= _transform.GetGrid(uid);
    }

    private void OnParentChanged(EntityUid uid, ShipWeaponHomeGridComponent component, ref EntParentChangedMessage args)
    {
        component.HomeGrid ??= _transform.GetGrid(uid);
    }

    private void OnExamined(EntityUid uid, ShipWeaponHomeGridComponent component, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange || component.HomeGrid == null)
            return;

        var loc = IsOnHomeGrid(uid, component)
            ? "ship-weapon-home-grid-examine-bound"
            : "ship-weapon-home-grid-examine-wrong-grid";

        args.PushMarkup(Loc.GetString(loc));
    }

    public bool IsOnHomeGrid(EntityUid uid, ShipWeaponHomeGridComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return true;

        if (!component.LockToHomeGrid || component.HomeGrid == null)
            return true;

        return _transform.GetGrid(uid) == component.HomeGrid;
    }
}
