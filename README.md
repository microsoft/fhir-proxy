# FHIR Proxy

## Table of Contents
1. [**Overview**](#overview)
    - [What is FHIR Proxy?](#paragraph1)
    - [What is the purpose of FHIR Proxy?](#paragraph2)
        - [Fine-grained Role-Based Access Control (RBAC)](#paragraph3)
        - [SMART on FHIR App Scoping](#paragraph5)
        - [Triggerable Event Publishing](#paragraph7)
2. [**Architecture**](#architecture)
3. [**Installation**](#installation)
4. [**Configuration**](#configuration)
5. [**Contributing**](#contributing)

# Overview <a name="overview"></a>

## What is FHIR Proxy? <a name="paragraph1"></a>
FHIR Proxy is a gatekeeping server application that works in conjunction with [Azure API for FHIR](https://docs.microsoft.com/en-us/azure/healthcare-apis/fhir/overview)/[FHIR Server for Azure](https://github.com/microsoft/fhir-server) (FHIR Server). When FHIR Proxy is deployed alongside FHIR Server in the same Azure Tenant, the FHIR Proxy becomes the public endpoint for the FHIR Server – standing in front of the FHIR Server’s API and acting as a checkpoint in the exchange of FHIR data to/from the FHIR Server.

## What is the purpose of FHIR Proxy? <a name="paragraph2"></a>
FHIR Proxy gives FHIR Server administrators an extra set of controls for filtering, transforming, and/or redistributing information as it passes into and out of the FHIR Server. This enhanced control in the transfer of FHIR data is accomplished via FHIR Proxy’s services in three key areas:
+ Fine-grained Role-Based Access Control
+ SMART on FHIR App Scoping
+ Triggerable Event Publishing

### Fine-grained Role-Based Access Control (RBAC) <a name="paragraph3"></a>
With its built-in Azure Active Directory (AAD) Identities integration, **FHIR Server** by itself allows a User/Service Principal to perform FHIR **read**, **write**, **delete**, and/or **export** actions based on several pre-defined AAD roles. When a User/Service Principal is authenticated as one (or more) of these FHIR Server roles, the User/Service Principal has permission to perform the assigned FHIR data action(s) on *any* FHIR record contained in the FHIR Server’s datastore. In the absence of FHIR Proxy, this course-grained RBAC on the server side assumes that a more fine-grained set of FHIR data permissions is in place on the end-user/application side for establishing FHIR Participant roles such as **Patient**, **Practitioner**, **RelatedPerson**, and so on.

With the addition of **FHIR Proxy**, FHIR administrators can manage fine-grained RBAC on the server side. Like FHIR Server, **FHIR Proxy** relies on AAD to authenticate client requests, but FHIR Proxy allows for finer-grained definition of User/Service Principal roles, thus making it possible to set server-side FHIR access policies for any number of FHIR Participants. FHIR Proxy allows each Participant role to be arbitrarily defined with FHIR **read**, **write**, **delete**, and/or **export** permissions on a Resource (FHIR) level of control. This RBAC for specific Resources (FHIR) is built out through FHIR Proxy’s plugin module interface, allowing the insertion of FHIR data filters to parametrically mask FHIR records on their way into and out of the FHIR Server. The extensible data processing FHIR Proxy enables through plugin modules lets administrators apply custom logic to manage access to FHIR records in a wide range of end-user scenarios. 

### SMART on FHIR App Scoping <a name="paragraph5"></a>
In addition to enhancing FHIR Server’s RBAC, FHIR Proxy also offers support for SMART on FHIR app scoping, which is another method of providing controlled access to FHIR records. Unlike FHIR Proxy’s RBAC where permissions are based on individual role assignments in AAD, SMART on FHIR app scoping incorporates OAuth 2.0 SMART scopes, which are FHIR data actions that comprise the “scope” of FHIR activity allowed for a client app. FHIR Proxy presents SMART scopes to administrators as a list of operations they can pick and choose from in defining the scope of a client app’s permissions. The client app then requests FHIR Proxy for authorization to access FHIR data within a particular scope, and if the request matches the SMART scope(s) assigned to the app, the app is authorized to launch and engage with the FHIR data. The app in this case would be a SMART app, and the SMART scopes themselves are exposed as FHIR Proxy API endpoints that the app has authorization to call as a consumer. For a SMART app to access the FHIR Proxy SMART scopes API, the app must be registered in the AAD tenant, and requests for SMART scope authorization must be accompanied by a valid OAuth 2.0 JSON Web Token (JWT).

FHIR Proxy’s SMART scoping has many benefits for FHIR Server – the major one being that it brings FHIR Server into compliance with [HL7’s SMART App Launch Framework](http://hl7.org/fhir/smart-app-launch/scopes-and-launch-context/index.html). With SMART scoping, the permissions delegated to a SMART app can be federated to the app’s end-users (defined in [HL7’s SMART App Launch Implementation Guide](http://hl7.org/fhir/smart-app-launch/scopes-and-launch-context/index.html) as “patients” or “users”) without having to assign each end-user a role in the AAD tenant. Furthermore, once the SMART scopes have been defined and set up as API endpoints on FHIR Proxy, it is straightforward to create scope definitions for different SMART apps. This can greatly simplify the scoping of client permissions down to specific Resources (FHIR) – requiring no special configuration on the server side when grouping permissions for a custom “scope” (for example, for a client’s boutique SMART app). Moreover, the OAuth 2.0 authentication scheme gives a means of issuing client secrets to trusted SMART apps, thus hardening FHIR data security.

### Triggerable Event Publishing <a name="paragraph7"></a>
FHIR Proxy also extends FHIR Server functionality with triggers that can be set to listen for Create, Update, and Delete (CUD) actions performed on FHIR Data. Administrators can set these triggers in the FHIR Proxy to automatically publish to Azure Event Hub when a CUD action occurs in the FHIR Server. This functionality is important for enabling near-real-time Clinical Decision Support (CDS) via [HL7 CDS Hooks](https://cds-hooks.hl7.org/) across Care Teams. The recipient(s) could be anyone among the FHIR Participants permitted to subscribe to the alerts, including the Patient.

# Architecture <a name="architecture"></a>
![FHIR Proxy Architecture](fhirproxy_arch_V2.png)

# Installation <a name="installation"></a>
Please see the [installation guide](https://github.com/microsoft/fhir-proxy/INSTALL.md) for instructions on how to install FHIR Proxy.

# Configuration <a name="configuration"></a>
Please see the [configuration guide](https://github.com/microsoft/fhir-proxy/CONFIG.md) for instructions on how to configure FHIR Proxy.

# Contributing <a name="contributing"></a>
Please see our [contributing guide](https://github.com/microsoft/fhir-proxy/CONTRIBUTING.md) for information about contributing to FHIR Proxy.