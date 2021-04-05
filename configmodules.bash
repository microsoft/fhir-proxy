#!/bin/bash
set -euo pipefail
IFS=$'\n\t'

# -e: immediately exit if any command has a non-zero exit status
# -o: prevents errors in a pipeline from being masked
# IFS new value is less likely to cause confusing bugs when looping arrays or arguments (e.g. $@)
#
# Enable/Disable Proxy Pre/Post Modules
#

usage() { echo "Usage: $0 -n <proxy name> -g <resourceGroupName> -i <subscription id>" 1>&2; exit 1; }


declare resourceGroupName=""
declare faname=""
declare preprocessors=""
declare postprocessors=""
declare defsubscriptionId=""
declare subscriptionId=""
declare stepresult=""
declare listofprocessors=""
declare enableprocessors=""
declare msg=""
declare num
options=("FHIRProxy.postprocessors.FHIRCDSSyncAgentPostProcess2" "FHIRProxy.postprocessors.DateSortPostProcessor" "FHIRProxy.postprocessors.ParticipantFilterPostProcess" "FHIRProxy.postprocessors.PublishFHIREventPostProcess" "FHIRProxy.postprocessors.ConsentOptOutFilter" "FHIRProxy.preprocessors.ProfileValidationPreProcess" "FHIRProxy.preprocessors.TransformBundlePreProcess" "FHIRProxy.preprocessors.EverythingPatientPreProcess")
choices=("" "" "" "" "" "" "" "")
# Initialize parameters specified from command line
while getopts ":g:n:i:
" arg; do
	case "${arg}" in
		n)
			faname=${OPTARG}
			;;
		g)
			resourceGroupName=${OPTARG}
			;;
		i)
			subscriptionId=${OPTARG}
			;;
	esac
done
shift $((OPTIND-1))
if [[ -z "$resourceGroupName" ]]; then
	echo "You musty provide a resource group name."
	usage
fi
if [[ -z "$faname" ]]; then
	echo "You musty provide the name of the proxy function app."
	usage
fi
echo "Executing "$0"..."
echo "Checking Azure Authentication..."
#login to azure using your credentials
az account show 1> /dev/null

if [ $? != 0 ];
then
	az login
fi
defsubscriptionId=$(az account show --query "id" --out json | sed 's/"//g') 
if [[ -z "$subscriptionId" ]]; then
	echo "Enter your subscription ID ["$defsubscriptionId"]:"
	read subscriptionId
	if [ -z "$subscriptionId" ] ; then
		subscriptionId=$defsubscriptionId
	fi
	[[ "${subscriptionId:?}" ]]
fi
echo "Setting subscription default..."
#set the default subscription id
az account set --subscription $subscriptionId

set +e

#Check for existing RG
if [ $(az group exists --name $resourceGroupName) = false ]; then
	echo "Resource group with name" $resourceGroupName "could not be found."
	usage
fi
menu() {
    echo "Please select the proxy modules you want to enable:"
    for i in ${!options[@]}; do 
        printf "%3d%s) %s\n" $((i+1)) "${choices[i]:- }" "${options[i]}"
    done
    if [[ "$msg" ]]; then echo "$msg"; fi
}
prompt="Select an option (again to uncheck, ENTER when done): "
while menu && read -rp "$prompt" num && [[ "$num" ]]; do
    [[ "$num" != *[![:digit:]]* ]] &&
    (( num > 0 && num <= ${#options[@]} )) ||
    { msg="Invalid option: $num"; continue; }
    ((num--)); msg="${options[num]} was ${choices[num]:+un}checked"
    [[ "${choices[num]}" ]] && choices[num]="" || choices[num]="+"
done
for i in ${!options[@]}; do 
    if [[ "${choices[i]}" ]]; then
		if [[ "${options[i]}" == *".preprocessors."* ]]; then
			preprocessors+="${options[i]}",
		fi
		if [[ "${options[i]}" == *".postprocessors."* ]]; then
			postprocessors+="${options[i]}",
		fi
	fi
done
preprocessors="${preprocessors%?}"
postprocessors="${postprocessors%?}"
echo "Configuring Secure FHIR Proxy App ["$faname"]..."
stepresult=$(az functionapp config appsettings set --name $faname --resource-group $resourceGroupName --settings FP-PRE-PROCESSOR-TYPES=$preprocessors FP-POST-PROCESSOR-TYPES=$postprocessors)
if [ $? != 0 ];
then
	echo "Problem updating appsettings..."
	exit 1;
fi
echo "Pre-Processors enabled:"$preprocessors
echo "Post-Processors enabled:"$postprocessors
echo "Remember to check required configuration settings for each module!"
