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
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "AadSmartOnFhirProxy/token")] HttpRequest req,
            ILogger log)
        {
            string aadname = Utils.GetEnvironmentVariable("FP-LOGIN-AUTHORITY", "login.microsoftonline.com");
            string aadpolicy = Utils.GetEnvironmentVariable("FP-LOGIN-POLICY", "");
            string tenant = Utils.GetEnvironmentVariable("FP-LOGIN-TENANT", Utils.GetEnvironmentVariable("FP-RBAC-TENANT-NAME", ""));
            if (string.IsNullOrEmpty(tenant))
            {
                return new ContentResult() { Content = "Login Tenant not Configured...Cannot proxy AD Token Request", StatusCode = 500, ContentType = "text/plain" };
            }
            string ct = req.Headers["Content-Type"].FirstOrDefault();
            if (string.IsNullOrEmpty(ct) || !ct.Contains("application/x-www-form-urlencoded"))
            {
                return new ContentResult() { Content = "Content-Type invalid must be application/x-www-form-urlencoded", StatusCode = 400, ContentType = "text/plain" };

            }
            string appiduri = req.Scheme + "://" + req.Host.Value;
            string code = null;
            string redirect_uri = null;
            string client_id = null;
            string client_secret = null;
            string grant_type = null;
            string refresh_token = null;
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


            //POST to token endpoint
            var client = new HttpClient();
            client.BaseAddress = new Uri($"https://{aadname}");
            string path = tenant;
            if (!string.IsNullOrEmpty(aadpolicy)) path += $"/{aadpolicy}";
            path += "/oauth2/v2.0/token";
            var request = new HttpRequestMessage(HttpMethod.Post, path);
            request.Content = new FormUrlEncodedContent(keyValues);
            var response = await client.SendAsync(request);
            string contresp = await response.Content.ReadAsStringAsync();
            JObject obj = JObject.Parse(contresp);
            //Load Access Token and check for Context Claims and Set Context if requested or linked
            if (!obj["access_token"].IsNullOrEmpty())
            {
                var handler = new JwtSecurityTokenHandler();
                var token = handler.ReadToken((string)obj["access_token"]) as JwtSecurityToken;
                ClaimsIdentity ci = new ClaimsIdentity(token.Claims);
                if (ci.HasScope("launch.patient"))
                {
                    
                    var pt = FHIRProxyAuthorization.GetFHIRId(ci, "Patient", log);
                    if (!string.IsNullOrEmpty(pt))
                    {
                        log.LogInformation($"Launch Scope for patient...{pt}");
                        obj["patient"] = pt;
                    }
                }

            }
            //Replace Scopes back to SMART from Fully Qualified AD Scopes
            if (!obj["scope"].IsNullOrEmpty())
            {
                string sc = obj["scope"].ToString();
                sc = sc.Replace(appiduri + "/", "");
                sc = sc.Replace("patient.", "patient/");
                sc = sc.Replace("user.", "user/");
                sc = sc.Replace("launch.", "launch/");
                if (!sc.Contains("openid")) sc = sc + " openid";
                if (!sc.Contains("offline_access")) sc = sc + " offline_access";
                obj["scope"] = sc;
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

