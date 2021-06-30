## Adding FHIR ID Mapping as Custom Claim to AAD Access Token
To support SMART Launch with Patient Context you can associated Active Directory user accounts with a Patient resource logical ID in the FHIR Server.  This association allows for returning the patientid to calling SMART Applications as detailed in the patient/launch specification of the [SMART App Launch Framework](http://www.hl7.org/fhir/smart-app-launch/)</br>
<B>Note: You musy have rights to administer claims policy in your AD Tenant and read/write user profiles in order to proceed</B>
1. [Download and Install Windows Powershell for your platform](https://docs.microsoft.com/en-us/powershell/scripting/install/installing-powershell?view=powershell-7.1)
2. Launch Powershell with Administrator privledges
3. Follow the [prerequisites](https://docs.microsoft.com/en-us/azure/active-directory/develop/active-directory-claims-mapping#prerequisites) for running AD Policy Commands
4. Create the custom claim mapping and create the field 'extensionfhirpatientid' to store it:
   ```
    New-AzureADPolicy -Definition @('{
        "ClaimsMappingPolicy":
        {
            "Version":1,"IncludeBasicClaimSet":"true", 
            "ClaimsSchema": [{"Source":"user","ID":"extensionfhirpatientid","SamlClaimType":"http://schemas.xmlsoap.org/ws/2005/05/identity/claims/fhirpatientid","JwtClaimType":"fhirpatientid"}]
        }
    }') -DisplayName "FHIRAccessExtraClaims" -Type "ClaimsMappingPolicy"
    ```
5. Get the new policy mapping id:
    ```
    Get-AzureADPolicy
    ```
6. Apply this policy to your fhir-proxy client principle ID (You can find it on [Azure Portal](https://portal.azure.com) > Enterprise Applications > fhir-proxy install name (e.g. myfhirproxy.azurewebsites.net):
    ```
        Add-AzureADServicePrincipalPolicy -Id <ObjectId of the fhir-proxy ServicePrincipal> -RefObjectId <ObjectId of the Policy>
    ```
7. To check if it succeed, you must see the policy it by running this command:
    ```
        Get-AzureADServicePrincipalPolicy -Id <ObjectId of the fhir-proxy ServicePrincipal>
    ```
8. Obtain the Patient resource FHIR logical id of the Patient you want to associate with a user account.  You can query the FHIR Server using any valid [FHIR Patient resource](https://www.hl7.org/fhir/patient.html) query parameters to identify the user's Patient resource.
9. Go to [Microsoft Graph Explorer](https://developer.microsoft.com/en-us/graph/graph-explore), sign in with your tenant admin account:
    1. Select Patch option, for the request
    2. Select beta for the api version
    3. Use the endpoint: ```https://graph.microsoft.com/beta/users/{oid of the AD user account to add FHIR Mapping too}```
    4. For Request Body, paste the following:
       ```
        {"onPremisesExtensionAttributes":{"extensionfhirpatientid":"<fhir logical id>"}}
       ```
    5. Then select Run Query
    6. To confirm, run a GET request in the same endpoint and check the extensionfhirpatientid value
10. Go to [Azure Portal](https://portal.azure.com) > App Registration > fhir-proxy client principle
11. Select Manifest and set ```"acceptMappedClaims": true```, and Save
12. When the user logs in via the proxy client a new claim type 'fhirpatientid' with the fhir Patient logical id of the user will be in the access token. This claim will be read by the proxy and used to scope requests to the FHIR Server to include only resources for the Patient specified and this value will also be placed in SMART patient/launch patient field
---

### How to file issues and get help  

This project uses GitHub Issues to track bugs and feature requests. Please search the existing 
issues before filing new issues to avoid duplicates.  For new issues, file your bug or 
feature request as a new Issue.

For help and questions about using this project, please open an [issue](https://github.com/microsoft/fhir-proxy/issues) against the Github repository. We actively triage these and will work on this as best effort.

### Microsoft Support Policy  

Support for this **PROJECT or PRODUCT** is limited to the resources listed above.