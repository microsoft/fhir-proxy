# FHIR Proxy - SMART on FHIR Configuration for ONC g10 Certification
### Introduction
This guide will give you fhir-proxy configuration steps for certifying with the ONC g10 Inferno Test Kit.<br/><br/>
<I>Note: These steps only cover the SMART/OAuth related configurations, appropriate us core data sets and FHIR Server content are not covered.</I> 
###  Prerequesites
1. You must meet the prerequesites and complete the instructions in the ```Configure fhirUser Custom Claim Policy``` and ```Associate AAD User with FHIR Server Resource``` sections of the [Adding fhirUser as Custom Claim to AAD OAuth Tokens](/docs/addingfhiridcustomclaim.md) document.
2. You need to know the KeyVault name used to hold secrets for your fhir-proxy deployment
3. You need to know the Resource Group and name of your fhir-proxy deployment
### Launch Azure Cloud Shell (Bash Environment)  

  
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
5. Create your new test session at [ONC Inferno Standalone Patient App Test](https://inferno.healthit.gov/suites/g10_certification)
6. In your inferno browser session Execute the 1 Standalone Patient App test you will need to provide FHIR Server IIS (URL), the Client Id and the Client Secret above

### 2 Limited Access App Test
1. In the bash cloud shell enter the following command to enable FP-SMART-SESSION-SCOPES on the fhir-proxy
	```azurecli-interactive
    az functionapp config appsettings set --name <fhir-proxy-appname>  --resource-group <fhir-proxy-resource-group> --settings FP-SMART-SESSION-SCOPES=true
	```
2. In your inferno browser session Execute the 2 Limited Access App Test

3. 3. When prompted enter your login/username and select the resources you specified in Expected Resource Grant for Limited Access Launch field of the test

### 3 EHR Practitioner App Test
### 4 Single Patient API Test
### 7 Multi-Patient API Test