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
declare kvname="";
declare kvexists=""
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

declare tags="HealthArchitectures=FHIR-Proxy"
declare script_dir="$( cd "$( dirname "${BASH_SOURCE[0]}" )" &> /dev/null && pwd )"

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
	echo "@Microsoft.KeyVault(SecretUri=https://"$kvname".vault.azure.net/secrets/"$@"/)"
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
	echo "Enter your deployment prefix - proxy components begin with this prefix ["$defdeployprefix"]:"
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
if [ -z "$kvname" ]; then
	echo "Enter a keyvault name to store/retreive FHIR Server configuration ["$deployprefix$keyVaultAccountNameSuffix"]:"
	read kvname
	if [ -z "$kvname" ] ; then
		kvname=$deployprefix$keyVaultAccountNameSuffix
	fi
	[[ "${kvname:?}" ]]
fi
#Check KV exists
echo "Checking for keyvault "$kvname"..."
kvexists=$(az keyvault list --query "[?name == '$kvname'].name" --out tsv)
if [[ -n "$kvexists" ]]; then
	echo "Loading FHIR Server Credential/Connection Information from "$kvname"..."
	set +e
	fsurl=$(az keyvault secret show --vault-name $kvname --name FS-URL --query "value" --out tsv)
	if [ -n "$fsurl" ]; then
		fstenant=$(az keyvault secret show --vault-name $kvname --name FS-TENANT-NAME --query "value" --out tsv)
		fsclientid=$(az keyvault secret show --vault-name $kvname --name FS-CLIENT-ID --query "value" --out tsv)
		fssecret=$(az keyvault secret show --vault-name $kvname --name FS-SECRET --query "value" --out tsv)
		fsaud=$(az keyvault secret show --vault-name $kvname --name FS-RESOURCE --query "value" --out tsv)
	fi
fi
#Prompt for FHIR Server Parameters
if [ -z "$fsurl" ]; then
			echo $kvname" does not exist or does not contain FHIR Server settings..."
            echo " "
            echo "Gathering your API for FHIR Service - or FHIR Server information..."
			echo "Enter the destination FHIR Server URL (aka Endpoint):"
			read fsurl
			if [ -z "$fsurl" ] ; then
				echo "You must provide a destination FHIR Server URL"
				exit 1;
			fi
			echo "Enter the Tenant ID of the FHIR Server Service Client (used to connect to the FHIR Service). Empty for MSI[]:"
			read fstenant
			if [ ! -z "$fstenant" ] ; then
				echo "Enter the FHIR Server - Service Client Application ID (used to connecto to the FHIR Service). Leave Empty for MSI[]:"
				read fsclientid
				echo "Enter the FHIR Server - Service Client Secret (used to connect to the FHIR Service). Leave Empty for MSI[]:"
				read fssecret
			fi
			echo "Enter the FHIR Server - Service Client Audience/Resource (FHIR Service URL) ["$fsurl"]:"
			read fsaud
			if [ -z "$fsaud" ] ; then
					fsaud=$fsurl
			fi
			[[ "${fsaud:?}" ]]
fi
#Function App Name
echo "Enter the proxy function app name["$deffaname"]:"
	read faname
	if [ -z "$faname" ] ; then
		faname=$deffaname
	fi
	[[ "${faname:?}" ]]

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
		az group create --name $resourceGroupName --location $resourceGroupLocation --tags $tags 1> /dev/null
	)
	else
	echo "Using existing resource group..."
fi

#Set up variables
faresourceid="/subscriptions/"$subscriptionId"/resourceGroups/"$resourceGroupName"/providers/Microsoft.Web/sites/"$faname

# Final Check 
#
echo "Starting deployment of... $0 -i $subscriptionId -g $resourceGroupName -l $resourceGroupLocation -p $defdeployprefix"
read -p 'Press Enter to continue, or Ctrl+C to exit'

set +e

# Start deployment

