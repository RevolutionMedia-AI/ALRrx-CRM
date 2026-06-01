namespace ALRrx.Application.DTOs;

public class SalesSummaryDto
{
    public decimal TotalSales { get; set; }
    public int TotalCount { get; set; }
    public SaleRecord? LastSale { get; set; }
    public List<SaleRecord> AllSales { get; set; } = new();
    public List<string> AvailableSellers { get; set; } = new();
    public List<string> AvailablePackages { get; set; } = new();
}
