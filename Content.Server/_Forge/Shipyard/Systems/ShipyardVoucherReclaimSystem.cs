using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Server._NF.Shipyard.Components;
using Content.Server.Popups;
using Content.Shared._Forge.Shipyard.Components;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared.Lathe;
using Content.Shared.Materials;

namespace Content.Server._Forge.Shipyard.Systems;

/// <summary>
/// Shipyard vouchers can always be recycled. Unused vouchers return 100% lathe recipe materials.
/// </summary>
public sealed class ShipyardVoucherReclaimSystem : EntitySystem
{
    [Dependency] private SharedLatheSystem _lathe = default!;
    [Dependency] private PopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShipyardVoucherComponent, MapInitEvent>(OnVoucherMapInit);
        SubscribeLocalEvent<ShipyardVoucherComponent, AttemptMaterialReclaimEvent>(OnAttemptMaterialReclaim);
        SubscribeLocalEvent<ShipyardVoucherComponent, GotReclaimedEvent>(OnGotReclaimed);
    }

    private void OnVoucherMapInit(EntityUid uid, ShipyardVoucherComponent component, MapInitEvent args)
    {
        EnsureInitialRedemptions(component);
    }

    private void OnAttemptMaterialReclaim(EntityUid uid, ShipyardVoucherComponent component, ref AttemptMaterialReclaimEvent args)
    {
        if (args.User is not { } user || QualifiesForFullMaterialReclaim(uid, component))
            return;

        _popup.PopupClient(Loc.GetString("shipyard-voucher-reclaim-no-materials"), uid, user);
    }

    private void OnGotReclaimed(EntityUid uid, ShipyardVoucherComponent component, ref GotReclaimedEvent args)
    {
        RemComp<ShipyardVoucherFullReclaimComponent>(uid);

        if (QualifiesForFullMaterialReclaim(uid, component) && TryGetReclaimMaterials(uid, out var materials))
        {
            var composition = EnsureComp<PhysicalCompositionComponent>(uid);
            composition.MaterialComposition.Clear();

            foreach (var (material, amount) in materials)
                composition.MaterialComposition[material] = amount;

            EnsureComp<ShipyardVoucherFullReclaimComponent>(uid);
            return;
        }

        if (TryComp<PhysicalCompositionComponent>(uid, out var existing))
            existing.MaterialComposition.Clear();
    }

    public bool CanReclaim(EntityUid uid, ShipyardVoucherComponent component)
        => QualifiesForFullMaterialReclaim(uid, component);

    public bool QualifiesForFullMaterialReclaim(EntityUid uid, ShipyardVoucherComponent component)
    {
        if (HasComp<ShuttleDeedComponent>(uid))
            return false;

        EnsureInitialRedemptions(component);

        if (component.RedemptionsLeft < component.InitialRedemptions)
            return false;

        return TryGetReclaimMaterials(uid, out _);
    }

    private static void EnsureInitialRedemptions(ShipyardVoucherComponent component)
    {
        if (component.InitialRedemptions == 0)
            component.InitialRedemptions = component.RedemptionsLeft;
    }

    private bool TryGetReclaimMaterials(EntityUid uid, [NotNullWhen(true)] out Dictionary<string, int>? materials)
    {
        materials = null;
        var protoId = MetaData(uid).EntityPrototype?.ID;
        if (protoId == null || !_lathe.TryGetRecipesFromEntity(protoId, out var recipes) || recipes.Count == 0)
            return false;

        var recipe = recipes.Count == 1
            ? recipes[0]
            : recipes.OrderByDescending(static r => r.Materials.Values.Sum()).First();

        materials = new Dictionary<string, int>();
        foreach (var (material, amount) in recipe.Materials)
            materials[material] = amount;

        return materials.Count > 0;
    }
}
