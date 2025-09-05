# Auth0 Operator Go API Package

This package provides Go types and utilities for managing Auth0 resources in Kubernetes clusters. It allows Go applications to programmatically create, read, update, and delete Auth0 Custom Resource Definitions (CRDs) that are managed by the Auth0 Operator.

> ✅ **Latest Release**: v1.0.1 is now available! The Go API is ready for use.

## Architecture Overview

This Go API package works alongside the existing C# Auth0 Operator and uses different toolchains for CRD generation:

### CRD Generation Toolchains

- **C# Operator** (`src/Alethic.Auth0.Operator/`):
  - **Framework**: KubeOps framework
  - **Generation**: `dotnet kubeops generate operator`
  - **Output**: `src/Alethic.Auth0.Operator/config/a0*.yaml`
  - **Purpose**: Production CRDs used by the running C# operator

- **Go API Package** (`api/v1/`):
  - **Framework**: controller-gen from kubebuilder
  - **Generation**: `make generate` (uses controller-gen)
  - **Output**: `config/crd/bases/kubernetes.auth0.com_*.yaml`
  - **Purpose**: Schema validation and Go client development

Both toolchains generate functionally identical CRDs from their respective type definitions, ensuring full compatibility between the C# operator and Go client applications.

## Supported Resources

- **A0Client** - Auth0 applications/clients
- **A0Connection** - Auth0 identity provider connections  
- **A0ClientGrant** - Permission grants between clients and resource servers
- **A0ResourceServer** - Auth0 APIs and resource servers
- **A0Tenant** - Auth0 tenant configurations

## Quick Start

### Installing the API Package

```bash
# Add to your Go project
go get github.com/seatgeek/auth0-operator@v1.0.1

# Add required Kubernetes dependencies  
go get k8s.io/client-go@v0.32.1
go get k8s.io/apimachinery@v0.32.1

# Note: If you encounter checksum verification issues, use:
# GOSUMDB=off go get github.com/seatgeek/auth0-operator@v1.0.1
```

### Basic Usage Example

```go
package main

import (
    "context"
    "fmt"
    
    auth0v1 "github.com/seatgeek/auth0-operator/api/v1"
    metav1 "k8s.io/apimachinery/pkg/apis/meta/v1"
    "k8s.io/client-go/dynamic"
    "k8s.io/client-go/tools/clientcmd"
    "k8s.io/apimachinery/pkg/runtime"
    "k8s.io/apimachinery/pkg/runtime/schema"
)

func main() {
    // Create Kubernetes config
    config, err := clientcmd.BuildConfigFromFlags("", "/path/to/kubeconfig")
    if err != nil {
        panic(err)
    }
    
    // Create dynamic client
    dynamicClient, err := dynamic.NewForConfig(config)
    if err != nil {
        panic(err)
    }
    
    // Define the A0Client GroupVersionResource
    gvr := schema.GroupVersionResource{
        Group:    "kubernetes.auth0.com",
        Version:  "v1", 
        Resource: "a0clients",
    }
    
    // Create A0Client resource
    client := &auth0v1.A0Client{
        ObjectMeta: metav1.ObjectMeta{
            Name:      "my-app",
            Namespace: "default",
        },
        Spec: auth0v1.A0ClientSpec{
            TenantRef: &auth0v1.V1TenantReference{
                Name: "my-tenant",
            },
            Conf: &auth0v1.ClientConf{
                Name:            stringPtr("My Application"),
                ApplicationType: stringPtr("spa"),
                Callbacks:       []string{"http://localhost:3000/callback"},
            },
        },
    }
    
    // Convert to unstructured for dynamic client
    unstructuredObj, err := runtime.DefaultUnstructuredConverter.ToUnstructured(client)
    if err != nil {
        panic(err)
    }
    
    // Create the resource in Kubernetes
    result, err := dynamicClient.Resource(gvr).Namespace("default").Create(
        context.TODO(), 
        &unstructured.Unstructured{Object: unstructuredObj}, 
        metav1.CreateOptions{},
    )
    if err != nil {
        panic(err)
    }
    
    fmt.Printf("Created A0Client: %s\n", result.GetName())
}

func stringPtr(s string) *string {
    return &s
}
```

