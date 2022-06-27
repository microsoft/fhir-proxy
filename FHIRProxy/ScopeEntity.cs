
using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace FHIRProxy
{
    public class ScopeEntity : TableEntity
    {
        public ScopeEntity()
        {

        }
        public ScopeEntity(string applicationId,string principalId)
        {
            this.PartitionKey = applicationId;
            this.RowKey = principalId;
        }
        public string RequestedScopes { get; set; }
        public string ISSRefreshToken { get; set; }
        public DateTime ValidUntil { get; set; }
    }
}
