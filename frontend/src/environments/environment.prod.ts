/**
 * Production. Point apiBaseUrl at the Azure App Service URL before deploying,
 * and add that same frontend origin to Cors:AllowedOrigins on the backend (§19).
 */
export const environment = {
  production: true,
  apiBaseUrl: 'https://meditrail-api.azurewebsites.net'
};
