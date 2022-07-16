using Microsoft.Azure.Services.AppAuthentication;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Threading.Tasks;

namespace FHIRProxy
{
    public static class ADUtils
    {
        public static bool isTokenExpired(string bearerToken)
        {
            
            if (bearerToken == null) return true;
            var handler = new JwtSecurityTokenHandler();
            var token = handler.ReadToken(bearerToken) as JwtSecurityToken;
            var tokenExpiryDate = token.ValidTo;

            // If there is no valid `exp` claim then `ValidTo` returns DateTime.MinValue
            if (tokenExpiryDate == DateTime.MinValue) return true;

            // If the token is in the past then you can't use it
            if (tokenExpiryDate < DateTime.UtcNow) return true;
           
            return false;
            
        }
        public static async Task<string> GetAADAccessToken(string authority, string clientId, string clientSecret, string audience, bool msi,ILogger log)
        {
            try
            {
                if (msi)
                {
                    var _azureServiceTokenProvider = new AzureServiceTokenProvider();
                    return await _azureServiceTokenProvider.GetAccessTokenAsync(audience);

                }
                else
                {
                    var _authContext = new AuthenticationContext(authority);
                    var _clientCredential = new ClientCredential(clientId, clientSecret);
                    var _authResult = await _authContext.AcquireTokenAsync(audience, _clientCredential);
                    return _authResult.AccessToken;
                }

            }
            catch (Exception e)
            {
                log.LogError($"GetAADAccessToken: Exception getting access token: {e.Message}");
                return null;
            }

        }
        
        public static bool isMSI(string resource, string tenant = null, string clientid = null, string secret = null)
        {
            return (!string.IsNullOrEmpty(resource) && (!string.IsNullOrEmpty(tenant) && string.IsNullOrEmpty(clientid) && string.IsNullOrEmpty(secret)));
        }
    }
}
