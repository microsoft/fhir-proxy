// version 0.1a
//
@description('Azure Active Directory Tenant that is associated with the subscription where the FHIR Proxy is beng deployed')
param tenantId string = subscription().tenantId

@description('Azure Region where resource will be created, defaults to the region that was used when creating the resource group')
param resourceLocation string = resourceGroup().location

@description('Unique string that will be used to generate resource names, defaults to the uniqueString function')
param uniqueValue string = take(uniqueString(subscription().subscriptionId,resourceGroup().name), 8)

@description('Tags to be applied to resources that are deployed in this template')
param resourceTags object

var haTag = {
  'HealthArchitectures-Solutions': 'FHIR-Proxy'
}
var assignedTags = union(resourceTags,haTag)

// FHIR Server Configration Parameters
@description('FHIR Server URL, https://fhirserver.azurewebsites.net/')
param fhirServerUrl string
param fhirServerTenantName string
param fhirServerClientId string
@secure()
@description('FHIR Server Client Secret')
param fhirServerSecret string
param fhirServerResource string
@description('FHIR Proxy preprocessor function list')
param fhirProxyPreProcess string = 'FHIRProxy.preprocessors.TransformBundlePreProcess'
@description('FHIR Proxy postprocessor function list')
param fhirProxyPostProcess string = ''

@description('Minimum TLS vserion used by Storage Account')
param minimumTlsVersion string = 'TLS1_2'

@description('Specify the name that will be assigned to the FHIR Proxy service')
param fhirProxyName string = ''
var fpName = empty(fhirProxyName) ? 'fhirproxy${uniqueValue}' : fhirProxyName
// Azure Key Vault Parameters and Variables
@description('Key Vault name')
param keyVaultName string =''

@description('Specifies how long Azure will keep the Key Vault after is has been deleted. Must be in the range of 7-90 days.')
param softDeleteRetentionInDays int = 30

@description('Specifies the permissions to secrets in the vault. Valid values are: all, get, list, set, delete, backup, restore, recover, and purge.')
param secretsPermissions array = [
  'get'
]

@description('Specifies the permissions to keys in the vault. Valid values are: all, encrypt, decrypt, wrapKey, unwrapKey, sign, verify, get, list, create, update, import, delete, backup, restore, recover, and purge.')
param keysPermissions array = [
  'get'
]

@description('Specifies the object ID of a user, service principal or security group in the Azure Active Directory tenant for the vault. The object ID must be unique for the list of access policies. Get it by using Get-AzADUser or Get-AzADServicePrincipal cmdlets.')
param objectId string = ''

var enabledForDeployment = false
var enabledForDiskEncryption = false
var enabledForTemplateDeployment = false
var enableRbacAuthorization = false
var enabledForSoftDelete = true
@allowed([
  'standard'
  'premium'
])
@description('Specifies whether the key vault is a standard vault or a premium vault. The default setting is standard')
param kvSkuName string = 'standard'
var deployKeyVault = true
var kvName = empty(keyVaultName) ? 'keyvault${uniqueValue}' : keyVaultName
resource keyVault 'Microsoft.KeyVault/vaults@2019-09-01' = if (deployKeyVault) {
  dependsOn:[
    fhirProxyFunctionApp
  ]
  name: kvName
  location: resourceLocation
  tags: assignedTags
  properties: {
    tenantId: tenantId
    sku: {
      family: 'A'
      name: kvSkuName
    }
    accessPolicies: [
      {
        objectId: length(objectId) > 0 ? objectId : fhirProxyFunctionApp.identity.principalId
        tenantId: tenantId
        permissions: {
          keys: keysPermissions
          secrets: secretsPermissions
        }
      }
    ]
    enabledForDeployment: enabledForDeployment
    enabledForDiskEncryption: enabledForDiskEncryption
    enabledForTemplateDeployment: enabledForTemplateDeployment
    softDeleteRetentionInDays: softDeleteRetentionInDays
    enableSoftDelete: enabledForSoftDelete
    enableRbacAuthorization: enableRbacAuthorization
    networkAcls: {
      defaultAction: 'Allow'
      bypass: 'AzureServices'
    }
  }
}
// TO DO enable suport for using a KV that already exists. Create parameter either request ID or RG and KV name
// currently assume that the KV is created during the deployment
var keyVaultUri = deployKeyVault ? keyVault.properties.vaultUri : ''

