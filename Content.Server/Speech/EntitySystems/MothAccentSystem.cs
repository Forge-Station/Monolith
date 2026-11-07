using System.Text.RegularExpressions;
using Content.Server.Speech.Components;

namespace Content.Server.Speech.EntitySystems;

public sealed class MothAccentSystem : EntitySystem
{
    private static readonly Regex RegexLowerBuzz = new Regex("z{1,3}");
    private static readonly Regex RegexUpperBuzz = new Regex("Z{1,3}");
    private static readonly Regex RegexLowerBuzzRu = new Regex("з{1,3}"); //Forge-Change-localization
    private static readonly Regex RegexUpperBuzzRu = new Regex("З{1,3}"); //Forge-Change-localization

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MothAccentComponent, AccentGetEvent>(OnAccent);
    }

    private void OnAccent(EntityUid uid, MothAccentComponent component, AccentGetEvent args)
    {
        var message = args.Message;

        // buzzz
        message = RegexLowerBuzz.Replace(message, "zzz");
        // buZZZ
        message = RegexUpperBuzz.Replace(message, "ZZZ");

        // бззз
        message = RegexLowerBuzzRu.Replace(message, "ззз"); //Forge-Change-localization
        // бЗЗЗ
        message = RegexUpperBuzzRu.Replace(message, "ЗЗЗ"); //Forge-Change-localization

        args.Message = message;
    }
}
