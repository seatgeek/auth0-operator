package v1

import (
	metav1 "k8s.io/apimachinery/pkg/apis/meta/v1"
	"k8s.io/apimachinery/pkg/runtime"
)

// A0Connection is the Schema for the a0connections API
// +kubebuilder:object:root=true
// +kubebuilder:subresource:status
// +kubebuilder:resource:shortName=a0con
// +genclient
type A0Connection struct {
	metav1.TypeMeta   `json:",inline"`
	metav1.ObjectMeta `json:"metadata,omitempty"`

	Spec   A0ConnectionSpec   `json:"spec,omitempty"`
	Status A0ConnectionStatus `json:"status,omitempty"`
}

// A0ConnectionList contains a list of A0Connection
// +kubebuilder:object:root=true
type A0ConnectionList struct {
	metav1.TypeMeta `json:",inline"`
	metav1.ListMeta `json:"metadata,omitempty"`
	Items           []A0Connection `json:"items"`
}

// A0ConnectionSpec defines the desired state of A0Connection
type A0ConnectionSpec struct {
	// Policy defines the allowed operations for this connection
	// +kubebuilder:validation:Optional
	Policy []V1EntityPolicyType `json:"policy,omitempty"`

	// TenantRef is a reference to the A0Tenant this connection belongs to
	// +kubebuilder:validation:Required
	TenantRef *V1TenantReference `json:"tenantRef"`

	// Find specifies how to find an existing connection in Auth0
	// +kubebuilder:validation:Optional
	Find *ConnectionFind `json:"find,omitempty"`

	// Init specifies the initial configuration when creating a new connection
	// +kubebuilder:validation:Optional
	Init *ConnectionConf `json:"init,omitempty"`

	// Conf specifies the desired configuration for the connection
	// +kubebuilder:validation:Required
	Conf *ConnectionConf `json:"conf"`
}

// A0ConnectionStatus defines the observed state of A0Connection
type A0ConnectionStatus struct {
	// Id is the Auth0 connection ID
	// +kubebuilder:validation:Optional
	Id *string `json:"id,omitempty"`

	// LastConf contains the last applied configuration
	// +kubebuilder:validation:Optional
	// +kubebuilder:pruning:PreserveUnknownFields
	LastConf *runtime.RawExtension `json:"lastConf,omitempty"`
}

// ConnectionFind specifies how to find an existing connection in Auth0
type ConnectionFind struct {
	// ConnectionId is the Auth0 connection ID to search for
	// +kubebuilder:validation:Optional
	ConnectionId *string `json:"id,omitempty"`
}

// ConnectionConf represents the Auth0 Connection configuration
type ConnectionConf struct {
	// Name is the connection name
	// +kubebuilder:validation:Optional
	Name *string `json:"name,omitempty"`

	// DisplayName is the human-friendly name of the connection
	// +kubebuilder:validation:Optional
	DisplayName *string `json:"display_name,omitempty"`

	// Strategy defines the type of identity provider (e.g., auth0, google-oauth2, samlp)
	// +kubebuilder:validation:Optional
	Strategy *string `json:"strategy,omitempty"`

	// Options contains connection-specific configuration options
	// +kubebuilder:validation:Optional
	// +kubebuilder:pruning:PreserveUnknownFields
	Options *runtime.RawExtension `json:"options,omitempty"`

	// ProvisioningTicketUrl is the provisioning ticket URL for enterprise connections
	// +kubebuilder:validation:Optional
	ProvisioningTicketUrl *string `json:"provisioning_ticket_url,omitempty"`

	// Metadata contains arbitrary key-value pairs for the connection
	// +kubebuilder:validation:Optional
	// +kubebuilder:pruning:PreserveUnknownFields
	Metadata *runtime.RawExtension `json:"metadata,omitempty"`

	// Realms contains the realms for which the connection will be used
	// +kubebuilder:validation:Optional
	Realms []string `json:"realms,omitempty"`

	// ShowAsButton indicates whether to display the connection as a button
	// +kubebuilder:validation:Optional
	ShowAsButton *bool `json:"show_as_button,omitempty"`

	// IsDomainConnection indicates whether this is a domain connection
	// +kubebuilder:validation:Optional
	// +kubebuilder:default:=false
	IsDomainConnection *bool `json:"is_domain_connection,omitempty"`
}

// Enums for connection configuration

// ConnectionOptionsPrecedence defines the precedence order for user identifiers
// +kubebuilder:validation:Enum=email;phone_number;username
type ConnectionOptionsPrecedence string

const (
	ConnectionOptionsPrecedenceEmail       ConnectionOptionsPrecedence = "email"
	ConnectionOptionsPrecedencePhoneNumber ConnectionOptionsPrecedence = "phone_number"
	ConnectionOptionsPrecedenceUsername    ConnectionOptionsPrecedence = "username"
)

// ConnectionOptionsPasswordPolicy defines password policy strength levels
type ConnectionOptionsPasswordPolicy string

const (
	ConnectionOptionsPasswordPolicyNone      ConnectionOptionsPasswordPolicy = "none"
	ConnectionOptionsPasswordPolicyLow       ConnectionOptionsPasswordPolicy = "low"
	ConnectionOptionsPasswordPolicyFair      ConnectionOptionsPasswordPolicy = "fair"
	ConnectionOptionsPasswordPolicyGood      ConnectionOptionsPasswordPolicy = "good"
	ConnectionOptionsPasswordPolicyExcellent ConnectionOptionsPasswordPolicy = "excellent"
)

// ConnectionOptionsAttributeStatus defines the status of connection attributes
type ConnectionOptionsAttributeStatus string

const (
	ConnectionOptionsAttributeStatusRequired ConnectionOptionsAttributeStatus = "required"
	ConnectionOptionsAttributeStatusOptional ConnectionOptionsAttributeStatus = "optional"
	ConnectionOptionsAttributeStatusInactive ConnectionOptionsAttributeStatus = "inactive"
)

// ChallengeUi defines passkey challenge UI options
type ChallengeUi string

const (
	ChallengeUiBoth     ChallengeUi = "both"
	ChallengeUiAutoFill ChallengeUi = "autofill"
	ChallengeUiButton   ChallengeUi = "button"
)

// SetUserRootAttributes defines when to set user root attributes
type SetUserRootAttributes string

const (
	SetUserRootAttributesOnEachLogin  SetUserRootAttributes = "on_each_login"
	SetUserRootAttributesOnFirstLogin SetUserRootAttributes = "on_first_login"
	SetUserRootAttributesNeverOnLogin SetUserRootAttributes = "never_on_login"
)
