using System.Diagnostics.CodeAnalysis;
using System.Text;
using Content.Shared._EE.Contractors.Prototypes;
using Content.Shared.Preferences;
using JetBrains.Annotations;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared.Roles;

[UsedImplicitly]
[Serializable, NetSerializable]
public sealed partial class NationalityRequirement : JobRequirement
{
    [DataField(required: true)]
    public HashSet<ProtoId<NationalityPrototype>> Nationalities = new();

    public override bool Check(
        IEntityManager entManager,
        IPrototypeManager protoManager,
        HumanoidCharacterProfile? profile,
        IReadOnlyDictionary<string, TimeSpan> playTimes,
        [NotNullWhen(false)] out FormattedMessage? reason)
    {
        reason = new FormattedMessage();

        if (profile is null)
            return true;

        var profileNational = new ProtoId<NationalityPrototype>(profile.Nationality);

        var sb = new StringBuilder();
        sb.Append("[color=yellow]");

        foreach (var nat in Nationalities)
        {
            sb.Append(Loc.GetString(protoManager.Index(nat).NameKey) + " ");
        }

        sb.Append("[/color]");

        if (!Inverted)
        {
            reason = FormattedMessage.FromMarkupPermissive(
                $"{Loc.GetString("role-timer-whitelisted-nationality")}\n{sb}");

            if (!Nationalities.Contains(profileNational))
                return false;
        }
        else
        {
            reason = FormattedMessage.FromMarkupPermissive(
                $"{Loc.GetString("role-timer-blacklisted-nationality")}\n{sb}");

            if (Nationalities.Contains(profileNational))
                return false;
        }
        return true;
    }
}
