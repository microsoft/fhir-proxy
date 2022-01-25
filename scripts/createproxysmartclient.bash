#!/bin/bash
set -euo pipefail
IFS=$'\n\t'

# -e: immediately exit if any command has a non-zero exit status
# -o: prevents errors in a pipeline from being masked
# IFS new value is less likely to cause confusing bugs when looping arrays or arguments (e.g. $@)
#
# Setup SMART Application Client for use with FHIR Proxy --- Author Steve Ordahl Principal Architect Health Data Platform
#

usage() { echo "Usage: $0 -k <keyvault> -n <smart client name> -a (If you are a tenant admin, add FHIRUserClaim to smartapp id token, must have FHIRUserClaim custom claim policy defined for the tenant) -s (to store credentials in <keyvault>) -p (to generate postman environment) -u (For Public Client Registration)" 1>&2; exit 1; }

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
declare repurls=""
declare spappid=""
declare sptenant=""
declare spsecret=""
declare storekv=""
declare genpostman=""
declare pmenv=""
declare pmuuid=""
declare pmfhirurl=""
declare scopesdef="fhirUser launch/patient patient/Patient.read"
declare scopes=""
declare smartscopes=""
declare public=""
declare scopearr
declare permissions=""
declare owner=""
declare replyurls=""
declare adduserclaim=""
declare fhiruserclaimid=""
declare spobjectid=""
declare fhiruserclaimname="FHIRUserClaim"
declare postmanreply="https://oauth.pstmn.io/v1/callback"
declare msggraphid=""
declare msgopenid=""
declare msgprofileid=""
declare msgofflineaccessid=""
declare publicclient=""
# Initialize parameters specified from command line
while getopts ":k:n:spau" arg; do
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
		a)
			adduserclaim="yes"
			;;
		u)
			publicclient="yes"
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
if [[ -z "$spname" ]]; then
	echo "Enter a name for this smart client [fhirproxy-smart-client]: "
	read spname
fi
if [ -z "$spname" ]; then
	spname="fhirproxy-smart-client"
fi	
echo "Enter the application reply-urls (space seperated) ["$postmanreply"]:"
read replyurls
if [ -z "$replyurls" ]; then
	replyurls=$postmanreply
fi

echo "Enter SMART Scopes required for this application (scopes seperated by spaces) ["$scopesdef"]:"
read scopes
if [ -z "$scopes" ]; then
	scopes=$scopesdef
