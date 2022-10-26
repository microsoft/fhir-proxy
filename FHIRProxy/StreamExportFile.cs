using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace FHIRProxy
{
    public class StreamExportFile
    {
        public static async Task<FileStreamResult> ExportFile(ClaimsPrincipal principal,string path,ILogger log)
        {
            ClaimsIdentity ci = (ClaimsIdentity)principal.Identity;
            string cp = path.Replace("/fhir/","");
            string[] exportpath = cp.Split("/");
            string oid = exportpath[1];
            StringBuilder sb = new StringBuilder();
            for (int x=2; x < exportpath.Length; x++)
            {
                if (sb.Length==0)
                {
                    sb.Append(exportpath[x]);

                } else
                {
                    sb.Append($"/{exportpath[x]}");
                }
            }
            if (!ci.ObjectId().Equals(oid))
            {
                throw new Exception($"Token Object Id {ci.ObjectId()} is not authorized to access container {oid}.");
            }
            log.LogInformation($"Downloding:{sb.ToString()}");
            BlobServiceClient blobServiceClient = new BlobServiceClient(Utils.GetEnvironmentVariable("FP-EXPORT-STORAGEACCT"));
            BlobContainerClient container = blobServiceClient.GetBlobContainerClient(oid);
            BlobClient blob = container.GetBlobClient(sb.ToString());
            var blobStream = await blob.OpenReadAsync().ConfigureAwait(false);
            return new FileStreamResult(blobStream, "application/fhir+ndjson");
        }
    }
}
