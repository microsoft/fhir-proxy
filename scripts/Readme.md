# FHIR-Proxy Getting startd scripts Readme
Script purpose, order of execution and other steps necessary to get up and running with FHIR-Proxy


## Prerequisites 

These scripts will gather (and export) information necessary to the proper operation of the FHIR-Proxy and will store that information into the FHIR-Proxy Keyvault.  
 - Prerequisites:  Azure API for FHIR 
 - Prerequisites:  Global Admin Privelages to resiter Application Service Clients 
 - Prerequisites:  Subscription Contributor role to provision resources  


## Step 1.  deployFhirProxy.bash
This is the main component deployment script for the Azure Components and FHIR-Proxy code.  Note that retry logic is used to account for provisioning delays, i.e., networking provisioning is taking some extra time.  Default retry logic is 5 retries.   

Azure Components installed 
 - Function App (FHIR-Proxy) with App Insights and Storage 
 - Function App Service plan 
 - Keyvault (if not already installed)

Information needed by this script 
 - Resource Group Name
 - Resource Group Location 
 - Key Vault Name
 - FHIR Service Information (URL, Service Client ID, Secret, Tenant ID)
 


FHIR-Proxy Application Configuration values loaded by this script 

Name                               | Value                      | Located              
-----------------------------------|----------------------------|--------------------
APPINSIGHTS_INSTRUMENTATIONKEY     | GUID                       | App Service Config  
APPINSIGHTS_CONNECTION_STRING      | InstrumentationKey         | App Service Config 
AzureWebJobsStorage                | Endpoint                   | App Service Config 
FUNCTIONS_EXTENSION_VERSION        | Function Version           | App Service Config 
FUNCTIONS_WORKER_RUNTIME           | Function runtime           | App Service Config
FP-REDISCONNECTION                 | RedisCache connection      | App Service Config
FP-RBAC-CLIENT-ID                  | Client ID                  | Keyvault reference 
FP-RBAC-CLIENT-SECRET              | Client Secret              | Keyvault reference  
FP-RBAC-NAME                       | Client Name                | Keyvault reference 
FP-RBAC-TENANT-NAME                | Tenant ID / Name           | Keyvault reference 
FP-ADMIN-ROLE                      | Proxy Role Name            | App Service Config
FP-PARTICIPANT-ACCESS              | Proxy Role Name            | App Service Config
FP-READER-ROLE                     | Proxy Role Name            | App Service Config
FP-WRITER-ROLE                     | Proxy Role Name            | App Service Config
FP-GLOBAL-ACCESS-ROLES             | Proxy Role Name            | App Service Config
FP-PATIENT-ACCESS-ROLES            | Proxy Role Name            | App Service Config
FP-PARTICIPANT-ACCESS-ROLES        | Proxy Role Name            | App Service Config
FP-STORAGEACCT                     | Storage account connection | App Service Config
FS-TENANT-NAME                     | FHIR Tenant ID / Name      | App Service Config
FS-CLIENT-ID                       | FHIR Client ID             | Keyvault reference  
FS-CLIENT-SECRET                   | FHIR Client Secret         | Keyvault reference  
FS-RESOURCE                        | FHIR Resource              | Keyvault reference   


FP-PRE-PROCESSOR-TYPES=FHIRProxy.preprocessors.TransformBundlePreProcess



## Step 2.  createProxyServiceClient.bash

Created fhir proxy service principal client "$spname" on "$(date)
		echo "This client can be used for OAuth2 client_credentials flow authentication to the FHIR Proxy"
		echo " "

FHIR-Proxy Application Configuration values loaded by this script 

Name                               | Value                      | Located              
-----------------------------------|----------------------------|--------------------
FP-SC-TENANT-NAME                  | GUID                       | App Service Config  
FP-SC-CLIENT-ID                    | InstrumentationKey         | App Service Config 
FP-SC-SECRET                       | Endpoint                   | App Service Config 
FP-SC-RESOURCE                     | Function Version           | App Service Config 
