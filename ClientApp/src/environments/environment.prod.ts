export const environment = {
  production: true,
  // Docker: nginx serves static files only (no reverse proxy), so the
  // browser calls the Gateway directly — see ClientApp/nginx.conf.
  apiBaseUrl: 'http://localhost:5005',
};
