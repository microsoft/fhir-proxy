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
            string aauth = req.Scheme + "://" + req.Host.Value + "/oauth2/authorize";
            string atoken = req.Scheme + "://" + req.Host.Value + "/oauth2/token";
            var md = nextresult.toJToken();
            //Absoulute Form of URL
            md["url"] = req.Scheme + "://" + req.Host.Value + "/metadata";
            var rest = md.SelectToken("$.rest[?(@.mode=='server')]");
            if (!rest.IsNullOrEmpty())
            {
                JToken secext = rest.SelectToken("$.security.extension[?(@.url=='http://fhir-registry.smarthealthit.org/StructureDefinition/oauth-uris')].extension");
                if (!secext.IsNullOrEmpty()) 
                {
                            JToken tokenep = secext.SelectToken("$.[?(@.url=='token')]");
                            JToken authep = secext.SelectToken("$.[?(@.url=='authorize')]");
                            if (!tokenep.IsNullOrEmpty()) tokenep["valueUri"] = atoken;
                            if (!authep.IsNullOrEmpty()) authep["valueUri"] = aauth;        
                }
                JToken sof = rest.SelectToken("$.security.service[0].coding[?(@.system == 'http://terminology.hl7.org/CodeSystem/restful-security-service')]");
                if (!sof.IsNullOrEmpty())
                {
                    sof["code"] = "SMART-on-FHIR";
                }
                nextresult.Content = md;
            }
            
            //TODO: Modify Capability as needed
            return ProxyFunction.genContentResult(nextresult, log);
        }
    }
}

