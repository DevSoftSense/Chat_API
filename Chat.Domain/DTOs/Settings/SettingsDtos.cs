namespace Chat.Domain.DTOs.Settings;

public sealed class ChatSettingsDto
{
    public SettingsProfileDto Profile { get; set; } = new();
    public MessageDefaultsDto MessageDefaults { get; set; } = new();
    public NotificationSettingsDto Notifications { get; set; } = new();
    public StatusSettingsDto Status { get; set; } = new();
    public PrivacySettingsDto Privacy { get; set; } = new();
    public StorageSettingsDto Storage { get; set; } = new();
    public List<IntegrationSettingsDto> Integrations { get; set; } = [];
}

public sealed class SettingsProfileDto
{
    public int EmployeeId { get; set; }
    public long UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? PhotoUrl { get; set; }
    public string Role { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Timezone { get; set; } = string.Empty;
    public string Language { get; set; } = "en";
    public string DateFormat { get; set; } = "DD MMM YYYY";
}

public sealed class MessageTypeOptionDto
{
    public int MessageTypeId { get; set; }
    public string TypeName { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public sealed class MessageDefaultsDto
{
    public List<MessageTypeOptionDto> MessageTypes { get; set; } = [];
    public int DefaultMessageTypeId { get; set; }
    public bool RequestReadReceipt { get; set; } = true;
    public bool AutoSaveDraft { get; set; }
}

public sealed class NotificationSettingsDto
{
    public bool MessageReceived { get; set; } = true;
    public bool GroupMessages { get; set; } = true;
    public bool Announcements { get; set; } = true;
    public bool SoundEnabled { get; set; } = true;
    public bool DesktopEnabled { get; set; } = true;
    public bool EmailEnabled { get; set; }
    public bool MobilePushEnabled { get; set; } = true;
}

public sealed class StatusSettingsDto
{
    public string Status { get; set; } = "Online";
    public string StatusMessage { get; set; } = string.Empty;
}

public sealed class PrivacySettingsDto
{
    public string WhoCanMessage { get; set; } = "Everyone";
    public string ShowLastSeen { get; set; } = "ContactsOnly";
    public bool BlockListAvailable { get; set; }
}

public sealed class StorageSettingsDto
{
    public long UsedBytes { get; set; }
    public decimal UsedMB { get; set; }
    public int LimitMB { get; set; }
    public decimal Percentage { get; set; }
}

public sealed class IntegrationSettingsDto
{
    public int ProviderId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset? ConnectedOn { get; set; }
    public DateTimeOffset? LastSyncedOn { get; set; }
}

public sealed class UpdateProfileSettingsRequest
{
    public string? FullName { get; set; }
    public string? Phone { get; set; }
    public string? PhotoUrl { get; set; }
    public string? Language { get; set; }
    public string? DateFormat { get; set; }
}

public sealed class UpdateStatusSettingsRequest
{
    public string Status { get; set; } = "Online";
    public string? StatusMessage { get; set; }
}

public sealed class UpdateMessageDefaultsRequest
{
    public int DefaultMessageTypeId { get; set; }
    public bool RequestReadReceipt { get; set; }
    public bool AutoSaveDraft { get; set; }
}

public sealed class UpdateNotificationSettingsRequest
{
    public bool MessageReceived { get; set; }
    public bool GroupMessages { get; set; }
    public bool Announcements { get; set; }
    public bool SoundEnabled { get; set; }
    public bool DesktopEnabled { get; set; }
    public bool EmailEnabled { get; set; }
    public bool MobilePushEnabled { get; set; }
}

public sealed class UpdatePrivacySettingsRequest
{
    public string WhoCanMessage { get; set; } = "Everyone";
    public string ShowLastSeen { get; set; } = "ContactsOnly";
}