// Storage Account parameters and vaiables
@description('Storage account name. Defaults to storageaccount<uniqueid>')
param storageAccountName string = ''

@allowed([
  'Standard_LRS'
  'Standard_GRS'
  'Standard_RAGRS'
  'Standard_ZRS'
  'Premium_LRS'
  'Premium_ZRS'
  'Standard_GZRS'
  'Standard_RAGZRS'
])
@description('Storage account SKU. Defaults to Standard_LRS')
param storageSku string = 'Standard_LRS'
@description('')
param storageKind string = 'StorageV2'
@description('')
param storageKeySource string = 'Microsoft.Storage'
var deployStorageAccount = true
var saName = empty(storageAccountName) ? 'storageaccount${uniqueValue}' : storageAccountName

resource storageAccount 'Microsoft.Storage/storageAccounts@2021-01-01' = if (deployStorageAccount) {
  name: saName
  location: resourceLocation
  tags: assignedTags
  kind: storageKind
  sku: {
    name: storageSku
    tier: 'Standard'
  }
  properties: {
    accessTier: 'Hot'
    supportsHttpsTrafficOnly: true
    allowBlobPublicAccess: false
    isHnsEnabled: false
    isNfsV3Enabled: false
    minimumTlsVersion: minimumTlsVersion
    encryption: {
      services: {
        file: {
          keyType: 'Account'
          enabled: true
        }
        blob: {
          keyType: 'Account'
          enabled: true
        }
        table: {
          keyType: 'Account'
          enabled: true
        }
        queue: {
          keyType: 'Account'
          enabled: true
        }
      }
      keySource: storageKeySource
      requireInfrastructureEncryption: false
    }
  }
}
// Blob Services for Storage Account
resource blobServices 'Microsoft.Storage/storageAccounts/blobServices@2019-06-01' = {
  name: '${storageAccount.name}/default'
  properties: {
    cors: {
      corsRules: []
    }
    deleteRetentionPolicy: {
      enabled: true
      days: 7
    }
  }
}

// redis cache parameters and variables
@description('Minimum TLS vserion used by Redis Cache')
param redisMinimumTlsVersion string = '1.2'
@description('Redis cache resource name')
param redisCacheName string = ''
var rcName = empty(redisCacheName) ? 'rediscache${uniqueValue}' : redisCacheName
var deployRedisCache = true
resource redisCache 'Microsoft.Cache/redis@2020-06-01' = if (deployRedisCache) {
  name: rcName
  location: resourceLocation
  tags: assignedTags
  properties: {
    minimumTlsVersion: redisMinimumTlsVersion
    sku: {
      family: 'C'
      name: 'Basic'
      capacity: 0
    }
  }
}
@description('Git repo that contains the FHIR Proxy software')
param fhirProxyRepoUrl string = 'https://github.com/microsoft/fhir-proxy'
@description('Git repo branch name')
param fhirProxyRepoBranch string = 'main'
@description('App Service Plan SKU (S1)')
param appServicePlanSku string = 'S1'
@description('FHIR Proxy App Service resource name')
param fhirProxyServiceName string = ''

var deployAppServicePlan = true
var aspName = empty(fhirProxyServiceName) ? 'appserviceplan${uniqueValue}' : '${fhirProxyServiceName}-asp'
resource appServicePlan 'Microsoft.Web/serverfarms@2020-09-01' = if (deployAppServicePlan) {
  name: aspName
  location: resourceLocation
  tags: assignedTags
  sku: {
    name: appServicePlanSku
  }
  kind: 'functionapp'
}

resource fhirProxyFunctionApp 'Microsoft.Web/sites@2020-12-01' = {
  name: fpName
  location: resourceLocation
  identity: {
    type: 'SystemAssigned'
  }
  kind: 'functionapp'
  properties: {
    enabled: true
    httpsOnly: true
    clientAffinityEnabled: false
    serverFarmId: appServicePlan.id
    siteConfig: {
      alwaysOn: true
      ftpsState:'FtpsOnly'
    }
  }
}

