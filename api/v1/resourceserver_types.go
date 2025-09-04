package v1

import (
	metav1 "k8s.io/apimachinery/pkg/apis/meta/v1"
	"k8s.io/apimachinery/pkg/runtime"
)

// A0ResourceServer is the Schema for the a0resourceservers API
// +kubebuilder:object:root=true
// +kubebuilder:subresource:status
// +kubebuilder:resource:shortName=a0api
// +genclient
type A0ResourceServer struct {
	metav1.TypeMeta   `json:",inline"`
	metav1.ObjectMeta `json:"metadata,omitempty"`

	Spec   A0ResourceServerSpec   `json:"spec,omitempty"`
	Status A0ResourceServerStatus `json:"status,omitempty"`
}

// A0ResourceServerList contains a list of A0ResourceServer
// +kubebuilder:object:root=true
type A0ResourceServerList struct {
	metav1.TypeMeta `json:",inline"`
	metav1.ListMeta `json:"metadata,omitempty"`
	Items           []A0ResourceServer `json:"items"`
}

// A0ResourceServerSpec defines the desired state of A0ResourceServer
type A0ResourceServerSpec struct {
	// Policy defines the allowed operations for this resource server
	// +kubebuilder:validation:Optional
	Policy []V1EntityPolicyType `json:"policy,omitempty"`

	// TenantRef is a reference to the A0Tenant this resource server belongs to
	// +kubebuilder:validation:Required
	TenantRef *V1TenantReference `json:"tenantRef"`

	// Init specifies the initial configuration when creating a new resource server
	// +kubebuilder:validation:Optional
	Init *ResourceServerConf `json:"init,omitempty"`

	// Conf specifies the desired configuration for the resource server
	// +kubebuilder:validation:Required
	Conf *ResourceServerConf `json:"conf"`
}

// A0ResourceServerStatus defines the observed state of A0ResourceServer
type A0ResourceServerStatus struct {
	// Id is the Auth0 resource server ID
	// +kubebuilder:validation:Optional
	Id *string `json:"id,omitempty"`

	// Identifier is the Auth0 resource server identifier
	// +kubebuilder:validation:Optional
	Identifier *string `json:"identifier,omitempty"`

	// LastConf contains the last applied configuration
	// +kubebuilder:validation:Optional
	// +kubebuilder:pruning:PreserveUnknownFields
	LastConf *runtime.RawExtension `json:"lastConf,omitempty"`
}

