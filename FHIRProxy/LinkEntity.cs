
using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace FHIRProxy
{
    public class LinkEntity : TableEntity
    {
        public LinkEntity()
        {

        }
        public LinkEntity(string resourceType,string principalId)
        {
            this.PartitionKey = resourceType;
            this.RowKey = principalId;
        }
        public string LinkedResourceId { get; set; }
        public DateTime ValidUntil { get; set; }
    }
}
