## Adding fhirUser as Custom Claim to AAD OAuth Tokens
To support SMART Launch fhirUser scope to restrict content to the logged in user and/or set patient/user Context, you can associate AAD user accounts with a specific FHIR resource on the FHIR Server. This association allows for populating the fhirUser claim in OAuth tokens and setting context for the calling SMART Applications as detailed in the [SMART App Launch Framework](http://www.hl7.org/fhir/smart-app-launch/)</br>

### Prerequisites
1. <B><I>You must have rights to administer claims policy in your AAD Tenant and read/write user profiles in order to proceed.</B></I>
2. You have installed the fhir-proxy in the tenant.

### Configure fhirUser Custom Claim
1. [Download and Install Windows Powershell for your platform](https://docs.microsoft.com/en-us/powershell/scripting/install/installing-powershell?view=powershell-7.1)
2. Launch Powershell with Administrator privileges.
3. Follow the [prerequisites](https://docs.microsoft.com/en-us/azure/active-directory/develop/active-directory-claims-mapping#prerequisites) for running AD Policy Commands.
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
6. Apply this policy to your FHIR Proxy client principal ID (You can find it in [Azure Portal](https://portal.azure.com) > Enterprise Applications > fhir-proxy install name (e.g. myfhirproxy.azurewebsites.net) > Object Id under Properties:
    ```
        Add-AzureADServicePrincipalPolicy -Id <ObjectId of the fhir-proxy ServicePrincipal> -RefObjectId <ObjectId of the Policy>
    ```
7. To check if it succeeded, you must see the policy by running this command:
    ```
        Get-AzureADServicePrincipalPolicy -Id <ObjectId of the fhir-proxy ServicePrincipal>
    ```
8. Go to [Azure Portal](https://portal.azure.com) > App Registration > fhir-proxy client principal.
9. Select Manifest and set ```"acceptMappedClaims": true```, and Save.
10. Obtain the resource FHIR logical id of the user. You can query the FHIR Server using any valid target resource query parameters to match to the AAD User. The target resources recognized by the Proxy are currently Patient, Practitioner and RelatedPerson.
11. Map the FHIR server resource logical id into the onPremisesExtensionAttribute specified in the claims schema above (e.g. extensionAttribute1 in this case):
    1. Open [Microsoft Graph Explorer](https://developer.microsoft.com/en-us/graph/graph-explorer), sign in with your tenant admin account. 
    2. Select Patch option, for the request.
    3. Select beta for the api version.
    4. Use the endpoint: ```https://graph.microsoft.com/beta/users/{oid of the AD user account to add FHIR Mapping too}```.
    5. For Request Body, paste the following:
       ```
        {"onPremisesExtensionAttributes":{"extensionAttribute1":"<fhir logical id>"}}
       ```
        Note: <fhir logical id> should be in the form of <resourceType/logicalid> for example to map the AAD user to a Patient resource with logical id 1234 the form of the value should be ```Patient/1234```.
    5. Then select Run Query.
    6. To confirm, run a GET request on the same endpoint and check the extensionAttribute1 value. You should see the fhir logical id in extensionAtrribute1:
```
        
        "onPremisesExtensionAttributes": {
            "extensionAttribute1": "Patient/1234",
            "extensionAttribute2": null,
        }
```      
12. When the user logs in via the proxy client, a new claim type 'fhirUser' with the fhir Patient logical id of the user will be in the access and id tokens. This claim will be read by the proxy and used to scope requests to the FHIR Server to include only resources for the Patient specified and this value will also be placed in the SMART patient/launch patient field.

<I>Note: You can repeat steps 10-11 for any additional users you wish to map. Steps 1-9 are performed only once.</I>

---

### How to file issues and get help  

This project uses GitHub Issues to track bugs and feature requests. Please search the existing 
issues before filing new issues to avoid duplicates.  For new issues, file your bug or 
feature request as a new Issue.

For help and questions about using this project, please open an [issue](https://github.com/microsoft/fhir-proxy/issues) against the Github repository. We actively triage these and will work on this as best effort.

### Microsoft Support Policy  

Support for this **PROJECT or PRODUCT** is limited to the resources listed above.