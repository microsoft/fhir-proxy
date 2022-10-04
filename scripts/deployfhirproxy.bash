#!/bin/bash
set -euo pipefail
IFS=$'\n\t'

# -e: immediately exit if any command has a non-zero exit status
# -o: prevents errors in a pipeline from being masked
# IFS new value is less likely to cause confusing bugs when looping arrays or arguments (e.g. $@)
#
#FHIR Proxy Setup --- Author Steve Ordahl Principal Architect Health Data Platform
#




#########################################
# HealthArchitecture Deployment Settings 
#########################################
declare TAG="HealthArchitectures = FHIR-Proxy"
declare functionSKU="B1"
declare functionWorkers="2"
declare storageSKU="Standard_LRS"


#########################################
# FHIR Proxy Default App Settings Variables 
#########################################
declare suffix=$RANDOM
declare defresourceGroupLocation="westus2"
declare defresourceGroupName="proxy-fhir-"$suffix
declare defdeployPrefix="proxy"$suffix
declare defAppName="sfp-"$defdeployPrefix
declare defKeyVaultName="kv-"$defdeployPrefix
declare genPostmanEnv="yes"


declare fpurl=""
declare fpappname=""


#########################################
# Common Variables
#########################################
declare script_dir="$( cd -P -- "$( dirname "${BASH_SOURCE[0]}" )" &> /dev/null && pwd -P )"
declare defSubscriptionId=""
declare subscriptionId=""
declare resourceGroupName=""
declare resourceGroupExists=""
declare useExistingResourceGroup=""
declare createNewResourceGroup=""
declare resourceGroupLocation=""
declare storageAccountNameSuffix="store"
declare storageConnectionString=""
declare serviceplanSuffix="asp"
declare deployredis=""
declare redisAccountNameSuffix="cache"
declare redisConnectionString=""
declare redisKey=""
declare stepresult=""
declare distribution="distribution/publish.zip"
declare postmanTemplate="postmantemplate.json"

# FHIR
declare defAuthType="MSI"
declare authType=""
declare fhirServiceUrl=""
declare fhirServiceClientId=""
declare fhirServiceClientSecret=""
declare fhirServiceTenantId=""
declare fhirServiceAudience=""
declare fhirResourceId=""
declare fhirServiceName=""
declare fhirServiceExists=""
declare fhirServiceProperties=""
declare fhirServiceClientAppName=""
declare fhirServiceClientObjectId=""
declare fhirServiceClientRoleAssignment=""
declare fhirServiceWorkspace=""

# KeyVault 
declare keyVaultName=""
declare keyVaultExists=""
declare useExistingKeyVault=""
declare createNewKeyVault=""

# Postman 
declare proxyAppName=""
declare fhirServiceUrl=""
declare fhirServiceClientId=""
declare fhirServiceClientSecret=""
declare fhirServiceTenant=""
declare fhirServiceAudience=""
declare deployPrefix=""
declare stepresult=""

declare functionAppHost=""
declare fakey=""
declare faresourceid=""
declare roleadmin="FHIRProxyAdministrator"
declare rolereader="FHIRProxyReader"
declare rolewriter="FHIRProxyWriter"
declare rolepatient="Patient"
declare roleparticipant="Practitioner,RelatedPerson"
declare roleglobal="DataScientist"
declare spappid=""
declare spsecret=""
declare sptenant=""
declare spreplyurls=""
declare tokeniss=""
declare preprocessors=""
declare postprocessors=""
declare msi=""
declare owner=""
declare origperm=""
declare msifhirserverdefault=""
declare msifhirservername=""
declare msifhirserverrg=""
declare msifhirserverrid=""
declare msirolename="FHIR Data Contributor"
declare fptokensecret=$(openssl rand -hex 24)

