using Microsoft.Azure.Services.AppAuthentication;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace FHIRProxy
{
    public static class ADUtils
    {
        public static ClaimsPrincipal BearerToClaimsPrincipal(HttpRequest req)
        {
            var jwtstr = req.Headers["Authorization"].First();
            if (jwtstr == null) return null;
            jwtstr = jwtstr.Split(" ")[1];
            var handler = new JwtSecurityTokenHandler();
            var token = handler.ReadToken(jwtstr) as JwtSecurityToken;
            ClaimsIdentity ci = new ClaimsIdentity(token.Claims, "ExternalOIDC");
            ClaimsPrincipal user = new ClaimsPrincipal(ci);
            return user;
        }
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
        private static string LoadJWKS(string jwksurl, ILogger log)
        {
            using (HttpClient client = new HttpClient())
            {
                // Call asynchronous network methods in a try/catch block to handle exceptions
                try
                {
                        var keys = client.GetStringAsync(jwksurl).GetAwaiter().GetResult();
                        return keys;
                    
                }
                catch (HttpRequestException e)
                {
                    log.LogError($"LoadJWKS: Error loading jwks keys from {jwksurl} Message :{e.Message}");
                    return null;
                }
            }

        }
        public static JObject LoadOIDCConfiguration(string iss, ILogger log)
        {
            if (string.IsNullOrEmpty(iss))
            {
                log.LogError($"LoadOIDCConfiguration: Issuer is not defined or empty");
                return null;
            }
            string url = iss;
            if (!url.EndsWith("/")) url += "/";
            url = url + ".well-known/openid-configuration";
            using (HttpClient client = new HttpClient())
            {
                // Call asynchronous network methods in a try/catch block to handle exceptions
                try
                {
                    string responseBody = client.GetStringAsync(url).GetAwaiter().GetResult();
                    JObject obj = JObject.Parse(responseBody);
                    return obj;
                }
                catch (Newtonsoft.Json.JsonReaderException re)
                {
                    log.LogError($"LoadOIDCConfiguration: JSON Parsing Error loading open-id configuration from {url} Message: {re.Message}");
                    return null;
                }
                catch (HttpRequestException e)
                {
                    log.LogError($"LoadOIDCConfiguration: Communication Error loading open-id configuration from {url} Message: {e.Message}");
                    return null;
                }
                catch(Exception uh)
                {
                    log.LogError($"LoadOIDCConfiguration: General Error loading open-id configuration from {url} Message: {uh.Message}");
                    return null;
                }
            }


        }
        public static JwtSecurityToken ValidateToken(string authToken, string jwksurl, string hostname, ILogger log)
        {
                var tokenHandler = new JwtSecurityTokenHandler();
                var validationParameters = GetValidationParameters(jwksurl,hostname,log);
                SecurityToken validatedToken;
                var principal = tokenHandler.ValidateToken(authToken, validationParameters, out validatedToken);
                return (JwtSecurityToken)validatedToken;      
        }
        private static TokenValidationParameters GetValidationParameters(string jwksurl, string hostname,ILogger log)
        {
            var jwks = Utils.GetEnvironmentVariable("FP-OIDC-JWKS");
            if (string.IsNullOrEmpty(jwks))
            {
                jwks = LoadJWKS(jwksurl, log);
            }
            var signingKeys = new JsonWebKeySet(jwks).GetSigningKeys();
            var retVal = new TokenValidationParameters()
            {
                ValidateAudience = true,
                ValidAudiences = GetValidAudiences(hostname),
                ValidateLifetime = true,
                ValidIssuers = GetValidIssuers(),
                IssuerSigningKeys = signingKeys,
                RequireSignedTokens = true
            };
            return retVal;
        }
        public static string GetIssuer()
        {
            var iss = Utils.GetEnvironmentVariable("FP-OIDC-ISSUER");
            if (iss == null)
            {
                iss = $"https://login.microsoftonline.com/{Utils.GetEnvironmentVariable("FP-LOGIN-TENANT", "")}/v2.0";
            }
            return iss;
        }
        
        public static List<string> GetValidIssuers()
        {
            List<string> retVal = new List<string>();
            var validissuers = Utils.GetEnvironmentVariable("FP-OIDC-VALID-ISSUERS");
            if (string.IsNullOrEmpty(validissuers))
            {
                string iss = GetIssuer();
                retVal.Add(iss);
                if (iss.Contains("login.microsoftonline.com"))
                {
                    retVal.Add($"https://sts.windows.net/{Utils.GetEnvironmentVariable("FP-LOGIN-TENANT", "")}/");
                }
            }
            else
            {
                var s = validissuers.Split(",");
                foreach (string i in s)
                {
                        retVal.Add(i);
                }
            }
            return retVal;
            
        }
        public static string GetAppIdURI(string hostname)
        {
            return Utils.GetEnvironmentVariable("FP-OIDC-AAD-APPID-URI", "api://" + hostname);
        }
        public static List<string> GetValidAudiences(string hostname)
        {
            List<string> retVal = new List<string>();
            var validaud = Utils.GetEnvironmentVariable("FP-OIDC-VALID-AUDIENCES");
            if (string.IsNullOrEmpty(validaud)) return null;
            var s = validaud.Split(",");
            foreach (string a in s)
            {
                retVal.Add(a);
            }
            return retVal;

        }
        public static string GenerateFHIRProxyAccessToken(JwtSecurityToken validatedIdentityToken,JwtSecurityToken sourceaccessToken,ILogger log)
        {
            var secret = Utils.GetEnvironmentVariable("FP-ACCESS-TOKEN-SECRET");
            //generate token that is valid for 7 days
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.ASCII.GetBytes(secret);
            ClaimsIdentity access_ci = new ClaimsIdentity(sourceaccessToken.Claims);
            ClaimsIdentity id_ci = new ClaimsIdentity(validatedIdentityToken.Claims);
            List<Claim> fpAccessClaims = new List<Claim>();
            string oidclaimkey = Utils.GetEnvironmentVariable("FP-OIDC-TOKEN-IDENTITY-CLAIM", "oid");
            var oid = access_ci.SingleClaim(oidclaimkey);
            if (oid == null) throw new Exception($"Cannot find oid claim {oidclaimkey} in original access token");
            fpAccessClaims.Add(new Claim("oid", oid.Value));
            var tid = access_ci.Tenant();
            if (string.IsNullOrEmpty(tid))
            {
                tid = access_ci.SingleClaim("iss").Value;
                tid = tid.Replace("https://", "").Replace("http://", "").Replace("api://","");
            }
            fpAccessClaims.Add(new Claim("tid", tid));
            var fhiruserclaim = id_ci.fhirUserClaim();
            if (fhiruserclaim != null)
            {
                fpAccessClaims.Add(fhiruserclaim);
            } else
            {
                //Look for a external mapping 
                if (fhiruserclaim == null)
                {
                    var fhiruser = FHIRProxyAuthorization.GetMappedFHIRUser(tid, oid.Value, log);
                    if (!string.IsNullOrEmpty(fhiruser))
                    {
                        fpAccessClaims.Add(new Claim(Utils.GetEnvironmentVariable("FP-FHIR-USER-CLAIM", "fhirUser"), fhiruser));
                    }
                }
            }
            var roles = access_ci.RoleClaims();
            if (roles != null) fpAccessClaims.AddRange(roles);
            var scopes = access_ci.ScopeString();
            if (scopes != null)
            {
                scopes = scopes.Replace("/", ".");
                fpAccessClaims.Add(new Claim("scp", scopes));
            }
            var appid = access_ci.SingleClaim("appId");
            if (appid != null) fpAccessClaims.Add(appid);
            fpAccessClaims.Add(access_ci.SingleClaim("iss"));
            fpAccessClaims.Add(access_ci.SingleClaim("aud"));
            fpAccessClaims.Add(access_ci.SingleClaim("sub"));
            var now = DateTime.UtcNow;
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(fpAccessClaims),
                IssuedAt = now,
                NotBefore = now,
                Expires = now.AddSeconds(Utils.GetIntEnvironmentVariable("FP-ACCESS-TOKEN-LIFESPAN-SECONDS", "3599")),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha384Signature),
                Issuer = "https://fhir-proxy.azurehealthcareapis.com"
            };
            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }
    }
}
