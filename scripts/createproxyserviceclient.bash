#!/bin/bash
set -euo pipefail
IFS=$'\n\t'

# -e: immediately exit if any command has a non-zero exit status
# -o: prevents errors in a pipeline from being masked
# IFS new value is less likely to cause confusing bugs when looping arrays or arguments (e.g. $@)
#
# Setup Application Service Client to FHIR Proxy --- Author Steve Ordahl Principal Architect Health Data Platform
#

# Script variables 
declare script_dir="$( cd -P -- "$( dirname "${BASH_SOURCE[0]}" )" &> /dev/null && pwd -P )"
declare stepresult=""
declare spname=""
declare kvname=""
declare kvexists=""
declare defsubscriptionId=""
declare fpclientid=""
declare fptenantid=""
declare fpsecret=""
declare fphost=""
declare repurls=""
declare spappid=""
declare sptenant=""
declare spsecret=""
declare storekv=""
declare genpostman=""
declare pmenv=""
declare pmuuid=""
declare pmfhirurl=""
declare defproxySvcClient="fpsc-client"$RANDOM
declare kvanswer=""
declare postman_answer=""
declare fpscurl=""


# Script Functions
#
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


function result () {
    if [[ $1 = "ok" ]]; then
        echo -e "..................... [ \033[32m ok \033[37m ] \r" 
      else
        echo -e "..................... [ \033[31m failed \033[37m ] \r"
        exit 1
      fi
    echo -e "\033[37m \r"
    sleep 1
}

usage() { echo "Usage: $0 -k <keyvault> -n <serviceClient name>" 1>&2; exit 1; }

## Script Main Body (start here)
# Initialize parameters specified from command line
#

while getopts ":k:n:p:" arg; do
	case "${arg}" in
		k)
			kvname=${OPTARG}
			;;
		n)
			spname=${OPTARG:0:16}
			spname=${spname,,}
			;;
	esac
done
shift $((OPTIND-1))
echo "Executing "$0"..."
echo "Note: You must be authenticated to the same tenant as the proxy server"
echo "Checking Azure Authentication..."

#login to azure using your credentials
az account show 1> /dev/null

if [ $? != 0 ];
then
	az login
fi

# set default subscription 
defsubscriptionId=$(az account show --query "id" --out json | sed 's/"//g') 

# Test for correct directory path / destination 
if [ -f "${script_dir}/$0" ] && [ -f "${script_dir}/postmantemplate.json" ] ; then
	echo "Checking Script execution directory..."
else
	echo "Please ensure you launch this script from within the ./scripts directory"
	usage ;
fi


# Prompt for parameters is some required parameters are missing

echo " "
echo "Collecting Script Parameters "
echo " "


if [[ -z "$kvname" ]]; then
	echo "Enter keyvault name that contains the fhir proxy configuration: "
	read kvname
    if [[ -z "$kvname" ]]; then

	    echo "Keyvault name must be specified"
	    usage
    fi
fi

# Check KV exists
# 
echo "Checking for keyvault "$kvname"..."
kvexists=$(az keyvault list --query "[?name == '$kvname'].name" --out tsv)
if [[ -z "$kvexists" ]]; then
	echo "Cannot Locate Key Vault "$kvname" this deployment requires access to the proxy keyvault"
	exit 1
fi

# Set a Default value for the App Name 
#
defproxySvcClient=${defproxySvcClient:0:16}
defproxySvcClient=${defproxySvcClient,,}

if [[ -z "$spname" ]]; then
	echo "Enter a name for this service client [$defproxySvcClient]: "
	read spname
    if [ -z "$spname" ]; then
        spname=$defproxySvcClient
    fi
	[[ "${spname:?}" ]]
fi


# Prompt for final confirmation
#
echo "--- "
echo "Ready to start deployment of FHIR Proxy Service Client: ["$spname"] with the following values:"
echo "Keyvault:.............................. "$kvname
echo " "
echo "Please validate the settings above before continuing"
read -p 'Press Enter to continue, or Ctrl+C to exit'

