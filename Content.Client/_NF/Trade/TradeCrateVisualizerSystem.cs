using Content.Shared._NF.Trade;
using Robust.Client.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Client._NF.Trade;

/// <summary>
/// Visualizer for trade crates, largely based on Nyano's mail visualizer (thank you)
/// </summary>
public sealed partial class TradeCrateVisualizerSystem : VisualizerSystem<TradeCrateComponent>
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    private const string FallbackIconID = "CargoOther";
    private const string CargoPriorityActiveState = "cargo_priority_active";
    private const string CargoPriorityInactiveState = "cargo_priority_inactive";

    protected override void OnAppearanceChange(EntityUid uid, TradeCrateComponent component, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null)
            return;

        _appearance.TryGetData(uid, TradeCrateVisuals.DestinationIcon, out string job, args.Component);

        if (string.IsNullOrEmpty(job))
            job = FallbackIconID;

        if (!_proto.TryIndex<TradeCrateDestinationPrototype>(job, out var icon))
            icon = _proto.Index<TradeCrateDestinationPrototype>(FallbackIconID);

        SpriteSystem.LayerSetTexture((uid, args.Sprite), TradeCrateVisualLayers.Icon, _sprite.Frame0(icon.Icon));
        SpriteSystem.LayerSetVisible((uid, args.Sprite), TradeCrateVisualLayers.Icon, true);
        if (_appearance.TryGetData(uid, TradeCrateVisuals.IsPriority, out bool isPriority) && isPriority)
        {
            SpriteSystem.LayerSetVisible((uid, args.Sprite), TradeCrateVisualLayers.Priority, true);
            if (_appearance.TryGetData(uid, TradeCrateVisuals.IsPriorityInactive, out bool inactive) && inactive)
                SpriteSystem.LayerSetRsiState((uid, args.Sprite), TradeCrateVisualLayers.Priority, CargoPriorityInactiveState);
            else
                SpriteSystem.LayerSetRsiState((uid, args.Sprite), TradeCrateVisualLayers.Priority, CargoPriorityActiveState);
        }
        else
            SpriteSystem.LayerSetVisible((uid, args.Sprite), TradeCrateVisualLayers.Priority, false);
    }
}

public enum TradeCrateVisualLayers : byte
{
    Icon,
    Priority
}
