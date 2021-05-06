using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FHIRProxy
{
    public static class MetaDataOverride
    {
        [FunctionName("MetaDataOverride")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "fhir/metadata")] HttpRequest req,
            ILogger log)
        {
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var nextresult = await FHIRClient.CallFHIRServer("metadata",requestBody,req.Method,log);

            //Reverse proxy content string 
            nextresult = Utils.reverseProxyResponse(nextresult, req);
            //Replace SMARTonFHIR Proxy endpoints
            string aauth = req.Scheme + "://" + req.Host.Value + "/AadSmartOnFhirProxy/authorize";
            string atoken = req.Scheme + "://" + req.Host.Value + "/AadSmartOnFhirProxy/token";
            var md = nextresult.toJToken();
            var rest = md["rest"];
            if (!rest.IsNullOrEmpty())
            {
                JArray r = (JArray)rest;
                foreach(JToken tok in r)
                {
                    if (!tok["mode"].IsNullOrEmpty() && ((string)tok["mode"]).Equals("server"))
                    {
                        if (!tok["security"].IsNullOrEmpty())
                        {
                            JArray urls = (JArray)tok["security"]["extension"][0]["extension"];
                            foreach(JToken u in urls)
                            {
                                if (((string)u["url"]).Equals("token"))
                                {
                                    u["valueUri"] = atoken;
                                }
                                if (((string)u["url"]).Equals("authorize"))
                                {
                                    u["valueUri"] = aauth;
                                }
                            }
                            nextresult.Content = md;
                            break;
                        }
                       
                    }
                }
            }
            //TODO: Modify Capability as needed
            return ProxyFunction.genContentResult(nextresult, log);
        }
    }
}