function intro {
	# Display the intro - give the user a chance to cancel 
	#
	echo " "
	echo "FHIR-Proxy Application installation script... "
	echo " - Prerequisite:  Azure API for FHIR or AHDS FHIR Server must be installed"
	echo " - Prerequisite:  KeyVault containing FHIR and FHIR-Proxy settings must be available"
	echo " - Prerequisite:  Azure CLI (bash) access from the Azure Portal"
	echo " - Prerequisite:  You must have rights to provision Function Apps and App Insights at the Subscription level"
	echo " "
	echo "Note: Default naming approach is (Azure component - [Deploy Prefix + Random Number] Azure type)"  
	read -p 'Press Enter to continue, or Ctrl+C to exit...'
}

function fail {
  echo $1 >&2
  exit 1
}

function retry {
  local n=1
  local max=5
  local delay=15
  echo "Starting retry logic..."
  while true; do
    "$@" && break || {
      if [[ $n -lt $max ]]; then
        ((n++))
        echo "Command failed. Retry Attempt $n/$max in $delay seconds:" >&2
        sleep $delay;
      else
        fail "The command has failed after $n attempts."
      fi
    }
  done
  echo "Completed retry logic..."
}

function kvuri {
	echo "@Microsoft.KeyVault(SecretUri=https://"$keyVaultName".vault.azure.net/secrets/"$@"/)"
}

usage() { echo "Usage: $0 -i <subscriptionId> -g <resourceGroupName> -l <resourceGroupLocation> -k <keyVaultName> -n <deployPrefix>" 1>&2; exit 1; }


#########################################
#  Script Main Body (start script execution here)
#########################################
#
# Initialize parameters specified from command line
#
while getopts ":i:g:l:k:n:w" arg; do
	case "${arg}" in
		n)
			deployPrefix=${OPTARG:0:14}
			deployPrefix=${deployPrefix,,}
			deployPrefix=${deployPrefix//[^[:alnum:]]/}
			;;
		k)
			keyVaultName=${OPTARG}
			;;
		i)
			subscriptionId=${OPTARG}
			;;
		g)
			resourceGroupName=${OPTARG}
			;;
		l)
			resourceGroupLocation=${OPTARG}
			;;
		*)
			usage
			;;
		esac
done
shift $((OPTIND-1))
echo "Executing "$0"..."
echo "Checking Azure Authentication..."

# login to azure using your credentials
#
az account show 1> /dev/null

if [ $? != 0 ];
then
	az login
fi

# set default subscription
#
defsubscriptionId=$(az account show --query "id" --out tsv)
 

echo "Checking Script Execution directory..."
# Test for correct directory path / destination 
if [ -f "${script_dir}/fhirroles.json" ] ; then
	echo "  necessary files found, continuing..."
else
	echo "  necessary files not found... Please ensure you launch this script from within the ./scripts directory"
	usage ;
fi

# Call the into function 
# 
intro


# ---------------------------------------------------------------------
# Prompt for common parameters if some required parameters are missing
# 
echo " "
echo "Collecting Azure Parameters (unless supplied on the command line) "

if [[ -z "$subscriptionId" ]]; then
	echo "Enter your subscription ID <press Enter to accept default> ["$defsubscriptionId"]:"
	read subscriptionId
	if [ -z "$subscriptionId" ] ; then
		subscriptionId=$defsubscriptionId
	fi
	[[ "${subscriptionId:?}" ]]
fi

if [[ -z "$resourceGroupName" ]]; then
	echo "This script will look for an existing resource group, otherwise a new one will be created "
	echo "You can create new resource groups with the CLI using: az group create "
	echo "Enter a resource group name <press Enter to accept default> ["$defresourceGroupName"]: "
	read resourceGroupName
	if [ -z "$resourceGroupName" ] ; then
		resourceGroupName=$defresourceGroupName
	fi
	[[ "${resourceGroupName:?}" ]]
fi


