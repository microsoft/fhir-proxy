##  Configuration
The FHIR Proxy is configured on installation to be paired to a FHIR Server via a service client. Default roles are added to the application and are configured for specific access in the configuration settings section of the function app.
Enablement of pre/post processing modules is accomplished via the ```configmodules.bash``` utility.</br>

Important: Most pre/post processing modules will require additional [configuration](https://docs.microsoft.com/en-us/azure/azure-functions/functions-how-to-use-azure-function-app-settings) after enablement in order to function. Please check the details of the module for instructions.</br>

Notice

    THIS SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
    IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
    FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
    AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
    LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
    OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
    SOFTWARE


## Enabling Pre/Post Processing Modules
By default, no pre/post processors are configured to run. You will need to enable and configure them following the steps below:

1. [Open Azure Cloud Shell](https://shell.azure.com) - you can also access this from [azure portal](https://portal.azure.com).
2. Select Bash Shell.
3. Clone this repo (if needed) ```git clone https://github.com/microsoft/fhir-proxy```.
4. Switch to the FHIR/FHIRproxy subdirectory of this repo ```cd scripts/```.
5. Run the ```configmodules.bash``` script and follow the prompts to launch the selector.
5. Select the option number of a module to enable it (select it again to disable).
6. To accept and configure selected processors press ENTER.


**Note:** The utility does not read current configuration. It will simply enable the modules you specify and update the function configuration. To disable all modules, press ENTER without selecting options. To escape menu selection and abort updates, press CTRL-C. 

## Date Sort Post-Processor
This post-processing module allows for a date-based sorting alternative on FHIR Servers that do not natively support `_sort`. The processor implements a top level `_sort=date` or `_sort=-date` (reverse chron) query parameter for supported resource queries up to a hard maximum of 5000.</br> 

The resources supported for top `level_sort=date` are: Observation, DiagnosticReport, Encounter, CarePlan, CareTeam, EpisodeOfCare, and Claim. Any other resources will be ignored and not sorted.</br> 

This processor is limited to process 5000 resource entries in a search-set bundle. For accurate results, it is imperative that you limit your query so as not to exceed the maximum number of resources. 

This processor also has the potential to cause server delays in responses. For large result sets, use with caution. 

<I>Hints: Specify a large `_count` parameter value to reduce calls to the server and select limiting parameters for resource queries.</I> 

A log warning will be issued for requests that exceed the 5000 resource sort limit, but no error response will be returned - just the truncated data set.</br> 

This process requires no additional configuration.  

## Publish Event Post-Processor
This processor will publish FHIR Server Create/Update and Delete events for affected resources to a configured Event Hub. These events can be subscribed to by any number of consumers in order to trigger orchestrated workflows (e.g. Clinical Decision Support, Audits, Alerts, etc.).</br>
In addition to the action date, the Event Hub message consists of the following information:
+ `action` - HTTP Verb used to modify the FHIR Server
+ `resourcetype` - The type of resource affected (e.g. Patient, Observation, etc.)
+ `id` - The resource logical id on the FHIR Server that was affected
+ `version` - The version number of the resource affected
+ `lastupdated` - The date/time the affected resource was updated

You can use the data in the Event Hub message to make decisions and get information about affected resources to facilitate CDS or other workflows.

This process requires two configuration settings on the function app:
```
     FP-MOD-EVENTHUB-CONNECTION: <A valid EventHubs namespace connection string>
     FP-MOD-EVENTHUB-NAME: <A valid Event Hub in the specified EventHubs namespace connection>
```

## Transform Bundle Pre-Processor
This processing module will transform incoming transaction bundle requests into batch bundle requests and maintain UUID associations of contained resources. This is an alternative to updating FHIR Servers unable to handle transaction based requests.</br>
This processor will maintain internal logical id references when converted to batch mode; however, no transaction support will be included (e.g. Rollback for errors). It will be the client's responsibility to address any referential integrity or data issues that arise from server errors. Success or error status can be obtained using the batch-response bundle response.

This processor requires no additional configuration.

## FHIRCDSSyncAgent Post-Processor
This processing modules will filter resources based on the environment variable "SA-FHIRMAPPEDRESOURCES" and post the resource to the Sync Agent queue. Since the queue is partition based, this module will use the patient ID as the partition key for patient related resources which enables Sync Agent to process resources in a FIrst In, FIRST Out (FIFO) order. For all non-patient related resources, the module will default to using the resource type as the partition key. Users can use the SA-UNIQUEPARTITIONKEYFORNONPATIENTRESOURCE to generate a unique parititon key for all non patient related resources. The processing module also has a bulk mode which if enabled will send all the messages to an unordered queue. 

| Environment Variable | Notes |
| -- | -- |
| FP-SA-BULKLOAD | Boolean (True/False). Indicating whether to send all resources to unordered queue for faster processing. Resources will lose FIFO order. |
| SERVICEBUSNAMESPACEFHIRUPDATES | The name space of the Service Bus Queue. |
| SA-SERVICEBUSQUEUENAMEFHIRBULK | If bulk mode is enabled, the messages will be posted to this unordered queue. |
| SA-SERVICEBUSQUEUENAMEFHIRUPDATES | If bulk mode is not enabled, the messaged will be posted to this ordered queue. |
| SA-UNIQUEPARTITIONKEYFORNONPATIENTRESOURCE | Boolean (True/False). When using ordered queue, set this to true to generate unique partition keys for each of the non-patient resources like Practitioner. This will allow sync agent to process more resources parallely |
| SA-FHIRMAPPEDRESOURCES | A list of comma-separated FHIR resources to send to the Sync Agent Queue. Any resource not in the list will be filtered out |

## Participant Filter Post-Processor
This processing module will filter resources linked to a user registered in a Patient Participant Role such that only records referencing that user's Patient resource are returned. Note: this only filters patient-based linked resources. You can use this module as a basis for building your own security filtering (e.g., filtering records for a user in a Practitioner Participant Role linked to a Practitioner resource, etc.).</br>

## How the Participant Post-Processor works
![F H I R Proxy Seq](images/architecture/FHIRProxy_Seq.png)

## Configuring Participant Authorization Roles for Users
At a minimum, users must be placed in one or more FHIR Participant roles in order to appropriately filter results from the FHIR Server. The predefined Access roles are Patient, Practitioner, and RelatedPerson. 
1. [Login to Azure Portal](https://portal.azure.com) - _Note: If you have multiple tenants, make sure you switch to the directory that contains the Secure FHIR Proxy._
2. [Access the Azure Active Directory Enterprise Application Blade](https://ms.portal.azure.com/#blade/Microsoft_AAD_IAM/StartboardApplicationsMenuBlade/AllApps/menuId/).
3. Change the Application Type Drop Down to All Applications and click the Apply button.
4. Enter the application id from above in the search box to locate the Secure FHIR Proxy application.
5. Click on the Secure FHIR Proxy application in the list.
6. Click on Users and Groups from the left hand navigation menu.
7. Click on the +Add User button.
8. Click on the Select Role assignment box.
9. Select the access role you want to assign to specific users.
   The following are the predefined FHIR Access roles:
   + Patient - This user is a patient and is linked to a Patient resource in the FHIR Server.
   + Practitioner - This user is a practitioner and is linked to a Practitioner resource in the FHIR Server.
   + RelatedPerson - This user is a relative/caregiver accompanying a patient and is linked to a RelatedPerson resource in the FHIR Server.
    
   When the role is selected, click the Select button at the bottom of the panel.
10. Select the Users assignment box.
11. Select and/or Search and Select registered users/guests that you want to assign the selected role to.
12. When all required users have been selected, click the select button at the bottom of the panel.
13. Click the Assign button.
14. Congratulations! The selected users have been assigned their participant roles and can now be linked to FHIR Resources.
[]()
## Linking Users in Participant Roles to FHIR Resources
1. Make sure you have configured Participant Authorization Roles for users (see instructions above).
2. Obtain the FHIR Resource ID you wish to link to an AAD User Principal. Note you can use any search methods for the resources described in the FHIR specification. It is strongly recommended to use a known Business Identifier in your query to ensure a specific and correct match.
   For example:
   To find a specific Patient in FHIR with an MRN of 1234567 you could issue the following URL in your browser:
   
   ```https://<your fhir proxy url>/fhir/Patient?identifier=1234567```
   
   To find a specific Practitioner with last name Smith, in this case you can use other fields to validate like address, identifiers, etc. 
   
   ```https://<your fhir proxy address>/fhir/Practitioner?name=smith```
    
   The Resource ID is located in the `id` field of the returned resource or resource member in a search bundle:
   
   ```"id": "3bdaac8f-5c8e-499d-b906-aab31633337d"``` 
 
   _Note: You will need to login as a user in a FHIR Reader and/or FHIR Administrative role to view._
 
 3. You will need to obtain the Participant user's AAD Object ID. Make sure the Role they are in corresponds to the FHIR Resource you are linking. 
   
 4. Now you can link the FHIR Resource ID to the AAD user principal Object ID by entering the following URL in your browser:</br>
 
    ```https://<your fhir proxy url>/manage/link/<FHIR ResourceName>/<FHIR ResourceID>/<AD Object ID>``` 

    For example, to connect Dr. Mickey in your AAD tenant whose Object ID is `9293929-8281-dj89-a999-ppoiiwjwj` to the FHIR Practitioner Resource ID `3bdaac8f-5c8e-499d-b906-aab31633337d`, you would enter the following URL:
    
    ```https://<your fhir proxy url>/manage/link/Practitioner/3bdaac8f-5c8e-499d-b906-aab31633337d/9293929-8281-dj89-a999-ppoiiwjwj```
     
    _Note: You will need to login as a user in a FHIR Administrative role to perform the assignment._

5.  You're done. The user principal is now in a Participant Role connected to a Resource ID.

## Consent Opt-Out Filter

This processing module adds the ability to deny access to FHIR Server resources for patients who have elected to OPTOUT everyone or specific individuals and/or organizations from access to their medical data.

This module operates on the access policy that the health information of patients is accessible automatically to authorized users, but the patient can opt out completely.

It will honor any opt-out consent record(s) effective period, denying access to everyone or specific Organizations, Practitioners, RelatedPersons, and Patients (Actors).

This module will only filter if the appropriate OPT-OUT Consent resource is stored in the FHIR Server and is in force.

__For Example:__  
The following Consent resource will not allow any individuals affiliated with the specified organization (`66fa407d-d890-43a5-a6e3-eb82d3bfa393`) access to any resources on the FHIR Server that are related to Patient (`9ec3be2f-342c-4cb6-b2dd-c124747ef1bb`) for the period 4/20/2020-12/31/2020:
```
{
    "resourceType": "Consent",
    "id": "7d044901-068e-470d-ac98-8f3889144476",
    "meta": {
        "versionId": "2",
        "lastUpdated": "2020-07-24T16:02:42.802+00:00"
    },
    "text": {
        "status": "generated",
        "div": "<div xmlns=\"http://www.w3.org/1999/xhtml\"><p>Patient wishes to withhold disclosure of all data from a timeframe to any provider.\n</p>\n</div>"
    },
    "status": "active",
    "scope": {
        "coding": [
            {
                "system": "http://terminology.hl7.org/CodeSystem/consentscope",
                "code": "patient-privacy"
            }
        ]
    },
    "category": [
        {
            "coding": [
                {
                    "system": "http://loinc.org",
                    "code": "59284-0"
                }
            ]
        }
    ],
    "patient": {
        "reference": "Patient/9ec3be2f-342c-4cb6-b2dd-c124747ef1bb"
    },
    "dateTime": "2020-05-18",
    "organization": [
        {
            "reference": "Organization/66fa407d-d890-43a5-a6e3-eb82d3bfa393"
        }
    ],
    "sourceAttachment": {
        "title": "Withhold records for time frame"
    },
    "policyRule": {
        "coding": [
            {
                "system": "http://terminology.hl7.org/CodeSystem/consentpolicycodes",
                "code": "hipaa-restrictions"
            }
        ]
    },
    "provision": {
        "type": "deny",
        "period": {
            "start": "2020-04-20T00:00:00.0000+05:00",
            "end": "2020-12-31T23:59:00.0000+05:00"
        },
        "actor": [
            {
                "role": {
                    "coding": [
                        {
                            "system": "http://terminology.hl7.org/CodeSystem/v3-ParticipationType",
                            "code": "CST"
                        }
                    ]
                },
                "reference": {
                    "reference": "Organization/66fa407d-d890-43a5-a6e3-eb82d3bfa393"
                }
            }
        ]
    }
}
```
Notes: 
+ If no Period is specified, the Consent provision will be deemed in force. If no start date is specified, the default will be the earliest supported date/time. If no end date is specified, the default will be the latest supported date/time.
+ If no Actors are specified in the Consent Provision, all individuals will be denied access.
+ If the user is not linked to a FHIR resource and specific actors are specified in the opt-out consent record, the filter will be unable to determine exclusion and the user will be allowed access by default policy.
+ Organization is determined by the linked association with an Organization resource.
+ If multiple consent records are present, the most restrictive policy will be used and actor filters will be aggregated.
+ This filter only covers access. Updates are permitted to protect recorded data.
+ This filter does not allow exceptions on specific resources. All resources related to the patient are filtered.
 
This process requires configuration settings on the function app:
```
    FP-MOD-CONSENT-OPTOUT-CATEGORY:<A valid CodeableConcept search string to load access consent records>
```

The recommended value for category in your consent records is LOINC code 59284-0 Consent Document - the parameter value would be:
```http://loinc.org|59284-0```.

It is also required for users to be linked to FHIR Participant roles/resources. Please see the [Linking Users in Participant Roles to FHIR Resources](https://github.com/microsoft/fhir-proxy/blob/main/docs/configuration.md#linking-users-in-participant-roles-to-fhir-resources) section in the Participant Access Filter Module above.

## Everything Patient Pre-Processor
This pre-processing module implements a limited `$everything` at the Patient level. It returns the Patient and up to 5000 related resources for the Patient. Paging or other query parameters are not currently supported.

<I>Notes:</br> This module is provided as a building block example. If used in production, the returned resource limitation of 5000 should be noted to end users.</br> This module should be executed after all requests modifying pre-processors since it will call the FHIR Server and stop execution of other pre-processors.</I>

---

### How to file issues and get help  

This project uses GitHub Issues to track bugs and feature requests. Please search the existing 
issues before filing new issues to avoid duplicates. For new issues, file your bug or 
feature request as a new Issue.

For help and questions about using this project, please open an [issue](https://github.com/microsoft/fhir-proxy/issues) against the Github repository. We actively triage these and will work on this as best effort.

### Microsoft Support Policy  

Support for this **PROJECT or PRODUCT** is limited to the resources listed above.
