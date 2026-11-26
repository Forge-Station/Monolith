using Content.Shared.Construction.Components;
using Content.Shared.Materials;
using Content.Shared.Materials.OreSilo;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.TradeGoods.Systems;

/// <summary>
/// Keeps trade crates out of silos and flatpackers. A crate's material has no stack entity, so storage that isn't a lathe
/// could never give it back; these take only materials they can eject.
/// </summary>
public sealed class WFCrateMaterialStorageSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private SharedMaterialStorageSystem _storage = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<OreSiloComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<OreSiloComponent, GetMaterialWhitelistEvent>(OnGetWhitelist);
        SubscribeLocalEvent<FlatpackCreatorComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<FlatpackCreatorComponent, GetMaterialWhitelistEvent>(OnGetWhitelist);
    }

    private void OnMapInit(EntityUid uid, IComponent component, MapInitEvent args)
    {
        _storage.UpdateMaterialWhitelist(uid);
    }

    private void OnGetWhitelist(EntityUid uid, IComponent component, ref GetMaterialWhitelistEvent args)
    {
        foreach (var material in _proto.EnumeratePrototypes<MaterialPrototype>())
        {
            if (material.StackEntity != null && !args.Whitelist.Contains(material.ID))
                args.Whitelist.Add(material.ID);
        }
    }
}
