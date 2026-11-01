using Content.Shared._WF.Companies;
using NUnit.Framework;

namespace Content.Tests._WF.Companies;

/// <summary>Covers payroll shares, cuts, rank order and name rules.</summary>
[TestFixture]
public sealed class WFCompanyPolicyTest
{
    [TestCase(1000, 600, 100)]
    [TestCase(600, 600, 100)]
    [TestCase(300, 600, 50)]
    [TestCase(0, 600, 0)]
    [TestCase(100, 0, 100)]
    public void PayrollSharesShortfallsEvenly(long balance, long due, int expected)
    {
        Assert.That(WFCompanyPolicy.PayrollShare(balance, due), Is.EqualTo(expected));
    }

    [TestCase(1000, 10, 100)]
    [TestCase(999, 10, 99)]
    [TestCase(1000, 0, 0)]
    [TestCase(1000, 90, 500)]
    [TestCase(-5, 10, 0)]
    public void CutsRoundDownAndCap(int amount, int cut, int expected)
    {
        Assert.That(WFCompanyPolicy.CutOf(amount, cut), Is.EqualTo(expected));
    }

    [Test]
    public void OnlyHigherRanksActOnLowerOnes()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WFCompanyPolicy.Outranks(null, 0), Is.True, "The owner outranks every rank.");
            Assert.That(WFCompanyPolicy.Outranks(0, null), Is.False, "Nobody outranks the owner.");
            Assert.That(WFCompanyPolicy.Outranks(0, 1), Is.True);
            Assert.That(WFCompanyPolicy.Outranks(1, 1), Is.False);
            Assert.That(WFCompanyPolicy.Outranks(2, 1), Is.False);
        });
    }

    [Test]
    public void NamesAndTagsAreNormalized()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WFCompanyPolicy.TryNormalizeName("  Hawkline   Salvage ", out var name), Is.True);
            Assert.That(name, Is.EqualTo("Hawkline Salvage"));
            Assert.That(WFCompanyPolicy.TryNormalizeName("<b>bad</b>", out _), Is.False);
            Assert.That(WFCompanyPolicy.TryNormalizeTag(" hawk ", out var tag), Is.True);
            Assert.That(tag, Is.EqualTo("HAWK"));
            Assert.That(WFCompanyPolicy.TryNormalizeTag("H", out _), Is.False);
            Assert.That(WFCompanyPolicy.TryNormalizeTag("HAWKEYE", out _), Is.False);
            Assert.That(WFCompanyPolicy.WithinWithdrawCap(false, 300, 200, 100), Is.True);
            Assert.That(WFCompanyPolicy.WithinWithdrawCap(false, 300, 200, 101), Is.False);
            Assert.That(WFCompanyPolicy.WithinWithdrawCap(true, 0, 0, 1000000), Is.True);
        });
    }

    [Test]
    public void ApplicationsArePlainLines()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WFCompanyPolicy.TryNormalizeApplication(" I fly.\r\nI fix. ", out var message), Is.True);
            Assert.That(message, Is.EqualTo("I fly.\nI fix."));
            Assert.That(WFCompanyPolicy.TryNormalizeApplication(null, out message), Is.True);
            Assert.That(message, Is.Empty);
            Assert.That(WFCompanyPolicy.TryNormalizeApplication(new string('a', WFCompanyPolicy.MaximumApplicationLength + 1), out _), Is.False);
            Assert.That(WFCompanyPolicy.TryNormalizeApplication("bell\u0007", out _), Is.False);
        });
    }
}
