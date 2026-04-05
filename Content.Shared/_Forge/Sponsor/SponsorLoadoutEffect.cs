using System.Diagnostics.CodeAnalysis;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Preferences.Loadouts.Effects;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Shared._Forge.Sponsor;

public sealed partial class SponsorLoadoutEffect : LoadoutEffect
{
    [DataField(required: true)]
    public SponsorLevel Level;

    public override bool Validate(
        HumanoidCharacterProfile profile,
        RoleLoadout loadout,
        ICommonSession? session,
        IDependencyCollection collection,
        [NotNullWhen(false)] out FormattedMessage? reason)
    {
        reason = new FormattedMessage();

        if (session == null)
            return true;

        SponsorData.SponsorNames.TryGetValue(Level, out var name);
        SponsorData.SponsorColor.TryGetValue(Level, out var color);

        var sponsorManager = collection.Resolve<ISharedSponsorManager>();

        if (!sponsorManager.TryGetSponsor(session.UserId, out var sponsor))
        {
            reason = FormattedMessage.FromMarkupPermissive(
                Loc.GetString("loadout-requirement-sponsor-level",
                    ("level", name!),
                    ("color", color!)));
            return false;
        }

        if (sponsor >= Level)
            return true;

        reason = FormattedMessage.FromMarkupPermissive(
            Loc.GetString("loadout-requirement-sponsor-level",
                ("level", name!),
                ("color", color!)));

        return false;

    }
}
