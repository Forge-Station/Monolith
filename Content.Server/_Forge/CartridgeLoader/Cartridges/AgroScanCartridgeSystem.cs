using Content.Server.Botany.Components;
using Content.Shared.CartridgeLoader;
using Robust.Shared.Audio;

namespace Content.Server.CartridgeLoader.Cartridges;

public sealed class AgroScanCartridgeSystem : EntitySystem
{
    [Dependency] private readonly CartridgeLoaderSystem _cartridgeLoader = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<AgroScanCartridgeComponent, CartridgeAddedEvent>(OnCartridgeAdded);
        SubscribeLocalEvent<AgroScanCartridgeComponent, CartridgeRemovedEvent>(OnCartridgeRemoved);
    }

    private void OnCartridgeAdded(Entity<AgroScanCartridgeComponent> ent, ref CartridgeAddedEvent args)
    {
        var analyzer = EnsureComp<PlantAnalyzerComponent>(args.Loader);
        analyzer.Settings.ScanDelay = 0.8f;
        analyzer.Settings.ExposeAdvancedData = true;
        analyzer.ScanningEndSound ??= new SoundPathSpecifier("/Audio/Items/Medical/healthscanner.ogg");
    }

    private void OnCartridgeRemoved(Entity<AgroScanCartridgeComponent> ent, ref CartridgeRemovedEvent args)
    {
        if (!_cartridgeLoader.HasProgram<AgroScanCartridgeComponent>(args.Loader))
            RemComp<PlantAnalyzerComponent>(args.Loader);
    }
}
