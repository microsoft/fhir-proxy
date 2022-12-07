using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Azure.Services.AppAuthentication;
using System.Linq;
using System.Reflection;
using System.Net;
using System.Security.Claims;

namespace FHIRProxy
{
    public static class SMARTValidateAccessToken
    {
        [FunctionName("SMARTValidateAccessToken")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "oauth2/validate")] HttpRequest req,
            ILogger log)
        {
            string msg = null;
            int sc = (int)HttpStatusCode.OK;
            string ct = "text/plain";
            var jwtstr = req.Headers["Authorization"].FirstOrDefault();
            if (string.IsNullOrEmpty(jwtstr))
            {
                msg = $"No acccess token found in Authorization Header";
                sc = (int)HttpStatusCode.Unauthorized;
            }
            else
            {
                try
                {
                    jwtstr = jwtstr.Split(" ")[1];
                    var principal = FHIRProxyAuthorization.ValidateFPToken(jwtstr, log);
                    var ci = (ClaimsIdentity)principal.Identity;
                    string iss = ci.SingleClaimValue("iss");
                    msg = $"Valid Bearer access token in authorization header. Issuer: {iss}";
                }
                catch (Exception e)
                {
                    msg = $"Error validating Access Token: {e.Message}";
                    sc = (int)HttpStatusCode.Unauthorized;
                
                }
            }
            return new ContentResult() { Content = msg, StatusCode = sc, ContentType = ct };
            
        }
    }
}