if [[ -z "$resourceGroupLocation" ]]; then
	echo "If creating a *new* resource group, you need to set a location "
	echo "You can lookup locations with the CLI using: az account list-locations "
	echo "Enter resource group location <press Enter to accept default> ["$defresourceGroupLocation"]: "
	read resourceGroupLocation
	if [ -z "$resourceGroupLocation" ] ; then
		resourceGroupLocation=$defresourceGroupLocation
	fi
	[[ "${resourceGroupLocation:?}" ]]
fi

# Ensure there are subscriptionId and resourcegroup names 
#
if [ -z "$subscriptionId" ] || [ -z "$resourceGroupName" ]; then
	echo "Either one of subscriptionId, resourceGroupName is empty, exiting..."
	exit 1
fi

# Check if the resource group exists
#
echo " "
echo "Checking for existing Resource Group named ["$resourceGroupName"]"
resourceGroupExists=$(az group exists --name $resourceGroupName)
if [[ "$resourceGroupExists" == "true" ]]; then
    echo "  Resource Group ["$resourceGroupName"] found"
    useExistingResourceGroup="yes" 
    createNewResourceGroup="no" ;
else
    echo "  Resource Group ["$resourceGroupName"] not found a new Resource group will be created"
    useExistingResourceGroup="no" 
    createNewResourceGroup="yes"
fi

# ---------------------------------------------------------------------
# Prompt for script parameters if some required parameters are missing
#
echo " "
echo "Collecting Script Parameters (unless supplied on the command line).."

