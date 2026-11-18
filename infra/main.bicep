// CapitecAssessment - minimal Azure Container Apps infrastructure (resource-group scope)
// Only a Container Apps environment and a Container App. Secrets are supplied by
// GitHub Actions as secure parameters and stored as Container App secrets.
targetScope = 'resourceGroup'

@allowed(['Development', 'Production'])
param environmentType string = 'Production'

param location string = resourceGroup().location

@description('Base name used for all resources.')
param appName string = 'capitecassessment'

@description('Full image reference, e.g. myacr.azurecr.io/capitecassessment:<sha>')
param containerImage string
param acrServer string
param acrUsername string
@secure()
param acrPassword string

@secure()
param databaseConnectionString string
@secure()
param otpHashKey string
@secure()
param emailConnectionString string
param emailSenderAddress string

var tags = {
  app: appName
  environment: environmentType
}

// No Log Analytics workspace: avoids an extra billable resource.
resource env 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${appName}-env'
  location: location
  tags: tags
  properties: {}
}

resource app 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${appName}-api'
  location: location
  tags: tags
  properties: {
    managedEnvironmentId: env.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
      }
      registries: [
        {
          server: acrServer
          username: acrUsername
          passwordSecretRef: 'acr-password'
        }
      ]
      secrets: [
        { name: 'acr-password', value: acrPassword }
        { name: 'database-connection-string', value: databaseConnectionString }
        { name: 'otp-hashkey', value: otpHashKey }
        { name: 'email-connection-string', value: emailConnectionString }
      ]
    }
    template: {
      containers: [
        {
          name: 'api'
          image: containerImage
          // Smallest Consumption-plan size to minimise cost
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: [
            { name: 'ASPNETCORE_HTTP_PORTS', value: '8080' }
            { name: 'ConnectionStrings__DefaultConnection', secretRef: 'database-connection-string' }
            { name: 'Otp__HashKey', secretRef: 'otp-hashkey' }
            { name: 'AzureCommunicationServices__Email__ConnectionString', secretRef: 'email-connection-string' }
            { name: 'AzureCommunicationServices__Email__SenderAddress', value: emailSenderAddress }
          ]
          // Probes hit the app-only health endpoint so they never wake the database
          probes: [
            {
              type: 'Startup'
              httpGet: { path: '/api/v1/health', port: 8080 }
              initialDelaySeconds: 3
              periodSeconds: 5
              failureThreshold: 12
            }
            {
              type: 'Liveness'
              httpGet: { path: '/api/v1/health', port: 8080 }
              periodSeconds: 30
              failureThreshold: 3
            }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 1
        rules: [
          {
            name: 'http-scaler'
            http: { metadata: { concurrentRequests: '50' } }
          }
        ]
      }
    }
  }
}

output containerAppUrl string = 'https://${app.properties.configuration.ingress.fqdn}'
