using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Web;

namespace FHIRProxy
{
    public static class SMARTProxyAuthorize
    {
        [FunctionName("SMARTProxyAuthorize")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "AadSmartOnFhirProxy/authorize")] HttpRequest req,
            ILogger log)
        {
            string aadname=Utils.GetEnvironmentVariable("FP-LOGIN-AUTHORITY","login.microsoftonline.com");
            string aadpolicy = Utils.GetEnvironmentVariable("FP-LOGIN-POLICY", "");
            string tenant = Utils.GetEnvironmentVariable("FP-LOGIN-TENANT",Utils.GetEnvironmentVariable("FP-RBAC-TENANT-NAME",""));
            string appiduri = req.Scheme + "://" + req.Host.Value;
            if (string.IsNullOrEmpty(tenant))
            {
                
                return new ContentResult() { Content = "Login Tenant not Configured...Cannot proxy AD Authorize Request", StatusCode = 500 , ContentType = "text/plain" };
            }
            string response_type = req.Query["response_type"];
            string client_id = req.Query["client_id"];
            string redirect_uri = req.Query["redirect_uri"];
            string launch = req.Query["launch"];
            string scope = req.Query["scope"];
            string state = req.Query["state"];
            //To fully qualify SMART scopes to be compatible with AD Scopes we'll need and audience/application URI for the registered application
            //Check for Application Audience on request
            string aud = req.Query["aud"];
            if (!string.IsNullOrEmpty(aud) && aud.EndsWith("/fhir")) aud = appiduri;
          
            if (string.IsNullOrEmpty(aud))
            {
                //If no Audience on request lookup in configuration, audience should be Application ID Uri for registered app
                aud = Utils.GetEnvironmentVariable($"FP-LOGIN-AUD-{client_id}");
                //default Audience to api://client_id
                if (string.IsNullOrEmpty(aud)) aud = appiduri;
            }
            string newQueryString = $"response_type={response_type}&redirect_uri={redirect_uri}&client_id={client_id}";
            
            if (!string.IsNullOrEmpty(launch))
            {
                //TODO: Not sure if there is a use for us to handle launch parameter, for now only the launch/{patient/user} in scope is supported.
            }

            if (!string.IsNullOrEmpty(state))
            {
                newQueryString += $"&state={HttpUtility.UrlEncode(state)}";
            }
            //Convert SMART on FHIR Scopes to Fully Qualified AAD Scopes
            if (!string.IsNullOrEmpty(scope))
            {
                string[] scopes = scope.Split(' ');
                var scopeString = "";
                foreach (var s in scopes)
                {
                    if (!string.IsNullOrEmpty(scopeString)) scopeString += " ";
                    if (s.StartsWith("launch",System.StringComparison.InvariantCultureIgnoreCase) || s.StartsWith("patient/", System.StringComparison.InvariantCultureIgnoreCase) || s.StartsWith("user/", System.StringComparison.InvariantCultureIgnoreCase))
                    {
                        var newScope = s.Replace("/", ".");
                        scopeString += $"{aud}/{newScope}";
                    } else
                    {
                        scopeString += s;
                    }
                }
                newQueryString += $"&scope={HttpUtility.UrlEncode(scopeString)}";
            }
            string redirect = $"https://{aadname}/{tenant}";
            if (!string.IsNullOrEmpty(aadpolicy)) redirect += $"/{aadpolicy}";
            redirect += $"/oauth2/v2.0/authorize?{newQueryString}";
            return new RedirectResult(redirect, false);
           
        }
    }
}

