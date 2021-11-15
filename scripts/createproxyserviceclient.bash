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
declare defFhirSvcClient="fhirProxy-svc-client"$RANDOM
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

usage() { echo "Usage: $0 -k <keyvault> -n <service client name> -s (to store credentials in <keyvault>) -p (to generate postman environment)" 1>&2; exit 1; }

## Script Main Body (start here)
# Initialize parameters specified from command line
#

while getopts ":k:n:sp" arg; do
	case "${arg}" in
		k)
			kvname=${OPTARG}
			;;
		n)
			spname=${OPTARG}
			;;
		s)
			storekv="yes"
			;;
		p)
			genpostman="yes"
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
	echo "Cannot Locate Key Vault "$kvname" this deployment requires access to the proxy keyvault...Is the Proxy Installed?"
	exit 1
fi

if [[ -z "$spname" ]]; then
	echo "Enter a name for this service client [$defFhirSvcClient]: "
	read spname
    if [ -z "$spname" ]; then
        spname=$defFhirSvcClient
    fi
	[[ "${spname:?}" ]]
fi

if [[ -z "$storekv" ]]; then
	echo "Do you want to store the service client "$defFhirSvcClient" credentials in the keyvault "$kvname"? [y/n]: "
	read kvanswer
    if [[ $kvanswer =  "y" ]]; then
        storekv="yes"
    fi
fi

if [[ -z "$genpostman" ]]; then
	echo "Do you want to generate a Postman Environment ? [y/n]: "
	read postman_answer
    if [[ $postman_answer = "y" ]]; then
        genpostman="yes"
    fi
fi

# Final Check 
#
echo "Starting deployment of... $0 -k $kvname -n $spname -s $storekv -p $genpostman"
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
		stepresult=$(az ad sp create-for-rbac -n $spname)
		spappid=$(echo $stepresult | jq -r '.appId')
		sptenant=$(echo $stepresult | jq -r '.tenant')
		spsecret=$(echo $stepresult | jq -r '.password')
		stepresult=$(az ad app permission add --id $spappid --api $fpclientid --api-permissions 24c50db1-1e11-4273-b6a0-b697f734bcb4=Role 2d1c681b-71e0-4f12-9040-d0f42884be86=Role)
		stepresult=$(az ad app permission grant --id $spappid --api $fpclientid)
		if [ -n "$storekv" ]; then
			echo "Updating Keyvault with new Service Client Settings..."
			stepresult=$(az keyvault secret set --vault-name $kvname --name "FP-SC-TENANT-NAME" --value $sptenant)
			stepresult=$(az keyvault secret set --vault-name $kvname --name "FP-SC-CLIENT-ID" --value $spappid)
			stepresult=$(az keyvault secret set --vault-name $kvname --name "FP-SC-SECRET" --value $spsecret)
			stepresult=$(az keyvault secret set --vault-name $kvname --name "FP-SC-RESOURCE" --value $fpclientid)
			fpscurl="https://"$fphost
			stepresult=$(az keyvault secret set --vault-name $kvname --name "FP-SC-URL" --value $fpscurl)
		fi
		if [ -n "$genpostman" ]; then
			echo "Generating Postman environment for proxy access..."
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
		fi
		echo " "
		echo "************************************************************************************************************"
		echo "Created fhir proxy service principal client "$spname" on "$(date)
		echo "This client can be used for OAuth2 client_credentials flow authentication to the FHIR Proxy"
		echo " "
		if [ -n "$storekv" ]; then
			echo "Your client credentials have been securely stored as secrets in keyvault "$kvname
			echo "The secret prefix is FP-SC-"
		else
			echo "Please note the following reference information for use in authentication calls:"
			echo "Your Service Prinicipal Client/Application ID is: "$spappid
			echo "Your Service Prinicipal Client Secret is: "$spsecret
			echo "Your Service Principal Tenant Id is: "$sptenant
			echo "Your Service Principal Resource/Audience is: "$fpclientid
		fi
		echo " "
		if [ -n "$genpostman" ]; then
			echo "For your convenience a Postman environment "$spname".postman_environment.json has been generated"
			echo "It can imported along with the FHIR CALLS-Sample.postman_collection.json into postman to test your proxy access"
			echo "For Postman Importing help please reference the following URL:"
			echo "https://learning.postman.com/docs/getting-started/importing-and-exporting-data/#importing-postman-data"
		fi
		echo "You will need to access Azure portal and grant admin consent to "$spname" API Permissions"
		echo "For more information see https://docs.microsoft.com/en-us/azure/active-directory/manage-apps/grant-admin-consent#grant-admin-consent-in-app-registrations"
		echo "************************************************************************************************************"
		echo " "
		echo "Note: The display output and files created by this script can contain sensitive information please protect it!"
		echo " "
)
