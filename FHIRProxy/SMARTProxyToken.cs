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
            string tenant = Utils.GetEnvironmentVariable("FP-LOGIN-TENANT");
            if (tenant == null)
            {
                return new ContentResult() { Content = "Login Tenant not Configured...Cannot proxy AD Token Request", StatusCode = 500, ContentType = "text/plain" };
            }
            //Read in Form Collection
            IFormCollection col = req.Form;
            string code = col["code"];
            string redirect_uri = col["redirect_uri"];
            string client_id = col["client_id"];
            string client_secret = col["client_secret"];
            string grant_type = col["grant_type"];
            //Create Key Value Pairs List
            var keyValues = new List<KeyValuePair<string, string>>();
            keyValues.Add(new KeyValuePair<string, string>("grant_type", grant_type));
            keyValues.Add(new KeyValuePair<string, string>("code", code));
            keyValues.Add(new KeyValuePair<string, string>("redirect_uri", redirect_uri));
            keyValues.Add(new KeyValuePair<string, string>("client_id", client_id));
            keyValues.Add(new KeyValuePair<string, string>("client_secret", client_secret));
            //POST to token endpoint
            var client = new HttpClient();
            client.BaseAddress = new Uri($"https://{aadname}");
            string path = tenant;
            if (!string.IsNullOrEmpty(aadpolicy)) path += $"/{aadpolicy}";
            path += "/oauth2/v2.0/token";
            var request = new HttpRequestMessage(HttpMethod.Post, path);
            request.Content = new FormUrlEncodedContent(keyValues);
            var response = await client.SendAsync(request);
           
            var cr = new ContentResult()
            {
                Content = await response.Content.ReadAsStringAsync(),
                StatusCode = (int)response.StatusCode,
                ContentType = "application/json"
            };
            log.LogInformation($"Response:{cr.Content}");
            return cr;
        }
    }
}