## Publishing New Versions

### Pre-Release Checklist

Before publishing a new version, ensure all changes are complete and tested:

```bash
# 1. Ensure all changes are committed  
git status

# 2. Run code generation to update generated files
make generate

# 3. Run tests to ensure everything works
make test

# 4. Run linting
make lint

# 5. Update CHANGELOG.md with new version details
# Edit CHANGELOG.md to document your changes
```

### Version Release Process

Follow semantic versioning for the API package:

- **MAJOR** (`v1.0.0 → v2.0.0`): Breaking changes to Go API types or interfaces
- **MINOR** (`v1.0.0 → v1.1.0`): New features, new CRD types, backward compatible changes  
- **PATCH** (`v1.0.0 → v1.0.1`): Bug fixes, documentation updates, backward compatible

#### Manual Release Process

```bash
# 1. Determine next version (follow semantic versioning guidelines above)
VERSION="v1.2.3"

# 2. Create and push git tag
git tag ${VERSION}
git push origin ${VERSION}

# 3. Create GitHub release manually
gh release create ${VERSION} \
  --title "${VERSION}" \
  --notes-file CHANGELOG.md \
  --latest

# Alternative: Create release from web UI
# Visit: https://github.com/seatgeek/auth0-operator/releases/new
# - Tag: v1.2.3  
# - Title: v1.2.3
# - Description: Copy from CHANGELOG.md
```

#### Automated Release Process

```bash  
# Complete automated release (requires scripts/release.sh)
make release VERSION=v1.2.3

# Or step by step:
./scripts/release.sh v1.2.3
```

### Breaking Change Guidelines

When making breaking changes to the API:

1. **Increment major version** (e.g., `v1.x.x` → `v2.0.0`)
2. **Create migration guide** documenting the changes
3. **Update examples** to reflect new API usage
4. **Maintain previous major version** for a transition period

## Consuming the API Package

### Installation Commands

#### New Go Project Setup

```bash
# 1. Initialize your Go application
mkdir my-auth0-app && cd my-auth0-app
go mod init github.com/yourorg/my-auth0-app

# 2. Add the Auth0 Operator API dependency  
go get github.com/seatgeek/auth0-operator@v1.0.1

# 3. Add required Kubernetes dependencies
go get k8s.io/client-go@v0.32.1
go get k8s.io/apimachinery@v0.32.1
go get k8s.io/api@v0.32.1
```

#### Existing Project Integration

```bash
# Add to existing Go project
go get github.com/seatgeek/auth0-operator@v1.0.1
go mod tidy
```

### Import Statements

```go
import (
    // Core Auth0 API types
    auth0v1 "github.com/seatgeek/auth0-operator/api/v1"
    
    // Kubernetes core types
    metav1 "k8s.io/apimachinery/pkg/apis/meta/v1"
    "k8s.io/apimachinery/pkg/runtime"
    "k8s.io/apimachinery/pkg/runtime/schema"
    "k8s.io/apimachinery/pkg/apis/meta/v1/unstructured"
    
    // Kubernetes client libraries
    "k8s.io/client-go/dynamic"
    "k8s.io/client-go/tools/clientcmd"
)
```

### Creating Auth0 Resources

#### A0Tenant Example

```go
func createTenant() (*auth0v1.A0Tenant, error) {
    tenant := &auth0v1.A0Tenant{
        ObjectMeta: metav1.ObjectMeta{
            Name:      "my-auth0-tenant",
            Namespace: "default",
        },
        Spec: auth0v1.A0TenantSpec{
            Name: "my-tenant",
            Auth: &auth0v1.TenantAuth{
                Domain: stringPtr("my-tenant.auth0.com"),
                SecretRef: &auth0v1.V1SecretReference{
                    Name:      "auth0-credentials",
                    Namespace: "default",
                },
            },
            Conf: &auth0v1.TenantConf{
                FriendlyName: stringPtr("My Auth0 Tenant"),
                SupportEmail: stringPtr("support@mycompany.com"),
            },
        },
    }
    
    return tenant, nil
}
```

