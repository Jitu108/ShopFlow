import { TokenStore } from './token-store';

describe('TokenStore', () => {
  let store: TokenStore;

  beforeEach(() => {
    sessionStorage.clear();
    store = new TokenStore();
  });

  it('keeps the access token in memory only, not sessionStorage', () => {
    store.setAccessToken('access-1');
    expect(store.getAccessToken()).toBe('access-1');
    expect(sessionStorage.getItem('shopflow.refreshToken')).toBeNull();
  });

  it('persists the refresh token to sessionStorage', () => {
    store.setRefreshToken('refresh-1');
    expect(store.getRefreshToken()).toBe('refresh-1');
    expect(sessionStorage.getItem('shopflow.refreshToken')).toBe('refresh-1');
  });

  it('setTokens sets both at once', () => {
    store.setTokens('access-1', 'refresh-1');
    expect(store.getAccessToken()).toBe('access-1');
    expect(store.getRefreshToken()).toBe('refresh-1');
  });

  it('a fresh instance still sees a refresh token left in sessionStorage (simulates surviving a reload)', () => {
    store.setRefreshToken('refresh-survives-reload');
    const afterReload = new TokenStore();
    expect(afterReload.getAccessToken()).toBeNull();
    expect(afterReload.getRefreshToken()).toBe('refresh-survives-reload');
  });

  it('clear wipes both the in-memory access token and the persisted refresh token', () => {
    store.setTokens('access-1', 'refresh-1');
    store.clear();
    expect(store.getAccessToken()).toBeNull();
    expect(store.getRefreshToken()).toBeNull();
  });
});
