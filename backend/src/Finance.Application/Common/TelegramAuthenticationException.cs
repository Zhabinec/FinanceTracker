namespace Finance.Application.Common;

public sealed class TelegramAuthenticationException(string message) : UnauthorizedAccessException(message);
