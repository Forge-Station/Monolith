using System.Globalization;
using Content.Server.Chat.Managers;
using Content.Shared.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Forge.KIAS;

[TestFixture]
public sealed class KiasAnnouncementSanitizationTests
{
    [Test]
    public async Task RepeatedAnnouncementsKeepTextBoundariesAndFollowCultureChanges()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var map = await pair.CreateTestMap();
        var em = pair.Server.ResolveDependency<IEntityManager>();
        var config = pair.Server.ResolveDependency<IConfigurationManager>();
        var sanitizer = pair.Server.ResolveDependency<IChatSanitizationManager>();
        await pair.Server.WaitAssertion(() =>
        {
            var culture = CultureInfo.CurrentCulture;
            var enabled = config.GetCVar(CCVars.ChatSanitizerEnabled);
            var speaker = em.SpawnEntity("MobHuman", map.GridCoords);
            try
            {
                config.SetCVar(CCVars.ChatSanitizerEnabled, true);
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
                Assert.That(sanitizer.TrySanitizeEmoteShorthands("lol, warning :)", speaker, out var text, out var last), Is.True);
                Assert.That(text, Is.EqualTo("warning"));
                Assert.That(sanitizer.TrySanitizeEmoteShorthands(":)", speaker, out _, out var smile), Is.True);
                Assert.That(last, Is.EqualTo(smile));
                for (var i = 0; i < 32; i++)
                {
                    Assert.That(sanitizer.TrySanitizeEmoteShorthands("Airlock IDK sealed", speaker, out text, out _), Is.True);
                    Assert.That(text, Is.EqualTo("Airlock sealed"));
                    Assert.That(sanitizer.TrySanitizeEmoteShorthands("lollipop idkInside", speaker, out text, out _), Is.False);
                    Assert.That(text, Is.EqualTo("lollipop idkInside"));
                }
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
                Assert.That(sanitizer.TrySanitizeEmoteShorthands("IDK", speaker, out text, out _), Is.False);
                Assert.That(text, Is.EqualTo("IDK"));
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
                Assert.That(sanitizer.TrySanitizeEmoteShorthands("IDK", speaker, out text, out _), Is.True);
                Assert.That(text, Is.Empty);
                config.SetCVar(CCVars.ChatSanitizerEnabled, false);
                Assert.That(sanitizer.TrySanitizeEmoteShorthands("  lol :)  ", speaker, out text, out var disabledEmote), Is.False);
                Assert.That(text, Is.EqualTo("  lol :)  "));
                Assert.That(disabledEmote, Is.Null);
            }
            finally
            {
                CultureInfo.CurrentCulture = culture;
                config.SetCVar(CCVars.ChatSanitizerEnabled, enabled);
            }
        });
        await pair.CleanReturnAsync();
    }
}
