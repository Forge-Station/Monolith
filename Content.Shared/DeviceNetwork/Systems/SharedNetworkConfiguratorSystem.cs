using Content.Shared.Actions;
using Content.Shared.DeviceNetwork.Components;
using Content.Shared.Tools.Components; // Forge-Change-add: MultipleToolComponent
using Content.Shared.Tools.Systems; // Forge-Change-add: SharedToolSystem.PulseQuality gate
using Content.Shared.UserInterface;
using Robust.Shared.Serialization;

namespace Content.Shared.DeviceNetwork.Systems;

public abstract class SharedNetworkConfiguratorSystem : EntitySystem
{
    [Dependency] private readonly SharedToolSystem _toolSystem = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NetworkConfiguratorComponent, ActivatableUIOpenAttemptEvent>(OnUiOpenAttempt);
    }

    // Forge-Change-start: OmnitoolModsuitGauntlet — network configurator only in pulsing mode (Z)
    protected bool CanUseNetworkConfigurator(EntityUid uid)
    {
        if (!HasComp<MultipleToolComponent>(uid))
            return true;

        return _toolSystem.HasQuality(uid, SharedToolSystem.PulseQuality);
    }
    // Forge-Change-end

    private void OnUiOpenAttempt(EntityUid uid, NetworkConfiguratorComponent configurator, ActivatableUIOpenAttemptEvent args)
    {
        if (!CanUseNetworkConfigurator(uid)) // Forge-Change: block multitool UI outside pulsing mode
        {
            args.Cancel();
            return;
        }

        if (configurator.LinkModeActive)
            args.Cancel();
    }
}

public sealed partial class ClearAllOverlaysEvent : InstantActionEvent
{
}

[Serializable, NetSerializable]
public enum NetworkConfiguratorVisuals
{
    Mode
}

[Serializable, NetSerializable]
public enum NetworkConfiguratorLayers
{
    ModeLight
}
