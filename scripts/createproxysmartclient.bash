#!/bin/bash
set -euo pipefail
IFS=$'\n\t'

# -e: immediately exit if any command has a non-zero exit status
# -o: prevents errors in a pipeline from being masked
# IFS new value is less likely to cause confusing bugs when looping arrays or arguments (e.g. $@)
#
# Setup SMART Application Client for use with FHIR Proxy --- Author Steve Ordahl Principal Architect Health Data Platform
#

usage() { echo "Usage: $0 -k <keyvault> -n <smart client name> -a (If you are a tenant admin, add FHIRUserClaim to smartapp id token, must have FHIRUserClaim custom claim policy defined for the tenant) -p (to generate postman environment) -u (For Public Client Registration)" 1>&2; exit 1; }
function intro {
	# Display the intro - give the user a chance to cancel 
	#
	echo " "
	echo "Create fhir-proxy SMART Client script... "
	echo " - Prerequisite:  Azure API for FHIR or AHDS FHIR Server must be installed"
	echo " - Prerequisite:  FHIR-Proxy must be installed"
	echo " - Prerequisite:  KeyVault containing FHIR and FHIR-Proxy settings must be available"
	echo " - Prerequisite:  Follow the Configure fhirUser Custom Claim Policy"
    echo "		  section of the Adding fhirUser as Custom Claim to AAD OAuth"
	echo "		  Tokens document (./docs/addingfhiridcustomclaim.md)" 
	echo " - Prerequisite:  Azure CLI (bash) access from the Azure Portal"
	echo " - Prerequisite:  You must have rights to register applications, assign permissions and configure claims policy in your AAD tenant"
	echo " "
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
declare genpostman=""
declare pmenv=""
declare pmuuid=""
declare pmfhirurl=""
declare pmproxyurl=""
declare scopesdef="openid fhirUser launch/patient patient/Patient.read"
declare scopes=""
declare scopeid=""
declare smartscopes=""
declare public=""
declare scopearr
declare permissions=""
declare owner=""
declare replyurls
declare replyurlstemp=""
declare replyarr
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
declare pgsplit
declare pglen=0
declare pgjson=""
declare pguuid=""
declare pgcondesc=""
declare pgextrascopes="launch launch.patient fhirUser"
declare pgset=""
declare pgbody=""
declare pgassign=""
declare pgassignarr
declare pgnewscopes=""
# Initialize parameters specified from command line
while getopts ":k:n:spau" arg; do
	case "${arg}" in
		k)
			kvname=${OPTARG}
			;;
		n)
			spname=${OPTARG}
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

defsubscriptionId=$(az account show --query "id" --out tsv) 
# Call the intro function 
# 
intro
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
read replyurlstemp
if [ -z "$replyurlstemp" ]; then
	replyurlstemp=$postmanreply
