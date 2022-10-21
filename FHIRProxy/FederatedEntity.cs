
using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace FHIRProxy
{
    public class FederatedEntity : TableEntity
    {
        public FederatedEntity()
        {

        }
        public FederatedEntity(string clientId)
        {
            this.PartitionKey = "federatedentities";
            this.RowKey = clientId;
            this.ValidUntil = DateTime.MaxValue;
        }
        public string ClientId {
            get { return this.RowKey; }
        }
        //Comma Delimeted string of valid issuers
        public string ValidIssuers { get; set; }
        public string ValidAudiences { get; set; }
        public DateTime ValidUntil { get; set; }
    }
}
