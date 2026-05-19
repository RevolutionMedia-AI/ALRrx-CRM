namespace ALRrx.Application.DTOs;

public sealed record TimeFilterDto
{
    public string Period { get; init; } = "Today";
    public DateTime? CustomStart { get; init; }
    public DateTime? CustomEnd { get; init; }
}
