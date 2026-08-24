export interface LoginRequest {
  email: string;
  password: string;
}

export interface RegisterRequest {
  email: string;
  password: string;
  displayName: string;
}

// Matches Identity.Application.DTOs.AuthResponse (camelCase over the wire).
export interface AuthApiResponse {
  accessToken: string;
  refreshToken: string;
  email: string;
  displayName: string;
  role: string;
}

export type UserRole = 'Customer' | 'Vendor' | 'Admin';

export interface AuthUser {
  userId: string;
  email: string;
  displayName: string;
  role: UserRole;
  emailVerified: boolean;
}
