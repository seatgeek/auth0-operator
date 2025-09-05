package v1

import (
	metav1 "k8s.io/apimachinery/pkg/apis/meta/v1"
	"k8s.io/apimachinery/pkg/runtime"
)

// A0ClientGrant is the Schema for the a0clientgrants API
// +kubebuilder:object:root=true
// +kubebuilder:subresource:status
// +kubebuilder:resource:shortName=a0cgr
// +genclient
type A0ClientGrant struct {
	metav1.TypeMeta   `json:",inline"`
	metav1.ObjectMeta `json:"metadata,omitempty"`

	Spec   A0ClientGrantSpec   `json:"spec,omitempty"`
	Status A0ClientGrantStatus `json:"status,omitempty"`
}

// A0ClientGrantList contains a list of A0ClientGrant
// +kubebuilder:object:root=true
type A0ClientGrantList struct {
	metav1.TypeMeta `json:",inline"`
	metav1.ListMeta `json:"metadata,omitempty"`
	Items           []A0ClientGrant `json:"items"`
}

// A0ClientGrantSpec defines the desired state of A0ClientGrant
type A0ClientGrantSpec struct {
	// Policy defines the allowed operations for this client grant
	// +kubebuilder:validation:Optional
	Policy []V1EntityPolicyType `json:"policy,omitempty"`

	// TenantRef is a reference to the A0Tenant this client grant belongs to
	// +kubebuilder:validation:Required
	TenantRef *V1TenantReference `json:"tenantRef"`

	// Init specifies the initial configuration when creating a new client grant
	// +kubebuilder:validation:Optional
	Init *ClientGrantConf `json:"init,omitempty"`

	// Conf specifies the desired configuration for the client grant
	// +kubebuilder:validation:Required
	Conf *ClientGrantConf `json:"conf"`
}

// A0ClientGrantStatus defines the observed state of A0ClientGrant
type A0ClientGrantStatus struct {
	// Id is the Auth0 client grant ID
	// +kubebuilder:validation:Optional
	Id *string `json:"id,omitempty"`

	// LastConf contains the last applied configuration
	// +kubebuilder:validation:Optional
	// +kubebuilder:pruning:PreserveUnknownFields
	LastConf *runtime.RawExtension `json:"lastConf,omitempty"`
}

// ClientGrantConf defines the configuration for an Auth0 client grant
type ClientGrantConf struct {
	// ClientRef is a reference to the A0Client
	// +kubebuilder:validation:Optional
	ClientRef *V1ClientReference `json:"clientRef,omitempty"`

	// Audience is a reference to the A0ResourceServer (audience)
	// +kubebuilder:validation:Optional
	Audience *V1ResourceServerReference `json:"audience,omitempty"`

	// Scope lists the scopes granted to the client
	// +kubebuilder:validation:Optional
	Scope []string `json:"scope,omitempty"`
}

// V1ClientReference represents a reference to an A0Client resource
type V1ClientReference struct {
	// Namespace is the namespace of the referenced client.
	// If empty, the same namespace as the referencing resource is assumed.
	// +optional
	Namespace *string `json:"namespace,omitempty"`

	// Name is the name of the referenced client
	// +optional
	Name *string `json:"name,omitempty"`

	// Id is the Auth0 ID of the client
	// +optional
	Id *string `json:"id,omitempty"`
}