#### A0Client Example

```go
func createClient() (*auth0v1.A0Client, error) {
    client := &auth0v1.A0Client{
        ObjectMeta: metav1.ObjectMeta{
            Name:      "my-spa-app",
            Namespace: "default",
        },
        Spec: auth0v1.A0ClientSpec{
            TenantRef: &auth0v1.V1TenantReference{
                Name: "my-auth0-tenant",
            },
            Conf: &auth0v1.ClientConf{
                Name:            stringPtr("My SPA Application"),
                Description:     stringPtr("Single Page Application"),
                ApplicationType: stringPtr("spa"),
                Callbacks: []string{
                    "http://localhost:3000/callback",
                    "https://myapp.com/callback",
                },
                AllowedOrigins: []string{
                    "http://localhost:3000",
                    "https://myapp.com",
                },
                OidcConformant: boolPtr(true),
            },
        },
    }
    
    return client, nil
}
```

#### A0Connection Example

```go
func createConnection() (*auth0v1.A0Connection, error) {
    connection := &auth0v1.A0Connection{
        ObjectMeta: metav1.ObjectMeta{
            Name:      "my-database-connection",
            Namespace: "default",
        },
        Spec: auth0v1.A0ConnectionSpec{
            TenantRef: &auth0v1.V1TenantReference{
                Name: "my-auth0-tenant",
            },
            Conf: &auth0v1.ConnectionConf{
                Name:     stringPtr("My Database"),
                Strategy: stringPtr("auth0"),
                ShowAsButton: boolPtr(true),
            },
        },
    }
    
    return connection, nil
}
```

### CRD Generation from Go Types

You can generate CRD YAML files from the Go types for deployment:

```bash
# Option 1: Use this repository's tooling
git clone https://github.com/seatgeek/auth0-operator.git
cd auth0-operator
make manifests
# CRDs will be generated in config/crd/bases/

# Option 2: In your own project (requires controller-gen)
go install sigs.k8s.io/controller-tools/cmd/controller-gen@v0.17.2
controller-gen crd paths="github.com/seatgeek/auth0-operator/api/v1" output:dir="./crds"

# Option 3: Generate inline in your Go code
# See examples/crd-generation/ for programmatic CRD generation
```

### Applying CRDs to Kubernetes

```bash
# Apply the generated CRDs to your cluster
kubectl apply -f config/crd/bases/

# Or apply individual CRDs  
kubectl apply -f config/crd/bases/kubernetes.auth0.com_a0clients.yaml
kubectl apply -f config/crd/bases/kubernetes.auth0.com_a0connections.yaml
kubectl apply -f config/crd/bases/kubernetes.auth0.com_a0clientgrants.yaml
kubectl apply -f config/crd/bases/kubernetes.auth0.com_a0resourceservers.yaml
kubectl apply -f config/crd/bases/kubernetes.auth0.com_a0tenants.yaml

# Verify CRDs are installed
kubectl get crd | grep auth0
```

### Version Management

#### Pin to Specific Version

```bash
# Pin to exact version in go.mod  
go get github.com/seatgeek/auth0-operator@v1.0.1

# Or edit go.mod directly:
# require github.com/seatgeek/auth0-operator v1.0.1
```

#### Update to Latest Version

```bash  
# Update to latest version
go get -u github.com/seatgeek/auth0-operator

# Update to latest patch of current minor version
go get -u=patch github.com/seatgeek/auth0-operator

# Clean up dependencies
go mod tidy
```

#### Version Constraints in go.mod