fi
IFS=" " read -a replyarr <<< $replyurlstemp
IFS=$'\n\t'
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
		if [ -z "$fphost" ]; then
			echo $kvname" does not appear to contain fhir proxy settings...Is the Proxy Installed?"
			exit 1
		fi
		fpclientid=$(az keyvault secret show --vault-name $kvname --name FP-SP-ID --query "value" --out tsv)
		echo "Registering FHIR SMART Client for AAD Auth..."
		stepresult=$(az ad sp create-for-rbac -n $spname --only-show-errors)
		spappid=$(echo $stepresult | jq -r '.appId')
		sptenant=$(echo $stepresult | jq -r '.tenant')
		spsecret=$(echo $stepresult | jq -r '.password')
		echo "Setting app owner to signed-in user..."
		owner=$(az ad signed-in-user show --query id --output tsv --only-show-errors)
		stepresult=$(az ad app owner add --id $spappid --owner-object-id $owner --only-show-errors)
		if [ -n "$publicclient" ]; then
			echo "Enabling public client access..."
			stepresult=$(az ad app update  --id $spappid  --set publicClient=true --only-show-errors)
		fi
		echo "Configuring reply URLs..."
		for var in "${replyarr[@]}"
		do
			stepresult=$(az ad app update --id $spappid --web-redirect-uris "${var}" --only-show-errors)
		done
		#Delegate openid, profile and offline_access permissions from MS Graph
		echo "Loading MS Graph OAuth2 openid permissions..."
		msggraphid="00000003-0000-0000-c000-000000000000"
		msgopenid=$(az ad sp show --id $msggraphid --query "oauth2PermissionScopes[?value=='openid'].id | [0]" --out tsv --only-show-errors)
		msgprofileid=$(az ad sp show --id $msggraphid --query "oauth2PermissionScopes[?value=='profile'].id | [0]" --out tsv --only-show-errors)
		msgofflineaccessid=$(az ad sp show --id $msggraphid --query "oauth2PermissionScopes[?value=='offline_access'].id | [0]" --out tsv --only-show-errors)
		echo "Delegating required MS Graph OAuth2 openid permissions to SMART app..."
		stepresult=$(az ad app permission add --id $spappid --api $msggraphid --api-permissions $msgopenid"=Scope" 2> /dev/null)
		stepresult=$(az ad app permission add --id $spappid --api $msggraphid --api-permissions $msgprofileid"=Scope" 2> /dev/null)
		stepresult=$(az ad app permission add --id $spappid --api $msggraphid --api-permissions $msgofflineaccessid"=Scope" 2> /dev/null)
		echo "Loading MS Graph ID for fhir-proxy Application Id:"$fpclientid
		stepresult=$(az rest --method GET --uri "https://graph.microsoft.com/v1.0/applications?\$filter=appId eq '${fpclientid}'")
		spobjectid=$(echo $stepresult | jq -r ".value[] | select(.appId==\"$fpclientid\") | .id")
		#Iterate Scopes and add to permission string
		echo "Processing the following SMART permissions "$scopes"..."
		#load Existing scopes
		pgset=$(az ad sp show --id $fpclientid --query oauth2PermissionScopes --only-show-errors)
		pgassign=""
		#Add Scopes that do not exist
		for var in "${scopearr[@]}"
		do
			scopeid=""
			#See if scope exists
			scopeid=$(echo $pgset | jq -r ".[] | select(.value==\"$var\") | .id")
			if [[ -z "$scopeid" ]]; then
				pgjson=""
				IFS="." read -a pgsplit <<< $var
				IFS=$'\n\t'
				pglen=${#pgsplit[@]}
				if [[ $pglen -eq 3 ]]; then
					pguuid=$(cat /proc/sys/kernel/random/uuid)
					pgcondesc=${pgsplit[2]}
					pgcondesc=${pgcondesc//read/Read}
					pgcondesc=${pgcondesc//write/Write}
					pgcondesc=${pgcondesc//*/Read\/Write}
					pgcondesc=$pgcondesc" "${pgsplit[1]}" as a "${pgsplit[0]}
					pgjson="{\"id\":\""$pguuid"\",\"isEnabled\": true,\"type\":\"User\",\"adminConsentDescription\": \""$pgcondesc"\",\"adminConsentDisplayName\": \""$pgcondesc"\",\"userConsentDescription\": \""$pgcondesc"\",\"userConsentDisplayName\": \""$pgcondesc"\",\"value\":\""$var"\"}"
					scopeid=$pguuid
					pgnewscopes="yes"
				else
					if [[ "$pgextrascopes" == *"$var"* ]]; then
						pguuid=$(cat /proc/sys/kernel/random/uuid)
						pgcondesc="Access or perform "$var
						pgjson="{\"id\":\""$pguuid"\",\"isEnabled\": true,\"type\":\"User\",\"adminConsentDescription\": \""$pgcondesc"\",\"adminConsentDisplayName\": \""$pgcondesc"\",\"userConsentDescription\": \""$pgcondesc"\",\"userConsentDisplayName\": \""$pgcondesc"\",\"value\":\""$var"\"}"
						scopeid=$pguuid
						pgnewscopes="yes"
					fi
				fi
				if [ -n "$pgjson" ]; then
					pgset=$(echo $pgset | jq -c --argjson bashpgjson "$pgjson" '. += [$bashpgjson]')
				fi
			fi
			if [[ -n "$scopeid" ]]; then
				if [[ -n "$pgassign" ]]; then
					pgassign=$pgassign" "$scopeid"=Scope"
				else
					pgassign=$scopeid"=Scope"
				fi
			fi
		done
		if [[ -n "$pgnewscopes" ]]; then
			echo "Setting new scopes on fhir-proxy service principal..."
			pgset="{\"api\":{\"oauth2PermissionScopes\":"$pgset
			pgset=$pgset"}}"
			stepresult=$(az rest --method PATCH --uri https://graph.microsoft.com/v1.0/applications/$spobjectid --body $pgset)
		fi
		echo "Assigning Permissions to new SMART Client..."
		#Iterate Scope Assignments and add to permission string
		IFS=" " read -a pgassignarr	<<< $pgassign
		IFS=$'\n\t'
		for var in "${pgassignarr[@]}"
		do
			stepresult=$(az ad app permission add --id $spappid --api $fpclientid --api-permissions $var 2> /dev/null)
		done
		if [ -n "$genpostman" ]; then
			echo "Generating Postman environment for access..."
			set +e
			rm $spname".postman_environment.json" 2>/dev/null
			set -e
			pmuuid=$(cat /proc/sys/kernel/random/uuid)
			pmenv=$(<./postmantemplate.json)
			pmscope=${scopes/patient./patient\/}
			pmscope=${pmscope/launch./launch\/}
			pmfhirurl="https://"$fphost"/fhir"
			pmproxyurl="https://"$fphost
			pmenv=${pmenv/~guid~/$pmuuid}
			pmenv=${pmenv/~envname~/$spname}
			pmenv=${pmenv/~tenentid~/$sptenant}
			pmenv=${pmenv/~proxyurl~/$pmproxyurl}
			pmenv=${pmenv/~clientid~/$spappid}
			pmenv=${pmenv/~clientsecret~/$spsecret}
			pmenv=${pmenv/~fhirurl~/$pmfhirurl}
			pmenv=${pmenv/~scope~/$pmscope}
			pmenv=${pmenv/~callbackurl~/$postmanreply}
			echo $pmenv >> $spname".postman_environment.json"
		fi
		if [ -n "$adduserclaim" ]; then
			echo "Looking for custom claim policy for FHIRUserClaim...(Note: This will fail if you are not a tenant administrator)"
			stepresult=$(az rest --method GET --uri 'https://graph.microsoft.com/beta/policies/claimsMappingPolicies')
			fhiruserclaimid=$(echo $stepresult | jq -r ".value[] | select(.displayName==\"$fhiruserclaimname\") | .id")
			if [ -n "$fhiruserclaimid" ]; then
				echo "Loading MS Graph Principal ID for Application Id:"$spappid
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
					stepresult=$(az ad app update --id $spappid --set api='{"acceptMappedClaims":true}' --only-show-errors)
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
		echo "This client can be used for SMART Application Launch and Context Access to FHIR Server via the FHIR Proxy"
		echo "Please note the following reference information for use in authentication calls:"
		echo "Your Service Prinicipal Client/Application ID is: "$spappid
		if [ -n "$publicclient" ]; then
			echo "This is a public client registration"
		else 
			echo "Your Service Prinicipal Client Secret is: "$spsecret
		fi
		echo "Your FHIR Server ISS: https://"$fphost"/fhir"
		echo " "
		if [ -n "$genpostman" ]; then
			echo "For your convenience a Postman environment "$spname".postman_environment.json has been generated"
			echo "It can imported along with the fhir-proxy-smart-client-sample.postman_collection.json into postman to test your proxy access"
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
