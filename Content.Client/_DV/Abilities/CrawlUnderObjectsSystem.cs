using Content.Shared._DV.Abilities;
using Robust.Client.GameObjects;
using DrawDepth = Content.Shared.DrawDepth.DrawDepth;

namespace Content.Client._DV.Abilities;

/// <summary>
/// Client half of sneaking: drops the sprite to the depth mice use while the appearance data says the mob is
/// sneaking, and puts the old depth back afterwards.
/// </summary>
public sealed partial class HideUnderTableAbilitySystem : SharedCrawlUnderObjectsSystem
{
    [Dependency] private SpriteSystem _sprite = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CrawlUnderObjectsComponent, AppearanceChangeEvent>(OnAppearanceChange);
    }

    private void OnAppearanceChange(Entity<CrawlUnderObjectsComponent> ent, ref AppearanceChangeEvent args)
    {
        if (args.Sprite is not { } sprite || !args.AppearanceData.TryGetValue(SneakMode.Enabled, out var value))
            return;

        if (value is true)
        {
            if (ent.Comp.OriginalDrawDepth != null)
                return;

            ent.Comp.OriginalDrawDepth = sprite.DrawDepth;
            _sprite.SetDrawDepth((ent.Owner, sprite), (int) DrawDepth.SmallMobs);
        }
        else
        {
            if (ent.Comp.OriginalDrawDepth is not { } original)
                return;

            _sprite.SetDrawDepth((ent.Owner, sprite), original);
            ent.Comp.OriginalDrawDepth = null;
        }
    }
}