```go
module github.com/yourorg/my-auth0-app

go 1.22

require (
    // Pin to specific version
    github.com/seatgeek/auth0-operator v1.2.3
    
    // Allow minor updates within v1
    // github.com/seatgeek/auth0-operator v1.2
    
    // Allow any v1 version (not recommended for production)
    // github.com/seatgeek/auth0-operator v1
)
```

## Advanced Usage Patterns

### Working with Dynamic Clients

Since the full typed client generation requires additional setup, you can use Kubernetes dynamic clients:

```go
import (
    "k8s.io/client-go/dynamic"
    "k8s.io/apimachinery/pkg/apis/meta/v1/unstructured"
    "k8s.io/apimachinery/pkg/runtime"
)

func createResourceWithDynamicClient(dynamicClient dynamic.Interface) error {
    // Create your Auth0 resource using the typed structs
    client := &auth0v1.A0Client{ /* ... */ }
    
    // Convert to unstructured
    unstructuredObj, err := runtime.DefaultUnstructuredConverter.ToUnstructured(client)
    if err != nil {
        return err
    }
    
    gvr := schema.GroupVersionResource{
        Group:    "kubernetes.auth0.com",
        Version:  "v1",
        Resource: "a0clients",  // Note: plural form
    }
    
    // Create resource
    _, err = dynamicClient.Resource(gvr).Namespace("default").Create(
        context.TODO(),
        &unstructured.Unstructured{Object: unstructuredObj},
        metav1.CreateOptions{},
    )
    
    return err
}
```

### Resource References

Use the provided reference types for linking resources:

```go
// Reference a tenant
tenantRef := &auth0v1.V1TenantReference{
    Name:      "my-tenant",
    Namespace: stringPtr("auth0-system"), // Optional, defaults to same namespace
}

// Reference a connection 
connectionRef := &auth0v1.V1ConnectionReference{
    Name: stringPtr("my-connection"),
    // Can also reference by Auth0 ID:
    // Id: stringPtr("con_abc123xyz"),
}

// Reference a secret
secretRef := &auth0v1.V1SecretReference{
    Name:      "auth0-credentials", 
    Namespace: "default",
}
```

### Generating CRD Manifests

Create a simple Go program to generate CRD manifests:

```go
// cmd/crd-gen/main.go
package main

import (
    "fmt"
    "os"
    
    auth0v1 "github.com/seatgeek/auth0-operator/api/v1"
    "sigs.k8s.io/yaml"
)

func main() {
    // Example A0Client
    client := &auth0v1.A0Client{
        TypeMeta: metav1.TypeMeta{
            APIVersion: "kubernetes.auth0.com/v1",
            Kind:       "A0Client",
        },
        ObjectMeta: metav1.ObjectMeta{
            Name:      "example-client",
            Namespace: "default",
        },
        Spec: auth0v1.A0ClientSpec{
            TenantRef: &auth0v1.V1TenantReference{Name: "my-tenant"},
            Conf: &auth0v1.ClientConf{
                Name: stringPtr("Example Application"),
                ApplicationType: stringPtr("spa"),
            },
        },
    }
    
    // Convert to YAML
    yamlData, err := yaml.Marshal(client)
    if err != nil {
        panic(err)
    }
    
    fmt.Print(string(yamlData))
}
```

Build and run:

```bash
go build -o crd-gen cmd/crd-gen/main.go
./crd-gen > example-client.yaml
kubectl apply -f example-client.yaml
```

## Development Workflow

### Regenerating Code and CRDs

When making changes to the API types:

```bash
# Regenerate DeepCopy methods and CRDs
make generate

# Verify generated code is up to date
make verify

# Run tests
make test

# Build project
make build
```

### Available Make Targets

```bash
make help           # Show all available targets
make generate       # Generate DeepCopy methods and CRDs
make manifests      # Generate CRD manifests only
make test          # Run all tests
make build         # Build the project  
make lint          # Run linter (requires golangci-lint)
make fmt           # Format Go code
make vet           # Run go vet
make tidy          # Run go mod tidy
make clean         # Clean generated files
make verify        # Verify generated code is current
make controller-gen # Download controller-gen tool
```

