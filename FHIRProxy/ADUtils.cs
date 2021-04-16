using Microsoft.Azure.Services.AppAuthentication;
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
        public static async Task<string> GetOAUTH2BearerToken(string resource, string tenant = null, string clientid = null, string secret = null)
        {
            if (!string.IsNullOrEmpty(resource) && (string.IsNullOrEmpty(tenant) && string.IsNullOrEmpty(clientid) && string.IsNullOrEmpty(secret)))
            {
                //Assume Managed Service Identity with only resource provided.
                var azureServiceTokenProvider = new AzureServiceTokenProvider();
                var _accessToken = await azureServiceTokenProvider.GetAccessTokenAsync(resource);
                return _accessToken;
            }
            else
            {
                using (System.Net.WebClient client = new System.Net.WebClient())
                {
                    byte[] response =
                     client.UploadValues("https://login.microsoftonline.com/" + tenant + "/oauth2/token", new NameValueCollection()
                     {
                        {"grant_type","client_credentials"},
                        {"client_id",clientid},
                        { "client_secret", secret },
                        { "resource", resource }
                     });


                    string result = System.Text.Encoding.UTF8.GetString(response);
                    JObject obj = JObject.Parse(result);
                    return (string)obj["access_token"];
                }
            }
        }
    }
}
