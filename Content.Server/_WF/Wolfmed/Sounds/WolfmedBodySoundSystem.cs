using Content.Server.Chat.Systems;
using Content.Server.Speech.EntitySystems;
using Content.Server.Speech.Muting;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.Sounds;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Chat.Prototypes;
using Content.Shared.Humanoid;
using Content.Shared.Speech.Components;
using Robust.Server.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Wolfmed.Sounds;

/// <summary>
/// The Bobmed body sounds: the voices of the Wolfmed emotes, the rule that a machine makes no body noises, a bone's
/// crack and a tendon's snap.
/// </summary>
/// <remarks>
/// The upstream emote sound sets cannot be extended without editing each one, so the Wolfmed emotes are voiced from
/// two _WF sets chosen by sex (<see cref="MaleEmotes"/>, <see cref="FemaleEmotes"/>) whenever the body's own set
/// has nothing for them. A species that maps one of these emotes itself keeps its own sound. Every emote in those
/// sets, and the dying gasp, is flesh only: a mechanical body plays nothing for them.
/// </remarks>
public sealed class WolfmedBodySoundSystem : EntitySystem
{
    /// <summary>The Wolfmed emotes' voices for a male or unsexed body, as VocalSystem falls back to male.</summary>
    public static readonly ProtoId<EmoteSoundsPrototype> MaleEmotes = "WFWolfmedMaleEmotes";

    /// <summary>The Wolfmed emotes' voices for a female body.</summary>
    public static readonly ProtoId<EmoteSoundsPrototype> FemaleEmotes = "WFWolfmedFemaleEmotes";

    public static readonly ProtoId<SoundCollectionPrototype> CrackCollection = "WFWolfmedBoneCrack";
    public static readonly ProtoId<SoundCollectionPrototype> SnapCollection = "WFWolfmedTendonSnap";

    /// <summary>The wound whose opening snaps.</summary>
    public static readonly ProtoId<WoundPrototype> TendonWound = "WFWolfmedTendonCutWound";

    /// <summary>Upstream emotes that are flesh only, on top of everything in the Wolfmed sets.</summary>
    private static readonly HashSet<string> OrganicOnlyEmotes = new() { "Gasp" };

    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private WolfmedOrganicSoundSystem _organic = default!;
    [Dependency] private WolfmedWoundTraitSystem _traits = default!;
    [Dependency] private Life.WolfmedSpawnInjurySystem _spawnInjury = default!;

    /// <summary>Bodies that have cracked or snapped this tick, so a blast breaking four bones is one crack.</summary>
    private readonly HashSet<(EntityUid Body, string Sound)> _playedThisTick = new();

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        // After a mute has already silenced the emote, before any voice is asked for one.
        SubscribeLocalEvent<WoundHostComponent, EmoteEvent>(OnEmote,
            before: new[] { typeof(VocalSystem), typeof(MumbleAccentSystem) },
            after: new[] { typeof(MutingSystem) });
        SubscribeLocalEvent<WoundableComponent, FractureGradeChangedEvent>(OnFractureGrade);
        SubscribeLocalEvent<WolfmedWoundLifecycleEvent>(OnWoundLifecycle);
    }

    /// <inheritdoc/>
    public override void Update(float frameTime) => _playedThisTick.Clear();

    private void OnEmote(Entity<WoundHostComponent> body, ref EmoteEvent args)
    {
        if (args.Handled || !args.Emote.Category.HasFlag(EmoteCategory.Vocal))
            return;

        var wolfmed = IsWolfmedEmote(args.Emote.ID);
        if (!wolfmed && !OrganicOnlyEmotes.Contains(args.Emote.ID))
            return;

        // Swallowed here, so the species set is never asked for a body noise a chassis cannot make.
        if (!_organic.IsOrganicBody(body))
        {
            args.Handled = true;
            return;
        }

        if (!wolfmed || !TryComp(body, out VocalComponent? vocal) ||
            vocal.EmoteSounds?.Sounds.ContainsKey(args.Emote.ID) == true)
            return;

        args.Handled = _chat.TryPlayEmoteSound(body, _prototypes.Index(GetEmoteSet(body)), args.Emote);
    }

    /// <summary>Whether one of the Wolfmed sets voices this emote.</summary>
    public bool IsWolfmedEmote(string emote) =>
        _prototypes.TryIndex(MaleEmotes, out var male) && male.Sounds.ContainsKey(emote) ||
        _prototypes.TryIndex(FemaleEmotes, out var female) && female.Sounds.ContainsKey(emote);

    /// <summary>The Wolfmed set for this body's sex, read the way VocalSystem reads it.</summary>
    public ProtoId<EmoteSoundsPrototype> GetEmoteSet(EntityUid body) =>
        CompOrNull<HumanoidAppearanceComponent>(body)?.Sex == Sex.Female ? FemaleEmotes : MaleEmotes;

    /// <summary>
    /// The sound this body makes for an emote: its own species set first, then the Wolfmed set for its sex. Null for
    /// a body noise from a machine, or an emote nothing voices.
    /// </summary>
    public SoundSpecifier? GetEmoteSound(EntityUid body, string emote)
    {
        var wolfmed = IsWolfmedEmote(emote);
        if ((wolfmed || OrganicOnlyEmotes.Contains(emote)) && HasComp<WoundHostComponent>(body) &&
            !_organic.IsOrganicBody(body))
            return null;

        if (!TryComp(body, out VocalComponent? vocal))
            return null;

        if (vocal.EmoteSounds?.Sounds.TryGetValue(emote, out var own) == true)
            return own;

        return wolfmed && _prototypes.Index(GetEmoteSet(body)).Sounds.TryGetValue(emote, out var sound)
            ? sound
            : null;
    }

    private void OnFractureGrade(Entity<WoundableComponent> part, ref FractureGradeChangedEvent args)
    {
        // A new fracture comes in from None, so creation and a worsening break are the same test.
        if (args.Grade <= args.OldGrade || args.Body is not { } body || !_traits.IsOrganic(part.AsNullable()))
            return;

        PlayAtBody(body, CrackCollection);
    }

    private void OnWoundLifecycle(ref WolfmedWoundLifecycleEvent args)
    {
        if (args.Kind != WolfmedWoundLifecycle.Created || args.Prototype != TendonWound ||
            CompOrNull<BodyPartComponent>(args.Part)?.Body is not { } body || !_traits.IsOrganic(args.Part) ||
            _spawnInjury.IsApplying(body))
            return;

        PlayAtBody(body, SnapCollection);
    }

    /// <summary>
    /// Plays at the body rather than the part: a part lives in the body's container and has no position of its own.
    /// </summary>
    private void PlayAtBody(EntityUid body, ProtoId<SoundCollectionPrototype> collection)
    {
        if (TerminatingOrDeleted(body) || !_playedThisTick.Add((body, collection)))
            return;

        _audio.PlayPvs(new SoundCollectionSpecifier(collection), body, AudioParams.Default.WithVariation(0.1f));
    }
}
