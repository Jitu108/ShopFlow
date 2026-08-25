namespace Identity.Application.DTOs;

// Deliberately just id + displayName — this is returned from a public,
// unauthenticated endpoint (customers browsing the catalog need vendor
// names), so it must never carry email, role, or anything else from
// UserProfileDto.
public record VendorSummaryDto(
    Guid Id,
    string DisplayName
);
