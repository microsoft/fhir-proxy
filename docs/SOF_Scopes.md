# **Smart on FHIR Scope Configuration for Registered Applications**

If you have not already created a service principle for your FHIR Proxy, check out this \&lt;\&lt;link\&gt;\&gt; for instructions on how to do so.

In this guide you will learn how to configure scopes for your Smart on FHIR applications to utilize the oauth permissions exposed in your FHIR Proxy Application.

Detailed information about FHIR launch contexts and scope can be found [here](http://www.hl7.org/fhir/smart-app-launch/scopes-and-launch-context/).

Scopes are used to limit the resources a SMART on FHIR app can access within a launch context. It is best practice to grant the minimum scopes necessary for an application. A scope is made up of the context, the FHIR resource and the type of action that can be taken.

![](images/smart_on_fhir_1.png)

When using the configuring the FHIR Proxy for a SMART on FHIR application follow these steps:

_These can be done through the Azure portal or though the Azure CLI. Here will cover the Azure Portal workflow_

1. **Create a registered application service principle in Azure AD for the Smart on FHIR application**

For more information on the designed interaction between the Smart on FHIR app, the service principle and the authentication on the FHIR proxy check out this [Application Model walk through](https://docs.microsoft.com/en-us/azure/active-directory/develop/application-model)

![](images/smart_on_fhir_2.png)

1. **Create context, resource, and action type specific scopes**
2. **Add designated scopes to the API for the Smart on FHIR registered application service client**
3. **Configure the redirect URL for the Smart on FHIR application in the registered application service client**

1. **Create a registered application in Azure AD**

This tutorial provides a step by step for **creating a registered application in Azure AD**

[Register a service app in Azure AD - Azure API for FHIR | Microsoft Docs](https://docs.microsoft.com/en-us/azure/healthcare-apis/fhir/register-service-azure-ad-client-app)

1. To **create context, resource, and action type specific scopes** open the Expose an API blade in the registered application

![](images/smart_on_fhir_3.png)

Click + Add Scope and name the scope according to the context.resource.action naming convention

![](images/smart_on_fhir_4.png)

You will now have a scope defined that can be delegated to the Smart on FHIR app API ![](RackMultipart20210623-4-2pa71h_html_f670d17e80e983e6.png)

For more information on creating scopes, check out this [quickstart](https://docs.microsoft.com/en-us/azure/active-directory/develop/quickstart-configure-app-expose-web-apis#:~:text=Sign%20in%20to%20the%20Azure%20portal.%20If%20you,Select%20Expose%20an%20API%20%3E%20Add%20a%20scope.)

1. Determine the minimal set of scope your Smart on FHIR application needs and **add designated scopes to the API for the Smart on FHIR registered application service client**

Open the API permissions blade on the registered application and Click + Add a permission. The scopes you created in step 2 should be available to add to your application.

![](images/smart_on_fhir_5.png)

1. To enable the authentication flow outlined below **configure the redirect URL for the Smart on FHIR application in the service client**

To do this, open the Authentication blade on the registered application. click + Add a platform, select Web and enter the redirect URL from your Smart on FHIR app.

![](images/smart_on_fhir_6.png)

You have now configured Azure AD to facilitate the authorization workflow below for you Smart on FHIR app and the FHIR Proxy service principle.

![](images/smart_on_fhir_7.png)

![](images/smart_on_fhir_8.gif)

FHIR Proxy

_ **User to Patient Mapping to utilize the Patient Context** _

To launch an app in the patient context, an Azure AD Identifier will need to be mapped to a FHIR Patient resource Id. This can be done as part of the authentication process of the Smart on FHIR app, using a third-party to authorize on the user&#39;s behalf, or by entering a mapping into the Identitylinks table in the FHIR Proxy storage account.

Mapping an Azure AD Identifier to a FHIR Patient resource Id allows the person logging in with the patient context to access data for their patient Id only.

Entering a mapping in the Identitylinks table can easily be done through the **Azure Storage Account Explorer**.

1. Connect Azure Storage Account Explorer to your Storage Account
![](images/smart_on_fhir_9.png) 

Select storage account or service

![](images/smart_on_fhir_10.png)

Select connection string. You can find the connection string on Access keys blade for the Storage Account in the Azure Portal

![](images/smart_on_fhir_11.png)

Once you&#39;ve connected to the storage account you can open the Identitylinks table and add and entry using the + Add button.

![](images/smart_on_fhir_12.png)

The **RowKey** is the Azure AD Object Id for the user logging in and the **LinkedResourceId** is the FHIR Patient Id.

![](images/smart_on_fhir_3.png) 
The Azure AD Object Id can be found in Azure AD -\&gt; Users -\&gt; the selected user on the Profile blade.