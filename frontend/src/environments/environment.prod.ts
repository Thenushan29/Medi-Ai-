/**
 * Production. The Angular app is served from the API's wwwroot (same origin),
 * so the base URL is relative — ApiService appends "/api" itself. No CORS needed.
 */
export const environment = {
  production: true,
  apiBaseUrl: ''
};
