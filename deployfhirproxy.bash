#!/bin/bash
set -euo pipefail
IFS=$'\n\t'

# -e: immediately exit if any command has a non-zero exit status
# -o: prevents errors in a pipeline from being masked
# IFS new value is less likely to cause confusing bugs when looping arrays or arguments (e.g. $@)
#
#FHIR Proxy Setup --- Author Steve Ordahl Principal Architect Health Data Platform
#
declare defsubscriptionId=""
declare subscriptionId=""
declare resourceGroupName=""
declare resourceGroupLocation=""
declare keyVaultAccountNameSuffix="kv"$RANDOM
declare storageAccountNameSuffix="store"$RANDOM
declare storageConnectionString=""
declare redisAccountNameSuffix="cache"$RANDOM
declare redisConnectionString=""
declare redisKey=""
declare serviceplanSuffix="asp"
declare faname=""
declare deffaname="sfp"$RANDOM
declare fsurl=""
declare fsclientid=""
declare fssecret=""
declare fstenant=""
declare fsaud=""
declare deployzip="distribution/publish.zip"
declare deployprefix=""
declare defdeployprefix=""
declare stepresult=""
declare fahost=""
declare fakey=""
declare faresourceid=""
declare roleadmin="Administrator"
declare rolereader="Reader"
declare rolewriter="Writer"
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

function fail {
  echo $1 >&2
  exit 1
}

function retry {
  local n=1
  local max=5
  local delay=15
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
}
function kvuri {
	echo "@Microsoft.KeyVault(SecretUri=https://"$deployprefix$keyVaultAccountNameSuffix".vault.azure.net/secrets/"$@"/)"
}