### Schema Validation

The Go API package includes automated schema validation to ensure compatibility with the existing C# operator:

```bash
# Compare generated CRDs with existing ones
make validate-schema

# This runs scripts/validate-schema.sh which compares:
# - Go-generated CRDs in config/crd/bases/ (from controller-gen)
# - C#-generated CRDs in src/Alethic.Auth0.Operator/config/ (from KubeOps)
```

**Why Two Sets of CRDs?**

The repository contains CRDs generated by both toolchains:

1. **C# CRDs** (`src/Alethic.Auth0.Operator/config/`): Production CRDs used by the running operator
2. **Go CRDs** (`config/crd/bases/`): Validation CRDs to verify Go type definitions are correct

The schema validation ensures that Go applications using this API package will generate CRDs that are 100% compatible with the existing C# operator, enabling seamless interoperability.

## Troubleshooting

### Common Import Issues

**Problem**: `go get` fails or module not found
```bash
# Solution: Ensure you're using the correct module path
go get github.com/seatgeek/auth0-operator@latest

# Check if specific version exists
go list -m -versions github.com/seatgeek/auth0-operator
```

**Problem**: Type compilation errors
```bash
# Solution: Ensure you have the right Kubernetes dependencies
go get k8s.io/client-go@v0.32.1
go get k8s.io/apimachinery@v0.32.1
go mod tidy
```

**Problem**: CRD schema mismatch with existing operator
```bash
# Solution: Check that your import version matches your cluster's operator version
kubectl get crd a0clients.kubernetes.auth0.com -o yaml | grep version:

# Update to matching version
go get github.com/seatgeek/auth0-operator@v1.x.x
```

### Kubernetes Client Setup

**Problem**: Authentication issues with Kubernetes cluster
```bash
# Ensure kubeconfig is properly configured
kubectl config current-context

# Test basic connectivity
kubectl get nodes

# For in-cluster usage, use:
config, err := rest.InClusterConfig()
```

**Problem**: RBAC permissions for CRDs
```bash
# Ensure your service account has proper permissions
kubectl auth can-i create a0clients --as=system:serviceaccount:default:my-service-account

# Apply RBAC if needed (example):
kubectl apply -f - <<EOF
apiVersion: rbac.authorization.k8s.io/v1
kind: ClusterRole
metadata:
  name: auth0-resources-reader
rules:
- apiGroups: ["kubernetes.auth0.com"]
  resources: ["a0clients", "a0connections", "a0clientgrants", "a0resourceservers", "a0tenants"]
  verbs: ["get", "list", "watch", "create", "update", "patch", "delete"]
EOF
```

## Examples and Patterns

### Integration in Operators

If you're building your own Kubernetes operator that manages Auth0 resources:

```go
// In your controller
import auth0v1 "github.com/seatgeek/auth0-operator/api/v1"

func (r *MyControllerReconciler) createAuth0Client(ctx context.Context, name string) error {
    client := &auth0v1.A0Client{
        ObjectMeta: metav1.ObjectMeta{
            Name:      name,
            Namespace: r.Namespace,
        },
        Spec: auth0v1.A0ClientSpec{
            TenantRef: &auth0v1.V1TenantReference{Name: r.TenantName},
            Conf: &auth0v1.ClientConf{
                Name:            &name,
                ApplicationType: stringPtr("regular_web"),
            },
        },
    }
    
    return r.Client.Create(ctx, client)
}
```

### CLI Tools

Building CLI tools that generate Auth0 resources:

```go
// cmd/auth0-cli/main.go
func generateClientManifest(name, namespace, tenantName string) {
    client := &auth0v1.A0Client{
        TypeMeta: metav1.TypeMeta{
            APIVersion: "kubernetes.auth0.com/v1", 
            Kind:       "A0Client",
        },
        ObjectMeta: metav1.ObjectMeta{Name: name, Namespace: namespace},
        Spec: auth0v1.A0ClientSpec{
            TenantRef: &auth0v1.V1TenantReference{Name: tenantName},
            Conf: &auth0v1.ClientConf{Name: &name},
        },
    }
    
    yamlData, _ := yaml.Marshal(client)
    fmt.Print(string(yamlData))
}
```

