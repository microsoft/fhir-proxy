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
### Add the fhirUser custom claim policy to your registered SMART Application Principal
1. Find the Object Id of your registered SMART Application
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
    5. Then select Run Query
    6. To confirm, run a GET request on the same endpoint and check the extensionAttribute1 value. You should see the fhir logical id in extensionAtrribute1:
```
        
        "onPremisesExtensionAttributes": {
            "extensionAttribute1": "Patient/1234",
            "extensionAttribute2": null,
        }
```
<I>Note: This should be done for each user accessing any SMART Applications</I>
### Runtime Usage      
When the user authenticates via the SMART Application client a new claim type 'fhirUser' with the fhir Patient logical id of the user will be in the access and id tokens. This claim will be read by the proxy and used to scope requests to the FHIR Server to include only resources for the Patient specified and this value will also be placed in SMART patient/launch patient field


---

### How to file issues and get help  

This project uses GitHub Issues to track bugs and feature requests. Please search the existing 
issues before filing new issues to avoid duplicates.  For new issues, file your bug or 
feature request as a new Issue.

For help and questions about using this project, please open an [issue](https://github.com/microsoft/fhir-proxy/issues) against the Github repository. We actively triage these and will work on this as best effort.

### Microsoft Support Policy  

Support for this **PROJECT or PRODUCT** is limited to the resources listed above.