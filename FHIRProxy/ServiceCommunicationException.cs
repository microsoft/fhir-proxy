using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Linq;
namespace FHIRProxy
{
    public class ServiceCommunicationException : Exception
    {
        public static readonly HttpStatusCode[] httpStatusCodesWorthRetrying = {
            HttpStatusCode.RequestTimeout, // 408
            HttpStatusCode.InternalServerError, // 500
            HttpStatusCode.BadGateway, // 502
            HttpStatusCode.ServiceUnavailable, // 503
            HttpStatusCode.GatewayTimeout, // 504
            HttpStatusCode.TooManyRequests //429
        };
        public ServiceCommunicationException(HttpStatusCode status, string responsecontent = null, string requestcontent = null, string requesturl = null)
        {
            this.Status = status;
            this.ResponseBody = responsecontent;
            this.RequestUrl = requesturl;
            this.RequestBody = requestcontent;
        }
        public string RequestUrl { get; set; }
        public string RequestBody { get; set; }
        public string ResponseBody { get; set; }
        public HttpStatusCode Status { get; set; }
        public bool isRetryable()
        {
            return httpStatusCodesWorthRetrying.Contains(Status);
        }
    }
}