# Set default values for proxy service and application names 
#
defdeployPrefix=${defdeployPrefix:0:14}
defdeployPrefix=${defdeployPrefix//[^[:alnum:]]/}
defdeployPrefix=${defdeployPrefix,,}

if [[ -z "$deployPrefix" ]]; then
	echo "Enter your deploy prefix - proxy components begin with this prefix ["$defdeployPrefix"]:"
	read deployPrefix
	if [ -z "$deployPrefix" ] ; then
		deployPrefix=$defdeployPrefix
	fi
	deployPrefix=${deployPrefix:0:14}
	deployPrefix=${deployPrefix//[^[:alnum:]]/}
    deployPrefix=${deployPrefix,,}
	[[ "${deployPrefix:?}" ]] ;
else 
	proxyAppName="sfp-"${deployPrefix}
fi

# Set a Default Function App Name
# 
if [[ -z "$proxyAppName" ]]; then
	echo "Enter the proxy function app name - this is the name of the proxy function app ["$defAppName"]:"
	read proxyAppName
	if [ -z "$proxyAppName" ] ; then
		proxyAppName=$defAppName
	fi
fi
[[ "${proxyAppName:?}" ]]


#
if [[ -z "$keyVaultName" ]]; then
	echo "Enter a Key Vault name <press Enter to accept default> ["$defKeyVaultName"]:"
	read keyVaultName
	if [ -z "$keyVaultName" ] ; then
		keyVaultName=$defKeyVaultName
	fi
	[[ "${keyVaultName:?}" ]]
fi

# Check KV exists
#
echo "Checking for keyvault "$keyVaultName"..."
keyVaultExists=$(az keyvault list --query "[?name == '$keyVaultName'].name" --out tsv)
if [[ -n "$keyVaultExists" ]]; then
	set +e 
	echo "  "$keyVaultName" found"
	echo " "
	echo "Checking for FHIR Service configuration..."
	fhirServiceUrl=$(az keyvault secret show --vault-name $keyVaultName --name FS-URL --query "value" --out tsv)
	if [ -n "$fhirServiceUrl" ]; then
		echo "  FHIR Service URL: "$fhirServiceUrl

        fhirResourceId=$(az keyvault secret show --vault-name $keyVaultName --name FS-URL --query "value" --out tsv | awk -F. '{print $1}' | sed -e 's/https\:\/\///g') 
		echo "  FHIR Service Resource ID: "$fhirResourceId 

		fhirServiceTenant=$(az keyvault secret show --vault-name $keyVaultName --name FS-TENANT-NAME --query "value" --out tsv)
		echo "  FHIR Service Tenant ID: "$fhirServiceTenant 
		
		fhirServiceClientId=$(az keyvault secret show --vault-name $keyVaultName --name FS-CLIENT-ID --query "value" --out tsv)
		echo "  FHIR Service Client ID: "$fhirServiceClientId
		
		fhirServiceClientSecret=$(az keyvault secret show --vault-name $keyVaultName --name FS-SECRET --query "value" --out tsv)
		echo "  FHIR Service Client Secret: *****"
		
		fhirServiceAudience=$(az keyvault secret show --vault-name $keyVaultName --name FS-RESOURCE --query "value" --out tsv) 
		echo "  FHIR Service Audience: "$fhirServiceAudience 
		
		useExistingKeyVault="yes"
    	createNewKeyVault="no" ;
	else	
		echo "  unable to read FHIR Service URL from ["$keyVaultName"]" 
        echo "  setting script to create new FHIR Service Entry in existing Key Vault ["$keyVaultName"]"
        useExistingKeyVault="yes"
        createNewKeyVault="no"
	fi 
else
	echo "  Script will deploy new Key Vault ["$keyVaultName"]" 
    useExistingKeyVault="no"
    createNewKeyVault="yes"
fi

# Prompt for FHIR Server Parameters if not found in KeyVault
#
if [ -z "$fhirServiceUrl" ]; then
	echo "Enter the destination FHIR Server URL (aka Endpoint):"
	read fhirServiceUrl
	if [ -z "$fhirServiceUrl" ] ; then
		echo "You must provide a destination FHIR Server URL"
		exit 1 ;
	fi
fi


# ---------------------------------------------------------------------
# Prompt for Service Principal Parameters
# 
echo " "
echo "FHIR-Proxy works with two (2) connections, one to the FHIR Service, the other to external services (such as Postman)"
echo "This script creates the connection from FHIR-Proxy to the FHIR Service for Authorization.  This Authentication can be"
echo "accomplished with either a Service Principal (SP) or a Managed Service Identity (MSI) account.  Note:  the ./createproxyserviceclient.bash script will"
echo "create the Service Principal for external service (such as Postman).  For more information please see the FHIR-Proxy"
echo "documentation at https://github.com/microsoft/fhir-proxy/blob/main/docs/Readme.md " 
echo " "
until [[ "$authType" == "MSI" ]] || [[ "$authType" == "SP" ]]; do
	echo "Which authentication method should be used internally to connect from the fhir-proxy to the FHIR Service MSI or SP? ["$defAuthType"]:"
	read authType
	if [ -z "$authType" ] ; then
		authType=$defAuthType
	fi
	authType=${authType^^}
done
# Setup Auth type based on input 
# 
if [[ "$authType" == "SP" ]] ; then 
	echo "Auth Type is set to Service Principal (SP)"

	if [ -z "$fhirServiceTenant" ] ; then
		echo "  Enter the FHIR Service - Tenant ID (GUID)"
		read fhirServiceTenant
		if [ -z "$fhirServiceTenant" ] ; then
			echo "You must provide a FHIR Service - Tenant ID (GUID)"
			exit 1;
		fi
	fi 
	[[ "${fhirServiceTenant:?}" ]]

	if [ -z "$fhirServiceClientId" ] ; then 
		echo "  Enter the FHIR Service - SP Client ID (GUID)"
		read fhirServiceClientId
		if [ -z "$fhirServiceClientId" ] ; then
			echo "You must provide a FHIR Service - SP Client ID (GUID)"
			exit 1;
		fi
	fi 
	[[ "${fhirServiceClientId:?}" ]]

	if [ -z "$fhirServiceClientSecret" ] ; then 
		echo "  Enter the FHIR Service - SP Client Secret"
		read fhirServiceClientSecret
		if [ -z "$fhirServiceClientSecret" ] ; then
			echo "You must provide a FHIR Service - SP Client Secret"
			exit 1;
		fi
	fi 
	[[ "${fhirServiceClientSecret:?}" ]]

	if [ -z "$fhirServiceAudience" ] ; then 
		echo "  Enter the FHIR Service - SP Audience (URL)"
		read fhirServiceAudience
		if [ -z "$fhirServiceAudience" ] ; then
			echo "You must provide a FHIR Service - SP Audience (URL)"
			exit 1;
		fi
	fi 
	[[ "${fhirServiceAudience:?}" ]]
else
		echo "Auth Type is set to Managed Service Identity (MSI)"		
		echo "Note: API for FHIR or AHDS FHIR Server must be in same tenant as fhir-proxy for MSI..."
		msifhirserverdefault=${fhirServiceUrl#https://}
		msifhirserverdefault=${msifhirserverdefault%%.*}
		if [[ "$fhirServiceUrl" == *".fhir.azurehealthcareapis.com"* ]]; then
			IFS='-' read -ra Arr <<< "$msifhirserverdefault"
			fhirServiceWorkspace=${Arr[0]}
			msifhirservername=${Arr[1]}
			IFS=$'\n\t'
			msifhirserverrg=$(az resource list --name $fhirServiceWorkspace/$msifhirservername --resource-type 'Microsoft.HealthcareApis/workspaces/fhirservices' --query "[0].resourceGroup" --output tsv)
		else 
			msifhirservername=$msifhirserverdefault
			msifhirserverrg=$(az resource list --name $msifhirservername --resource-type 'Microsoft.HealthcareApis/services' --query "[0].resourceGroup" --output tsv)
		fi
		fhirServiceAudience=${fhirServiceUrl}
fi
sptenant=$(az account show --name $subscriptionId --query "tenantId" --out tsv)

# Prompt for final confirmation
#
echo "--- "
echo "Ready to start deployment of FHIR-Proxy Application: ["$proxyAppName"] with the following values:"
echo "Proxy Component Deploy Prefix:......... "$deployPrefix
echo "FHIR Service URL:...................... "$fhirServiceUrl
echo "FHIR Service Auth Type:................ "$authType
if [[ "$authType" == "MSI" ]] ; then
	echo "  FHIR Server Workspace................ "$fhirServiceWorkspace
	echo "  FHIR Server Name..................... "$msifhirservername
	echo "  FHIR Server Resource Group........... "$msifhirserverrg
fi
echo "Subscription ID:....................... "$subscriptionId
echo "Subscription Tenant ID:................ "$sptenant
echo "Resource Group Name:................... "$resourceGroupName
echo "  Use Existing Resource Group:......... "$useExistingResourceGroup
echo "  Create New Resource Group:........... "$createNewResourceGroup
echo "Resource Group Location:............... "$resourceGroupLocation 
echo "KeyVault Name:......................... "$keyVaultName
echo "  Use Existing Key Vault:.............. "$useExistingKeyVault
echo "  Create New Key Vault:................ "$createNewKeyVault
echo " "
echo "Please validate the settings above before continuing"
read -p 'Press Enter to continue, or Ctrl+C to exit...'


#############################################################
#  Start Azure Setup  
#############################################################
#
echo " "
echo "Setting default subscription"
az account set --subscription $subscriptionId

# Set up variables
faresourceid="/subscriptions/"$subscriptionId"/resourceGroups/"$resourceGroupName"/providers/Microsoft.Web/sites/"$proxyAppName
if [[ -z "$fhirServiceWorkspace" ]]; then
	msifhirserverrid="/subscriptions/"$subscriptionId"/resourceGroups/"$msifhirserverrg"/providers/Microsoft.HealthcareApis/services/"$msifhirservername
else
	msifhirserverrid="/subscriptions/"$subscriptionId"/resourceGroups/"$msifhirserverrg"/providers/Microsoft.HealthcareApis/workspaces/"$fhirServiceWorkspace"/fhirservices/"$msifhirservername
fi
echo "Starting Azure Deployments "
(
    if [[ "$useExistingResourceGroup" == "no" ]]; then
        echo " "
        echo "Creating Resource Group ["$resourceGroupName"] in location ["$resourceGroupLocation"]"
        set -x
        az group create --subscription $subscriptionId --name $resourceGroupName --location $resourceGroupLocation --output none --tags $TAG ;
    else
        echo "Using Existing Resource Group ["$resourceGroupName"]"
    fi


    if [[ "$useExistingKeyVault" == "no" ]]; then
        echo " "
        echo "Creating Key Vault ["$keyVaultName"] in location ["$resourceGroupName"]"
        set -x
        stepresult=$(az keyvault create --name $keyVaultName --subscription $subscriptionId --resource-group $resourceGroupName --location  $resourceGroupLocation --tags $TAG --output none) ;
    else
        echo "Using Existing Key Vault ["$keyVaultName"]"
    fi
)

echo "Storing FHIR Service information in KeyVault ["$keyVaultName"]"
(
	echo "Storing FHIR Server Information in KeyVault..."
	stepresult=$(az keyvault secret set --vault-name $keyVaultName --name "FS-URL" --value $fhirServiceUrl)
	stepresult=$(az keyvault secret set --vault-name $keyVaultName --name "FS-RESOURCE" --value $fhirServiceAudience)
	stepresult=$(az keyvault secret set --vault-name $keyVaultName --name "FP-ACCESS-TOKEN-SECRET" --value $fptokensecret)
	if [[ "$authType" == "SP" ]] ; then 
		stepresult=$(az keyvault secret set --vault-name $keyVaultName --name "FS-TENANT-NAME" --value $fhirServiceTenant)
		stepresult=$(az keyvault secret set --vault-name $keyVaultName --name "FS-CLIENT-ID" --value $fhirServiceClientId)
		stepresult=$(az keyvault secret set --vault-name $keyVaultName --name "FS-SECRET" --value $fhirServiceClientSecret)
		stepresult=$(az keyvault secret set --vault-name $keyVaultName --name "FS-CLIENT-SECRET" --value $fhirServiceClientSecret)
		stepresult=$(az keyvault secret set --vault-name $keyVaultName --name "FS-ISMSI" --value "false")
	else
		stepresult=$(az keyvault secret set --vault-name $keyVaultName --name "FS-ISMSI" --value "true")
	fi
	
)

echo "Starting Secure FHIR Proxy App ["$proxyAppName"] deployment..."
(
	# Create App Service Plan
	echo "Creating Secure FHIR Proxy Function App Service Plan ["$deployPrefix$serviceplanSuffix"]..."
	stepresult=$(az appservice plan create --subscription $subscriptionId --resource-group $resourceGroupName --name $deployPrefix$serviceplanSuffix --number-of-workers $functionWorkers --sku $functionSKU --tags $TAG)

	if [ $?  != 0 ];
	then
		echo "App service plan creation failed"
		exit 1;
	fi

	# Create Storage Account
	echo "Creating Storage Account ["$deployPrefix$storageAccountNameSuffix"]..."
	stepresult=$(az storage account create --name $deployPrefix$storageAccountNameSuffix --subscription $subscriptionId --resource-group $resourceGroupName --location  $resourceGroupLocation --sku $storageSKU --encryption-services blob --https-only true --tags $TAG)

	echo "Retrieving Storage Account Connection String..."
	storageConnectionString=$(az storage account show-connection-string --subscription $subscriptionId --resource-group $resourceGroupName --name $deployPrefix$storageAccountNameSuffix --query "connectionString" --out tsv)
	
	echo "Storing Storage Account Connection String in Key Vault..."
	stepresult=$(az keyvault secret set --vault-name $keyVaultName --name "FP-STORAGEACCT" --value $storageConnectionString)
		
	# Create Redis Cache to Support Proxy Modules (Default is not deployed)
	if [[ -n "$deployredis" ]]; then
		echo "Creating Redis Cache ["$deployPrefix$redisAccountNameSuffix"]..."
		stepresult=$(az redis create --subscription $subscriptionId --location $resourceGroupLocation --name $deployPrefix$redisAccountNameSuffix --resource-group $resourceGroupName --sku Basic --vm-size c0 --tags $TAG)
	
		echo "Creating Redis Connection String..."
		redisKey=$(az redis list-keys --subscription $subscriptionId --resource-group $resourceGroupName --name $deployPrefix$redisAccountNameSuffix --query "primaryKey" --out tsv)
		redisConnectionString=$deployPrefix$redisAccountNameSuffix".redis.cache.windows.net:6380,password="$redisKey",ssl=True,abortConnect=False"
	
		echo "Storing Redis Connection String in KeyVault..."
		stepresult=$(az keyvault secret set --vault-name $keyVaultName --name "FP-REDISCONNECTION" --value $redisConnectionString)
	fi	
	# Create Proxy function app
	echo "Creating Secure FHIR Proxy Function App ["$proxyAppName"]..."
	functionAppHost=$(az functionapp create --subscription $subscriptionId --name $proxyAppName --storage-account $deployPrefix$storageAccountNameSuffix  --plan $deployPrefix$serviceplanSuffix  --resource-group $resourceGroupName --runtime dotnet --os-type Windows --functions-version 4 --tags $TAG --query defaultHostName --output tsv --only-show-errors)

	echo "FHIR-Proxy Function App Host name = "$functionAppHost
	
	stepresult=$(az functionapp stop --name $proxyAppName --subscription $subscriptionId --resource-group $resourceGroupName)
	
	echo "Storing FHIR Proxy Function App Host name in KeyVault..."
	stepresult=$(az keyvault secret set --vault-name $keyVaultName --name "FP-HOST" --value $functionAppHost)
	
	echo "Creating Function App MSI for KeyVault Access..."
	msi=$(az functionapp identity assign --subscription $subscriptionId --resource-group $resourceGroupName --name $proxyAppName --query "principalId" --out tsv)
	
	echo "Setting KeyVault Policy to allow Secret access..."
	stepresult=$(az keyvault set-policy -n $keyVaultName --secret-permissions list get --object-id $msi)
		
	#If using MSI set fhir-proxy function app role assignment on FHIR Server
	if [[ "$authType" == "MSI" ]] ; then 
		echo "Setting fhir-proxy function app role assignment on FHIR Server..."
		stepresult=$(retry az role assignment create --assignee "${msi}" --role "${msirolename}" --scope "${msifhirserverrid}" --only-show-errors)
	fi
		
	# Add App Settings
	echo "Configuring Secure FHIR Proxy App ["$proxyAppName"]..."
	stepresult=$(az functionapp config appsettings set --name $proxyAppName --subscription $subscriptionId --resource-group $resourceGroupName --settings FP-PRE-PROCESSOR-TYPES=FHIRProxy.preprocessors.TransformBundlePreProcess  FP-ADMIN-ROLE=$roleadmin FP-READER-ROLE=$rolereader FP-WRITER-ROLE=$rolewriter FP-GLOBAL-ACCESS-ROLES=$roleglobal FP-PATIENT-ACCESS-ROLES=$rolepatient FP-PARTICIPANT-ACCESS-ROLES=$roleparticipant FP-STORAGEACCT=$(kvuri FP-STORAGEACCT) FS-ISMSI=$(kvuri FS-ISMSI) FS-URL=$(kvuri FS-URL) FS-TENANT-NAME=$(kvuri FS-TENANT-NAME) FS-CLIENT-ID=$(kvuri FS-CLIENT-ID) FS-SECRET=$(kvuri FS-SECRET) FS-RESOURCE=$(kvuri FS-RESOURCE) FP-ACCESS-TOKEN-SECRET=$(kvuri FP-ACCESS-TOKEN-SECRET) FP-LOGIN-TENANT=$sptenant)
	
	echo "Deploying Secure FHIR Proxy Function App from source repo to ["$functionAppHost"]..."
	stepresult=$(retry az functionapp deployment source config --branch v2.0 --manual-integration --name $proxyAppName --repo-url https://github.com/microsoft/fhir-proxy --subscription $subscriptionId --resource-group $resourceGroupName)
	
	echo "Creating service principal for fhir-proxy..."
	stepresult=$(az ad sp create-for-rbac -n $functionAppHost --only-show-errors)
	spappid=$(echo $stepresult | jq -r '.appId')
	stepresult=$(az ad app update --id $spappid --identifier-uris "api://"$functionAppHost --app-roles @${script_dir}/fhirroles.json)
	echo "Storing fhir-proxy application information in keyvault..."
	stepresult=$(az keyvault secret set --vault-name $keyVaultName --name "FP-SP-NAME" --value $functionAppHost)
	stepresult=$(az keyvault secret set --vault-name $keyVaultName --name "FP-SP-ID" --value $spappid)
	echo "Updating function configuration with fhir-proxy application..."
	stepresult=$(az functionapp config appsettings set --name $proxyAppName --subscription $subscriptionId --resource-group $resourceGroupName --settings FP-SP-NAME=$(kvuri FP-SP-NAME) FP-SP-ID=$(kvuri FP-SP-ID))
	echo "Setting fhir-proxy application owner to signed-in user..."
	owner=$(az ad signed-in-user show --query id --output tsv --only-show-errors)
	stepresult=$(az ad app owner add --id $spappid --owner-object-id $owner --only-show-errors)
	
	echo "Starting fhir proxy function app..."
	stepresult=$(az functionapp start --name $proxyAppName --subscription $subscriptionId --resource-group $resourceGroupName)
	
	echo " "
	echo "************************************************************************************************************"
	echo "Secure FHIR Proxy Platform has successfully been deployed to group "$resourceGroupName" on "$(date)
	echo "Please note the following reference information for future use:"
	echo "Your secure FHIR proxy host is: https://"$functionAppHost
	echo "Your FHIR Endpoint via the proxy is: https://"$functionAppHost"/fhir"
	echo "You can see proxied capability statement here: https://"$functionAppHost"/fhir/metadata"
	echo "Your app configuration settings are stored securely in KeyVault: "$keyVaultName
	echo " "
	echo "Next Steps:  "
	echo " 1) You must run the ./createproxyserviceclient.bash script for each service principal you want to allow access"
	echo "    to FHIR Service via the FHIR Proxy"
	stepresult=${functionAppHost/.azurewebsites.net/}"-sc"
	echo "    You can use the following command to create a service client called "$stepresult":"
	echo "    ./createproxyserviceclient.bash -k "$keyVaultName" -n "$stepresult
	echo " 2) An Azure AD Application Administrator (RBAC role) must grant consent to the added FHIR Proxy Roles for each created service principal"
	echo " 3) You must run the ./createproxysmartclient.bash for each SMART Application you want to register for access"
	echo "    to FHIR Service via the FHIR Proxy"
	stepresult=${functionAppHost/.azurewebsites.net/}"-smart-client"
	echo "    You can use the following command to create a SMART client called "$stepresult":"
	echo "    ./createproxysmartclient.bash -k "$keyVaultName" -n "$stepresult "-a -p"
	echo " "
	echo "  You can view the ./Readme.md for more detailed information"
	echo "************************************************************************************************************"
	echo " "
)
	
if [ $?  != 0 ];
 then
	echo "FHIR Proxy deployment had errors. Consider deleting resource group "$resourceGroupName" and trying again..."
fi
