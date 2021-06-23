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