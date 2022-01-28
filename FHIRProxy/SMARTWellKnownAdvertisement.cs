using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace FHIRProxy
{
    public static class SMARTWellKnownAdvertisement
    {
        [FunctionName("KnownAdvertisementSMART")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "fhir/.well-known/smart-configuration")] HttpRequest req,
            ILogger log)
        {
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            string aauth = req.Scheme + "://" + req.Host.Value + "/oauth2/authorize";
            string atoken = req.Scheme + "://" + req.Host.Value + "/oauth2/token";
            JObject obj = new JObject();
            obj["token_endpoint"] = atoken;
            JArray arr = new JArray();
            arr.Add("private_key_jwt");
            obj["token_endpoint_auth_methods_supported"] = arr;
            JArray arr1 = new JArray();
            arr1.Add("RS384");
            arr1.Add("ES384");
            obj["token_endpoint_auth_signing_alg_values_supported"] = arr1;
            obj["authorization_endpoint"] = aauth;
            JArray arr2 = new JArray();
            arr2.Add("system/*.read");
            obj["scopes_supported"] = arr2;
            JArray arr3 = new JArray();
            arr3.Add("launch-ehr");
            arr3.Add("launch-standalone");
            arr3.Add("client-public");
            arr3.Add("Patient Access for Standalone Apps");
            arr3.Add("Patient Access for EHR Launch");
            arr3.Add("sso-openid-connect");
            arr3.Add("context-standalone-patient");
            arr3.Add("permission-offline");
            arr3.Add("permission-patient");
            obj["capabilities"] = arr3;
            return new JsonResult(obj);
        }
    }
}
