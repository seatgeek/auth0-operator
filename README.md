# Kubernetes operator for Auth0 management

## About The Project

This Auth0 Kubernetes Operator is responsible for managing the lifecycle of Auth0 resources in a Kubernetes cluster.

It automates the deployment, configuration, and management of Auth0 resources, such as clients, connections, resource servers and more.

### Installation

`helm install -n auth0 auth0 oci://ghcr.io/alethic/auth0-operator`

## Usage

This operator is a cluster-wide operator. We would like to eventually support namespace-only (TODO).

Each available Auth0 resource type exposed by the management type is mapped nearly 1:1 to a Kubernetes document. Tenant is Tenant, Client is Client, etc. Resources each have a `spec.conf` entry which represents the contents of an Auth0 Management API update or create request to apply.

A secret is required to authenticate with Auth0's management API. This secret must contain the `clientId` and `clientSecret` fields.

At least a single `Tenant` resource is required. This `Tenant` resource must contain `spec.auth` with `domain` and `secretRef` to specify the authentication information.

Other resources, such as `Client`, `ResourceServer`, etc, must have a `spec.tenantRef` value refering to the owning tenant to manage. The name of the Kubernetes resource does not refer to the `name` field in the Auth0 Management API.

Since the entire API is derived from the Auth0 Management API their documentation is relevant: [Auth0 Management API](https://auth0.com/docs/api/management/v2).

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

### Client

```
apiVersion: kubernetes.auth0.com/v1
kind: Client
metadata:
  name: example-client
  namespace: example
spec:
  conf:
    allowed_clients: []
    app_type: spa
    name: example-client
  tenantRef:
    name: example-tenant
```
