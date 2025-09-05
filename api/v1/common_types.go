package v1

// V1EntityPolicyType defines the policy types for Auth0 entities
// +kubebuilder:validation:Enum=Create;Update;Delete
type V1EntityPolicyType string

const (
	// PolicyTypeCreate allows the operator to create the associated entity in the tenant
	PolicyTypeCreate V1EntityPolicyType = "Create"
	// PolicyTypeUpdate allows the operator to update the associated entity in the tenant
	PolicyTypeUpdate V1EntityPolicyType = "Update"
	// PolicyTypeDelete allows the operator to delete the associated entity in the tenant
	PolicyTypeDelete V1EntityPolicyType = "Delete"
)

// V1TenantReference represents a reference to an A0Tenant resource
type V1TenantReference struct {
	// Namespace is the namespace of the referenced tenant.
	// If empty, the same namespace as the referencing resource is assumed.
	// +optional
	Namespace *string `json:"namespace,omitempty"`

	// Name is the name of the referenced tenant
	// +kubebuilder:validation:Required
	Name string `json:"name"`
}

// V1SecretReference represents a reference to a Kubernetes Secret
type V1SecretReference struct {
	// Name is the name of the secret
	// +kubebuilder:validation:Required
	Name string `json:"name"`

	// Namespace is the namespace of the secret
	// +kubebuilder:validation:Required
	Namespace string `json:"namespace"`
}

// V1ConnectionReference represents a reference to an A0Connection resource
type V1ConnectionReference struct {
	// Namespace is the namespace of the referenced connection.
	// If empty, the same namespace as the referencing resource is assumed.
	// +optional
	Namespace *string `json:"namespace,omitempty"`

	// Name is the name of the referenced connection
	// +optional
	Name *string `json:"name,omitempty"`

	// Id is the Auth0 ID of the connection
	// +optional
	Id *string `json:"id,omitempty"`
}

// V1ResourceServerReference represents a reference to an A0ResourceServer resource
type V1ResourceServerReference struct {
	// Identifier is the identifier of the resource server
	// +optional
	Identifier *string `json:"identifier,omitempty"`

	// Scopes are the scopes associated with this resource server
	// +optional
	Scopes []string `json:"scopes,omitempty"`
}
