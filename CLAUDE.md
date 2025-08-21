# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a Kubernetes operator for Auth0 management written in C# (.NET 9.0). It automates the lifecycle of Auth0 resources (tenants, clients, connections, resource servers, etc.) through Kubernetes Custom Resource Definitions (CRDs).

## Architecture

The project is structured as a multi-layered solution:

- **Alethic.Auth0.Operator** - Main operator application containing controllers and finalizers
- **Alethic.Auth0.Operator.Core** - Core models and extensions for Auth0 API integration  
- **Alethic.Auth0.Operator.Tests** - MSTest-based unit tests

The operator uses the KubeOps framework for Kubernetes integration and Auth0's Management API for resource management.

## Key Components

- **Controllers** (`src/Alethic.Auth0.Operator/Controllers/`) - Handle reconciliation of each resource type
- **Finalizers** (`src/Alethic.Auth0.Operator/Finalizers/`) - Handle cleanup when resources are deleted
- **Models** (`src/Alethic.Auth0.Operator/Models/`) - Kubernetes resource definitions
- **Core Models** (`src/Alethic.Auth0.Operator.Core/Models/`) - Auth0 API model mappings

## Build and Development Commands

### Building the solution
```bash
dotnet build Alethic.Auth0.Operator.sln
```

### Running tests
```bash
dotnet test src/Alethic.Auth0.Operator.Tests/
```

### Building distribution artifacts
```bash
dotnet build Alethic.Auth0.Operator.dist.msbuildproj
```

### Generating CRDs and Operator Resources
The project uses KubeOps CLI to generate Custom Resource Definitions and other Kubernetes resources from C# entity models.

#### Prerequisites
```bash
# Restore the KubeOps CLI tool (version must match project dependencies)
dotnet tool restore
```

#### Generate all operator resources (CRDs, RBAC, Deployment, etc.)
```bash
# Generate to a custom output directory
dotnet kubeops generate operator auth0-operator src/Alethic.Auth0.Operator/Alethic.Auth0.Operator.csproj --out ./generated

# Generate directly to config directory (overwrites existing files)
dotnet kubeops generate operator auth0-operator src/Alethic.Auth0.Operator/Alethic.Auth0.Operator.csproj --out src/Alethic.Auth0.Operator/config --clear-out
```

**Important Notes:**
- Must target the **project file** (`Alethic.Auth0.Operator.csproj`), not the solution file
- KubeOps CLI version in `.config/dotnet-tools.json` must match the KubeOps NuGet package versions
- Generated CRDs will use `a0*` plural names (e.g., `a0clients`, `a0connections`) based on the `A0*` Kind names in the C# models
- Use `--clear-out` to remove existing files before generation

### Container operations
The project supports container builds through MSBuild targets. The main operator image is built as `auth0-operator-image`.

### Helm chart
Helm chart is available at `charts/auth0-operator/` and published to `oci://ghcr.io/seatgeek/auth0-operator`.

## Supported Auth0 Resources

- **A0Tenant** (`kubernetes.auth0.com/v1:A0Tenant`) - Auth0 tenant configuration
- **A0Client** (`kubernetes.auth0.com/v1:A0Client`) - Auth0 applications  
- **A0ClientGrant** (`kubernetes.auth0.com/v1:A0ClientGrant`) - Permissions between clients and resource servers
- **A0ResourceServer** (`kubernetes.auth0.com/v1:A0ResourceServer`) - APIs in Auth0
- **A0Connection** (`kubernetes.auth0.com/v1:A0Connection`) - Identity providers and databases

## Configuration Requirements

- Tenant resources require authentication via Kubernetes secrets containing `clientId` and `clientSecret`
- All other resources must reference a tenant via `spec.tenantRef`
- The operator requires cluster-wide permissions

## Entity Policy Types

Resources support policy configuration through `spec.policy` array:
- `Create` - Allow creation of new Auth0 resources
- `Update` - Allow updates to existing Auth0 resources (default behavior includes both Create and Update)

## Development Notes

- The solution uses .NET 9.0 with nullable reference types enabled
- JSON serialization handles both Newtonsoft.Json (Auth0 SDK) and System.Text.Json (Kubernetes)
- Memory caching is used for Auth0 Management API clients (1-minute expiration)
- Retry logic handles Auth0 API rate limits and transient failures
- Kubernetes events are generated for reconciliation status