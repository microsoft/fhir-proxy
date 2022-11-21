# FHIR Proxy - SMART on FHIR Configuration for ONC g10 Certification
### Introduction
This guide will give you fhir-proxy configuration steps for certifying with the ONC g10 Inferno Test Kit using Azure Active Directory as the IDP<br/><br/>
<I>Note: These steps only cover the SMART/OAuth related configurations, appropriate us core data sets and FHIR Server content are not covered.</I> 
###  Prerequesites
1. You must have completed Step 1 of [deploying the fhir-proxy components](../scripts/Readme.md) for Azure Active Directory sucessfully 
1. You must meet the prerequesites and complete the instructions in the ```Configure fhirUser Custom Claim Policy``` and ```Associate AAD User with FHIR Server Resource``` sections of the [Adding fhirUser as Custom Claim to AAD OAuth Tokens](addingfhiridcustomclaim.md) document.<br/>You do not need to register SMART Applications this will be done as a part of the oncg10 configuration steps
2. You need to know the KeyVault name used to hold secrets for your fhir-proxy deployment
3. You need to know the Resource Group and name of your fhir-proxy deployment
4. You need to know the name of your fhir-proxy function app
### Launch Azure Cloud Shell (Bash Environment)  

  
[![Launch Azure Shell](../images/launchcloudshell.png "Launch Cloud Shell")](https://shell.azure.com/bash?target="_blank")

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
###  1 Standalone Patient App Test
1. In your cloud shell, register a private application (client) with the following script command:<br/>
   ```./createproxysmartclient.bash -k <key vault name> -n oncprivate -p -a```
2. When prompted for redirect url enter:<br/>
   https://inferno.healthit.gov/suites/custom/smart/redirect<br/>
3. When prompted for scopes enter:<br/>
   launch/patient openid fhirUser offline_access patient/Medication.read patient/AllergyIntolerance.read patient/CarePlan.read patient/CareTeam.read patient/Condition.read patient/Device.read patient/DiagnosticReport.read patient/DocumentReference.read patient/Encounter.read patient/Goal.read patient/Immunization.read patient/Location.read patient/MedicationRequest.read patient/Observation.read patient/Organization.read patient/Patient.read patient/Practitioner.read patient/Procedure.read patient/Provenance.read patient/PractitionerRole.read</br>
4. Once your application is registered your will receive the following output:<br/>
``` 
************************************************************************************************************
Registered SMART Application oncprivate on Thu Nov 17 12:54:02 EST 2022
This client can be used for SMART Application Launch and Context Access to FHIR Server via the FHIR Proxy
Please note the following reference information for use in authentication calls:
Your Service Prinicipal Client/Application ID is: <your client id>
Your Service Prinicipal Client Secret is: <your client secret>
Your FHIR Server ISS: https://<fhir-proxy-appname>.azurewebsites.net/fhir
```
5. Create your new test session at [ONC Inferno site](https://inferno.healthit.gov/suites/g10_certification) with the default options
6. In your inferno browser session Execute the 1 Standalone Patient App test you will need to provide FHIR Server IIS (URL), the Client Id and the Client Secret above

### 2 Limited Access App Test
1. In the bash cloud shell enter the following command to enable FP-SMART-SESSION-SCOPES on the fhir-proxy
	```azurecli-interactive
    az functionapp config appsettings set --name <fhir-proxy-appname>  --resource-group <fhir-proxy-resource-group> --settings FP-SMART-SESSION-SCOPES=true
	```
2. In your inferno browser session Execute the 2 Limited Access App Test

3. When prompted enter your login/username and select the resources you specified in Expected Resource Grant for Limited Access Launch field of the test the defaults are (Patient, Observation and Condition)

4. Press Continue and authorization will proceed and the test will continue if authenticated


### 3 EHR Practitioner App Test
1. In your cloud shell, register a private application (client) with the following script command:<br/>
   ```./createproxysmartclient.bash -k <key vault name> -n oncehr -p -a```
2. When prompted for redirect url enter:<br/>
   https://inferno.healthit.gov/suites/custom/smart/redirect<br/>
3. When prompted for scopes enter:<br/>
 launch openid fhirUser offline_access user/Medication.read user/AllergyIntolerance.read user/CarePlan.read user/CareTeam.read user/Condition.read user/Device.read user/DiagnosticReport.read user/DocumentReference.read user/Encounter.read user/Goal.read user/Immunization.read user/Location.read user/MedicationRequest.read user/Observation.read user/Organization.read user/Patient.read user/Practitioner.read user/Procedure.read user/Provenance.read user/PractitionerRole.read
4. Once your application is registered your will receive the following output:<br/>
``` 
************************************************************************************************************
Registered SMART Application oncehr on Thu Nov 17 12:54:02 EST 2022
This client can be used for SMART Application Launch and Context Access to FHIR Server via the FHIR Proxy
Please note the following reference information for use in authentication calls:
Your Service Prinicipal Client/Application ID is: <your client id>
Your Service Prinicipal Client Secret is: <your client secret>
Your FHIR Server ISS: https://<fhir-proxy-appname>.azurewebsites.net/fhir
```
5. In your inferno browser session Execute the 3 EHR Practitioner App test you will need to provide FHIR Server IIS (URL), the Client Id and the Client Secret above
6. When prompted to launch app from EHR you can simulate this by opening another browser tab and entering:<br/>
   ```https://inferno.healthit.gov/suites/custom/smart/launch?iss=<FHIR Server ISS from above>&launch=<desired context (e.g. any string)>```
7. You may then follow authorization steps and continue test
### 4 Single Patient API Test
1. This test will use the Standalone Patient App configuration and token
2. Simply Run the 4 Single Patient API test and accept defaulted values from the Standalone Patient App Test
<br/><I>Note: You must have a loaded a valid ONC test data collection on the FHIR Server for the patient in context to pass.</I>
### 7 Multi-Patient API Test
This test uses a federated identity credential via client_assertion to authenticate.  Azure Active directory supports this flow, however, their encryption limit is 256 bits the ONC requires a 384 bit encrpted token.  To overcome this limitation, the fhir-proxy has implemented a limited service client registration process to support the federated client credentials.
1. Obtain function app management access key (Note: This should be treated as highly confidential and be protected)<br/>
	1. Access [Azure Portal](https://portal.azure.com)
	2. Navigate to your fhir-proxy function app
	3. Select App Keys under the left hand navigation area.
	4. Click on the default host key value to reveal the key
	5. Click on the copy icon
	6. Securely save this key to use in these steps

2. In your cloud bash shell enter the following command with your values inserted:<br/>
   ```
   curl -d '{"name": "ONC Multi Patient Application Test","jwskeyset": "https://inferno.healthit.gov/suites/custom/g10_certification/.well-known/jwks.json"} ' -H "Content-Type: application/json" -X POST https://<fhir-proxy-name>.azurewebsites.net/manage/appregistration?code=<function-access-key>
   ```
3. You will receive output similar to the following:
   ```
   {
	"ClientId": "<client_id_guid>",
	"Name": "ONC Multi Patient Application Test",
	"ValidIssuers": "<client_id_guid>",
	"ValidAudiences": "https://<fhir-proxy-name>.azurewebsites.net/oauth2/token",
	"Scope": "system/*.read system/_operations.*",
	"JWKSetUrl": "https://inferno.healthit.gov/suites/custom/g10_certification/.well-known/jwks.json",
	"Status": "active",
	"PartitionKey": "federatedentities",
	"RowKey": "<client_id_guid>",
	"Timestamp": "0001-01-01T00:00:00+00:00",
	"ETag": "W/\"datetime'2022-11-21T15%3A51%3A37.6003364Z'\""
   }
   ```
4. You will need the client_id and the Scope string provided by the registration, in addition you will need a predetermined Group with at least 2 patient reference member entities. You can query your fhir server for this data.
5. Add the necessary pre and post processing modules for patient export to the fhir-proxy
    1. In your cloud bash shell in the scripts directory execute the following command to configure required ONC pre/post processing modules for the fhir-proxy
       ```
        ./configmodules.bash -n sto-onctest -g onctester -i $(az account show --query id --output tsv)        
	   ```
	2. You will see a menu similar to the following:
	   ```
	   Checking Azure Authentication...
       Setting subscription default...
       Please select the proxy modules you want to enable:
       1 ) FHIRProxy.postprocessors.FHIRCDSSyncAgentPostProcess2
       2 ) FHIRProxy.postprocessors.ParticipantFilterPostProcess
       3 ) FHIRProxy.postprocessors.PublishFHIREventPostProcess
       4 ) FHIRProxy.postprocessors.ConsentOptOutFilter
       5 ) FHIRProxy.preprocessors.TransformBundlePreProcess
       6 ) FHIRProxy.preprocessors.ONCExportPreProcess
       7 ) FHIRProxy.postprocessors.ONCExportPostProcess
       Select an option (again to uncheck, ENTER when done):
	   ```
	3. Enter the option number for the ONCExportPreProcess and press enter
	4. Enter the option number for the ONCExportPostProcess and press enter
	5. Press Enter, this will enable the ONC Export process modules
	<br/><I>Note: This function will overwrite existing proxy module configuration. If you require other proxy modules to be enabled you must enter those as well</I>
6. Execute the 7 Multi-Patient API Test
6. When prompted provide the fhir-proxy url (```https://<fhir-proxy-name>.azurewebsites.net/fhir```), the token endpoint (```https://<fhir-proxy-name>.azurewebsites.net/oauth2/token```), the client id, scope string, group resource id and patient ids in the group

### 9 Additional Test

#### 9.1 SMART Public Client Launch
1. In your cloud shell, register a public application (client) with the following script command:<br/>
   ```./createproxysmartclient.bash -k <key vault name> -n oncpublic -p -u -a```
2. When prompted for redirect url enter:<br/>
   https://inferno.healthit.gov/suites/custom/smart/redirect<br/>
3. When prompted for scopes enter:<br/>
   launch/patient openid fhirUser offline_access patient/Medication.read patient/AllergyIntolerance.read patient/CarePlan.read patient/CareTeam.read patient/Condition.read patient/Device.read patient/DiagnosticReport.read patient/DocumentReference.read patient/Encounter.read patient/Goal.read patient/Immunization.read patient/Location.read patient/MedicationRequest.read patient/Observation.read patient/Organization.read patient/Patient.read patient/Practitioner.read patient/Procedure.read patient/Provenance.read patient/PractitionerRole.read</br>
4. Once your application is registered your will receive the following output:<br/>
``` 
************************************************************************************************************
Registered SMART Application oncpublic on Thu Nov 17 12:54:02 EST 2022
This client can be used for SMART Application Launch and Context Access to FHIR Server via the FHIR Proxy
Please note the following reference information for use in authentication calls:
Your Service Prinicipal Client/Application ID is: <your client id>
This is a public client registration
Your FHIR Server ISS: https://<fhir-proxy-appname>.azurewebsites.net/fhir
```
5. Execute the 9.1 SMART Public Client Launch

<I>Note: For public clients it is recommended to use PKCE exchange</i>

#### 9.3 Token Revocation
1. Bearer token revocation is not directly supported.  Tokens may be invalidated by rotating the ```FP-ACCESS-TOKEN-SECRET``` stored in the fhir-proxy keyvault.
2. Refresh token revocation is accomplished by removing the refresh token from the scopestore table of the fhir-proxy storage account.  The refresh token is the rowkey. Use the storage browser to access the table located the refresh token guid as the rowkey in the partition defined by the application (client_id) and delete the row.

#### 9.4 SMART Invalid AUD Launch
1. Execute the 9.4 SMART Invalid Aud Launch with the default settings from the 1 Standalone Patient App test
#### 9.5 SMART Invalid Token Request
1. Execute the 9.5 SMART Invalid Token Request with the default settings from the 1 Standalone Patient App test
#### 9.8 EHR Launch with Patient Scopes
1. Execute the9.8 EHR Launch with Patient Scopes with the default settings from the 3 EHR Practitioner App Test
