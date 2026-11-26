using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using JetBrains.Annotations;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared._WF.Biotype;

/// <summary>Requires the character's effective biotype to be, or with <c>inverted</c> not to be, one of those listed.</summary>
[UsedImplicitly]
[Serializable, NetSerializable]
public sealed partial class WFBiotypeRequirement : JobRequirement
{
    [DataField(required: true)]
    public HashSet<WFBiotype> Biotypes = new();

    public override bool Check(IEntityManager entManager,
        IPrototypeManager protoManager,
        HumanoidCharacterProfile? profile,
        IReadOnlyDictionary<string, TimeSpan> playTimes,
        [NotNullWhen(false)] out FormattedMessage? reason)
    {
        var names = string.Join(" ", Biotypes.Select(b => Loc.GetString(WFBiotypeGates.NameKey(b))));
        var key = Inverted ? "wf-biotype-requirement-blacklisted" : "wf-biotype-requirement-whitelisted";
        reason = FormattedMessage.FromMarkupPermissive($"{Loc.GetString(key)}\n[color=yellow]{names}[/color]");

        // A null profile is a ghost role; don't block it.
        if (profile is null)
            return true;

        return Biotypes.Contains(WFBiotypeGates.Effective(profile, protoManager)) != Inverted;
    }
}
