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
            string code = null;
            string redirect_uri = null;
            string client_id = null;
            string client_secret = null;
            string grant_type = null;
            //Read in Form Collection
            IFormCollection col = req.Form;
            if (col != null)
            {
                code = col["code"];
                redirect_uri = col["redirect_uri"];
                client_id = col["client_id"];
                client_secret = col["client_secret"];
                grant_type = col["grant_type"];
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
                    
                    var pt = FHIRProxyAuthorization.GetFHIRIdFromOID(ci, "Patient", log);
                    if (!string.IsNullOrEmpty(pt))
                    {
                        log.LogInformation($"Launch Scope for patient...{pt}");
                        obj["patient"] = pt;
                    }
                }

            }
            req.HttpContext.Response.Headers.Add("Cache-Control","no-store");
            req.HttpContext.Response.Headers.Add("Pragma", "no-cache");
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

