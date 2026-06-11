namespace ALRrx.Application.DTOs;

public class TwilioSummaryDto
{
    public decimal TotalCost { get; set; }
    public int TotalCalls { get; set; }
    public int TotalMinutes { get; set; }
    public decimal InboundCost { get; set; }
    public decimal OutboundCost { get; set; }
    public decimal RecordingCost { get; set; }
    public int InboundCalls { get; set; }
    public int OutboundCalls { get; set; }
    public string Currency { get; set; } = "USD";
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public DateTime LastUpdated { get; set; }
}

public class TwilioCallDto
{
    public string Sid { get; set; } = "";
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string Status { get; set; } = "";
    public string Direction { get; set; } = "";
    public int DurationSeconds { get; set; }
    public decimal Cost { get; set; }
    public string Currency { get; set; } = "USD";
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public bool HasRecording { get; set; }
}

public class TwilioDailyCostDto
{
    public DateTime Date { get; set; }
    public decimal Cost { get; set; }
    public int CallCount { get; set; }
    public int Minutes { get; set; }
}