usage() { echo "Usage: $0 -i <subscriptionId> -g <resourceGroupName> -l <resourceGroupLocation> -p <prefix>" 1>&2; exit 1; }
# Initialize parameters specified from command line
while getopts ":i:g:n:l:p" arg; do
	case "${arg}" in
		p)
			deployprefix=${OPTARG:0:14}
			deployprefix=${deployprefix,,}
			deployprefix=${deployprefix//[^[:alnum:]]/}
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
		esac
done
shift $((OPTIND-1))
echo "Executing "$0"..."
echo "Checking Azure Authentication..."
#login to azure using your credentials
az account show 1> /dev/null

if [ $? != 0 ];
then
	az login
fi

defsubscriptionId=$(az account show --query "id" --out json | sed 's/"//g') 

#Prompt for parameters is some required parameters are missing
if [[ -z "$subscriptionId" ]]; then
	echo "Enter your subscription ID ["$defsubscriptionId"]:"
	read subscriptionId
	if [ -z "$subscriptionId" ] ; then
		subscriptionId=$defsubscriptionId
	fi
	[[ "${subscriptionId:?}" ]]
fi

if [[ -z "$resourceGroupName" ]]; then
	echo "This script will look for an existing resource group, otherwise a new one will be created "
	echo "You can create new resource groups with the CLI using: az group create "
	echo "Enter a resource group name"
	read resourceGroupName
	[[ "${resourceGroupName:?}" ]]
fi

defdeployprefix=${resourceGroupName:0:14}
defdeployprefix=${defdeployprefix//[^[:alnum:]]/}
defdeployprefix=${defdeployprefix,,}

if [[ -z "$resourceGroupLocation" ]]; then
	echo "If creating a *new* resource group, you need to set a location "
	echo "You can lookup locations with the CLI using: az account list-locations "
	
	echo "Enter resource group location:"
	read resourceGroupLocation
fi
#Prompt for parameters is some required parameters are missing
if [[ -z "$deployprefix" ]]; then
	echo "Enter your deployment prefix ["$defdeployprefix"]:"
	read deployprefix
	if [ -z "$deployprefix" ] ; then
		deployprefix=$defdeployprefix
	fi
	deployprefix=${deployprefix:0:14}
	deployprefix=${deployprefix//[^[:alnum:]]/}
    deployprefix=${deployprefix,,}
	[[ "${deployprefix:?}" ]]
fi
if [ -z "$subscriptionId" ] || [ -z "$resourceGroupName" ]; then
	echo "Either one of subscriptionId, resourceGroupName is empty"
	usage
fi
#Function App Name
echo "Enter the proxy function app name["$deffaname"]:"
	read faname
	if [ -z "$faname" ] ; then
		faname=$deffaname
	fi
	[[ "${faname:?}" ]]
#Prompt for FHIR Server Parameters
echo "Enter the destination FHIR Server URL:"
read fsurl
if [ -z "$fsurl" ] ; then
	echo "You must provide a destination FHIR Server URL"
	exit 1;
fi
echo "Enter the FHIR Server Service Client Tenant ID. Empty for MSI[]:"
read fstenant
if [ ! -z "$fstenant" ] ; then
	echo "Enter the FHIR Server Service Client Application ID. Leave Empty for MSI[]:"
	read fsclientid
	echo "Enter the FHIR Server Service Client Secret. Leave Empty for MSI[]:"
	read fssecret
fi
echo "Enter the FHIR Server/Service Client Audience/Resource ["$fsurl"]:"
	read fsaud
	if [ -z "$fsaud" ] ; then
		fsaud=$fsurl
	fi
	[[ "${fsaud:?}" ]]
echo "Setting default subscription and checking for resource group..."
#set the default subscription id
az account set --subscription $subscriptionId

set +e

#Check for existing RG
if [ $(az group exists --name $resourceGroupName) = false ]; then
	echo "Resource group with name" $resourceGroupName "could not be found. Creating new resource group.."
	set -e
	(
		set -x
		az group create --name $resourceGroupName --location $resourceGroupLocation 1> /dev/null
	)
	else
	echo "Using existing resource group..."
fi
#Set up variables
faresourceid="/subscriptions/"$subscriptionId"/resourceGroups/"$resourceGroupName"/providers/Microsoft.Web/sites/"$faname
#Start deployment
echo "Starting Secure FHIR Proxy deployment..."
(
		#set -x
		#Create Key Vault
		echo "Creating Key Vault ["$deployprefix$keyVaultAccountNameSuffix"]..."
		stepresult=$(az keyvault create --name $deployprefix$keyVaultAccountNameSuffix --resource-group $resourceGroupName --location  $resourceGroupLocation)
		#Store FHIR Server Information
		echo "Storing FHIR Server Information in Vault..."
		stepresult=$(az keyvault secret set --vault-name $deployprefix$keyVaultAccountNameSuffix --name "FS-URL" --value $fsurl)
		stepresult=$(az keyvault secret set --vault-name $deployprefix$keyVaultAccountNameSuffix --name "FS-TENANT-NAME" --value $fstenant)
		stepresult=$(az keyvault secret set --vault-name $deployprefix$keyVaultAccountNameSuffix --name "FS-CLIENT-ID" --value $fsclientid)
		stepresult=$(az keyvault secret set --vault-name $deployprefix$keyVaultAccountNameSuffix --name "FS-SECRET" --value $fssecret)
		stepresult=$(az keyvault secret set --vault-name $deployprefix$keyVaultAccountNameSuffix --name "FS-RESOURCE" --value $fsaud)
		
		#Create Storage Account
		echo "Creating Storage Account ["$deployprefix$storageAccountNameSuffix"]..."
		stepresult=$(az storage account create --name $deployprefix$storageAccountNameSuffix --resource-group $resourceGroupName --location  $resourceGroupLocation --sku Standard_LRS --encryption-services blob)
		echo "Retrieving Storage Account Connection String..."
		storageConnectionString=$(az storage account show-connection-string -g $resourceGroupName -n $deployprefix$storageAccountNameSuffix --query "connectionString" --out tsv)
		stepresult=$(az keyvault secret set --vault-name $deployprefix$keyVaultAccountNameSuffix --name "FP-STORAGEACCT" --value $storageConnectionString)
		#Redis Cache to Support Proxy Modules
		echo "Creating Redis Cache ["$deployprefix$redisAccountNameSuffix"]..."
		stepresult=$(az redis create --location $resourceGroupLocation --name $deployprefix$redisAccountNameSuffix --resource-group $resourceGroupName --sku Basic --vm-size c0)
		echo "Creating Redis Connection String..."
		redisKey=$(az redis list-keys -g $resourceGroupName -n $deployprefix$redisAccountNameSuffix --query "primaryKey" --out tsv)
		redisConnectionString=$deployprefix$redisAccountNameSuffix".redis.cache.windows.net:6380,password="$redisKey",ssl=True,abortConnect=False"
		stepresult=$(az keyvault secret set --vault-name $deployprefix$keyVaultAccountNameSuffix --name "FP-REDISCONNECTION" --value $redisConnectionString)
		#FHIR Proxy Function App
		#Create Service Plan
		echo "Creating Secure FHIR Proxy Function App Serviceplan["$deployprefix$serviceplanSuffix"]..."
		stepresult=$(az appservice plan create -g  $resourceGroupName -n $deployprefix$serviceplanSuffix --number-of-workers 2 --sku B1)
		#Create the function app
		echo "Creating Secure FHIR Proxy Function App ["$faname"]..."
		fahost=$(az functionapp create --name $faname --storage-account $deployprefix$storageAccountNameSuffix  --plan $deployprefix$serviceplanSuffix  --resource-group $resourceGroupName --runtime dotnet --os-type Windows --functions-version 3 --query defaultHostName --output tsv)
		stepresult=$(az functionapp stop --name $faname --resource-group $resourceGroupName)
		stepresult=$(az keyvault secret set --vault-name $deployprefix$keyVaultAccountNameSuffix --name "FP-HOST" --value $fahost)
		echo "Creating MSI for Function App..."
		msi=$(az functionapp identity assign -g $resourceGroupName -n $faname --query "principalId" --out tsv)
		echo "Setting KeyVault Policy to allow secret access..."
		stepresult=$(az keyvault set-policy -n $deployprefix$keyVaultAccountNameSuffix --secret-permissions list get set --object-id $msi)
		#Add App Settings
		echo "Configuring Secure FHIR Proxy App ["$faname"]..."
		stepresult=$(az functionapp config appsettings set --name $faname --resource-group $resourceGroupName --settings FP-PRE-PROCESSOR-TYPES=FHIRProxy.preprocessors.TransformBundlePreProcess FP-REDISCONNECTION=$(kvuri FP-REDISCONNECTION) FP-ADMIN-ROLE=$roleadmin FP-READER-ROLE=$rolereader FP-WRITER-ROLE=$rolewriter FP-GLOBAL-ACCESS-ROLES=$roleglobal FP-PATIENT-ACCESS-ROLES=$rolepatient FP-PARTICIPANT-ACCESS-ROLES=$roleparticipant FP-STORAGEACCT=$(kvuri FP-STORAGEACCT) FS-URL=$(kvuri FS-URL) FS-TENANT-NAME=$(kvuri FS-TENANT-NAME) FS-CLIENT-ID=$(kvuri FS-CLIENT-ID) FS-SECRET=$(kvuri FS-SECRET) FS-RESOURCE=$(kvuri FS-RESOURCE))
		echo "Deploying Secure FHIR Proxy Function App from source repo to ["$fahost"]..."
		stepresult=$(retry az functionapp deployment source config-zip --name $faname --resource-group $resourceGroupName --src $deployzip)
		echo "Creating Service Principal for AAD Auth"
		stepresult=$(az ad sp create-for-rbac -n "https://"$fahost)
		spappid=$(echo $stepresult | jq -r '.appId')
		sptenant=$(echo $stepresult | jq -r '.tenant')
		spsecret=$(echo $stepresult | jq -r '.password')
		spreplyurls="https://"$fahost"/.auth/login/aad/callback"
		tokeniss="https://sts.windows.net/"$sptenant
		echo "Storing FHIR Proxy Client Information in Vault..."
		stepresult=$(az keyvault secret set --vault-name $deployprefix$keyVaultAccountNameSuffix --name "FP-RBAC-NAME" --value "https://"$fahost)
		stepresult=$(az keyvault secret set --vault-name $deployprefix$keyVaultAccountNameSuffix --name "FP-RBAC-TENANT-NAME" --value $sptenant)
		stepresult=$(az keyvault secret set --vault-name $deployprefix$keyVaultAccountNameSuffix --name "FP-RBAC-CLIENT-ID" --value $spappid)
		stepresult=$(az keyvault secret set --vault-name $deployprefix$keyVaultAccountNameSuffix --name "FP-RBAC-CLIENT-SECRET" --value $spsecret)
		echo "Adding Sign-in User Read Permission on Graph API..."
		stepresult=$(az ad app permission add --id $spappid --api 00000002-0000-0000-c000-000000000000 --api-permissions 311a71cc-e848-46a1-bdf8-97ff7156d8e6=Scope)
		#echo "Granting Admin Consent to Permission..."
		#stepresult=$(az ad app permission grant --id $spappid --api 00000002-0000-0000-c000-000000000000)
		echo "Configuring reply urls for app..."
		stepresult=$(az ad app update --id $spappid --reply-urls $spreplyurls)
		echo "Adding FHIR Custom Roles to Manifest..."
		stepresult=$(az ad app update --id $spappid --app-roles @fhirroles.json)
		echo "Enabling AAD Authorization and Securing the FHIR Proxy"
		stepresult=$(az webapp auth update -g $resourceGroupName -n $faname --enabled true --action LoginWithAzureActiveDirectory --aad-allowed-token-audiences $fahost --aad-client-id $spappid --aad-client-secret $spsecret --aad-token-issuer-url $tokeniss)
		echo "Starting fhir proxy function app..."
		stepresult=$(az functionapp start --name $faname --resource-group $resourceGroupName)
		echo " "
		echo "************************************************************************************************************"
		echo "Secure FHIR Proxy Platform has successfully been deployed to group "$resourceGroupName" on "$(date)
		echo "Please note the following reference information for future use:"
		echo "Your secure fhir proxy host is: https://"$fahost
		echo "Your app configuration settings are stored securely in KeyVault: "$deployprefix$keyVaultAccountNameSuffix
		echo "************************************************************************************************************"
		echo " "
)
	
if [ $?  != 0 ];
 then
	echo "FHIR Proxy deployment had errors. Consider deleting resource group "$resourceGroupName" and trying again..."
fi
