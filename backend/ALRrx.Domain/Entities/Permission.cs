namespace ALRrx.Domain.Entities;

public sealed class Permission
{
    public int Id { get; set; }
    public string KeyName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Module { get; set; } = string.Empty;
}
