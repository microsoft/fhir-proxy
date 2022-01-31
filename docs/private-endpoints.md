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

## Step 3.  Configure FHIR Proxy to work with the Virtual Network and Private Links
Azure Functions offer multiple networking features across the hosting options. All the Azure Function networking options give customers some ability to access resources without using internet-routable addresses or to restrict internet access to a function app - see this **[link](https://docs.microsoft.com/en-us/azure/azure-functions/functions-networking-options)** for options. 

**a) Virtual network integration**
Virtual network integration gives your app access to resources in your virtual network, but it doesn't grant inbound private access to your app from the virtual network.  Virtual network integration is used only to make outbound calls from your app into your virtual network. The VNet integration feature behaves differently when it's used with virtual networks in the same region and with virtual networks in other regions.  [Read more](https://docs.microsoft.com/en-us/azure/azure-functions/functions-networking-options#virtual-network-integration)

**[Enable VNet Integration](https://docs.microsoft.com/en-us/azure/azure-functions/functions-networking-options#enable-vnet-integration)**

![vnet-integration](./images/private-endpoints/vnet-integration.png)
   
At this point the FHIR-Proxy acts as an application gateway, as it is the only external entry point to your FHIR Service.  
  
  
**b) Private Endpoint Connections**
When creating an inbound private endpoint connection for functions, you will also need a DNS record to resolve the private address. By default a private DNS record will be created for you when creating a private endpoint using the Azure portal.

To learn more, see [using Private Endpoints for Web Apps](https://docs.microsoft.com/en-us/azure/app-service/networking/private-endpoint).


**Tutorial:** Integrate Azure Functions with an Azure virtual network by using private endpoints. [link](https://docs.microsoft.com/en-us/azure/azure-functions/functions-create-vnet)

**b-1)** Stop the FHIR Proxy App
 
**b-2)** [Create the private endpoints for Azure Files Storage](https://docs.microsoft.com/en-us/azure/azure-functions/functions-create-vnet#lock-down-your-storage-account)

![storage-private-endpoint](./images/private-endpoints/storage-private-endpoint.png)

_Note: The private DNS update may fail due to dynamic IP's on the function_ - see sample error
```json
  {
    "status": "Failed",
    "error": {
        "code": "InvalidPrivateDnsZoneIds",
        "message": "Private Dns Zone group /subscriptions/###/resourceGroups/rg-fhir-dev-northcentralus-1432/providers/Microsoft.Network/privateEndpoints/pi-proxy4910-store/privateDnsZoneGroups/default has invalid private dns zone ids .",
        "details": []
    }
}
```
To correct this customers must add the Storage Private endpoint to the Private DNS setup 

**b-3)** Congirure Function App Settings  

https://docs.microsoft.com/en-us/azure/azure-functions/functions-create-vnet#configure-your-function-app-settings

### Private Endpoint Application Configuration settings 

[WEBSITE_CONTENTAZUREFILECONNECTIONSTRING](https://docs.microsoft.com/en-us/azure/azure-functions/functions-app-settings#website_contentazurefileconnectionstring) 
Connection string for storage account where the function app code and configuration are stored in event-driven scaling plans running on Windows 

[WEBSITE_CONTENTSHARE](https://docs.microsoft.com/en-us/azure/azure-functions/functions-app-settings#website_contentshare)
The file path to the function app code and configuration in an event-driven scaling plan on Windows. Used with ```WEBSITE_CONTENTAZUREFILECONNECTIONSTRING```. Default is a unique string that begins with the function app name

[WEBSITE_CONTENTOVERVNET](https://docs.microsoft.com/en-us/azure/azure-functions/functions-app-settings#website_contentovervnet)
A value of ```1``` enables your function app to scale when you have your storage account restricted to a virtual network. You should enable this setting when restricting your storage account to a virtual network. To learn more, see [Restrict your storage account to a virtual network](https://docs.microsoft.com/en-us/azure/azure-functions/configure-networking-how-to#restrict-your-storage-account-to-a-virtual-network).

[WEBSITE_DNS_SERVER](https://docs.microsoft.com/en-us/azure/azure-functions/functions-app-settings#website_dns_server)
Sets the DNS server used by an app when resolving IP addresses. This setting is often required when using certain networking functionality, such as [Azure DNS private zones](https://docs.microsoft.com/en-us/azure/azure-functions/functions-networking-options#azure-dns-private-zones) and [private endpoints](https://docs.microsoft.com/en-us/azure/azure-functions/functions-networking-options#restrict-your-storage-account-to-a-virtual-network).

_Note:  the VNET_Route_All setting has been replaced with a switch in the VNET Configuration blade, however it can still be used in the Application Configuration_
  
[WEBSITE_VNET_ROUTE_ALL](https://docs.microsoft.com/en-us/azure/azure-functions/functions-app-settings#website_vnet_route_all)
Indicates whether all outbound traffic from the app is routed through the virtual network. A setting value of 1 indicates that all traffic is routed through the virtual network. You need this setting when a [virtual network NAT gateway is used to define a static outbound IP address](https://docs.microsoft.com/en-us/azure/azure-functions/functions-how-to-use-nat-gateway).

**b-5)** Start the FHIR Proxy App

**b-6)** Configure the Function App Private Endpoint
If you want to make calls to Private Endpoints, then you must make sure that your DNS lookups resolve to the private endpoint. You can enforce this behavior by integrating with the Azure DNS private zone setup with FHIR above. [link](https://docs.microsoft.com/en-us/azure/azure-functions/functions-networking-options#private-endpoints)

![app-private-endpoint](./images/private-endpoints/proxy-app-private-endpoint.png)

 


## References 

Azure / Github Code Samples 

Connect to private endpoints with Azure Functions
https://docs.microsoft.com/en-us/samples/azure-samples/azure-functions-private-endpoints/connect-to-private-endpoints-with-azure-functions/ 

Azure Functions, Private Endpoints, and NAT Gateway
https://docs.microsoft.com/en-us/samples/azure-samples/non-http-trigger-azure-functions-premium-plan/non-http-trigger-azure-functions-premium-plan/ 
  

Application Configuration values loaded by this script 

Name                                     | Value                      | Located              
-----------------------------------------|----------------------------|-------------------- 
WEBSITE_CONTENTAZUREFILECONNECTIONSTRING | Storage Connection String  | App Service Config 
WEBSITE_CONTENTSHARE                     | String value of File path  | App Service Config 
WEBSITE_CONTENTOVERVNET                  | Fixed Value of 1 or 0      | App Service Config 
WEBSITE_DNS_SERVER                       | IP Address of Private DNS  | App Service Config
WEBSITE_VNET_ROUTE_ALL                   | Fixed Value of 1 or 0      | App Service Config 


