using System.Linq;
using Content.Client.Items.Systems;
using Content.Shared._Forge.Weapons.ChainsawShield;
using Content.Shared.Hands;
using Content.Shared.Toggleable;
using Robust.Client.GameObjects;

namespace Content.Client._Forge.Weapons.ChainsawShield;

public sealed partial class ChainsawShieldVisualsSystem : EntitySystem
{
    [Dependency] private SharedAppearanceSystem _appearance = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ChainsawShieldVisualsComponent, GetInhandVisualsEvent>(OnGetInhandVisuals,
            after: [typeof(ItemSystem)]);
    }

    private void OnGetInhandVisuals(EntityUid uid, ChainsawShieldVisualsComponent component, GetInhandVisualsEvent args)
    {
        var enabled = false;
        if (TryComp(uid, out AppearanceComponent? appearance))
            _appearance.TryGetData<bool>(uid, ToggleableVisuals.Enabled, out enabled, appearance);

        var visuals = enabled ? component.ActiveInhandVisuals : component.InactiveInhandVisuals;
        if (!visuals.TryGetValue(args.Location, out var layers))
            return;

        var i = 0;
        var defaultKey = $"inhand-{args.Location.ToString().ToLowerInvariant()}-chainsaw-shield";
        foreach (var layer in layers)
        {
            var key = layer.MapKeys?.FirstOrDefault();
            if (key == null)
            {
                key = i == 0 ? defaultKey : $"{defaultKey}-{i}";
                i++;
            }

            args.Layers.Add((key, layer));
        }
    }
}
