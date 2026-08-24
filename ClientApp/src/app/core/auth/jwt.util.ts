import { UserRole } from './auth.models';

// Identity.Infrastructure.Jwt.TokenService issues ClaimTypes.Email/Role, which
// serialize as these full URIs in the JWT payload, not plain "email"/"role" keys.
const EMAIL_CLAIM = 'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress';
const ROLE_CLAIM = 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role';

export interface DecodedToken {
  userId: string;
  email: string;
  role: UserRole;
  emailVerified: boolean;
  exp: number; // seconds since epoch
}

export function decodeJwt(token: string): DecodedToken | null {
  try {
    const payload = token.split('.')[1];
    const base64 = payload.replace(/-/g, '+').replace(/_/g, '/');
    const json = JSON.parse(atob(base64)) as Record<string, unknown>;
    return {
      userId: String(json['userId']),
      email: String(json[EMAIL_CLAIM]),
      role: json[ROLE_CLAIM] as UserRole,
      emailVerified: json['emailVerified'] === 'true',
      exp: Number(json['exp']),
    };
  } catch {
    return null;
  }
}

export function isTokenExpired(decoded: DecodedToken | null, skewSeconds = 5): boolean {
  if (!decoded) return true;
  return Date.now() / 1000 >= decoded.exp - skewSeconds;
}