var roleAdmin = 'Administrator'
var roleReader = 'Reader'
var roleWriter = 'Writer'
var rolePatient = 'Patient'
var roleParticipant = 'Practitioner,RelatedPerson'
var roleGlobal = 'DataScientist'
resource fhirProxyAppSettings 'Microsoft.Web/sites/config@2020-12-01' = {
  name: 'appsettings'
  parent: fhirProxyFunctionApp
  properties: {
    'FUNCTIONS_EXTENSION_VERSION': '~3'
    'FUNCTIONS_WORKER_RUNTIME': 'dotnet'
    'APPINSIGHTS_INSTRUMENTATIONKEY':appInsights.properties.InstrumentationKey
    'AzureWebJobsStorage': 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};EndpointSuffix=${environment().suffixes.storage};AccountKey=${listKeys(storageAccount.id, storageAccount.apiVersion).keys[0].value}'
    'FP-ADMIN-ROLE': roleAdmin
    'FP-READER-ROLE': roleReader
    'FP-WRITER-ROLE': roleWriter
    'FP-GLOBAL-ACCESS-ROLES': roleGlobal
    'FP-PATIENT-ACCESS-ROLES': rolePatient
    'FP-PARTICIPANT-ACCESS-ROLES': roleParticipant
    'FP-HOST': '@Microsoft.KeyVault(SecretUri=${keyVaultUri}/secrets/FP-HOST/)'
    'FP-PRE-PROCESSOR-TYPES': empty(fhirProxyPreProcess) ? 'FHIRProxy.preprocessors.TransformBundlePreProcess' : fhirProxyPreProcess
    'FP-POST-PROCESSOR-TYPES': empty(fhirProxyPostProcess) ? '' : fhirProxyPostProcess
    'FP-RBAC-NAME':'@Microsoft.KeyVault(SecretUri=${keyVaultUri}/secrets/FP-RBAC-NAME/)'
    'FP-RBAC-TENANT-NAME':'@Microsoft.KeyVault(SecretUri=${keyVaultUri}/secrets/FP-RBAC-TENANT-NAME/)'
    'FP-RBAC-CLIENT-ID':'@Microsoft.KeyVault(SecretUri=${keyVaultUri}/secrets/FP-RBAC-CLIENT-ID/)'
    'FP-RBAC-CLIENT-SECRET':'@Microsoft.KeyVault(SecretUri=${keyVaultUri}/secrets/FP-RBAC-CLIENT-SECRET/)'
    'FP-REDISCONNECTION': '@Microsoft.KeyVault(SecretUri=${keyVaultUri}/secrets/FP-REDISCONNECTION/)'
    'FP-STORAGEACCT': '@Microsoft.KeyVault(SecretUri=${keyVaultUri}/secrets/FP-STORAGEACCT/)'
    'FS-URL': '@Microsoft.KeyVault(SecretUri=${keyVaultUri}/secrets/FS-URL/)'
    'FS-TENANT-NAME': '@Microsoft.KeyVault(SecretUri=${keyVaultUri}/secrets/FS-TENANT-NAME/)'
    'FS-CLIENT-ID': '@Microsoft.KeyVault(SecretUri=${keyVaultUri}/secrets/FS-CLIENT-ID/)'
    'FS-SECRET': '@Microsoft.KeyVault(SecretUri=${keyVaultUri}/secrets/FS-SECRET/)'
    'FS-RESOURCE': '@Microsoft.KeyVault(SecretUri=${keyVaultUri}/secrets/FS-RESOURCE/)'
  }
}
// add code to support linking to an existing Log Analytics Workspace (id and or resource group/name)
var logAnalyticsWorkspaceName = 'laws-${uniqueValue}'
var deployLogAnalyticsWorkspace = true
resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2020-03-01-preview' = if (deployLogAnalyticsWorkspace) {
  name: logAnalyticsWorkspaceName
  location: resourceLocation
  tags: assignedTags
  properties: {
    retentionInDays: 30
    sku: {
      name: 'PerGB2018'
    }
  }
}
resource logAnalyticsWorkspaceDiagnostics 'Microsoft.Insights/diagnosticSettings@2017-05-01-preview' = if (deployLogAnalyticsWorkspace) {
  scope: logAnalyticsWorkspace
  name: 'diagnosticSettings'
  properties: {
    workspaceId: logAnalyticsWorkspace.id
    logs: [
      {
        category: 'Audit'
        enabled: true
        retentionPolicy: {
          days: 7
          enabled: true
        }
      }
    ]
    metrics: [
      {
        category: 'AllMetrics'
        enabled: true
        retentionPolicy: {
          days: 7
          enabled: true
        }
      }
    ]
  }
}

