namespace Identity.Application.DTOs;

public record UserProfileDto(
    Guid Id,
    string Email,
    string DisplayName,
    string Role,
    bool IsEmailVerified
);