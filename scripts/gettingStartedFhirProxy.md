# FHIR-Proxy Getting startd scripts Readme
Script purpose, order of execution and other steps necessary to get up and running with FHIR-Proxy


## Prerequisites 

These scripts will gather (and export) information necessary to the proper operation of the FHIR-Proxy and will store that information into the FHIR-Proxy Keyvault.  
 - Prerequisites:  Azure API for FHIR 
 - 
 - Prerequisites:  


## Step 1.  deployFhirProxy.bash
This is the main component deployment script for the Azure Components and Sync Agent code.  Note that retry logic is used to account for provisioning delays, i.e., networking provisioning is taking some extra time.  Default retry logic is 5 retries.   

Azure Components installed 
 - Function App (FHIR-Proxy) with App Insights and Storage 
 - Function App Service plan 
 - Keyvault

Information needed by this script 
 - 
 

Creating Service Principal for AAD Auth (FP-RBAC settings )

FHIR-Proxy Application Configuration values loaded by this script 

Name                               | Value                      | Located              
-----------------------------------|----------------------------|--------------------
APPINSIGHTS_INSTRUMENTATIONKEY     | GUID                       | App Service Config  
APPINSIGHTS_CONNECTION_STRING      | InstrumentationKey         | App Service Config 
AzureWebJobsStorage                | Endpoint                   | App Service Config 
FUNCTIONS_EXTENSION_VERSION        | Function Version           | App Service Config 
FUNCTIONS_WORKER_RUNTIME           | Function runtime           | App Service Config
FP-RBAC-CLIENT-ID                  |                            | Keyvault reference -> client ID
FP-RBAC-CLIENT-SECRET              |                            | Keyvault reference  
FP-RBAC-NAME                       |                            | Keyvault reference FP-RBAC-TENANT-NAME                |                            | Keyvault reference 
FP-REDISCONNECTION


## Step 2.  createProxyServiceClient.bash

Created fhir proxy service principal client "$spname" on "$(date)
		echo "This client can be used for OAuth2 client_credentials flow authentication to the FHIR Proxy"
		echo " "

 "FP-SC-TENANT-NAME" --value $sptenant)
 "FP-SC-CLIENT-ID" --value $spappid)
 "FP-SC-SECRET" --value $spsecret)
 "FP-SC-RESOURCE" --value $fpclientid) -> points to FP-RBAC-CLIENT-ID