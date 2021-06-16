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
        [FunctionName("MARTWellKnownAdvertisement")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "fhir/.well-known/smart-configuration")] HttpRequest req,
            ILogger log)
        {
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            string aauth = req.Scheme + "://" + req.Host.Value + "/AadSmartOnFhirProxy/authorize";
            string atoken = req.Scheme + "://" + req.Host.Value + "/AadSmartOnFhirProxy/token";
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
            return new JsonResult(obj);
        }
    }
}
