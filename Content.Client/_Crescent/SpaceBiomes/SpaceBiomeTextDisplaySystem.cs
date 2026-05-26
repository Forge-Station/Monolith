using Content.Shared._Crescent.SpaceBiomes;
using Robust.Shared.Prototypes;
using Content.Client.Audio;
using Robust.Client.Graphics;
using Robust.Shared.Timing;
using Content.Shared._Crescent.Vessel;

namespace Content.Client._Crescent.SpaceBiomes;

public sealed partial class SpaceTextDisplaySystem : EntitySystem
{
    [Dependency] private IPrototypeManager _protMan = default!;
    [Dependency] private IOverlayManager _overMan = default!;
    [Dependency] private ContentAudioSystem _audioSys = default!;

    private SpaceBiomeTextOverlay _overlay = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SpaceBiomeSwapMessage>(OnSwap);
        SubscribeLocalEvent<PlayerParentChangedMessage>(OnNewVesselEntered);
        _overlay = new();
        _overMan.AddOverlay(_overlay);
    }

    private void OnSwap(ref SpaceBiomeSwapMessage ev)
    {
        _audioSys.DisableAmbientMusic();
        SpaceBiomePrototype biome = _protMan.Index<SpaceBiomePrototype>(ev.Id);
        _overlay.Reset();
        _overlay.ResetDescription();
        // Forge-Change-start: space biome names and descriptions
        var nameKey = $"crescent-space-biome-{biome.ID}-name";
        _overlay.Text = Loc.TryGetString(nameKey, out var locName) ? locName : biome.Name;

        var descKey = $"crescent-space-biome-{biome.ID}-desc";
        _overlay.TextDescription = string.IsNullOrEmpty(biome.Description)
            ? ""
            : (Loc.TryGetString(descKey, out var locDesc) ? locDesc : biome.Description);

        _overlay.CharInterval = TimeSpan.FromSeconds(2f / Math.Max(_overlay.Text.Length, 1));
        if (_overlay.TextDescription == "")
            _overlay.CharIntervalDescription = TimeSpan.Zero;
        else
            _overlay.CharIntervalDescription = TimeSpan.FromSeconds(2f / Math.Max(_overlay.TextDescription.Length, 1));
        // Forge-Change-end: space biome names and descriptions
    }

    private void OnNewVesselEntered(ref PlayerParentChangedMessage ev)
    {
        if (ev.Grid == null) //player walked into space so we dont care
            return;

        var name = MetaData((EntityUid)ev.Grid).EntityName; //this should never be null. i hope
        var description = ""; //fallback for description is nothin'
        if (TryComp<VesselInfoComponent>((EntityUid)ev.Grid, out var vesselinfo))
            description = vesselinfo.Description;


        _overlay.Reset();             //these should be reset as well to match OnSwap
        _overlay.ResetDescription();

        if (_overlay.Text != null)
            return;

        if (name.Length == 0)
            return;

        _overlay.Text = Loc.TryGetString(name, out var locVesselName) ? locVesselName : name; // Forge-Change: space biome names and descriptions
        _overlay.TextDescription = description; // fallback is "" if no description is found.
        _overlay.CharInterval = TimeSpan.FromSeconds(2f / Math.Max(_overlay.Text!.Length, 1)); // Forge-Change: space biome names and descriptions

        if (_overlay.TextDescription == "")
            _overlay.CharIntervalDescription = TimeSpan.Zero; //if this is not done it tries dividing by 0 in the "else" clause
        else
            _overlay.CharIntervalDescription = TimeSpan.FromSeconds(2f / Math.Max(_overlay.TextDescription!.Length, 1)); // Forge-Change: space biome names and descriptions
    }
}
