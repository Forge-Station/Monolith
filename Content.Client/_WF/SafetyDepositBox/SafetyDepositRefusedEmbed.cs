using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Client.Guidebook.Richtext;
using Content.Shared.Storage;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client._WF.SafetyDepositBox;

/// <summary>Guidebook list of what a safety deposit box refuses, read from the box prototype's storage blacklist.</summary>
/// <remarks>
/// A component or tag with no <c>safety-deposit-refused-component-*</c> or <c>safety-deposit-refused-tag-*</c> string
/// is listed under its raw name, so a new blacklist entry shows up without touching the guide.
/// </remarks>
[UsedImplicitly]
public sealed partial class SafetyDepositRefusedEmbed : BoxContainer, IDocumentTag
{
    private const string BoxProto = "SafetyDepositBoxSmall";

    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private IComponentFactory _factory = default!;

    public SafetyDepositRefusedEmbed()
    {
        IoCManager.InjectDependencies(this);
        Orientation = LayoutOrientation.Vertical;
    }

    public bool TryParseTag(Dictionary<string, string> args, [NotNullWhen(true)] out Control? control)
    {
        control = null;

        if (!_prototype.TryIndex<EntityPrototype>(BoxProto, out var proto) ||
            !proto.TryComp<StorageComponent>(out var storage, _factory) ||
            storage.Blacklist is not { } blacklist)
        {
            IoCManager.Resolve<ILogManager>().GetSawmill("wf.safetydeposit").Error($"Safety deposit refused list: {BoxProto} has no storage blacklist");
            return false;
        }

        var lines = new List<string>();
        foreach (var name in blacklist.Components ?? Array.Empty<string>())
        {
            lines.Add(LocName("safety-deposit-refused-component-", name));
        }

        foreach (var tag in blacklist.Tags ?? new List<ProtoId<Shared.Tag.TagPrototype>>())
        {
            lines.Add(LocName("safety-deposit-refused-tag-", tag.Id));
        }

        foreach (var line in lines.Where(l => !string.IsNullOrEmpty(l)).Distinct().Order())
        {
            AddChild(new Label { Text = $"  - {line}" });
        }

        control = this;
        return true;
    }

    private static string LocName(string prefix, string id)
    {
        return IoCManager.Resolve<ILocalizationManager>().TryGetString(prefix + id.ToLowerInvariant(), out var text) ? text : id;
    }
}
