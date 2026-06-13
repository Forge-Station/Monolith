using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared.Materials;
using Robust.Shared.Prototypes;

namespace Content.Shared._Forge.Shipyard;

public static class VesselFabricationHelper
{
    public static Dictionary<ProtoId<MaterialPrototype>, int> GetFabricationMaterials(VesselPrototype vessel)
    {
        if (vessel.FabricationMaterials is { Count: > 0 })
            return new Dictionary<ProtoId<MaterialPrototype>, int>(vessel.FabricationMaterials);

        var price = Math.Max(vessel.Price, 1000);
        return new Dictionary<ProtoId<MaterialPrototype>, int>
        {
            ["Steel"] = (int)(price * 0.4),
            ["Plasteel"] = (int)(price * 0.25),
            ["Glass"] = (int)(price * 0.15),
            ["Plasma"] = (int)(price * 0.1),
        };
    }
}
