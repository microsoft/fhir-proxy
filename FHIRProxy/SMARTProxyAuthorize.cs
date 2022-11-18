using System
    .IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System.Web;
using System;
using Microsoft.Extensions.Azure;
using Azure;
using Polly;
using System.Linq;
using Azure.Core;
using System.Net.Http.Headers;
using static System.Net.Mime.MediaTypeNames;

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
            string prompt = req.Query["prompt"];
            string codechallenge = req.Query["code_challenge"];
            string codechallengemethod = req.Query["code_challenge_method"];
            string sessionscopes = req.Query["sessionscopes"];
            string loginhint = req.Query["login_hint"];
            //Redirect if SMART Session Scopes are enabled and sessionscopes not selected
            if (!string.IsNullOrEmpty(scope) && Utils.GetBoolEnvironmentVariable("FP-SMART-SESSION-SCOPES", false) && !string.IsNullOrEmpty(scope) && scope.Contains("launch/patient") && string.IsNullOrEmpty(sessionscopes))
            {
                string redurl = $"{req.Scheme}://{req.Host}/oauth2/dynamicscopes{req.QueryString}";
                return new RedirectResult(redurl, false);
            }
            //To fully qualify SMART scopes to be compatible with AD Scopes we'll need and audience/application URI for the registered application
            //Check for Application Audience on request
            string aud = req.Query["aud"];
            string newQueryString = $"response_type={response_type}&redirect_uri={HttpUtility.UrlEncode(redirect_uri)}&client_id={client_id}";
            //Add AAD consent prompt query parm if present
            if (!string.IsNullOrEmpty(prompt)) newQueryString += $"&prompt={prompt}";
            if (!string.IsNullOrEmpty(codechallenge)) newQueryString += $"&code_challenge={codechallenge}&code_challenge_method={codechallengemethod}";
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
                if (!scopeString.Contains("profile")) scopeString += " profile";
                //Verify audience is proxy
                if (!string.IsNullOrEmpty(aud) && !aud.Contains(req.Host.Value,StringComparison.InvariantCultureIgnoreCase))
                {
                    string r = redirect_uri + "?" + HttpUtility.UrlEncode($"error=Invalid Audience {aud}");
                    if (!string.IsNullOrEmpty(state)) r += $"state={HttpUtility.UrlEncode(state)}";
                    return new RedirectResult(redirect_uri, false);
                }
            }
            if (!string.IsNullOrEmpty(scopeString))
            {
                newQueryString += $"&scope={HttpUtility.UrlEncode(scopeString)}";
            }
            if (!string.IsNullOrEmpty(loginhint))
            {
                newQueryString += $"&login_hint={HttpUtility.UrlEncode(loginhint)}";
            }
            if (!string.IsNullOrEmpty(aud))
            {
                newQueryString += $"&aud={HttpUtility.UrlEncode(aud)}";
            }
            //Add Custom Request Parmeters
            foreach(string s in Utils.GetEnvironmentVaiableArray("FP-OIDC-CUSTOM-PARMS"))
            {
                string[] parm = s.Split("=");
                string qp = req.Query[parm[0]];
                if (string.IsNullOrEmpty(qp))
                {
                    if (parm.Length > 1)
                    {
                        qp = parm[1];
                    }
                }
                if (!string.IsNullOrEmpty(qp))
                {
                    newQueryString += $"&{parm[0]}={HttpUtility.UrlEncode(qp)}";
                }
            }
            string redirect = (string)config["authorization_endpoint"];
            redirect += $"?{newQueryString}";
            if (Utils.GetBoolEnvironmentVariable("FP-SMART-SESSION-SCOPES", false))
            {
                if (!string.IsNullOrEmpty(loginhint))
                {
                    var table = Utils.getTable(ProxyConstants.SCOPE_STORE_TABLE);
                    ScopeEntity se = new ScopeEntity(client_id, loginhint);
                    if (string.IsNullOrEmpty(sessionscopes))
                    {
                        Utils.deleteEntity(table, se);
                    }
                    else
                    {
                        se.RequestedScopes = sessionscopes;
                        se.ValidUntil = DateTime.UtcNow.AddSeconds(Utils.GetIntEnvironmentVariable("FP-SMART-SESSION-SCOPES-TIMEOUT", "60"));
                        Utils.setEntity(table, se);
                    }
                }
            }
            return new RedirectResult(redirect, false);
        }
    }
}

