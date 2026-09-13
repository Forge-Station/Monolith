using Content.Shared._WF.Genitals;
using Content.Shared._WF.Genitals.Components;
using Content.Shared._WF.Genitals.Systems;
using Content.Shared.Humanoid;
using Robust.Client.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Client._WF.Genitals;

/// <summary>Fills a lobby doll's GenitalsComponent from profile data; the doll never gets organs.</summary>
/// <remarks>Both preview reload paths of the creator pass through the client LoadProfile, which raises the event.</remarks>
public sealed partial class GenitalPreviewSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private GenitalsVisualizerSystem _visualizer = default!;
    [Dependency] private SharedGenitalsSystem _genitals = default!;

    /// <summary>Creator preview mode; the creator tab sets it and then reloads the preview.</summary>
    public GenitalPreviewMode Mode { get; set; } = GenitalPreviewMode.AsWorn;

    /// <summary>Creator arousal preview; null draws the resting state.</summary>
    public ArousalState? Arousal { get; set; }

    /// <summary>
    /// Whether the creator dresses the doll in job clothes: only As worn with the creator's clothes toggle on. Underwear
    /// only and Nude show the body without clothes, because the Under set draws above the jumpsuit.
    /// </summary>
    /// <remarks>The creator passes this to LobbyUIController.LoadProfileEntity and respawns the doll when the mode changes.</remarks>
    public bool DollWearsClothes(bool showClothes)
    {
        return showClothes && Mode == GenitalPreviewMode.AsWorn;
    }

    /// <summary>Back to As worn and resting arousal, redrawing dolls already filled with the old settings.</summary>
    /// <remarks>
    /// The creator calls this on every load, save and close. A save rebuilds the lobby preview and the character list before
    /// the editor reloads, so those dolls still carry the old mode.
    /// </remarks>
    public void ResetPreview()
    {
        if (Mode == GenitalPreviewMode.AsWorn && Arousal == null)
            return;

        Mode = GenitalPreviewMode.AsWorn;
        Arousal = null;

        var query = EntityQueryEnumerator<GenitalsComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out var genitals, out var sprite))
        {
            if (!genitals.IsPreview)
                continue;

            genitals.PreviewMode = GenitalPreviewMode.AsWorn;
            genitals.PreviewArousal = null;
            _visualizer.UpdateVisuals((uid, genitals, sprite));
        }
    }

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<HumanoidAppearanceComponent, GenitalPreviewProfileLoadedEvent>(OnPreviewLoaded);
    }

    private void OnPreviewLoaded(Entity<HumanoidAppearanceComponent> ent, ref GenitalPreviewProfileLoadedEvent args)
    {
        var uid = ent.Owner;
        if (!IsClientSide(uid))
            return;

        var profile = args.Profile;

        // Organ rules only; the profile itself is never rewritten here. An ineligible character shows no anatomy, as the
        // creator shows its age or species gate instead; both clamp a typed age to the species' range, as saving does.
        var anatomy = GenitalProfileValidator.ValidateOrgans(profile.Genitals, _proto);
        if (anatomy.IsEmpty || !GenitalProfileValidator.IsEligibleClamped(profile.Age, profile.Species.Id, _proto))
        {
            RemComp<GenitalsComponent>(uid);
            return;
        }

        var settings = _genitals.Settings;
        var skin = profile.Appearance.SkinColor;
        var genitals = EnsureComp<GenitalsComponent>(uid);
        genitals.Penis = GenitalStateBuilder.FromProfile(anatomy, GenitalSlot.Penis, skin, settings);
        genitals.Testicles = GenitalStateBuilder.FromProfile(anatomy, GenitalSlot.Testicles, skin, settings);
        genitals.Vagina = GenitalStateBuilder.FromProfile(anatomy, GenitalSlot.Vagina, skin, settings);
        genitals.Womb = anatomy.Womb;
        genitals.Breasts = GenitalStateBuilder.FromProfile(anatomy, GenitalSlot.Breasts, skin, settings);
        genitals.Arousal = 0;
        genitals.RevealMode = anatomy.RevealMode;
        genitals.Visibility = GenitalStateBuilder.VisibilityFromProfile(anatomy);
        genitals.Undergarments = UndergarmentFlags.None;
        genitals.IsPreview = true;
        genitals.PreviewMode = Mode;
        genitals.PreviewArousal = Arousal;

        _visualizer.UpdateVisuals((uid, genitals, CompOrNull<SpriteComponent>(uid)));
    }
}
