using Azure;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using static StackExchange.Redis.Role;

namespace FHIRProxy.postprocessors
{
    class PublishFHIRResponseToBlobPostProcessor : IProxyPostProcess
    {

        private BlobServiceClient _blobServiceClient = null;
        private string _storageContainerName = null;
        private Object lockobj = new object();
        public bool initializationfailed = false;
        private ServiceBusSender _sender = null;
        private int _retryCountSetting;

        public void InitBlobClient(ILogger log)
        {

            if (initializationfailed || _blobServiceClient != null) return;
            lock (lockobj)
            {
                if (_blobServiceClient == null)
                {
                    try
                    {
                        var storageAccountConnectionString = Utils.GetEnvironmentVariable("FP-STORAGEACCOUNT-CONNSTRING");
                        _blobServiceClient = new BlobServiceClient(storageAccountConnectionString);
                        _storageContainerName = Utils.GetEnvironmentVariable("FP-STORAGEACCOUNT-CONTAINER");
                        log.LogInformation($"Successfully initialized Blob Client");
                    }
                    catch (Exception e)
                    {
                        initializationfailed = true;
                        throw new Exception($"Failed to initialize Blob Client:{e.Message}->{e.StackTrace}");
                    }
                }
            }


        }
        public async Task<ProxyProcessResult> Process(FHIRResponse response, HttpRequest req, ILogger log, ClaimsPrincipal principal)
        {

            try
            {
                FHIRParsedPath pp = req.parsePath();
                //Do we need to send on to CDS? Don't send for GET/PATCH, errors, X-MS-FHIRCDSSynAgent Header is present
                if (req.Method.Equals("GET") ||
                    req.Method.Equals("PATCH"))
                {
                    log.LogInformation("PublishFHIRResponseToBlobPostProcessor: Not posting FHIR Response to Blob. Request was of type GET or PATCH");
                    return new ProxyProcessResult(true, "", "", response);
                }

                if ((int)response.StatusCode > 299)
                {
                    log.LogError($"PublishFHIRResponseToBlobPostProcessor: Not posting FHIR Response to Blob. FHIR Response Status Code {response.StatusCode}");
                    return new ProxyProcessResult(true, "", "", response);
                }

                InitBlobClient(log);
                _retryCountSetting = Utils.GetIntEnvironmentVariable("FP-BLOBALREADYEXISTS-RETRYCOUNT", "3");
                await WriteFHIRResponseToBlob((string)response.Content, log);
            }

            catch (Exception exception)
            {
                var errorMessage = $"PublishFHIRResponseToBlobPostProcessor Exception: {exception.Message}";
                log.LogError(exception, errorMessage);
                return new ProxyProcessResult(false, errorMessage, "", response);
            }

            return new ProxyProcessResult(true, "", "", response);

        }

        public async Task WriteFHIRResponseToBlob(string contents, ILogger log)
        {
            bool retry = true;
            var retryCount = 0;

            while (retry)
            {
                try
                {
                    var containerClient = _blobServiceClient.GetBlobContainerClient(_storageContainerName);
                    await containerClient.CreateIfNotExistsAsync();
                    var filePath = $"{Guid.NewGuid()}.json";
                    var blobClient = containerClient.GetBlobClient(filePath);
                    var contentStream = Encoding.UTF8.GetBytes(contents);
                    using (var ms = new MemoryStream(contentStream))
                    {
                        blobClient.Upload(ms);
                    }
                    log.LogInformation($"PublishFHIRResponseToBlobPostProcessor: Successfully created blob {filePath} in {_storageContainerName} with FHIR Response");
                    retry = false;
                }
                catch (RequestFailedException e)
                {
                    retryCount++;
                    if (e.ErrorCode.Equals("BlobAlreadyExists", StringComparison.OrdinalIgnoreCase) && retryCount < _retryCountSetting)
                    {
                        continue;
                    }
                    else
                    {
                        throw new Exception($"Failed to create blob with FHIR response in {_storageContainerName}. {e.Message} -> {e.StackTrace}");
                    }
                }
                catch (Exception e)
                {
                    throw new Exception($"Failed to create blob with FHIR response in {_storageContainerName}. {e.Message} -> {e.StackTrace}");
                }
            }
        }
    }
}