// ResourceServerConf defines the configuration for an Auth0 resource server (API)
type ResourceServerConf struct {
	// Id is the Auth0 resource server ID
	// +kubebuilder:validation:Optional
	Id *string `json:"id,omitempty"`

	// Identifier is the unique identifier for this resource server
	// +kubebuilder:validation:Optional
	Identifier *string `json:"identifier,omitempty"`

	// Name is the name of the resource server
	// +kubebuilder:validation:Optional
	Name *string `json:"name,omitempty"`

	// Scopes defines the scopes available for this resource server
	// +kubebuilder:validation:Optional
	Scopes []ResourceServerScope `json:"scopes,omitempty"`

	// SigningAlgorithm specifies the algorithm used to sign tokens
	// +kubebuilder:validation:Optional
	// +kubebuilder:validation:Enum=HS256;RS256;PS256
	SigningAlgorithm *string `json:"signing_alg,omitempty"`

	// SigningSecret is the secret used for signing (for HMAC algorithms)
	// +kubebuilder:validation:Optional
	SigningSecret *string `json:"signing_secret,omitempty"`

	// AllowOfflineAccess indicates whether refresh tokens can be issued
	// +kubebuilder:validation:Optional
	AllowOfflineAccess *bool `json:"allow_offline_access,omitempty"`

	// TokenLifetime specifies the token lifetime in seconds
	// +kubebuilder:validation:Optional
	// +kubebuilder:validation:Minimum=1
	TokenLifetime *int32 `json:"token_lifetime,omitempty"`

	// TokenLifetimeForWeb specifies the token lifetime for web apps in seconds
	// +kubebuilder:validation:Optional
	// +kubebuilder:validation:Minimum=1
	TokenLifetimeForWeb *int32 `json:"token_lifetime_for_web,omitempty"`

	// SkipConsentForVerifiableFirstPartyClients skips consent for verifiable first-party clients
	// +kubebuilder:validation:Optional
	SkipConsentForVerifiableFirstPartyClients *bool `json:"skip_consent_for_verifiable_first_party_clients,omitempty"`

	// VerificationLocation specifies where verification should occur
	// +kubebuilder:validation:Optional
	VerificationLocation *string `json:"verification_location,omitempty"`

	// Options contains additional resource server options
	// +kubebuilder:validation:Optional
	// +kubebuilder:pruning:PreserveUnknownFields
	Options *runtime.RawExtension `json:"options,omitempty"`

	// EnforcePolicy indicates whether to enforce authorization policies
	// +kubebuilder:validation:Optional
	EnforcePolicy *bool `json:"enforce_policies,omitempty"`

	// TokenDialect specifies the token format dialect
	// +kubebuilder:validation:Optional
	// +kubebuilder:validation:Enum=access_token;access_token_authz
	TokenDialect *string `json:"token_dialect,omitempty"`

	// ConsentPolicy specifies the consent policy
	// +kubebuilder:validation:Optional
	// +kubebuilder:validation:Enum=null;transactional-authorization-with-mfa
	ConsentPolicy *string `json:"consent_policy,omitempty"`

	// AuthorizationDetails contains authorization detail configurations
	// +kubebuilder:validation:Optional
	AuthorizationDetails []ResourceServerAuthorizationDetail `json:"authorization_details,omitempty"`

	// TokenEncryption contains token encryption configuration
	// +kubebuilder:validation:Optional
	TokenEncryption *TokenEncryption `json:"token_encryption,omitempty"`

	// ProofOfPossession contains proof of possession configuration
	// +kubebuilder:validation:Optional
	ProofOfPossession *ProofOfPossession `json:"proof_of_possession,omitempty"`
}

// ResourceServerScope defines a scope for a resource server
type ResourceServerScope struct {
	// Value is the scope value
	// +kubebuilder:validation:Optional
	Value *string `json:"value,omitempty"`

	// Description is the human-readable description of the scope
	// +kubebuilder:validation:Optional
	Description *string `json:"description,omitempty"`
}

// ResourceServerAuthorizationDetail defines authorization detail configuration
type ResourceServerAuthorizationDetail struct {
	// Type specifies the authorization detail type
	// +kubebuilder:validation:Optional
	Type *string `json:"type,omitempty"`
}

// TokenEncryption defines token encryption configuration
type TokenEncryption struct {
	// EncryptionKey contains the encryption key configuration
	// +kubebuilder:validation:Optional
	EncryptionKey *TokenEncryptionKey `json:"encryption_key,omitempty"`

	// Format specifies the token format
	// +kubebuilder:validation:Optional
	// +kubebuilder:validation:Enum=compact-nested-jwe
	Format *string `json:"format,omitempty"`
}

// TokenEncryptionKey defines the encryption key for tokens
type TokenEncryptionKey struct {
	// Name is the name of the encryption key
	// +kubebuilder:validation:Optional
	Name *string `json:"name,omitempty"`

	// Algorithm specifies the encryption algorithm
	// +kubebuilder:validation:Optional
	Algorithm *string `json:"alg,omitempty"`

	// KeyId is the key identifier
	// +kubebuilder:validation:Optional
	KeyId *string `json:"kid,omitempty"`

	// Pem contains the PEM-encoded key
	// +kubebuilder:validation:Optional
	Pem *string `json:"pem,omitempty"`
}

// ProofOfPossession defines proof of possession configuration
type ProofOfPossession struct {
	// Mechanism specifies the proof of possession mechanism
	// +kubebuilder:validation:Optional
	// +kubebuilder:validation:Enum=mtls
	Mechanism *string `json:"mechanism,omitempty"`

	// Required indicates whether proof of possession is required
	// +kubebuilder:validation:Optional
	Required *bool `json:"required,omitempty"`
}

