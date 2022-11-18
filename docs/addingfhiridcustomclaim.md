## Adding fhirUser as Custom Claim to AAD OAuth Tokens
To support SMART Launch, you are required to add a custom claim in the OAuth id_token issued by the identity provider. This custom claim is named ```fhirUser``` and it should contain a valid FHIR server resource reference that identifies/asscociates the authenticated user in the FHIR Server. This is used as context to restrict requested FHIR Server content to resources the authenticated user can access by policy and SMART application scopes. This document describes how to associate Active Directory user accounts with a specific FHIR resource on the FHIR Server and to populate the ```fhirUser``` custom claim. Please see the [SMART App Launch Framework](http://www.hl7.org/fhir/smart-app-launch/) for details.</br>

### Prerequisites
1. You must have rights to register applications, administer claims policy and read/write user profiles in your AAD Tenant
2. You have installed the fhir-proxy in the tenant.
3. You have registered a SMART Application with Azure Active directory and Delegated required SMART scopes from the fhir-proxy.
### Configure fhirUser Custom Claim Policy
1. [Download and Install Windows Powershell for your platform](https://docs.microsoft.com/en-us/powershell/scripting/install/installing-powershell?view=powershell-7.1)
2. Launch Powershell with Administrator privledges
3. Follow the [prerequisites](https://docs.microsoft.com/en-us/azure/active-directory/develop/active-directory-claims-mapping#prerequisites) for running AD Policy Commands
4. Create the custom claim fhirUser for the OAuth tokens and use an available onPremisesExtensionAttribute to store the mapping. In this case we will use the onPremisesExtensionAttribute extensionAttribute1 to store the FHIR resource Id of the user:
   ```
    New-AzureADPolicy -Definition @('{
        "ClaimsMappingPolicy":
        {
            "Version":1,"IncludeBasicClaimSet":"true", 
            "ClaimsSchema": [{"Source":"user","ID":"extensionAttribute1","SamlClaimType":"http://schemas.xmlsoap.org/ws/2005/05/identity/claims/fhirUser","JwtClaimType":"fhirUser"}]
        }
    }') -DisplayName "FHIRUserClaim" -Type "ClaimsMappingPolicy"
    ```
    <I>Note: You can use the [Microsoft Graph Explorer](https://developer.microsoft.com/en-us/graph/graph-explorer) to find available onPremisesExtensionAttribute to use to store the FHIR Resource ID mapping in. Make sure you use an attribute that is not in use for other purposes.</I>

5. Get the new policy mapping id:
    ```
    Get-AzureADPolicy
    ```
<I>Note: This is done only once for the AAD Tenant</I>

### Associate AAD User with FHIR Server Resource Reference
1. Obtain the resource FHIR logical id of the AAD user. You can query the FHIR Server using any valid target resource query parameters to match to the AAD User. The target resources recognized by the SMART Proxy are currently Patient, Practitioner and RelatedPerson
2. Map the FHIR server resource logical id into the onPremisesExtensionAttribute specified in the claims schema above (e.g. extensionAttribute1 in this case):
    1. Open [Microsoft Graph Explorer](https://developer.microsoft.com/en-us/graph/graph-explorer), sign in with your tenant admin account 
    2. Select Patch option, for the request
    3. Select beta for the api version
    4. Use the endpoint: ```https://graph.microsoft.com/beta/users/{oid of the AD user account to add FHIR Mapping too}```
    5. For Request Body, paste the following:
       ```
        {"onPremisesExtensionAttributes":{"extensionAttribute1":"<fhir logical id>"}}
       ```
        <I>Note: <fhir logical id> should be in the form of <resourceType/logicalid> for example to map the AAD user to a Patient resource with logical id 1234 the form of the value should be ```Patient/1234```</I>
    6. Then select Run Query
    7. To confirm, run a GET request on the same endpoint and check the extensionAttribute1 value. You should see the fhir logical id in extensionAtrribute1:
```
        
        "onPremisesExtensionAttributes": {
            "extensionAttribute1": "Patient/1234",
            "extensionAttribute2": null,
        }
```
<I>Note: This should be done for each user accessing any SMART Applications</I>
### Add a new SMART Client registration
Launch Azure Cloud Shell (Bash Environment)  
  
[![Launch Azure Shell](/images/launchcloudshell.png "Launch Cloud Shell")](https://shell.azure.com/bash?target="_blank")

Clone the repo to your Bash Shell (CLI) environment 
```azurecli-interactive
git clone --branch v2.0 https://github.com/microsoft/fhir-proxy
```
Change working directory to the repo Scripts directory
```azurecli-interactive
cd $HOME/fhir-proxy/scripts
```

Make the Bash Shell Scripts used for Deployment and Setup executable 
```azurecli-interactive
chmod +x *.bash 
```
The included script createProxySmartClient will generate SMART Service Clients that can be used to authenticate to the fhir-proxy using the interactive OAuth2.0 authorization code flow.
This is used for user authentication and consent to access the FHIR Server bound by defined SMART scopes.

You will need the following information about the SMART Client:
1. Valid Reply URL(s) for the SMART application
2. The SMART Scopes needed for the SMART application to function (e.g. fhirUser launch/patient patient/Patient.read patient/Observation.read)
3. If the SMART Application requires public or private access
Ensure that you are in the proper directory 
```azurecli-interactive
cd $HOME/fhir-proxy/scripts
``` 

Launch the createproxysmartclient.bash shell script 
```azurecli-interactive
./createproxysmartclient.bash 
``` 

It is recommended to use the createproxysmartclient script with command line options 
```azurecli
./createproxysmartclient.bash -k <keyvault> -n <smart client name> -a (If you are a tenant admin, add FHIRUserClaim to smartapp id token to support Launch context, must have FHIRUserClaim custom claim policy defined for the tenant) -p (to generate postman environment) -u (For Public Client Registration)
```
For example: Let's say you have a SMART Application to register called patientbrowser that allows a patient to view their medical record, with the following details:</br>
The application reply URL is: ```https://somepatientbrowser.com/auth/callback```.</br>
The application requires the following scopes: ``` fhirUser launch/patient patient/Patient.read patient/Condition.read patient/Observation.read```</br>
The application requires public access.</br>
The keyvault created from the fhir-proxy deployment is called ```proxykv123```</br>
You are an application administrator and want to register this application with the fhirUser claim custom policy.</br>

You would launch the createproxysmartclient.bash script from the command shell using the following:</br>
```./createproxysmartclient.bash -k proxykv123 -n patientbrowser -a -u -p```</br>

### Add the fhirUser custom claim policy to your existing registered SMART Application Principal
<I>Note: Follow this section instructions only if you have an existing SMART app registered you do not need to do these steps if you used the createproxysmartclient script with the -a option.</I>
1. 1. Find the Object Id of your registered SMART Application
   1. Access [Azure Portal](https://portal.azure.com)
   2. Goto Azure Active Directory
   3. Goto Enterprise Applications Blade
   4. Change Application Type to All Applications
   5. Enter the name of your SMART registered application
   6. Select Your SMART Application
   7. The Object Id is under Properties
2. Add the fhirUser custom claim policy to the SMART Application   
   1. From your Powershell console window execute the following: 
    ```
        Add-AzureADServicePrincipalPolicy -Id <ObjectId of the SMART Service Principal> -RefObjectId <ObjectId of the Policy>
    ```
3. Validate assignment was successful, from your powershell console execute this command:
    ```
        Get-AzureADServicePrincipalPolicy -Id <ObjectId of the SMART Application>
    ```
4. Allow Mapped Claims on your SMART Application.
   1. Access [Azure Portal](https://portal.azure.com)
   2. Goto Azure Active Directory
   3. Goto the App Registration Blade
   4. Select your SMART Application Registration
   5. Select Manifest
   6. Edit the JSON, find acceptedMappedClaims and set it to true.
    ```"acceptMappedClaims": true```
   7. Save the Manifest

<I>Note: This should be done for each SMART Application you register</I>


### Runtime Usage      
When the user authenticates via the SMART Application client a new claim type 'fhirUser' with the fhir Patient logical id of the user will be in the access and id tokens. This claim will be read by the proxy and used to scope requests to the FHIR Server to include only resources for the Patient specified. The value will also be placed in SMART token response patient field


---

### How to file issues and get help  

This project uses GitHub Issues to track bugs and feature requests. Please search the existing 
issues before filing new issues to avoid duplicates.  For new issues, file your bug or 
feature request as a new Issue.

For help and questions about using this project, please open an [issue](https://github.com/microsoft/fhir-proxy/issues) against the Github repository. We actively triage these and will work on this as best effort.

### Microsoft Support Policy  

Support for this **PROJECT or PRODUCT** is limited to the resources listed above.