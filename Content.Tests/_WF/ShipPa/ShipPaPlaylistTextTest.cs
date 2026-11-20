using System.Linq;
using Content.Server._WF.ShipPa;
using NUnit.Framework;

namespace Content.Tests._WF.ShipPa;

[TestFixture]
[TestOf(typeof(ShipPaPlaylistText))]
public sealed class ShipPaPlaylistTextTest
{
    [TestCase("https://www.youtube.com/watch?v=abc", "youtube.com/watch?v=abc")]
    [TestCase("https://user:secret@example.com:443/a/b#frag", "example.com/a/b")]
    [TestCase("https://example.com/", "example.com")]
    [TestCase("not a link", "not a link")]
    public void LinksLoseTheirSchemeLoginAndFragment(string url, string expected)
    {
        Assert.That(ShipPaPlaylistText.ShortenLink(url), Is.EqualTo(expected));
    }

    [Test]
    public void LongTextIsCutWithAnEllipsis()
    {
        var label = ShipPaPlaylistText.Label(new string('a', 200));

        Assert.That(label, Has.Length.EqualTo(ShipPaPlaylistText.MaxLabelLength));
        Assert.That(label, Does.EndWith("..."));
    }

    [Test]
    public void ShortTextIsKept()
    {
        Assert.That(ShipPaPlaylistText.Label("A short title"), Is.EqualTo("A short title"));
    }

    [TestCase("a\r\nb", "a b")]
    [TestCase("  x \t y  ", "x y")]
    [TestCase("a\u0007b", "a b")]
    public void WhitespaceAndControlCharactersBecomeOneSpace(string text, string expected)
    {
        Assert.That(ShipPaPlaylistText.Label(text), Is.EqualTo(expected));
    }

    [Test]
    public void InvisibleCharactersAreRemoved()
    {
        Assert.That(ShipPaPlaylistText.Label("ab\u202ecd\u200b"), Is.EqualTo("abcd"));
    }

    [Test]
    public void ACutNeverSplitsASurrogatePair()
    {
        var label = ShipPaPlaylistText.Label(string.Concat(Enumerable.Repeat("\U0001F600", 40)));

        Assert.That(label, Does.EndWith("..."));

        var body = label[..^3];
        Assert.That(body, Is.Not.Empty);
        Assert.That(char.IsHighSurrogate(body[^1]), Is.False);
    }
}
