using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace FHIRProxy
{
    public static class MetaDataOverride
    {
        [FunctionName("MetaDataOverride")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "fhir/metadata")] HttpRequest req,
            ILogger log)
        {
            FHIRClient fhirClient = FHIRClientFactory.getClient(log);
            var nextresult = await fhirClient.LoadResource("metadata");
            //Reverse proxy content string 
            nextresult = Utils.reverseProxyResponse(nextresult, req, "metadata");
            //TODO: Modify Capability as needed
            return ProxyFunction.genContentResult(nextresult, log);
        }
    }
}

