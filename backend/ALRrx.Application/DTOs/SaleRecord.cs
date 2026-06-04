namespace ALRrx.Application.DTOs;

public class SaleRecord
{
    public DateTime Timestamp { get; set; }
    public string SellerName { get; set; } = "";
    public DateTime SaleDate { get; set; }
    public string CustomerEmail { get; set; } = "";
    public string Package { get; set; } = "";
    public decimal Amount { get; set; }
}
