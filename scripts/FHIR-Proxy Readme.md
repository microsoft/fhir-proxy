# FHIR-Proxy Readme.md

Script purpose and order of execution 

## Prerequisites 

These scripts will gather (and export) information necessary to the proper operation of the FHIR-Proxy and will store that information into the Keyvault installed by the script (deployfhirproxy.bash)
 - Prerequisites:  Azure API for FHIR 
 - Prerequisites:  Clone the FHIR-Proxy repo from GitHub [instructions](https://github.com/microsoft/fhir-proxy#deploying-your-own-fhir-proxy) 
```azurecli
git clone https://github.com/microsoft/fhir-proxy
```


## Step 1.  deployFhirProxy.bash
This is the main component deployment script for the Azure Components and FHIR-Proxy code.  It can be executed with no arguments or with arguments
```azurecli
./deployFhirProxy.bash
```
or
 
```azurecli
./deployFhirProxy.bash -i <subscriptionId> -g <resourceGroupName> -l <resourceGroupLocation> -p <prefix>
```  

Azure Components installed 
 - Function App (FHIR-Proxy)
 - Function App Service plan 
 - KeyVault
 - RedisCache 
 - Storage Account
 - Application Insights 

Information needed by this script 
 - Azure API for FHIR Client Application ID and Secret [instuctions](https://docs.microsoft.com/en-us/azure/healthcare-apis/azure-api-for-fhir/register-confidential-azure-ad-client-app)
 - Azure API for FHIR Client Tenant ID  
 - Resource Group Location (recommended to install this in the same RG as Azure API for FHIR)
 - Resource Group Name (recommended to install this in the same RG as Azure API for FHIR)

 
FHIR-Proxy Application Configuration loaded by this script 

Name                               | Value              | Source 
-----------------------------------|--------------------|---------------------------------------------
SA-EMRAGENT-CONNECTION             | Keyvalut reference | Service Bus Connection Endpoint from Sync-Agent deployment
SA-EMRAGENT-CONNECTION-TOPIC       | Keyvalut reference | Static Queue name from Sync-Agent deployment 
SA-FHIR-USEMSI                     | Keyvalut reference | MSI Identity from Sync-Agent deployment 
SA-FHIRLOADERSTORAGECONNECTION     | Keyvalut reference | Data Store Connection Encpoint from FHIR-Loader deployment
SA-FHIRMAPPEDRESOURCES             | Keyvalut reference | Static Resource list from Sync-Agent setup 
SA-SERVICEBUSNAMESPACECDSUPDATES   | Keyvalut reference | Service Bus Connection Endpoint from Sync-Agent deployment
SA-SERVICEBUSNAMESPACEFHIRUPDATES  | Keyvalut reference | Service Bus Connection Endpoint from Sync-Agent deployment
SA-SERVICEBUSQUEUENAMECDSUPDATES   | Keyvalut reference | Service Bus Connection Endpoint from Sync-Agent deployment
SA-SERVICEBUSQUEUENAMEFHIRBULK     | Keyvalut reference | Static Queue name from Sync-Agent deployment
SA-SERVICEBUSQUEUENAMEFHIRUPDATES  | Keyvalut reference | Static Queue name from Sync-Agent deployment


## Step 2.  createProxyServiceClient.bash
This script can be re-run without issue

 - Prerequisites:  Dataverse Application ID and secret information  

Information needed by this script
 - KeyVault Name installed with FHIR-Proxy
 - CDS Instance URL
 - CDS Tenant ID,
 - CDS Client ID,
 - CDS Client Secret

Sync Agent Application Configuration loaded by this script 

Name                               | Value              | Source 
-----------------------------------|--------------------|---------------------------------------------
SA-CDSAUDIENCE                     | Keyvalut reference | Application URL from Sync-Agent setup 
SA-CDSCLIENTID                     | Keyvalut reference | Application Client ID from Sync-Agent setup
SA-CDSSECRET                       | Keyvalut reference | Application Client Secret from Sync-Agent setup
SA-CDSTENANTID                     | Keyvalut reference | Application Tenant ID from Sync-Agent setup


Dataverse Environment variables EXPORTED by this script.  Admins need the following information to configure Dataverse Environment Variables via FHIR Sync Administration
- Service Bus URL
- Service Queue
- Service Bus Shared Access Policy
- Service Bus Shared Access Policy Key



## Step 3.  controlSyncAgent.bash
Control Sync Agent script will start and stop the Sync Agent by adding or removing the FP-POST-PROCESSOR-TYPES from the FHIR Proxy Application Configuration. 

This script must be run to enable the FHIR-SyncAgent after the install 


FHIR-Proxy (yes, FHIR-Proxy) Application Configuration loaded by this script 

Name                               | Value                     | Source 
-----------------------------------|---------------------------|---------------------------------------------
FP-POST-PROCESSOR-TYPES            | Application Configuration | Proxy Module  

Note:  The current iteration of the FP-POST-PROCESSOR-TYPES is FHIRProxy.postprocessors.FHIRCDSSyncAgentPostProcess2


```bash
Usage: $0 -a <fhir-poxy appname> -g <resourceGroupName> -c <enable or disable>
```

-enable restores the FP-POST-PROCESSOR-TYPES from the FHIR-Proxy Application Configuration and restarts Proxy 

-disable removes the FP-POST-PROCESSOR-TYPES from the FHIR-Proxy Application Configuration and restarts Proxy 