set +e

# Start deployment
#
echo "Creating Service Client Principal "$spname"..."
(
	echo "Loading configuration settings from key vault "$kvname"..."
	fphost=$(az keyvault secret show --vault-name $kvname --name FP-HOST --query "value" --out tsv)
	fpclientid=$(az keyvault secret show --vault-name $kvname --name FP-RBAC-CLIENT-ID --query "value" --out tsv)
	if [ -z "$fpclientid" ] || [ -z "$fphost" ]; then
		echo $kvname" does not appear to contain fhir proxy settings...Is the Proxy Installed?"
		exit 1
	fi

	echo "Creating FHIR Proxy Client Service Principal for AAD Auth"
	stepresult=$(az ad sp create-for-rbac -n $spname --only-show-errors)
	spappid=$(echo $stepresult | jq -r '.appId')
	sptenant=$(echo $stepresult | jq -r '.tenant')
	spsecret=$(echo $stepresult | jq -r '.password')

	stepresult=$(az ad app permission add --id $spappid --api $fpclientid --api-permissions 24c50db1-1e11-4273-b6a0-b697f734bcb4=Role 2d1c681b-71e0-4f12-9040-d0f42884be86=Role)
	stepresult=$(az ad app permission grant --id $spappid --api $fpclientid)

	echo "Updating Keyvault with new Service Client Settings..."
	stepresult=$(az keyvault secret set --vault-name $kvname --name "FP-SC-TENANT-NAME" --value $sptenant)
	stepresult=$(az keyvault secret set --vault-name $kvname --name "FP-SC-CLIENT-ID" --value $spappid)
	stepresult=$(az keyvault secret set --vault-name $kvname --name "FP-SC-SECRET" --value $spsecret)
	stepresult=$(az keyvault secret set --vault-name $kvname --name "FP-SC-RESOURCE" --value $fpclientid)
	fpscurl="https://"$fphost
	stepresult=$(az keyvault secret set --vault-name $kvname --name "FP-SC-URL" --value $fpscurl)

	echo "Generating Postman environment for Proxy Service Client access..."
	rm $spname".postman_environment.json" 2>/dev/null
	pmuuid=$(cat /proc/sys/kernel/random/uuid)
	pmenv=$(<postmantemplate.json)
	pmfhirurl="https://"$fphost"/fhir"
	pmenv=${pmenv/~guid~/$pmuuid}
	pmenv=${pmenv/~envname~/$spname}
	pmenv=${pmenv/~tenentid~/$sptenant}
	pmenv=${pmenv/~clientid~/$spappid}
	pmenv=${pmenv/~clientsecret~/$spsecret}
	pmenv=${pmenv/~fhirurl~/$pmfhirurl}
	pmenv=${pmenv/~resource~/$fpclientid}
	echo $pmenv >> $spname".postman_environment.json"

	echo " "
	echo "************************************************************************************************************"
	echo "Created fhir proxy service principal client "$spname" on "$(date)
	echo "This client can be used for OAuth2 client_credentials flow authentication to the FHIR Proxy"
	echo " "
	echo "Your client credentials have been securely stored as secrets in keyvault "$kvname
	echo "The secret prefix is FP-SC-"
	echo " "
	echo "For your convenience a Postman environment "$spname".postman_environment.json has been generated"
	echo "It can imported along with the FHIR CALLS-Sample.postman_collection.json into postman to test your proxy access"
	echo "For Postman Importing help please reference the following URL:"
	echo "https://learning.postman.com/docs/getting-started/importing-and-exporting-data/#importing-postman-data"
	echo "You will need to access Azure portal and grant admin consent to "$spname" API Permissions"
	echo "For more information see https://docs.microsoft.com/en-us/azure/active-directory/manage-apps/grant-admin-consent#grant-admin-consent-in-app-registrations"
	echo "************************************************************************************************************"
	echo " "
)