fi
#convert SMART scopes to AD Compatible and place in array
scopes=${scopes//\//.}
IFS=" " read -a scopearr <<< $scopes
IFS=$'\n\t'
#Check KV exists
echo "Checking for keyvault "$kvname"..."
kvexists=$(az keyvault list --query "[?name == '$kvname'].name" --out tsv)
if [[ -z "$kvexists" ]]; then
	echo "Cannot Locate Key Vault "$kvname" this deployment requires access to the proxy keyvault...Is the Proxy Installed?"
	exit 1
fi

set +e
#Start deployment
echo "Creating SMART Client Application "$spname"..."
(
		echo "Loading configuration settings from key vault "$kvname"..."
		fphost=$(az keyvault secret show --vault-name $kvname --name FP-HOST --query "value" --out tsv)
		fpclientid=$(az keyvault secret show --vault-name $kvname --name FP-RBAC-CLIENT-ID --query "value" --out tsv)
		if [ -z "$fpclientid" ] || [ -z "$fphost" ]; then
			echo $kvname" does not appear to contain fhir proxy settings...Is the Proxy Installed?"
			exit 1
		fi
		echo "Registering FHIR SMART Client for AAD Auth..."
		stepresult=$(az ad sp create-for-rbac -n $spname --skip-assignment)
		spappid=$(echo $stepresult | jq -r '.appId')
		sptenant=$(echo $stepresult | jq -r '.tenant')
		spsecret=$(echo $stepresult | jq -r '.password')
		echo "Setting owner to signed in user..."
		owner=$(az ad signed-in-user show --query objectId --output tsv)
		stepresult=$(az ad app owner add --id $spappid --owner-object-id $owner)
		if [ -n "$publicclient" ]; then
			echo "Enabling public client access..."
			stepresult=$(az ad app update  --id $spappid  --set publicClient=true)
		fi
		echo "Setting reply-urls ["$replyurls"]..."
		stepresult=$(az ad app update --id $spappid --reply-urls $replyurls)
		#Delegate openid, profile and offline_access permissions from MS Graph
		echo "Loading MS Graph OAuth2 openid permissions..."
		msggraphid=$(az ad sp list --query "[?appDisplayName=='Microsoft Graph'].appId | [0]" --all --out tsv)	
		msgopenid=$(az ad sp show --id $msggraphid --query "oauth2Permissions[?value=='openid'].id | [0]" --out tsv)
		msgprofileid=$(az ad sp show --id $msggraphid --query "oauth2Permissions[?value=='profile'].id | [0]" --out tsv)
		msgofflineaccessid=$(az ad sp show --id $msggraphid --query "oauth2Permissions[?value=='offline_access'].id | [0]" --out tsv)
		echo "Delegating required MS Graph OAuth2 openid permissions to SMART app..."
		stepresult=$(az ad app permission add --id $spappid --api $msggraphid --api-permissions $msgopenid"=Scope" 2> /dev/null)
		stepresult=$(az ad app permission add --id $spappid --api $msggraphid --api-permissions $msgprofileid"=Scope" 2> /dev/null)
		stepresult=$(az ad app permission add --id $spappid --api $msggraphid --api-permissions $msgofflineaccessid"=Scope" 2> /dev/null)
		#Load SMART Scopes
		smartscopes=$(<smart-oauth2-permissions.json)
		#Iterate Scopes and add to permission string
		echo "Setting the following SMART permissions "$scopes"..."
		for var in "${scopearr[@]}"
		do
			stepresult=$(echo $smartscopes | jq -r ".[] | select(.value==\"$var\") | .id")
			if [ -n "$stepresult" ]; then
				stepresult=$(az ad app permission add --id $spappid --api $fpclientid --api-permissions $stepresult"=Scope" 2> /dev/null)
			else 
				echo "Scope "$var" was not found in allowed scopes for app"
			fi
		done
		stepresult=$(az ad app permission grant --id $spappid --api $fpclientid)
		if [ -n "$storekv" ]; then
			echo "Updating Keyvault with new SMART Client Settings..."
			stepresult=$(az keyvault secret set --vault-name $kvname --name "SMTC-${spname^^}-TENANT-NAME" --value $sptenant)
			stepresult=$(az keyvault secret set --vault-name $kvname --name "SMTC-${spname^^}-CLIENT-ID" --value $spappid)
			stepresult=$(az keyvault secret set --vault-name $kvname --name "SMTC-${spname^^}-SECRET" --value $spsecret)
			stepresult=$(az keyvault secret set --vault-name $kvname --name "SMTC-${spname^^}-RESOURCE" --value $fpclientid)
		fi
		if [ -n "$genpostman" ]; then
			echo "Generating Postman environment for SMART access..."
			pmuuid=$(cat /proc/sys/kernel/random/uuid)
            pmenv=$(<../samples/postmantemplateauth.json)
			pmscope=${scopes//./\/}
            pmfhirurl="https://"$fphost"/fhir"
            pmstsurl="https://"$fphost"/AadSmartOnFhirProxy"
            pmenv=${pmenv/~guid~/$pmuuid}
            pmenv=${pmenv/~envname~/$spname}
            pmenv=${pmenv/~tenentid~/$sptenant}
            pmenv=${pmenv/~stsurl~/$pmstsurl}
            pmenv=${pmenv/~clientid~/$spappid}
            pmenv=${pmenv/~clientsecret~/$spsecret}
            pmenv=${pmenv/~fhirurl~/$pmfhirurl}
            pmenv=${pmenv/~resource~/$fpclientid}
			pmenv=${pmenv/~scope~/$pmscope}
            echo $pmenv >> $spname".oauth2.postman_environment.json"
		fi
		if [ -n "$adduserclaim" ]; then
			echo "Looking for custom claim policy for FHIRUserClaim...(Note: This will fail if you are not a tenant administrator)"
			stepresult=$(az rest --method GET --uri 'https://graph.microsoft.com/beta/policies/claimsMappingPolicies')
			fhiruserclaimid=$(echo $stepresult | jq -r ".value[] | select(.displayName==\"$fhiruserclaimname\") | .id")
			if [ -n "$fhiruserclaimid" ]; then
				echo "Loading MS Graph ID for Application Id:"$spappid
				stepresult=$(az rest --method GET --uri "https://graph.microsoft.com/beta/servicePrincipals?filter=appId eq '${spappid}'")
				spobjectid=$(echo $stepresult | jq -r ".value[] | select(.appId==\"$spappid\") | .id")
				if [ -n "$spobjectid" ]; then
					pmenv=$(<./claimpolicytemplate.json)
					pmenv=${pmenv/~claimpolicyid~/$fhiruserclaimid}
					set -e
					(
						set -x
						az rest --method POST --uri https://graph.microsoft.com/beta/servicePrincipals/${spobjectid}/claimsMappingPolicies/\$ref --body $pmenv
					)
					echo "Updating Manifest to Accept Mapped Claims..."
					stepresult=$(az ad app update --id $spappid --set acceptMappedClaims=true)
				else
					echo "Cannot locate application id "$spappid" in Microsoft Graph."
				fi
			else
				echo "Cannot find a custom claim policy for name "$fhiruserclaimname" in tenant id "$sptenant"."
			fi
		fi
		echo " "
		echo "************************************************************************************************************"
		echo "Registered SMART Application "$spname" on "$(date)
		echo "This client can be used for SMART Application Launch and Context Access to FHIR Server via the SMART on FHIR Proxy"
		if [ -n "$storekv" ]; then
			echo "Your client credentials have been securely stored as secrets in keyvault "$kvname
			echo "The secret prefix is SMTC-${spname^^}"
			echo "Your FHIR Server ISS: https://"$fphost"/fhir"
		else
			echo "Please note the following reference information for use in authentication calls:"
			echo "Your Service Prinicipal Client/Application ID is: "$spappid
			echo "Your Service Prinicipal Client Secret is: "$spsecret
			echo "Your FHIR Server ISS: https://"$fphost"/fhir"
		fi
		echo " "
		if [ -n "$genpostman" ]; then
			echo "For your convenience a Postman environment "$spname".postman_environment.json has been generated"
			echo "It can imported along with the FHIR CALLS-Sample.postman_collection.json into postman to test your proxy access"
			echo "For Postman Importing help please reference the following URL:"
			echo "https://learning.postman.com/docs/getting-started/importing-and-exporting-data/#importing-postman-data"
		fi
		echo "You might need to access Azure portal and grant admin consent to "$spname" for some API Permissions"
		echo "For more information see https://docs.microsoft.com/en-us/azure/active-directory/manage-apps/grant-admin-consent#grant-admin-consent-in-app-registrations"
		echo "************************************************************************************************************"
		echo " "
		echo "Note: The display output and files created by this script can contain sensitive information please protect it!"
		echo " "
)
