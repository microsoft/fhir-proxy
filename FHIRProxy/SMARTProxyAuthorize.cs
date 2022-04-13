using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System.Web;
using System;

namespace FHIRProxy
{
    public static class SMARTProxyAuthorize
    {
        [FunctionName("SMARTProxyAuthorize")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "oauth2/authorize")] HttpRequest req,
            ILogger log)
        {
            var iss = ADUtils.GetIssuer();
            var isaad = Utils.GetBoolEnvironmentVariable("FP-OIDC-ISAAD", true);
            JObject config = await ADUtils.LoadOIDCConfiguration(iss,log);
            if (config==null)
            {
                return new ContentResult() { Content = $"Error retrieving open-id configuration from {iss}", StatusCode = 500, ContentType = "text/plain" };

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
            string scopeString = scope;
            if (isaad)
            {
                string appiduri = ADUtils.GetAppIdURI(req.Host.Value);
                scopeString = scope.ConvertSMARTScopeToAADScope(appiduri);
            }
            if (!string.IsNullOrEmpty(scopeString))
            {
                newQueryString += $"&scope={HttpUtility.UrlEncode(scopeString)}";
            }
            string redirect = (string)config["authorization_endpoint"];
            redirect += $"?{newQueryString}";
            return new RedirectResult(redirect, false);
        }
    }
}

