namespace Identity.Application.DTOs;

public record AuthResponse(
    string AccessToken,
    string RefreshToken,
    string Email,
    string DisplayName,
    string Role
);