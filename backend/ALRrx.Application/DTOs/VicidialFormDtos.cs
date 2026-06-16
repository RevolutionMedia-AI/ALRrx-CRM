using System.ComponentModel.DataAnnotations;

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
    public DateTime SaleDate { get; init; }

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

    public static bool TryParseBundle(string input, out BundleType result)
    {
        result = default;
        var normalized = input?.Trim().ToLowerInvariant().Replace(" ", "").Replace("-", "").Replace("/", "") ?? "";
        return normalized switch
        {
            "glp11month" => Assign(result = BundleType.Glp1_1Month),
            "glp13months" => Assign(result = BundleType.Glp1_3Months),
            "glp16months" => Assign(result = BundleType.Glp1_6Months),
            "glp112months" => Assign(result = BundleType.Glp1_12Months),
            "glp1gip1month" => Assign(result = BundleType.Glp1Gip_1Month),
            "glp1gip3months" => Assign(result = BundleType.Glp1Gip_3Months),
            "glp1gip6months" => Assign(result = BundleType.Glp1Gip_6Months),
            "glp1gip12months" => Assign(result = BundleType.Glp1Gip_12Months),
            _ => false
        };
    }

    private static bool Assign(BundleType _) => true;
}
