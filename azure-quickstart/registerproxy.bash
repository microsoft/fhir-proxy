#!/usr/bin/env bash
set -euo pipefail

keyVaultName=""
fhirProxyAppName=""
tenantId=""
saveToKeyVault=""
isLoggedIn=""

function usage { 
    echo "Usage: $0 -k <keyVaultName> -p <fhirProxyAppName> -t <tenantId> -h -s" 1>&2;
    echo " -s   save to key vault requuires -k" 1>&2; 
    echo " -h   help" 1>&2; 
    exit 1; 
}

if [[ ${#} -eq 0 ]]; then
   usage
fi
while getopts ":k:p:t:hs" arg; do
	case "${arg}" in
		k)
			keyVaultName=${OPTARG}
			;;
		p)
			fhirProxyAppName=${OPTARG}
			;;
        t)
			tenantId=${OPTARG}
			;;
        h)
			usage
			;;
        s)
			saveToKeyVault="true"
			;;
		esac
done
# Scenario List
#   a. Command line argument and output to screen need FHIR Proxy App Hostname
#   b. just ran the FHIR Proxy deploy script - use Azure Key Vault
#       *** since the KV permissions are highly restricted adding permissions needs to happen
#       *** do it in this script?
#       1. Read FHIR Proxy host name from key vault
#       2. Create application registration
#   c. save output to keyvault
#
if [ $saveToKeyVault = "true" ] && [ -z $keyVaultName ]; then
    usage
fi

# authenticate user
az account show 1> /dev/null
if [ $? != 0 ]; then
	isLoggedIn="false"
fi
if [ isLoggedIn = "false" ] && [ -z tenantId ]; then
    echo "az login"
fi
if [ isLoggedIn = "false" ] && [ -n tenantId ]; then
    echo "az login --tenant $tenantId"
fi

tenantId=$(az account show | jq -r '.tenantId')

if [ $saveToKeyVault = "true" ] && [ -n $keyVaultName ]; then
    kvexists=$(az keyvault list --query "[?name == '$keyVaultName'].name" --out tsv)
    if [ -z $kvexists ]; then
        echo "Unable to locate Key Vault '"$keyVaultName"' aborting"
        exit 1;
    fi
    echo "Key Vault '"$keyVaultName"' found"
fi

set +e
if [ -n $fhirProxyAppName ]; then
    echo "Creating Service Principal for AAD Auth"
    stepresult=$(az ad sp create-for-rbac -n "https://"$fhirProxyAppName --skip-assignment)
    spappid=$(echo $stepresult | jq -r '.appId')
    sptenant=$(echo $stepresult | jq -r '.tenant')
    spsecret=$(echo $stepresult | jq -r '.password')
    spreplyurls="https://"$fhirProxyAppName"/.auth/login/aad/callback"
    tokeniss="https://sts.windows.net/"$sptenant
    echo "Warning the following output contains sensitive information that should be protected and saved accordingly"
    echo "   Application ID     = '"$spappid"'"
    echo "   Tenant ID          = '"$sptenant"'"
    echo "   Application Secret = '"$spsecret"'"
    echo "   Token Issuer URL   = '"$tokeniss"'"
    echo "   Raw Application Registration output: "$stepresult

    echo "Adding Sign-in User Read Permission on Graph API..."
    stepresult=$(az ad app permission add --id $spappid --api 00000002-0000-0000-c000-000000000000 --api-permissions 311a71cc-e848-46a1-bdf8-97ff7156d8e6=Scope)
    echo "Configuring reply urls for app..."
    stepresult=$(az ad app update --id $spappid --reply-urls $spreplyurls)
    echo "Adding FHIR Custom Roles to Manifest..."
    stepresult=$(az ad app update --id $spappid --app-roles @fhirroles.json)

fi
if [ $saveToKeyVault = "true" ] && [ -n $keyVaultName ]; then
    stepresult=$(az keyvault secret set --vault-name $keyVaultName --name "FP-RBAC-NAME" --value "https://"$fhirProxyAppName)
    stepresult=$(az keyvault secret set --vault-name $keyVaultName --name "FP-RBAC-TENANT-NAME" --value $sptenant)
    stepresult=$(az keyvault secret set --vault-name $keyVaultName --name "FP-RBAC-CLIENT-ID" --value $spappid)
    stepresult=$(az keyvault secret set --vault-name $keyVaultName --name "FP-RBAC-CLIENT-SECRET" --value $spsecret)
fi

echo "completed successfully"