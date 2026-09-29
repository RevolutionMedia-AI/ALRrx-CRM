using System.Reflection;
using System.Text.Json;
using ALRrx.Application.DTOs;
using ALRrx.Application.UseCases;

namespace ALRrx.Application.Tests;

// Covers the two non-trivial parsers behind POST /api/vicidial-form/sale, which
// an automated phone agent calls: bundle spellings and sale-date normalization.
public class VicidialSaleInputTests
{
    [Theory]
    // Canonical display names.
    [InlineData("GLP-1 1 Month", BundleType.Glp1_1Month)]
    [InlineData("GLP-1 3 Months", BundleType.Glp1_3Months)]
    [InlineData("GLP-1 6 Months", BundleType.Glp1_6Months)]
    [InlineData("GLP-1 12 Months", BundleType.Glp1_12Months)]
    [InlineData("GLP-1/GIP 1 Month", BundleType.Glp1Gip_1Month)]
    [InlineData("GLP-1/GIP 3 Months", BundleType.Glp1Gip_3Months)]
    [InlineData("GLP-1/GIP 6 Months", BundleType.Glp1Gip_6Months)]
    [InlineData("GLP-1/GIP 12 Months", BundleType.Glp1Gip_12Months)]
    // What an LLM actually emits.
    [InlineData("glp-1 3-month", BundleType.Glp1_3Months)]
    [InlineData("GLP1 3mo", BundleType.Glp1_3Months)]
    [InlineData("GLP-1 3M", BundleType.Glp1_3Months)]
    [InlineData("glp 1 6 month", BundleType.Glp1_6Months)]
    [InlineData("GLP-1/GIP 3 meses", BundleType.Glp1Gip_3Months)]
    [InlineData("  glp1gip12month  ", BundleType.Glp1Gip_12Months)]
    [InlineData("glp112", BundleType.Glp1_12Months)]
    public void TryParseBundle_accepts_agent_spellings(string input, BundleType expected)
    {
        Assert.True(BundleTypeExtensions.TryParseBundle(input, out var actual));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("GLP-1")]
    [InlineData("GLP-1 4 Months")]
    [InlineData("Ozempic 3 Months")]
    [InlineData("Semaglutide 3 Months")]
    public void TryParseBundle_rejects_unknown(string? input)
    {
        Assert.False(BundleTypeExtensions.TryParseBundle(input, out _));
    }

    // 2026-09-29 21:35 UTC is 14:35 in America/Tijuana (UTC-7). Storing the UTC
    // wall clock would file the sale on the wrong day for the 21:00-24:00 band.
    [Theory]
    [InlineData("2026-09-29T21:35:00Z", "2026-09-29T14:35:00")]
    [InlineData("2026-09-29T23:59:00Z", "2026-09-29T16:59:00")]
    [InlineData("2026-10-01T05:00:00Z", "2026-09-30T22:00:00")]
    public void SaleDate_with_offset_lands_on_business_day(string json, string expected)
    {
        var parsed = JsonSerializer.Deserialize<DateTime>($"\"{json}\"");
        var stored = ToBusinessWallClock(parsed);
        Assert.Equal(DateTimeKind.Unspecified, stored.Kind);
        Assert.Equal(DateTime.Parse(expected), stored);
    }

    // The human form sends a bare wall clock. It must pass through untouched.
    [Fact]
    public void SaleDate_without_offset_passes_through()
    {
        var parsed = JsonSerializer.Deserialize<DateTime>("\"2026-09-29T14:35:00\"");
        Assert.Equal(DateTimeKind.Unspecified, parsed.Kind);
        Assert.Equal(parsed, ToBusinessWallClock(parsed));
    }

    private static DateTime ToBusinessWallClock(DateTime saleDate) =>
        (DateTime)typeof(SubmitVicidialSaleUseCase)
            .GetMethod("ToBusinessWallClock", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { saleDate })!;

    private static readonly List<ActiveAltrxAgentDto> ActiveAgents = new()
    {
        new() { User = "kevin.escalante", FullName = "Kevin Escalante" },
        new() { User = "jessica.duarte", FullName = "Jessica Duarte" },
    };

    [Theory]
    [InlineData("Kevin Escalante")]   // full name, as the human form sends
    [InlineData("kevin.escalante")]   // username fallback
    [InlineData("KEVIN ESCALANTE")]   // case-insensitive
    [InlineData("  Kevin Escalante ")]
    public void IsKnownAgent_accepts_active_agent(string salesRep)
        => Assert.True(ActiveAgentMatcher.IsKnownAgent(ActiveAgents, salesRep));

    [Theory]
    [InlineData("Not An Agent")]
    [InlineData("Kevin Escalante Jr")]
    [InlineData("kevin")]             // partial match must not pass
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void IsKnownAgent_rejects_others(string? salesRep)
        => Assert.False(ActiveAgentMatcher.IsKnownAgent(ActiveAgents, salesRep));

    // A deactivated Vicidial user drops out of the list, so the same name
    // stops validating.
    [Fact]
    public void IsKnownAgent_rejects_agent_no_longer_active()
    {
        var stillActive = new List<ActiveAltrxAgentDto>
        {
            new() { User = "jessica.duarte", FullName = "Jessica Duarte" },
        };
        Assert.False(ActiveAgentMatcher.IsKnownAgent(stillActive, "Kevin Escalante"));
    }
}