#Start deployment
echo "Starting Secure FHIR Proxy deployment..."
(
		#set -x
		#Store FHIR Server Information
		if [[ -z "$kvexists" ]]; then
			echo "Creating Key Vault "$kvname"..."
			stepresult=$(az keyvault create --name $kvname --resource-group $resourceGroupName --location  $resourceGroupLocation --tags $tags)
			if [ $? != 0 ]; then
				echo "Could not create new keyvault "$kvname
				exit 1
			fi
			echo "Storing FHIR Server Information in Vault..."
			stepresult=$(az keyvault secret set --vault-name $kvname --name "FS-URL" --value $fsurl)
			stepresult=$(az keyvault secret set --vault-name $kvname --name "FS-TENANT-NAME" --value $fstenant)
			stepresult=$(az keyvault secret set --vault-name $kvname --name "FS-CLIENT-ID" --value $fsclientid)
			stepresult=$(az keyvault secret set --vault-name $kvname --name "FS-SECRET" --value $fssecret)
			stepresult=$(az keyvault secret set --vault-name $kvname --name "FS-RESOURCE" --value $fsaud)
		fi
		#Create Storage Account
		echo "Creating Storage Account ["$deployprefix$storageAccountNameSuffix"]..."
		stepresult=$(az storage account create --name $deployprefix$storageAccountNameSuffix --resource-group $resourceGroupName --location  $resourceGroupLocation --sku Standard_LRS --encryption-services blob --tags $tags)
		echo "Retrieving Storage Account Connection String..."
		storageConnectionString=$(az storage account show-connection-string -g $resourceGroupName -n $deployprefix$storageAccountNameSuffix --query "connectionString" --out tsv)
		stepresult=$(az keyvault secret set --vault-name $kvname --name "FP-STORAGEACCT" --value $storageConnectionString)
		
		#Redis Cache to Support Proxy Modules
		echo "Creating Redis Cache ["$deployprefix$redisAccountNameSuffix"]..."
		stepresult=$(az redis create --location $resourceGroupLocation --name $deployprefix$redisAccountNameSuffix --resource-group $resourceGroupName --sku Basic --vm-size c0 --tags $tags)
		echo "Creating Redis Connection String..."
		redisKey=$(az redis list-keys -g $resourceGroupName -n $deployprefix$redisAccountNameSuffix --query "primaryKey" --out tsv)
		redisConnectionString=$deployprefix$redisAccountNameSuffix".redis.cache.windows.net:6380,password="$redisKey",ssl=True,abortConnect=False"
		stepresult=$(az keyvault secret set --vault-name $kvname --name "FP-REDISCONNECTION" --value $redisConnectionString)
		
		#FHIR Proxy Function App
		#Create Service Plan
		echo "Creating Secure FHIR Proxy Function App Serviceplan["$deployprefix$serviceplanSuffix"]..."
		stepresult=$(az appservice plan create -g  $resourceGroupName -n $deployprefix$serviceplanSuffix --number-of-workers 2 --sku B1 --tags $tags)
		
		#Create the function app
		echo "Creating Secure FHIR Proxy Function App ["$faname"]..."
		fahost=$(az functionapp create --name $faname --storage-account $deployprefix$storageAccountNameSuffix  --plan $deployprefix$serviceplanSuffix  --resource-group $resourceGroupName --runtime dotnet --os-type Windows --functions-version 3 --tags $tags --query defaultHostName --output tsv)
		stepresult=$(az functionapp stop --name $faname --resource-group $resourceGroupName)
		stepresult=$(az keyvault secret set --vault-name $kvname --name "FP-HOST" --value $fahost)
		echo "Creating MSI for Function App..."
		msi=$(az functionapp identity assign -g $resourceGroupName -n $faname --query "principalId" --out tsv)
		echo "Setting KeyVault Policy to allow secret access..."
		stepresult=$(az keyvault set-policy -n $kvname --secret-permissions list get --object-id $msi)
		
		#Add App Settings
		echo "Configuring Secure FHIR Proxy App ["$faname"]..."
		stepresult=$(az functionapp config appsettings set --name $faname --resource-group $resourceGroupName --settings FP-PRE-PROCESSOR-TYPES=FHIRProxy.preprocessors.TransformBundlePreProcess FP-REDISCONNECTION=$(kvuri FP-REDISCONNECTION) FP-ADMIN-ROLE=$roleadmin FP-READER-ROLE=$rolereader FP-WRITER-ROLE=$rolewriter FP-GLOBAL-ACCESS-ROLES=$roleglobal FP-PATIENT-ACCESS-ROLES=$rolepatient FP-PARTICIPANT-ACCESS-ROLES=$roleparticipant FP-STORAGEACCT=$(kvuri FP-STORAGEACCT) FS-URL=$(kvuri FS-URL) FS-TENANT-NAME=$(kvuri FS-TENANT-NAME) FS-CLIENT-ID=$(kvuri FS-CLIENT-ID) FS-SECRET=$(kvuri FS-SECRET) FS-RESOURCE=$(kvuri FS-RESOURCE))
		echo "Deploying Secure FHIR Proxy Function App from source repo to ["$fahost"]..."
		stepresult=$(retry az functionapp deployment source config --branch main --manual-integration --name $faname --repo-url https://github.com/microsoft/fhir-proxy --resource-group $resourceGroupName)
		echo "Creating Service Principal for AAD Auth"
		stepresult=$(az ad sp create-for-rbac -n "https://"$fahost --skip-assignment)
		spappid=$(echo $stepresult | jq -r '.appId')
		sptenant=$(echo $stepresult | jq -r '.tenant')
		spsecret=$(echo $stepresult | jq -r '.password')
		spreplyurls="https://"$fahost"/.auth/login/aad/callback"
		tokeniss="https://sts.windows.net/"$sptenant
		echo "Storing FHIR Proxy Client Information in Vault..."
		stepresult=$(az keyvault secret set --vault-name $kvname --name "FP-RBAC-NAME" --value "https://"$fahost)
		stepresult=$(az keyvault secret set --vault-name $kvname --name "FP-RBAC-TENANT-NAME" --value $sptenant)
		stepresult=$(az keyvault secret set --vault-name $kvname --name "FP-RBAC-CLIENT-ID" --value $spappid)
		stepresult=$(az keyvault secret set --vault-name $kvname --name "FP-RBAC-CLIENT-SECRET" --value $spsecret)
		echo "Adding Sign-in User Read Permission on Graph API..."
		stepresult=$(az ad app permission add --id $spappid --api 00000002-0000-0000-c000-000000000000 --api-permissions 311a71cc-e848-46a1-bdf8-97ff7156d8e6=Scope)
		
		#echo "Granting Admin Consent to Permission..."
		stepresult=$(az ad app permission grant --id $spappid --api 00000002-0000-0000-c000-000000000000)
		echo "Configuring reply urls for app..."
		stepresult=$(az ad app update --id $spappid --reply-urls $spreplyurls)
		echo "Adding FHIR Custom Roles to Manifest..."
		stepresult=$(az ad app update --id $spappid --app-roles @${script_dir}/fhirroles.json)
		echo "Enabling AAD Authorization and Securing the FHIR Proxy"
		stepresult=$(az webapp auth update -g $resourceGroupName -n $faname --enabled true --action AllowAnonymous --aad-allowed-token-audiences $fahost --aad-client-id $spappid --aad-client-secret $spsecret --aad-token-issuer-url $tokeniss)
		echo "Starting fhir proxy function app..."
		stepresult=$(az functionapp start --name $faname --resource-group $resourceGroupName)
		echo " "
		echo "************************************************************************************************************"
		echo "Secure FHIR Proxy Platform has successfully been deployed to group "$resourceGroupName" on "$(date)
		echo "Please note the following reference information for future use:"
		echo "Your secure fhir proxy host is: https://"$fahost
		echo "Your app configuration settings are stored securely in KeyVault: "$kvname
		echo "************************************************************************************************************"
		echo " "
)
	
if [ $?  != 0 ];
 then
	echo "FHIR Proxy deployment had errors. Consider deleting resource group "$resourceGroupName" and trying again..."
fi
