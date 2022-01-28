# Private Endpoint Setup 

The recommended approach to using FHIR-Proxy with Private Endpoints is to deploy FHIR-Proxy without the private endpoints, ensure it is working, then cut over to the Private Endpoints.  This approach allows customers to troubleshoot any potential issues as they appear. 

_Sample VNet configuration with FHIR and Proxy_

![network-diagram](./images/private-endpoints/network-diagram.png)
  
## Getting Started 
This setup expects customers already have an Azure FHIR Service along with FHIR Proxy up and running.  This setup will support additional components that work with VNet's and VNet Peering

**Prerequisites:**
- An Azure account with an active subscription
- An Azure Web App with a PremiumV2-tier or higher app service plan deployed in your Azure subscription.  _Note:  By default Proxy Function apps are deployed at a B1 SKU, therefore the App Service Plan must be Upgraded to a Premium V2 SKU_

![app-service-plan](./images/private-endpoints/app-service-plan.png) 

For more information and an example, see [Quickstart: Create an ASP.NET Core web app in Azure](https://docs.microsoft.com/en-us/azure/app-service/quickstart-dotnetcore)
  
For a detailed tutorial on creating a web app and an endpoint, see [Tutorial: Connect to a web app using an Azure Private Endpoint](https://docs.microsoft.com/en-us/azure/private-link/tutorial-private-endpoint-webapp-portal)


## Step 1. Creating a Virtual Network and Private Endpoint using the Azure portal 
Get started with Azure Private Link by using a Private Endpoint to connect securely to an Azure web app.  Instructing for setting up the Private Endpoints are **[here](https://docs.microsoft.com/en-us/azure/private-link/create-private-endpoint-portal)**

**a) Create a Virtual Network and Bastion Host [link](https://docs.microsoft.com/en-us/azure/private-link/create-private-endpoint-portal#create-a-virtual-network-and-bastion-host)**  

A private endpoint is a network interface that uses a private IP address from your virtual network. This network interface connects you privately and securely to a service powered by Azure Private Link. By enabling a private endpoint, you're bringing the service into your virtual network.    

Private Endpoint properties are defined in detail [here](https://docs.microsoft.com/en-us/azure/private-link/private-endpoint-overview#private-endpoint-properties).


**b) Create Sub-Nets within the Virtual Network (CIDR /24 preferred)**

Subnet Setup 
![subnets](./images/private-endpoints/vnet-subnets.png)

**c) Create a Virtual Machine for testing [example](https://docs.microsoft.com/en-us/azure/private-link/create-private-endpoint-portal#create-a-virtual-machine)**

  
_Note:  It is recommended that customers setup a VM with [Postman](https://www.postman.com/) on the vNet for testing see [create a virtual machine](https://docs.microsoft.com/en-us/azure/private-link/create-private-endpoint-portal#create-a-virtual-machine)_



## Step 2.  Configure Azure FHIR Private Link   
Private link enables you to access Azure API for FHIR over a private endpoint, which is a network interface that connects you privately and securely using a private IP address from your virtual network. With private link, you can access our services securely from your VNet as a first party service without having to go through a public Domain Name System (DNS). This **[article](https://docs.microsoft.com/en-us/azure/healthcare-apis/azure-api-for-fhir/configure-private-link)** describes how to create, test, and manage your private endpoint for Azure API for FHIR.

![fhir-setup](./images/private-endpoints/fhir-setup.png)


At this point you should be able to test your private endpoint using either your VM with Postman - or - a PC connected to the VPN Gateway (above).  Connections from the VNet should work, connections outside the VNet should fail.

Example using Postman on a VM connected via a Bastion Host.  Use these links for sample Postman [Environment](../samples/private_endpoint.postman_environment.json) and [Collection](../samples/FHIR_Commands.postman_collection.json) files. 

![fhir-test1](./images/private-endpoints/test-fhir1.png)

## Step 3.  Configure FHIR Proxy to work with the Virtual Network
   
Private link enables you to access Azure API for FHIR over a private endpoint, which is a network interface that connects you pr


### Private Endpoint Application Configuration settings 

[WEBSITE_CONTENTAZUREFILECONNECTIONSTRING](https://docs.microsoft.com/en-us/azure/azure-functions/functions-app-settings#website_contentazurefileconnectionstring) 
Connection string for storage account where the function app code and configuration are stored in event-driven scaling plans running on Windows 

[WEBSITE_CONTENTOVERVNET](https://docs.microsoft.com/en-us/azure/azure-functions/functions-app-settings#website_contentovervnet)
A value of ```1``` enables your function app to scale when you have your storage account restricted to a virtual network. You should enable this setting when restricting your storage account to a virtual network. To learn more, see [Restrict your storage account to a virtual network](https://docs.microsoft.com/en-us/azure/azure-functions/configure-networking-how-to#restrict-your-storage-account-to-a-virtual-network).

[WEBSITE_CONTENTSHARE](https://docs.microsoft.com/en-us/azure/azure-functions/functions-app-settings#website_contentshare)
The file path to the function app code and configuration in an event-driven scaling plan on Windows. Used with ```WEBSITE_CONTENTAZUREFILECONNECTIONSTRING```. Default is a unique string that begins with the function app name

[WEBSITE_DNS_SERVER](https://docs.microsoft.com/en-us/azure/azure-functions/functions-app-settings#website_dns_server)
Sets the DNS server used by an app when resolving IP addresses. This setting is often required when using certain networking functionality, such as [Azure DNS private zones](https://docs.microsoft.com/en-us/azure/azure-functions/functions-networking-options#azure-dns-private-zones) and [private endpoints](https://docs.microsoft.com/en-us/azure/azure-functions/functions-networking-options#restrict-your-storage-account-to-a-virtual-network).

[WEBSITE_VNET_ROUTE_ALL](https://docs.microsoft.com/en-us/azure/azure-functions/functions-app-settings#website_vnet_route_all)
Indicates whether all outbound traffic from the app is routed through the virtual network. A setting value of 1 indicates that all traffic is routed through the virtual network. You need this setting when a [virtual network NAT gateway is used to define a static outbound IP address](https://docs.microsoft.com/en-us/azure/azure-functions/functions-how-to-use-nat-gateway).

Application Configuration values loaded by this script 

Name                                     | Value                      | Located              
-----------------------------------------|----------------------------|--------------------
APPINSIGHTS_INSTRUMENTATIONKEY           | GUID                       | App Service Config  
APPINSIGHTS_CONNECTION_STRING            | InstrumentationKey         | App Service Config 
AzureWebJobsStorage                      | Endpoint                   | App Service Config 
FUNCTIONS_EXTENSION_VERSION              | Function Version           | App Service Config 
FUNCTIONS_WORKER_RUNTIME                 | Function runtime           | App Service Config
FP-REDISCONNECTION                       | RedisCache connection      | App Service Config
FP-RBAC-CLIENT-ID                        | Client ID                  | Keyvault reference 
FP-RBAC-CLIENT-SECRET                    | Client Secret              | Keyvault reference  
FP-RBAC-NAME                             | Client Name                | Keyvault reference 
FP-RBAC-TENANT-NAME                      | Tenant ID / Name           | Keyvault reference 
FP-ADMIN-ROLE                            | Proxy Role Name            | App Service Config
FP-PARTICIPANT-ACCESS                    | Proxy Role Name            | App Service Config
FP-READER-ROLE                           | Proxy Role Name            | App Service Config
FP-WRITER-ROLE                           | Proxy Role Name            | App Service Config
FP-GLOBAL-ACCESS-ROLES                   | Proxy Role Name            | App Service Config
FP-PATIENT-ACCESS-ROLES                  | Proxy Role Name            | App Service Config
FP-PARTICIPANT-ACCESS-ROLES              | Proxy Role Name            | App Service Config
FP-STORAGEACCT                           | Storage account connection | App Service Config
FS-TENANT-NAME                           | FHIR Tenant ID / Name      | App Service Config
FS-CLIENT-ID                             | FHIR Client ID             | Keyvault reference  
FS-CLIENT-SECRET                         | FHIR Client Secret         | Keyvault reference  
FS-RESOURCE                              | FHIR Resource              | Keyvault reference   
WEBSITE_CONTENTAZUREFILECONNECTIONSTRING | Storage Connection String  | App Service Config 
WEBSITE_CONTENTOVERVNET                  | Fixed Value of 1 or 0      | App Service Config 
WEBSITE_CONTENTSHARE                     | String value of File path  | App Service Config 
WEBSITE_DNS_SERVER                       | IP Address of Private DNS  | App Service Config
WEBSITE_VNET_ROUTE_ALL                   | Fixed Value of 1 or 0      | App Service Config 


