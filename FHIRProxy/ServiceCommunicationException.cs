using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Linq;
namespace FHIRProxy
{
    public class ServiceCommunicationException : Exception
    {
        private HttpStatusCode _code = HttpStatusCode.InternalServerError;
        public static readonly HttpStatusCode[] httpStatusCodesWorthRetrying = {
            HttpStatusCode.RequestTimeout, // 408
            HttpStatusCode.InternalServerError, // 500
            HttpStatusCode.BadGateway, // 502
            HttpStatusCode.ServiceUnavailable, // 503
            HttpStatusCode.GatewayTimeout, // 504
            HttpStatusCode.TooManyRequests //429
        };
        public ServiceCommunicationException(HttpStatusCode status)
        {
            _code = status;
        }
        public HttpStatusCode getStatus()
        {
            return _code;
        }
        public bool isRetryable()
        {
            return httpStatusCodesWorthRetrying.Contains(_code);
        }
    }
}
