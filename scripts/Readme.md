# FHIR-Proxy Getting startd scripts Readme
Script purpose, order of execution and other steps necessary to get up and running with FHIR-Proxy

## Errata 
There are no open issues at this time. 

## Prerequisites 

These scripts will gather (and export) information necessary to the proper deployment and configuration FHIR Proxy, an Application Service Principal for RBAC, and if needed a Key Vault and Resource Group.  All secure information will be stored in the Keyvault.  
 - Prerequisites:  User must have rights to deploy resources at the Subscription scope
 - Prerequisites:  User must have Application Administrator rights to assign Consent at the Service Principal scope in Step 2

__Note__
A Keyvault is necessary for securing Service Client Credentials used with the FHIR Service and FHIR-Proxy.  Only 1 Keyvault should be used as this script scans the keyvault for FHIR Service and FHIR-Proxy values. If multiple Keyvaults have been used, please use the [backup and restore](https://docs.microsoft.com/en-us/azure/key-vault/general/backup?tabs=azure-cli) option to copy values to 1 keyvault.

__Note__ 
The FHIR-Proxy scripts are designed for and tested from the Azure Cloud Shell - Bash Shell environment.


### Naming & Tagging
All Azure resource types have a scope that defines the level that resource names must be unique.  Some resource names, such as PaaS services with public endpoints have global scopes so they must be unique across the entire Azure platform.    Our deployment scripts strive to suggest naming standards that group logial connections while aligning with Azure Best Practices.  Customers are prompted to accept a default or suppoly their own names during installation, examples include:

Prefix      | Workload        |  Number     | Resource Type 
------------|-----------------|-------------|---------------
NA          | fhir            | random      | NA 
User input  | secure function | random      | storage 

Resources are tagged with their deployment script and origin.  Customers are able to add Tags after installation, examples include::

Origin              |  Deployment       
--------------------|-----------------
HealthArchitectures | FHIR-Proxy   

---

## Setup 
Please note you should deploy these components into a tenant and subscriotion where you have appropriate permissions to create and manage Application Registrations (ie Application Adminitrator RBAC Role), and can deploy Resources at the Subscription Scope. 

Launch Azure Cloud Shell (Bash Environment)  
  
[![Launch Azure Shell](/docs/images/launchcloudshell.png "Launch Cloud Shell")](https://shell.azure.com/bash?target="_blank")

Clone the repo to your Bash Shell (CLI) environment 
```azurecli-interactive
git clone https://github.com/microsoft/fhir-proxy 
```
Change working directory to the repo Scripts directory
```azurecli-interactive
cd ./fhir-proxy/scripts
```

Make the Bash Shell Scripts used for Deployment and Setup executable 
```azurecli-interactive
chmod +x *.bash 
```

## Step 1.  deployFhirproxy.bash
This is the main component deployment script for the Azure Components.    

Ensure you are in the proper directory 
```azurecli-interactive
cd $HOME/fhir-proxy/scripts
``` 

Launch the deployfhirproxy.bash shell script 
```azurecli-interactive
./deployfhirproxy.bash 
``` 

Optionally the deployment script can be used with command line options 
```azurecli
./deployfhirproxy.bash -i <subscriptionId> -g <resourceGroupName> -l <resourceGroupLocation> -k <keyVaultName> -n <deployPrefix>
```

Azure Components installed 
 - Resource Group (if needed)
 - Key Vault if needed (customers can choose to use an existing Keyvault as long as they have Purge Secrets access)
 - Azure AD Application Service Principal for RBAC 
 - Function App (FHIR-Proxy) with App Insights and Storage 
 - Function App Service plan 

Information needed by this script 
 - FHIR Service Name
 - KeyVault Name 
 - Resource Group Location 
 - Resource Group Name 

Application Configuration values loaded by this script 

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



## Step 2.  createProxyServiceClient.bash
Please review the Setup steps above and that you are in the Azure Cloud Shell (Bash Environment) from Step 1. 

Ensure that you are in the proper directory 
```azurecli-interactive
cd $HOME/fhir-proxy/scripts
``` 

Launch the createproxyserviceclient.bash shell script 
```azurecli-interactive
./createproxyserviceclient.bash 
``` 

Optionally the createproxyserviceclient script can be used with command line options 
```azurecli
./createproxyserviceclient.bash -k <keyVaultName> -n <serviceClient name>
```

Keyvault values loaded by this script 

Name                               | Value                      | Located              
-----------------------------------|----------------------------|--------------------
FP-SC-TENANT-NAME                  | GUID                       | Keyvault reference  
FP-SC-CLIENT-ID                    | Proxy Service Client ID    | Keyvault reference 
FP-SC-SECRET                       | Proxy Service Secret       | Keyvault reference 
FP-SC-RESOURCE                     | FHIR Resource ID           | Keyvault reference 
FP-SC-URL                          | Proxy URL                  | Keyvault reference 

---

# References 
FHIR-Proxy serves as a middle tier application / access and authorization endpoint.  To better understand the difference in these approaches users should review 

- Client Credentials, or Implicit Oauth flow with token 
- Auth clode flow with code for token exchange

To request an access token users make an HTTP POST to the tenant-specific Microsoft identity platform token endpoint with the following parameters.

```azurecli
https://login.microsoftonline.com/<tenant>/oauth2/v2.0/token
```
Overview of Proxy Auth 
![overview](../docs/images/authflow.png)

Note:  When using FHIR-Proxy, refrain from using the FS- Client Credentials

To read more about the Auth flow, refer to this Microsoft Document [doc](https://docs.microsoft.com/en-us/azure/active-directory/develop/v2-oauth2-client-creds-grant-flow)



