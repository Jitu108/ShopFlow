import { decodeJwt, isTokenExpired } from './jwt.util';

// Payload: { userId: "a1b2c3", ClaimTypes.Email: "test@example.com",
// ClaimTypes.Role: "Customer", emailVerified: "false", exp: 9999999999 }
const SAMPLE_JWT =
  'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJ1c2VySWQiOiJhMWIyYzMiLCJodHRwOi8vc2NoZW1hcy54bWxzb2FwLm9yZy93cy8yMDA1LzA1L2lkZW50aXR5L2NsYWltcy9lbWFpbGFkZHJlc3MiOiJ0ZXN0QGV4YW1wbGUuY29tIiwiaHR0cDovL3NjaGVtYXMubWljcm9zb2Z0LmNvbS93cy8yMDA4LzA2L2lkZW50aXR5L2NsYWltcy9yb2xlIjoiQ3VzdG9tZXIiLCJlbWFpbFZlcmlmaWVkIjoiZmFsc2UiLCJleHAiOjk5OTk5OTk5OTksImlzcyI6IlNob3BGbG93IiwiYXVkIjoiU2hvcEZsb3cifQ.fakesignature';

describe('decodeJwt', () => {
  it('decodes userId, email, role, and emailVerified from the full ClaimTypes URIs', () => {
    const decoded = decodeJwt(SAMPLE_JWT);
    expect(decoded).toEqual({
      userId: 'a1b2c3',
      email: 'test@example.com',
      role: 'Customer',
      emailVerified: false,
      exp: 9999999999,
    });
  });

  it('returns null for a malformed token', () => {
    expect(decodeJwt('not-a-jwt')).toBeNull();
  });
});

describe('isTokenExpired', () => {
  it('is true for a null decoded token', () => {
    expect(isTokenExpired(null)).toBe(true);
  });

  it('is false for a token whose exp is far in the future', () => {
    const decoded = decodeJwt(SAMPLE_JWT);
    expect(isTokenExpired(decoded)).toBe(false);
  });

  it('is true once exp is within the skew window', () => {
    const nowSeconds = Date.now() / 1000;
    expect(isTokenExpired({ userId: 'x', email: 'x', role: 'Customer', emailVerified: false, exp: nowSeconds + 1 }, 5)).toBe(
      true,
    );
  });
});