var appInsightName = 'appinsights-${uniqueValue}'
var deployApplicationInsights = true
resource appInsights 'microsoft.insights/components@2020-02-02-preview' = if (deployLogAnalyticsWorkspace && deployApplicationInsights){
  name: appInsightName
  location: resourceLocation
  kind: 'web'
  tags: assignedTags
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalyticsWorkspace.id
  }
}
resource createKeyVaultSecretHost 'Microsoft.KeyVault/vaults/secrets@2019-09-01' = {
  name: '${keyVault.name}/FP-HOST'
  properties:{
    value: fhirProxyFunctionApp.properties.defaultHostName
  }
}
resource createKeyVaultSecretUri 'Microsoft.KeyVault/vaults/secrets@2019-09-01' = {
  name: '${keyVault.name}/FP-RBAC-NAME'
  properties:{
    value: 'https://${fhirProxyFunctionApp.properties.defaultHostName}'
  }
}
resource createKeyVaultStorageSecret 'Microsoft.KeyVault/vaults/secrets@2019-09-01' = {
  name: '${keyVault.name}/FP-STORAGEACCT'
  properties:{
    value: 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};EndpointSuffix=${environment().suffixes.storage};AccountKey=${listKeys(storageAccount.id, storageAccount.apiVersion).keys[0].value}'
  }
}
resource createKeyVaultRedisSecret 'Microsoft.KeyVault/vaults/secrets@2019-09-01' = {
  name: '${keyVault.name}/FP-REDISCONNECTION'
  properties:{
    value: '${redisCache.properties.hostName}:${redisCache.properties.sslPort},password=${listKeys(redisCache.id, redisCache.apiVersion).primaryKey},ssl=True,abortConnect=False' 
  }
}
resource fhirServerUrlSecret 'Microsoft.KeyVault/vaults/secrets@2019-09-01' = if(!empty(fhirServerUrl)) {
  name: '${keyVault.name}/FS-URL'
  properties:{
    value: fhirServerUrl
  }
}
resource fhirServerTenantNameSecret 'Microsoft.KeyVault/vaults/secrets@2019-09-01' = if(!empty(fhirServerTenantName)) {
  name: '${keyVault.name}/FS-TENANT-NAME'
  properties:{
    value: fhirServerTenantName
  }
}
resource fhirServerClientIdSecret 'Microsoft.KeyVault/vaults/secrets@2019-09-01' = if(!empty(fhirServerClientId)) {
  name: '${keyVault.name}/FS-CLIENT-ID'
  properties:{
    value: fhirServerClientId
  }
}
resource fhirServerSecretSecret 'Microsoft.KeyVault/vaults/secrets@2019-09-01' = if(!empty(fhirServerSecret)) {
  name: '${keyVault.name}/FS-SECRET'
  properties:{
    value: fhirServerSecret
  }
}
resource fhirServerResourceSecret 'Microsoft.KeyVault/vaults/secrets@2019-09-01' = if(!empty(fhirServerResource)) {
  name: '${keyVault.name}/FS-RESOURCE'
  properties:{
    value: fhirServerResource
  }
}

resource deployProxyUsingCD 'Microsoft.Web/sites/sourcecontrols@2020-12-01' = {
  dependsOn: [
    fhirProxyAppSettings
    redisCache
  ]
  name:'web'
  parent: fhirProxyFunctionApp
  properties: {
    repoUrl: fhirProxyRepoUrl
    branch: fhirProxyRepoBranch
    isManualIntegration: true
  }
}

output fhirProxyHostName string = fhirProxyFunctionApp.properties.defaultHostName
output fhirProxyMsiObjectID string = fhirProxyFunctionApp.identity.principalId
