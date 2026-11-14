using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Preferences.Loadouts.Effects;
using Content.Shared.Roles;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._WF.RMCClothing;

/// <summary>Allows a loadout only in the listed role loadouts, such as officer armor in a shared armor group.</summary>
public sealed partial class WFRoleWhitelistLoadoutEffect : LoadoutEffect
{
    [DataField(required: true)]
    public List<ProtoId<RoleLoadoutPrototype>> Roles = new();

    public override bool Validate(HumanoidCharacterProfile profile, RoleLoadout loadout, ICommonSession? session,
        IDependencyCollection collection, [NotNullWhen(false)] out FormattedMessage? reason)
    {
        if (Roles.Contains(loadout.Role))
        {
            reason = null;
            return true;
        }

        var protos = collection.Resolve<IPrototypeManager>();
        var names = Roles.Select(r =>
            protos.TryIndex<JobPrototype>(r.Id.StartsWith("Job") ? r.Id[3..] : r.Id, out var job)
                ? job.LocalizedName
                : r.Id);
        reason = FormattedMessage.FromUnformatted(Loc.GetString("wf-role-whitelist-loadout-invalid",
            ("roles", string.Join(", ", names))));
        return false;
    }
}
