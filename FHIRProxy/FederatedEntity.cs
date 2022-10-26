
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
        public FederatedEntity(string clientId, string name)
        {
            this.PartitionKey = "federatedentities";
            this.RowKey = clientId;
            this.Status = "active";
            this.Name = name;
        }
        public string ClientId {
            get { return this.RowKey; }
        }
        //Comma Delimeted string of valid issuers
        public string Name { get; set; }
        public string ValidIssuers { get; set; }
        public string ValidAudiences { get; set; }
        public string Scope { get; set; }
        public string JWKSetUrl { get; set; }
        public string Status { get; set; }
    }
}
