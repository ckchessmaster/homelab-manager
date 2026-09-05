namespace ControlPlane.Api.Storage.Entities;

/// <summary>
/// Key-value system configuration store for infrastructure adapters and runtime settings.
/// </summary>
public class SystemSetting
{
    public string Key { get; set; } = string.Empty;

    public string ValueJson { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
