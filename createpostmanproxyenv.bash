#!/bin/bash
set -euo pipefail
IFS=$'\n\t'

# -e: immediately exit if any command has a non-zero exit status
# -o: prevents errors in a pipeline from being masked
# IFS new value is less likely to cause confusing bugs when looping arrays or arguments (e.g. $@)
#
# Create Postman Environment for FHIR Proxy --- Author Steve Ordahl Principal Architect Health Data Platform
#

usage() { echo "Usage: $0 -k <keyvault>" 1>&2; exit 1; }

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
declare stepresult=""
declare spname=""
declare kvname=""
declare kvexists=""
declare defsubscriptionId=""
declare fpclientid=""
declare fptenantid=""
declare fpsecret=""
declare fphost=""
declare fpurl=""
declare repurls=""
declare spappid=""
declare fptenant=""
declare fpsecret=""
declare storekv=""
declare genpostman=""
declare pmenv=""
declare pmuuid=""
declare pmfhirurl=""
declare pmstsurl=""
declare pmscope=""
# Initialize parameters specified from command line
while getopts ":k:n:sp" arg; do
        case "${arg}" in
                k)
                        kvname=${OPTARG}
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

defsubscriptionId=$(az account show --query "id" --out json | sed 's/"//g')

#Prompt for parameters is some required parameters are missing
if [[ -z "$kvname" ]]; then
        echo "Enter keyvault name that contains the fhir proxy configuration: "
        read kvname
fi
if [ -z "$kvname" ]; then
        echo "Keyvault name must be specified"
        usage
fi
#Check KV exists
echo "Checking for keyvault "$kvname"..."
kvexists=$(az keyvault list --query "[?name == '$kvname'].name" --out tsv)
if [[ -z "$kvexists" ]]; then
        echo "Cannot Locate Key Vault "$kvname" this deployment requires access to the proxy keyvault...Is the Proxy Installed?"
        exit 1
fi
set +e
#Start deployment
echo "Creating Postman environment for FHIR Proxy..."
(
                echo "Loading configuration settings from key vault "$kvname"..."
                fphost=$(az keyvault secret show --vault-name $kvname --name FP-HOST --query "value" --out tsv)
                fpclientid=$(az keyvault secret show --vault-name $kvname --name FP-RBAC-CLIENT-ID --query "value" --out tsv)
                fpurl=$(az keyvault secret show --vault-name $kvname --name FP-RBAC-NAME --query "value" --out tsv)
                fpsecret=$(az keyvault secret show --vault-name $kvname --name FP-RBAC-CLIENT-SECRET --query "value" --out tsv)
                fptenant=$(az keyvault secret show --vault-name $kvname --name FP-RBAC-TENANT-NAME --query "value" --out tsv)
                if [ -z "$fpclientid" ] || [ -z "$fphost" ]; then
                        echo $kvname" does not appear to contain fhir proxy settings...Is the Proxy Installed?"
                        exit 1
                fi
                echo "Generating Postman environment for proxy access..."
                rm $fphost".postman_environment.json" 2>/dev/null
                        pmuuid=$(cat /proc/sys/kernel/random/uuid)
                        pmenv=$(<postmantemplateauth.json)
						pmscope="https://"$fphost"/.default"
                        pmfhirurl="https://"$fphost"/fhir"
                        pmstsurl="https://"$fphost"/AadSmartOnFhirProxy"
                        pmenv=${pmenv/~guid~/$pmuuid}
                        pmenv=${pmenv/~envname~/$fphost}
                        pmenv=${pmenv/~tenentid~/$fptenant}
                        pmenv=${pmenv/~stsurl~/$pmstsurl}
                        pmenv=${pmenv/~clientid~/$fpclientid}
                        pmenv=${pmenv/~clientsecret~/$fpsecret}
                        pmenv=${pmenv/~fhirurl~/$pmfhirurl}
                        pmenv=${pmenv/~resource~/$fpclientid}
						pmenv=${pmenv/~scope~/$pmscope}
                        echo $pmenv >> $fphost".postman_environment.json"
                echo " "
                echo "************************************************************************************************************"
                echo "Created fhir proxy postman environment "$fphost" on "$(date)
                echo "This client can be used for OAuth2 code flow authentication to the FHIR Proxy"
                echo "For your convenience a Postman environment "$fphost".postman_environment.json has been generated"
                echo "It can imported along with the FHIR CALLS-Sample.postman_collection.json into postman to test your proxy access"
                echo "For Postman Importing help please reference the following URL:"
                echo "https://learning.postman.com/docs/getting-started/importing-and-exporting-data/#importing-postman-data"
                echo "For more information see https://docs.microsoft.com/en-us/azure/active-directory/manage-apps/grant-admin-consent#grant-admin-consent-in-app-registrations"
                echo "************************************************************************************************************"
                echo " "
                echo "Note: The display output and files created by this script can contain sensitive information please protect it!"
                echo " "
)