using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FHIRProxy
{
    public class ProxyConstants
    {
        public const string FEDERATED_APP_TABLE = "fedappregistrations";
        public const string IDENTITY_LINKS_TABLE = "identitylinks";
        public const string SCOPE_STORE_TABLE = "scopestore";
        public const string EXPORT_AGGREGATE_TABLE = "exportaggregate";
        public const string SMART_SESSION_ID_COOKIE = "x-ms-smartsession-id";
    }
}
