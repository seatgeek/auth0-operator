# Kubernetes operator for Auth0 management

## About The Project

This Auth0 Kubernetes Operator is responsible for managing the lifecycle of Auth0 resources in a Kubernetes cluster.

It automates the deployment, configuration, and management of Auth0 resources, such as clients, connections, resource servers and more.

### Installation

`helm install -n auth0 auth0 oci://ghcr.io/seatgeek/auth0-operator`

## Usage

This operator is a cluster-wide operator. We would like to eventually support namespace-only (TODO).

Each available Auth0 resource type exposed by the management type is mapped nearly 1:1 to a Kubernetes document. Tenant is Tenant, Client is Client, etc. Resources each have a `spec.conf` entry which represents the contents of an Auth0 Management API update or create request to apply. Resources also each have a `spec.init` entry which represents the same schema as `spec.conf`, but is only used on initial resource creation. Additionally, some resources have a `spec.find` entry which determines how the operator locates an existing Auth0 entity.

A secret is required to authenticate with Auth0's management API. This secret must contain the `clientId` and `clientSecret` fields.

At least a single `Tenant` resource is required. This `Tenant` resource must contain `spec.auth` with `domain` and `secretRef` to specify the authentication information.

Other resources, such as `Client`, `ResourceServer`, etc, must have a `spec.tenantRef` value refering to the owning tenant to manage. The name of the Kubernetes resource does not refer to the `name` field in the Auth0 Management API.

Each resource has a `spec.policy` entry which is a list of the following possible values: `Create`, `Update`, `Delete`. These policies determine what permissions the Auth0 operator has: Can it create new entities? Can it update existing entities? Can it delete remote entities?

Since the entire API is derived from the Auth0 Management API their documentation is relevant: [Auth0 Management API](https://auth0.com/docs/api/management/v2).

## Supported Resources

- [x] kubernetes.auth0.com/v1:Tenant `a0tenant`
- [x] kubernetes.auth0.com/v1:Client `a0app`
- [x] kubernetes.auth0.com/v1:ClientGrant `a0cgr`
- [x] kubernetes.auth0.com/v1:ResourceServer `a0api`
- [x] kubernetes.auth0.com/v1:Connection `a0con`

## Examples

### Tenant

```
apiVersion: kubernetes.auth0.com/v1
kind: Tenant
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

```
apiVersion: kubernetes.auth0.com/v1
kind: Client
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

```
apiVersion: kubernetes.auth0.com/v1
kind: ResourceServer
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

```
apiVersion: kubernetes.auth0.com/v1
kind: ClientGrant
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
