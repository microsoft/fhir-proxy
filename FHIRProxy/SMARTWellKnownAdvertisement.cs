using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Polly;
using System.Reflection;
using System.Security;

namespace FHIRProxy
{
    public static class SMARTWellKnownAdvertisement
    {
        public const string SMART_STYLE = "{   color_background: \"#edeae3\",   color_error: \"#9e2d2d\",   color_highlight: \"#69b5ce\",   color_modal_backdrop: \"\",   color_success: \"#498e49\",   color_text: \"#303030\",   dim_border_radius: \"6px\",   dim_font_size: \"13px\",   dim_spacing_size: \"20px\",   font_family_body: \"Georgia, Times, 'Times New Roman', serif\",   font_family_heading: \"'HelveticaNeue-Light', Helvetica, Arial, 'Lucida Grande', sans-serif;\" }";
        [FunctionName("KnownAdvertisementSMART")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "fhir/.well-known/smart-configuration")] HttpRequest req,
            ILogger log)
        {
            
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            string aauth = req.Scheme + "://" + req.Host.Value + "/oauth2/authorize";
            string atoken = req.Scheme + "://" + req.Host.Value + "/oauth2/token";
            if (req.Query.ContainsKey("smartstyle"))
            {
                return new JsonResult(JObject.Parse(SMART_STYLE));
            }
            JObject config = await ADUtils.LoadOIDCConfiguration(ADUtils.GetIssuer(), log);
            if (config == null)
            {
                return new ContentResult() { Content = $"Error retrieving open-id configuration from {ADUtils.GetIssuer()}", StatusCode = 500, ContentType = "text/plain" };

            }
            //Override for Proxy Intercept of authorization/token issue and add capabilities for SMART
            config["token_endpoint"] = atoken;
            config["authorization_endpoint"] = aauth;
            JArray arr3 = new JArray();
            arr3.Add("launch-standalone");
            arr3.Add("client-public");
            arr3.Add("Patient Access for Standalone Apps");
            arr3.Add("sso-openid-connect");
            arr3.Add("context-standalone-patient");
            arr3.Add("permission-offline");
            arr3.Add("permission-patient");
            arr3.Add("client-confidential-symmetric");
            arr3.Add("launch-ehr");
            arr3.Add("context-banner");
            arr3.Add("context-style");
            arr3.Add("context-ehr-patient");
            arr3.Add("permission-user");
            config["capabilities"] = arr3;
            return new JsonResult(config);
        }
    }
}
