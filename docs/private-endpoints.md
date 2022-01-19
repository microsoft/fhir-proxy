# Private Endpoint Setup 

## Private Endpoint Information 
A private endpoint is a network interface that uses a private IP address from your virtual network. This network interface connects you privately and securely to a service powered by Azure Private Link. By enabling a private endpoint, you're bringing the service into your virtual network.    

Private Endpoint properties are defined in detail [here](https://docs.microsoft.com/en-us/azure/private-link/private-endpoint-overview#private-endpoint-properties), some key items to understand before setting up Private Endpoints are

- The private endpoint must be deployed in the same region and subscription as the virtual network.

- Multiple private endpoints can be created using the same private link resource. For a single network using a common DNS server configuration, the recommended practice is to use a single private endpoint for a given private link resource. Use this practice to avoid duplicate entries or conflicts in DNS resolution.

- Multiple private endpoints can be created on the same or different subnets within the same virtual network. There are limits to the number of private endpoints you can create in a subscription. For details, see [Azure limits](https://docs.microsoft.com/en-us/azure/azure-resource-manager/management/azure-subscription-service-limits#networking-limits).

- The subscription from the private link resource must also be registered with Microsoft. Network resource provider. For details, see [Azure Resource Providers](https://docs.microsoft.com/en-us/azure/azure-resource-manager/management/resource-providers-and-types).


## Azure FHIR with Private Endpoints 
Private link enables you to access Azure API for FHIR over a private endpoint, which is a network interface that connects you privately and securely using a private IP address from your virtual network. With private link, you can access our services securely from your VNet as a first party service without having to go through a public Domain Name System (DNS). This [article](https://docs.microsoft.com/en-us/azure/healthcare-apis/azure-api-for-fhir/configure-private-link) describes how to create, test, and manage your private endpoint for Azure API for FHIR.


## Recommended Setup


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