### Testing with the Types

```go
func TestClientCreation(t *testing.T) {
    client := &auth0v1.A0Client{
        ObjectMeta: metav1.ObjectMeta{Name: "test-client"},
        Spec: auth0v1.A0ClientSpec{
            TenantRef: &auth0v1.V1TenantReference{Name: "test-tenant"},
            Conf: &auth0v1.ClientConf{
                Name: stringPtr("Test Client"),
                ApplicationType: stringPtr("spa"),
            },
        },
    }
    
    // Validate required fields are set
    assert.NotNil(t, client.Spec.TenantRef)
    assert.NotNil(t, client.Spec.Conf)
    assert.Equal(t, "Test Client", *client.Spec.Conf.Name)
}
```

## API Reference

### Resource Types

| Type | Kind | Short Name | Description |
|------|------|------------|-------------|
| A0Client | A0Client | a0app | Auth0 applications and clients |
| A0Connection | A0Connection | a0con | Auth0 identity provider connections |
| A0ClientGrant | A0ClientGrant | a0cgr | Permission grants between clients and APIs |
| A0ResourceServer | A0ResourceServer | a0api | Auth0 APIs and resource servers |
| A0Tenant | A0Tenant | a0tenant | Auth0 tenant configurations |

### GroupVersionResource (GVR) Values

For use with dynamic clients:

```go
// A0Client
gvr := schema.GroupVersionResource{
    Group: "kubernetes.auth0.com", Version: "v1", Resource: "a0clients",
}

// A0Connection  
gvr := schema.GroupVersionResource{
    Group: "kubernetes.auth0.com", Version: "v1", Resource: "a0connections", 
}

// A0ClientGrant
gvr := schema.GroupVersionResource{
    Group: "kubernetes.auth0.com", Version: "v1", Resource: "a0clientgrants",
}

// A0ResourceServer
gvr := schema.GroupVersionResource{
    Group: "kubernetes.auth0.com", Version: "v1", Resource: "a0resourceservers",
}

// A0Tenant
gvr := schema.GroupVersionResource{
    Group: "kubernetes.auth0.com", Version: "v1", Resource: "a0tenants",
}
```

## Utility Functions

Common helper functions for working with pointer types:

```go
// Helper functions for pointer types
func stringPtr(s string) *string { return &s }
func boolPtr(b bool) *bool { return &b }  
func int32Ptr(i int32) *int32 { return &i }

// Helper for checking pointer values safely
func stringValue(s *string) string {
    if s == nil { return "" }
    return *s
}

func boolValue(b *bool) bool {
    if b == nil { return false }
    return *b  
}
```

## Contributing

To contribute changes to this API package:

1. **Fork the repository** and create a feature branch
2. **Make changes** to the Go types in `api/v1/`
3. **Run code generation**: `make generate`  
4. **Test your changes**: `make test`
5. **Validate schema compatibility**: `make validate-schema`
6. **Submit a pull request** with a clear description

### Adding New Fields

When adding new fields to existing types:

1. **Add the field** with appropriate kubebuilder markers
2. **Mark as optional**: Use `+kubebuilder:validation:Optional` for new fields
3. **Use pointer types**: New fields should be pointers with `omitempty`  
4. **Regenerate**: Run `make generate` to update CRDs and DeepCopy methods
5. **Test compatibility**: Ensure existing resources still work

### Adding New Resource Types

For new Auth0 resource types:

1. **Create new `*_types.go` file** in `api/v1/`
2. **Define the structs** following the same pattern as existing types
3. **Add to register.go**: Include in `addKnownTypes` function
4. **Regenerate**: Run `make generate` 
5. **Test**: Create example resources and verify they work with the operator

## License

This project is licensed under the Apache 2.0 License - see the [LICENSE](LICENSE) file for details.