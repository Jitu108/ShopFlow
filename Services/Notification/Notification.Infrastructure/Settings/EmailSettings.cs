namespace Notification.Infrastructure.Settings;

public class EmailSettings
{
    public const string SectionName = "EmailSettings";

    public string Host { get; init; } = string.Empty;
    public int Port { get; init; }
    public string From { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
}
