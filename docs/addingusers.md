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

---

### How to file issues and get help  

This project uses GitHub Issues to track bugs and feature requests. Please search the existing 
issues before filing new issues to avoid duplicates.  For new issues, file your bug or 
feature request as a new Issue.

For help and questions about using this project, please open an [issue](https://github.com/microsoft/fhir-proxy/issues) against the Github repository. We actively triage these and will work on this as best effort.

### Microsoft Support Policy  

Support for this **PROJECT or PRODUCT** is limited to the resources listed above.