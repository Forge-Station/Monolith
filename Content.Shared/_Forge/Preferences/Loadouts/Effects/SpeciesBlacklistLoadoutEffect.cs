using System.Diagnostics.CodeAnalysis;
using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared.Preferences.Loadouts.Effects;

/// <summary>
/// Validates that a loadout cannot be used by blocked species.
/// </summary>
public sealed partial class SpeciesBlacklistLoadoutEffect : LoadoutEffect
{
    [DataField("blacklist", required: true)]
    public List<ProtoId<SpeciesPrototype>> Blacklist = new();

    public override bool Validate(HumanoidCharacterProfile profile, RoleLoadout loadout, ICommonSession? session, IDependencyCollection collection,
        [NotNullWhen(false)] out FormattedMessage? reason)
    {
        if (Blacklist.Contains(profile.Species))
        {
            reason = FormattedMessage.FromUnformatted(Loc.GetString("loadout-group-species-restriction"));
            return false;
        }

        reason = null;
        return true;
    }
}
