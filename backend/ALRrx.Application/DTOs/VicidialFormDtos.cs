using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace ALRrx.Application.DTOs;

public enum BundleType
{
    [Display(Name = "GLP-1 1 Month")]
    Glp1_1Month,

    [Display(Name = "GLP-1 3 Months")]
    Glp1_3Months,

    [Display(Name = "GLP-1 6 Months")]
    Glp1_6Months,

    [Display(Name = "GLP-1 12 Months")]
    Glp1_12Months,

    [Display(Name = "GLP-1/GIP 1 Month")]
    Glp1Gip_1Month,

    [Display(Name = "GLP-1/GIP 3 Months")]
    Glp1Gip_3Months,

    [Display(Name = "GLP-1/GIP 6 Months")]
    Glp1Gip_6Months,

    [Display(Name = "GLP-1/GIP 12 Months")]
    Glp1Gip_12Months,
}

public sealed record VicidialAuthRequest
{
    public string Key { get; init; } = string.Empty;
}

public sealed record VicidialAuthResponse
{
    public string Token { get; init; } = string.Empty;
    public DateTime ExpiresAt { get; init; }
    public string FormName { get; init; } = "ALTRX Sales Form";
}

public sealed class VicidialSaleRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "LeadId must be greater than zero if provided")]
    public int? LeadId { get; init; }

    [Required]
    public string SalesRep { get; init; } = string.Empty;

    [Required]
    public DateTime SaleDate { get; set; }

    [Required]
    public string ClientPhone { get; init; } = string.Empty;

    [Required]
    public string ClientName { get; init; } = string.Empty;

    [Required, EmailAddress]
    public string ClientEmail { get; init; } = string.Empty;

    [Required]
    public string Bundle { get; init; } = string.Empty;

    [Required, Range(0.01, 1000000)]
    public decimal Amount { get; init; }

    [Required, Url, StringLength(2048, MinimumLength = 1)]
    public string ConfirmationUrl { get; init; } = string.Empty;
}

public sealed record VicidialSaleDto
{
    public int Id { get; init; }
    public int? LeadId { get; init; }
    public string SalesRep { get; init; } = string.Empty;
    public DateTime SaleDate { get; init; }
    public string ClientPhone { get; init; } = string.Empty;
    public string ClientName { get; init; } = string.Empty;
    public string ClientEmail { get; init; } = string.Empty;
    public string Bundle { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string? ConfirmationUrl { get; init; }
    public DateTime CreatedAt { get; init; }
}

public sealed class VicidialSaleUpdateRequest
{
    [Required]
    public string EditorEmail { get; init; } = string.Empty;

    [Range(1, int.MaxValue, ErrorMessage = "LeadId must be greater than zero")]
    public int? LeadId { get; init; }
    public DateTime? SaleDate { get; init; }
    public string? ClientPhone { get; init; }
    public string? ClientName { get; init; }
    public string? ClientEmail { get; init; }
    public string? Bundle { get; init; }
    public decimal? Amount { get; init; }
    [Url, StringLength(2048, MinimumLength = 1)]
    public string? ConfirmationUrl { get; init; }
}

public sealed record ActiveAltrxAgentDto
{
    public string User { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
}

public sealed record VicidialSaleEnrichedDto
{
    public int Id { get; init; }
    public int? LeadId { get; init; }
    public string SalesRep { get; init; } = string.Empty;
    public DateTime SaleDate { get; init; }
    public string ClientPhone { get; init; } = string.Empty;
    public string ClientName { get; init; } = string.Empty;
    public string ClientEmail { get; init; } = string.Empty;
    public string Bundle { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string? ConfirmationUrl { get; init; }
    public DateTime CreatedAt { get; init; }
    public VicidialLeadDto? Lead { get; init; }
    public bool LeadFound { get; init; }
}

public sealed record VicidialCallTypeSalesRow
{
    public string AgentId { get; init; } = string.Empty;
    public string AgentName { get; init; } = string.Empty;
    public int OutboundSales { get; init; }
    public int InboundSales { get; init; }
    public decimal OutboundPct { get; init; }
    public decimal InboundPct { get; init; }
}

public sealed record VicidialCallCountsDto
{
    public int OutboundCalls { get; init; }
    public int InboundCalls { get; init; }
    public int OutboundSales { get; init; }
    public int InboundSales { get; init; }
}

public static class BundleTypeExtensions
{
    public static string ToDisplayName(this BundleType bundle) => bundle switch
    {
        BundleType.Glp1_1Month => "GLP-1 1 Month",
        BundleType.Glp1_3Months => "GLP-1 3 Months",
        BundleType.Glp1_6Months => "GLP-1 6 Months",
        BundleType.Glp1_12Months => "GLP-1 12 Months",
        BundleType.Glp1Gip_1Month => "GLP-1/GIP 1 Month",
        BundleType.Glp1Gip_3Months => "GLP-1/GIP 3 Months",
        BundleType.Glp1Gip_6Months => "GLP-1/GIP 6 Months",
        BundleType.Glp1Gip_12Months => "GLP-1/GIP 12 Months",
        _ => bundle.ToString()
    };

    // A phone AI agent does not reliably emit the canonical display string:
    // it writes "glp-1 3-month", "GLP1 3mo", "GLP-1/GIP 3 meses". The previous
    // switch only matched 8 hardcoded spellings and 400'd everything else, so
    // the caller had to guess. This normalizes separators away and then
    // matches the (drug, gip?, months, unit?) shape, tolerating the unit being
    // singular, plural, abbreviated, in Spanish, or absent entirely.
    private static readonly Regex BundleRegex = new(
        @"^(?<drug>glp1|glpone)(?<gip>gip)?(?<months>1|3|6|12)(?<unit>months?|mos?|meses|mes|m)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool TryParseBundle(string? input, out BundleType result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(input)) return false;

        var normalized = input.Trim().ToLowerInvariant()
            .Replace(" ", "").Replace("-", "").Replace("_", "").Replace("/", "").Replace(".", "");
        var match = BundleRegex.Match(normalized);
        if (!match.Success) return false;

        var gip = match.Groups["gip"].Success;
        result = (gip, match.Groups["months"].Value) switch
        {
            (false, "1") => BundleType.Glp1_1Month,
            (false, "3") => BundleType.Glp1_3Months,
            (false, "6") => BundleType.Glp1_6Months,
            (false, "12") => BundleType.Glp1_12Months,
            (true, "1") => BundleType.Glp1Gip_1Month,
            (true, "3") => BundleType.Glp1Gip_3Months,
            (true, "6") => BundleType.Glp1Gip_6Months,
            (true, "12") => BundleType.Glp1Gip_12Months,
            // Unreachable: the regex only admits 1/3/6/12.
            _ => BundleType.Glp1_1Month,
        };
        return true;
    }
}
