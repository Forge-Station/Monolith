using Content.Shared._Forge.Weapons.ChainsawShield;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.Projectiles;
using Robust.Shared.Audio.Systems;

namespace Content.Server._Forge.Weapons.ChainsawShield;

public sealed partial class ToggleableEmbedSoundSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ToggleableEmbedSoundComponent, EmbedEvent>(OnEmbed);
    }

    private void OnEmbed(EntityUid uid, ToggleableEmbedSoundComponent component, ref EmbedEvent args)
    {
        var sound = TryComp<ItemToggleComponent>(uid, out var toggle) && toggle.Activated
            ? component.ActiveSound
            : component.InactiveSound;

        _audio.PlayPvs(sound, uid);
    }
}
