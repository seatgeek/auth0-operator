# Kubernetes operator for Auth0 management

## About The Project

This Auth0 Kubernetes Operator is responsible for managing the lifecycle of Auth0 resources in a Kubernetes cluster.

It automates the deployment, configuration, and management of Auth0 resources, such as clients, connections, resource servers and more.

### Installation

`helm install -n auth0 auth0 oci://ghcr.io/seatgeek/auth0-operator`

## Usage

This operator is a cluster-wide operator. We would like to eventually support namespace-only (TODO).

Each available Auth0 resource type exposed by the management type is mapped nearly 1:1 to a Kubernetes document. Tenant is Tenant, Client is Client, etc. Resources each have a `spec.conf` entry which represents the contents of an Auth0 Management API update or create request to apply.

A secret is required to authenticate with Auth0's management API. This secret must contain the `clientId` and `clientSecret` fields.

At least a single `Tenant` resource is required. This `Tenant` resource must contain `spec.auth` with `domain` and `secretRef` to specify the authentication information.

Other resources, such as `Client`, `ResourceServer`, etc, must have a `spec.tenantRef` value refering to the owning tenant to manage. The name of the Kubernetes resource does not refer to the `name` field in the Auth0 Management API.

Since the entire API is derived from the Auth0 Management API their documentation is relevant: [Auth0 Management API](https://auth0.com/docs/api/management/v2).

## Development

### Generating CRDs and Kubernetes Resources

This project uses **KubeOps CLI** to automatically generate Custom Resource Definitions (CRDs) and other Kubernetes resources from C# entity models.

#### Prerequisites
```bash
# Restore the KubeOps CLI tool (ensure version matches project dependencies)
dotnet tool restore
```

#### Generate Resources
```bash
# Generate all operator resources (CRDs, RBAC, Deployment, etc.) to custom directory
dotnet kubeops generate operator auth0-operator src/Alethic.Auth0.Operator/Alethic.Auth0.Operator.csproj --out ./generated

# Generate directly to config directory (overwrites existing files)
dotnet kubeops generate operator auth0-operator src/Alethic.Auth0.Operator/Alethic.Auth0.Operator.csproj --out src/Alethic.Auth0.Operator/config --clear-out
```

#### When to Use KubeOps Generation
- **After modifying C# entity models** (adding/removing properties, changing attributes)
- **When updating KubernetesEntity attributes** (Kind names, API versions, etc.)
- **Before releasing** to ensure CRDs match code definitions
- **When onboarding new resources** to the operator

#### What to Avoid
- ❌ **Don't target the solution file** (`Alethic.Auth0.Operator.sln`) - causes assembly loading errors
- ❌ **Don't manually edit generated CRDs** - changes will be overwritten on next generation
- ❌ **Don't mix KubeOps CLI versions** - ensure CLI version matches NuGet package versions in project
- ❌ **Don't forget `--clear-out`** when regenerating to config directory

#### Generated Output
KubeOps generates:
- **CRDs** with `a0*` plural names (e.g., `a0clients`, `a0connections`) 
- **RBAC** rules (ClusterRole, ClusterRoleBinding)
- **Deployment** configuration
- **Service Account** definitions
- **Kustomization** file for deployment

#### Troubleshooting
If generation fails:
1. Ensure project builds successfully: `dotnet build Alethic.Auth0.Operator.sln`
2. Check KubeOps CLI version matches project dependencies
3. Use project file targeting, not solution file
4. Verify all entity models have proper `[KubernetesEntity]` attributes

## Supported Resources

- [x] kubernetes.auth0.com/v1:A0Tenant `a0tenant`
- [x] kubernetes.auth0.com/v1:A0Client `a0app`  
- [x] kubernetes.auth0.com/v1:A0ClientGrant `a0cgr`
- [x] kubernetes.auth0.com/v1:A0ResourceServer `a0api`
- [x] kubernetes.auth0.com/v1:A0Connection `a0con`

## Examples

### Tenant

```yaml
apiVersion: kubernetes.auth0.com/v1
kind: A0Tenant
metadata:
  name: example-tenant
  namespace: example
spec:
  auth:
    domain: example-tenant.us.auth0.com
    secretRef:
      name: example-tenant
  conf:
    friendly_name: My Tenant
  name: example-tenant
```

### Client (App)

https://auth0.com/docs/get-started/applications

```yaml
apiVersion: kubernetes.auth0.com/v1
kind: A0Client
metadata:
  name: example-client
  namespace: example
spec:
  tenantRef:
    name: example-tenant
  secretRef:
    name: example-client-secret
  conf:
    name: example-client
    app_type: spa
    grant_types:
      - client_credentials
```

## Client Secret

The Client resource supports an optional `secretRef` field which can point to either an existing secret (not implemented) or the name of a secret to be created with the extraction of the `client_id` and `client_secret` values from the app.

## ResourceServer

https://auth0.com/docs/get-started/apis

```yaml
apiVersion: kubernetes.auth0.com/v1
kind: A0ResourceServer
metadata:
  name: example-api
  namespace: example
spec:
  tenantRef:
    name: example-tenant
  conf:
    identifier: https://example.com/
    name: Example API
    allow_offline_access: false
    skip_consent_for_verifiable_first_party_clients: true
    token_lifetime: 86400
    token_lifetime_for_web: 7200
    signing_alg: RS256
    token_dialect: access_token
```

### ClientGrant

Grants permission for a Client to access a ResourceServer.

```yaml
apiVersion: kubernetes.auth0.com/v1
kind: A0ClientGrant
metadata:
  name: example-app-api
  namespace: example
spec:
  tenantRef:
    name: example-tenant
  conf:
    clientRef:
      name: example-client
    audience:
      name: example-api
    scope: []
```
# Test workflow trigger
