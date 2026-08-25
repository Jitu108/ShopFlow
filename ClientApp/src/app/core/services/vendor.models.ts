// Matches Identity.Application.DTOs.VendorSummaryDto (camelCase over the
// wire) — deliberately just id + displayName, from the public /api/vendors
// lookup endpoint.
export interface VendorSummary {
  id: string;
  displayName: string;
}
