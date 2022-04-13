using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Text;

namespace FHIRProxy
{
    public static class SMARTProxyToken
    {
        [FunctionName("SMARTProxyToken")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "oauth2/token")] HttpRequest req,
            ILogger log)
        {
            var iss = ADUtils.GetIssuer();
            var isaad = Utils.GetBoolEnvironmentVariable("FP-OIDC-ISAAD", true);
            string ct = req.Headers["Content-Type"].FirstOrDefault();
            if (string.IsNullOrEmpty(ct) || !ct.Contains("application/x-www-form-urlencoded"))
            {
                return new ContentResult() { Content = "Content-Type invalid must be application/x-www-form-urlencoded", StatusCode = 400, ContentType = "text/plain" };

            }
           
            string code = null;
            string redirect_uri = null;
            string client_id = null;
            string client_secret = null;
            string grant_type = null;
            string refresh_token = null;
            string scope = null;
            //Read in Form Collection
            IFormCollection col = req.Form;
            if (col != null)
            {
                code = col["code"];
                redirect_uri = col["redirect_uri"];
                client_id = col["client_id"];
                client_secret = col["client_secret"];
                grant_type = col["grant_type"];
                refresh_token = col["refresh_token"];
                scope = col["scope"];
            }
            //Check for Client Id and Secret in Basic Auth Header and use if not in POST body
            var authHeader = req.Headers["Authorization"].FirstOrDefault();
            string headclientid = null;
            string headsecret = null;
            if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Basic "))
            {
                string encodedUsernamePassword = authHeader.Substring("Basic ".Length).Trim();
                byte[] data = Convert.FromBase64String(encodedUsernamePassword);
                string decodedString = Encoding.UTF8.GetString(data);
                headclientid = decodedString.Substring(0, decodedString.IndexOf(":"));
                headsecret = decodedString.Substring(decodedString.IndexOf(":") + 1);
                if (string.IsNullOrEmpty(client_id)) client_id = headclientid;
                if (string.IsNullOrEmpty(client_secret)) client_secret = headsecret;
            }
            //Create Key Value Pairs List
            var keyValues = new List<KeyValuePair<string, string>>();
            if (!string.IsNullOrEmpty(grant_type))
            {
                keyValues.Add(new KeyValuePair<string, string>("grant_type", grant_type));
            }
            if (!string.IsNullOrEmpty(code))
            {
                keyValues.Add(new KeyValuePair<string, string>("code", code));
            }
            if (!string.IsNullOrEmpty(redirect_uri))
            {
                keyValues.Add(new KeyValuePair<string, string>("redirect_uri", redirect_uri));
            }
            if (!string.IsNullOrEmpty(client_id))
            {
                keyValues.Add(new KeyValuePair<string, string>("client_id", client_id));
            }
            if (!string.IsNullOrEmpty(client_secret))
            {
                keyValues.Add(new KeyValuePair<string, string>("client_secret", client_secret));
            }
            if (!string.IsNullOrEmpty(refresh_token))
            {
                keyValues.Add(new KeyValuePair<string, string>("refresh_token", refresh_token));
            }
            if (!string.IsNullOrEmpty(scope))
            {
                //Convert SMART on FHIR Scopes to Fully Qualified AAD Scopes
                string scopeString = scope;
                if (isaad)
                {
                    string appiduri = ADUtils.GetAppIdURI(req.Host.Value);
                    scopeString = scope.ConvertSMARTScopeToAADScope(appiduri);
                }
                keyValues.Add(new KeyValuePair<string, string>("scope", scopeString));
            }
            //Load Configuration
            JObject config = await ADUtils.LoadOIDCConfiguration(iss, log);
            if (config == null)
            {
                return new ContentResult() { Content = $"Error retrieving open-id configuration from {iss}", StatusCode = 500, ContentType = "text/plain" };

            }
            string tendpoint = (string)config["token_endpoint"];
            JObject obj = null;
            HttpResponseMessage response = null;
            //POST to the issuer token endpoint
            using (HttpClient client = new HttpClient())
            {
                // Call asynchronous network methods in a try/catch block to handle exceptions
                try
                {
                    var request = new HttpRequestMessage(HttpMethod.Post, tendpoint);
                    request.Content = new FormUrlEncodedContent(keyValues);
                    response = await client.SendAsync(request);
                    string contresp = await response.Content.ReadAsStringAsync();
                    obj = JObject.Parse(contresp);
                }
                catch (Exception re)
                {
                    log.LogError($"SMARTProxyToken:Error loading from {tendpoint} Message: {re.Message}");
                    return new ContentResult() { Content = $"Error loading from {tendpoint} Message: {re.Message}", StatusCode = 500, ContentType = "text/plain" };
                }
                if (!response.IsSuccessStatusCode)
                {
                    log.LogError($"SMARTProxyToken:Error loading token {obj.ToString()}");
                    return new ContentResult() { Content = $"{obj.ToString()}", StatusCode = (int)response.StatusCode, ContentType = "application/json" };
                }

            }
            //Validate Token from Issuer and generate a Proxy Access Token to replace access_token in token call
            var handler = new JwtSecurityTokenHandler();
            JwtSecurityToken orig_token = null;
            JwtSecurityToken proxy_access_token = null;
            string proxyAccessTokenString = null;
            string tokenscope = (string)obj["scope"];
            if (grant_type.ToLower().Equals("client_credentials"))
            {
                try
                {
                    //Client Credentials or Refresh Validate Returned Access Token 
                    orig_token = await ADUtils.ValidateToken((string)obj["access_token"], (string)config["jwks_uri"], req.Host.Value,true,log);
                }
                catch (Exception e)
                {
                    log.LogError($"SMARTProxyToken:Error validating issuer access token: {e.Message}");
                    return new ContentResult() { Content = $"Error validating issuer access token: {e.Message}", StatusCode = 403, ContentType = "text/plain" };
                }
               
                
            }
            else if (grant_type.ToLower().Equals("authorization_code") || grant_type.ToLower().Equals("refresh_token"))
            {
                //authorization_code need to validate identity from oidc issuer and produce a SMART Compliant Access token
                try
                {
                    orig_token = await ADUtils.ValidateToken((string)obj["id_token"], (string)config["jwks_uri"], req.Host.Value,false,log);
                }
                catch (Exception e)
                {
                    log.LogError($"SMARTProxyToken:Error validating issuer id token: {e.Message}");
                    return new ContentResult() { Content = $"Error validating issuer id token: {e.Message}", StatusCode = 403, ContentType = "text/plain" };
                }
                
            }
            //Undo AAD Scopes
            if (isaad)
            {
                string appiduri = ADUtils.GetAppIdURI(req.Host.Value);
                if (!appiduri.EndsWith("/")) appiduri = appiduri + "/";
                tokenscope = tokenscope.Replace(appiduri, "");
            }
            //Generate a Server Access Token for fhir-proxy and replace in token call.
            proxyAccessTokenString = ADUtils.GenerateFHIRProxyAccessToken(orig_token, tokenscope, log);
            //substitute our access token if created
            if (!string.IsNullOrEmpty(proxyAccessTokenString))
            {
                obj["access_token"] = proxyAccessTokenString;
                proxy_access_token = handler.ReadJwtToken(proxyAccessTokenString);
                ClaimsIdentity access_ci = new ClaimsIdentity(proxy_access_token.Claims);
                string fhiruser = access_ci.fhirUser();

                if (access_ci.HasScope("launch.patient") && fhiruser != null && fhiruser.StartsWith("Patient"))
                {

                    var pt = FHIRProxyAuthorization.GetFHIRIdFromFHIRUser(fhiruser);
                    if (!string.IsNullOrEmpty(pt))
                    {
                        obj["patient"] = pt;
                    }
                }
                //Replace Scopes back to SMART from Fully Qualified AD Scopes
                if (!obj["scope"].IsNullOrEmpty() && isaad)
                {
                    string appiduri = ADUtils.GetAppIdURI(req.Host.Value);
                    string sc = obj["scope"].ToString();
                    sc = sc.Replace(appiduri + "/", "");
                    sc = sc.Replace("patient.", "patient/");
                    sc = sc.Replace("user.", "user/");
                    sc = sc.Replace("system.", "system/");
                    sc = sc.Replace("launch.", "launch/");
                    if (!sc.Contains("openid")) sc = sc + " openid";
                    if (!sc.Contains("offline_access")) sc = sc + " offline_access";
                    obj["scope"] = sc;
                }
            }
            req.HttpContext.Response.Headers.Add("Cache-Control","no-store");
            req.HttpContext.Response.Headers.Add("Pragma", "no-cache");
            if (!string.IsNullOrEmpty(authHeader)) req.HttpContext.Response.Headers.Add("Authorization", authHeader);
            var cr = new ContentResult()
            {
                Content = obj.ToString(),
                StatusCode = (int)response.StatusCode,
                ContentType = "application/json"
            };
            return cr;
        }
    }
}

