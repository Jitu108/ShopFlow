import { UserRole } from '../auth/auth.models';

// Matches Identity.Application.DTOs.UserProfileDto (camelCase over the
// wire) — note isEmailVerified here, not emailVerified as in the JWT claim.
export interface UserProfile {
  id: string;
  email: string;
  displayName: string;
  role: UserRole;
  isEmailVerified: boolean;
}
