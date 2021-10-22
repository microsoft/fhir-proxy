# Deploying your own FHIR Proxy 


## Install Documentation

### Prerequisites
- Deploy only in a tenant that you control Applicatoin registrations, Enterprise Applications, Permissions and Role Definition Assignments
- [Azure Subscription](https://azure.microsoft.com/en-us/free/)
- [Azure API for FHIR](https://docs.microsoft.com/en-us/azure/healthcare-apis/fhir-paas-portal-quickstart) 

### Install Instructions
Follow the instructions in the [Scripts Readme file](../scripts/Readme.md) file 

## Testing
The new endpoint for your FHIR Server should now be: ```https://<secure proxy url from above>/fhir```. You can use any supported FHIR HTTP verb and any FHIR compliant request/query
For example to see conformance statement for the FHIR Server, use your browser and access the proxy endpoint:</br>
```https://<secure proxy url from above>/fhir/metadata```

Proxy endpoints will authenticate/authorize your access to the FHIR server will execute configured pre-processing routines, pass the modified request on to the FHIR Server via the configured service client, execute configured post-processing routines on the result and rewrite the server response to the client. 
The original user principal name and tenant are passed in custom headers to the FHIR server for accurate security and compliance auditing.  


## Adding Users/Groups to the FHIR Server Proxy
At a minimum users must be placed in one or more FHIR server roles in order to access the FHIR Server via the Proxy. The Access roles are Administrator, Resource Reader and Resource Writer 
1. [Login to Azure Portal](https://portal.azure.com) _Note: If you have multiple tenants make sure you switch to the directory that contains the Secure FHIR Proxy_
2. [Access the Azure Active Directory Enterprise Application Blade](https://ms.portal.azure.com/#blade/Microsoft_AAD_IAM/StartboardApplicationsMenuBlade/AllApps/menuId/)
3. Change the Application Type Drop Down to All Applications and click the Apply button
4. Enter the application id or application name from above in the search box to locate the Secure FHIR Proxy application
5. Click on the Secure FHIR Proxy application name in the list
6. Click on Users and Groups from the left hand navigation menu
7. Click on the +Add User button
8. Click on the Select Role assignment box
9. Select the access role you want to assign to specific users
   The following are the predefined FHIR Access roles:
   + Administrator - Full Privileges to Read/Write/Link resource to the FHIR Server
   + Resource Reader - Allowed to Read Resources from the FHIR Server
   + Resource Writer - Allowed to Create, Update, Delete Resources on the FHIR Server
  
    When the role is selected click the select button at the bottom of the panel

10. Select the Users assignment box
11. Select and/or Search and Select registered users/guests that you want to assign the selected role too.
12. When all users desired have been selected click the select button at the bottom of the panel.
13. Click the Assign button.
14. Congratulations the select users have been assigned the access role and can now perform allowed operations against the FHIR Server

## Adding Application Service Principals to the FHIR Server Proxy
You can create service client principals and register for Application API Access to the proxy. This is useful for using the proxy in machine driven service workflows where a human cannot sign-in. </br>
The FHIR Server Roles assignable to applications by default are: Resource Reader and Resource writer. You may add/change application assignable roles in the FHIR Proxy application manifest.
 
1. [Login to Azure Portal](https://portal.azure.com) _Note: If you have multiple tenants make sure you switch to the directory that contains the Secure FHIR Proxy_
2. [Register a new Application (Service Principal) with Azure Active Directory](https://docs.microsoft.com/en-us/azure/active-directory/develop/quickstart-register-app)
3. Create a new client secret note what it is and keep it secure.
4. Click on the API Permissions left on the left hand navigation menu
5. Click on the under Configured Permissions, Click + Add a Permission
6. On the Request API Permissions tab Click on the APIs my organization uses button
7. In the search box enter the name of your FHIR Proxy (e.g. myproxy.azurewebsites.net)
8. Choose your proxy registration from the list
9. Click on the Application Permissions Box
10. Select the Roles you want this principal to be assigned in the Proxy (Reader, Writer or Both)
11. Click the Add Permissions box at the bottom to commit
12. On the Configured permissions area you will need Administrator rights to Grant Administrator consent to the roles you assigned
13. Once granted the service principal will now have access to the proxy in the roles you assigned.
14. You can verify this by looking at the Enterprise Application blade for the proxy under user and group assignments you will see the service principal

Note: You can authenticate using client_credentials flow to your new application using it's application id and secret, the resource or audience should be the application id of the FHIR proxy.  Pass the obtained token in the Authorization header of your calls to the FHIR proxy.


---

### How to file issues and get help  

This project uses GitHub Issues to track bugs and feature requests. Please search the existing 
issues before filing new issues to avoid duplicates.  For new issues, file your bug or 
feature request as a new Issue.

For help and questions about using this project, please open an [issue](https://github.com/microsoft/fhir-proxy/issues) against the Github repository. We actively triage these and will work on this as best effort.

### Microsoft Support Policy  

Support for this **PROJECT or PRODUCT** is limited to the resources listed above.