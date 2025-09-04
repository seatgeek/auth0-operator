package v1

import (
	metav1 "k8s.io/apimachinery/pkg/apis/meta/v1"
	"k8s.io/apimachinery/pkg/runtime"
)

// A0Client is the Schema for the a0clients API
// +kubebuilder:object:root=true
// +kubebuilder:subresource:status
// +kubebuilder:resource:shortName=a0app
// +genclient
type A0Client struct {
	metav1.TypeMeta   `json:",inline"`
	metav1.ObjectMeta `json:"metadata,omitempty"`

	Spec   A0ClientSpec   `json:"spec,omitempty"`
	Status A0ClientStatus `json:"status,omitempty"`
}

// A0ClientList contains a list of A0Client
// +kubebuilder:object:root=true
type A0ClientList struct {
	metav1.TypeMeta `json:",inline"`
	metav1.ListMeta `json:"metadata,omitempty"`
	Items           []A0Client `json:"items"`
}

// A0ClientSpec defines the desired state of A0Client
type A0ClientSpec struct {
	// Policy defines the allowed operations for this client
	// +kubebuilder:validation:Optional
	Policy []V1EntityPolicyType `json:"policy,omitempty"`

	// TenantRef is a reference to the A0Tenant this client belongs to
	// +kubebuilder:validation:Required
	TenantRef *V1TenantReference `json:"tenantRef"`

	// SecretRef is a reference to a secret containing client credentials
	// +kubebuilder:validation:Optional
	SecretRef *V1SecretReference `json:"secretRef,omitempty"`

	// Find specifies how to find an existing client in Auth0
	// +kubebuilder:validation:Optional
	Find *ClientFind `json:"find,omitempty"`

	// Init specifies the initial configuration when creating a new client
	// +kubebuilder:validation:Optional
	Init *ClientConf `json:"init,omitempty"`

	// Conf specifies the desired configuration for the client
	// +kubebuilder:validation:Required
	Conf *ClientConf `json:"conf"`
}

// A0ClientStatus defines the observed state of A0Client
type A0ClientStatus struct {
	// Id is the Auth0 client ID
	// +kubebuilder:validation:Optional
	Id *string `json:"id,omitempty"`

	// LastConf contains the last applied configuration
	// +kubebuilder:validation:Optional
	// +kubebuilder:pruning:PreserveUnknownFields
	LastConf *runtime.RawExtension `json:"lastConf,omitempty"`
}

// ClientFind specifies how to find an existing client in Auth0
type ClientFind struct {
	// ClientId is the Auth0 client ID to search for
	// +kubebuilder:validation:Optional
	ClientId *string `json:"client_id,omitempty"`

	// CallbackUrls are callback URLs to match against
	// +kubebuilder:validation:Optional
	CallbackUrls []string `json:"callback_urls,omitempty"`

	// CallbackUrlMatchMode defines how to match callback URLs
	// +kubebuilder:validation:Optional
	CallbackUrlMatchMode *string `json:"callback_url_match_mode,omitempty"`
}

// ClientConf defines the configuration for an Auth0 client
type ClientConf struct {
	// SigningKeys are the signing keys for the client
	// +kubebuilder:validation:Optional
	SigningKeys []SigningKey `json:"signing_keys,omitempty"`

	// ApplicationType is the type of Auth0 application
	// +kubebuilder:validation:Optional
	// +kubebuilder:validation:Enum=box;cloudbees;concur;dropbox;echosign;egnyte;mscrm;native;newrelic;non_interactive;office365;regular_web;rms;salesforce;sentry;sharepoint;slack;springcm;spa;zendesk;zoom
	ApplicationType *string `json:"app_type,omitempty"`

	// TokenEndpointAuthMethod specifies the client authentication method
	// +kubebuilder:validation:Optional
	// +kubebuilder:validation:Enum=none;client_secret_post;client_secret_basic
	TokenEndpointAuthMethod *string `json:"token_endpoint_auth_method,omitempty"`

	// ClientAuthenticationMethods defines advanced authentication methods
	// +kubebuilder:validation:Optional
	ClientAuthenticationMethods *ClientAuthenticationMethods `json:"client_authentication_methods,omitempty"`

	// SignedRequestObject configuration
	// +kubebuilder:validation:Optional
	SignedRequestObject *SignedRequestObject `json:"signed_request_object,omitempty"`

	// AddOns configuration for third-party integrations
	// +kubebuilder:validation:Optional
	// +kubebuilder:pruning:PreserveUnknownFields
	AddOns *runtime.RawExtension `json:"addons,omitempty"`

	// AllowedClients lists allowed client IDs for this client
	// +kubebuilder:validation:Optional
	AllowedClients []string `json:"allowed_clients,omitempty"`

	// AllowedLogoutUrls lists allowed logout URLs
	// +kubebuilder:validation:Optional
	AllowedLogoutUrls []string `json:"allowed_logout_urls,omitempty"`

	// AllowedOrigins lists allowed origins for CORS
	// +kubebuilder:validation:Optional
	AllowedOrigins []string `json:"allowed_origins,omitempty"`

	// WebOrigins lists web origins for the client
	// +kubebuilder:validation:Optional
	WebOrigins []string `json:"web_origins,omitempty"`

	// InitiateLoginUri is the URI to initiate login
	// +kubebuilder:validation:Optional
	InitiateLoginUri *string `json:"initiate_login_uri,omitempty"`

	// Callbacks lists callback URLs
	// +kubebuilder:validation:Optional
	Callbacks []string `json:"callbacks,omitempty"`

	// ClientAliases lists client aliases
	// +kubebuilder:validation:Optional
	ClientAliases []string `json:"client_aliases,omitempty"`

	// ClientMetadata contains custom client metadata
	// +kubebuilder:validation:Optional
	// +kubebuilder:pruning:PreserveUnknownFields
	ClientMetadata *runtime.RawExtension `json:"client_metadata,omitempty"`

	// IsCustomLoginPageOn indicates if custom login page is enabled
	// +kubebuilder:validation:Optional
	IsCustomLoginPageOn *bool `json:"custom_login_page_on,omitempty"`

	// IsFirstParty indicates if this is a first-party client
	// +kubebuilder:validation:Optional
	IsFirstParty *bool `json:"is_first_party,omitempty"`

	// CustomLoginPage contains custom login page HTML
	// +kubebuilder:validation:Optional
	CustomLoginPage *string `json:"custom_login_page,omitempty"`

	// CustomLoginPagePreview contains custom login page preview HTML
	// +kubebuilder:validation:Optional
	CustomLoginPagePreview *string `json:"custom_login_page_preview,omitempty"`

	// EncryptionKey is the encryption key configuration
	// +kubebuilder:validation:Optional
	EncryptionKey *EncryptionKey `json:"encryption_key,omitempty"`

	// FormTemplate contains the form template
	// +kubebuilder:validation:Optional
	FormTemplate *string `json:"form_template,omitempty"`

	// GrantTypes lists allowed grant types
	// +kubebuilder:validation:Optional
	GrantTypes []string `json:"grant_types,omitempty"`

	// JwtConfiguration contains JWT configuration
	// +kubebuilder:validation:Optional
	JwtConfiguration *JwtConfiguration `json:"jwt_configuration,omitempty"`

	// Mobile contains mobile app configuration
	// +kubebuilder:validation:Optional
	Mobile *Mobile `json:"mobile,omitempty"`

	// Name is the client name
	// +kubebuilder:validation:Optional
	Name *string `json:"name,omitempty"`

	// Description is the client description
	// +kubebuilder:validation:Optional
	Description *string `json:"description,omitempty"`

	// LogoUri is the client logo URI
	// +kubebuilder:validation:Optional
	LogoUri *string `json:"logo_uri,omitempty"`

	// OidcConformant indicates OIDC conformance
	// +kubebuilder:validation:Optional
	OidcConformant *bool `json:"oidc_conformant,omitempty"`

	// OidcLogout contains OIDC logout configuration
	// +kubebuilder:validation:Optional
	OidcLogout *OidcLogoutConfig `json:"oidc_logout,omitempty"`

	// ResourceServers lists associated resource servers
	// +kubebuilder:validation:Optional
	ResourceServers []ClientResourceServerAssociation `json:"resource_servers,omitempty"`

	// Sso indicates if SSO is enabled
	// +kubebuilder:validation:Optional
	Sso *bool `json:"sso,omitempty"`

	// RefreshToken contains refresh token configuration
	// +kubebuilder:validation:Optional
	RefreshToken *RefreshToken `json:"refresh_token,omitempty"`

	// OrganizationUsage specifies organization usage policy
	// +kubebuilder:validation:Optional
	// +kubebuilder:validation:Enum=deny;allow;require
	OrganizationUsage *string `json:"organization_usage,omitempty"`

	// OrganizationRequireBehavior specifies organization require behavior
	// +kubebuilder:validation:Optional
	// +kubebuilder:validation:Enum=no_prompt;pre_login_prompt;post_login_prompt
	OrganizationRequireBehavior *string `json:"organization_require_behavior,omitempty"`

	// CrossOriginAuthentication indicates if cross-origin auth is enabled
	// +kubebuilder:validation:Optional
	CrossOriginAuthentication *bool `json:"cross_origin_authentication,omitempty"`

	// RequirePushedAuthorizationRequests indicates if PAR is required
	// +kubebuilder:validation:Optional
	RequirePushedAuthorizationRequests *bool `json:"require_pushed_authorization_requests,omitempty"`

	// DefaultOrganization specifies default organization settings
	// +kubebuilder:validation:Optional
	DefaultOrganization *DefaultOrganization `json:"default_organization,omitempty"`

	// ComplianceLevel specifies the compliance level
	// +kubebuilder:validation:Optional
	// +kubebuilder:validation:Enum=none;fapi1_adv_pkj_par;fapi1_adv_mtls_par
	ComplianceLevel *string `json:"compliance_level,omitempty"`

	// RequireProofOfPossession indicates if proof of possession is required
	// +kubebuilder:validation:Optional
	RequireProofOfPossession *bool `json:"require_proof_of_possession,omitempty"`

	// EnabledConnections lists enabled connections for this client
	// +kubebuilder:validation:Optional
	EnabledConnections []V1ConnectionReference `json:"enabled_connections,omitempty"`
}

// Supporting types

// SigningKey represents a signing key configuration
type SigningKey struct {
	// +kubebuilder:validation:Optional
	Cert *string `json:"cert,omitempty"`

	// +kubebuilder:validation:Optional
	Key *string `json:"key,omitempty"`

	// +kubebuilder:validation:Optional
	Pkcs7 *string `json:"pkcs7,omitempty"`
}

// ClientAuthenticationMethods defines advanced authentication methods
type ClientAuthenticationMethods struct {
	// +kubebuilder:validation:Optional
	PrivateKeyJwt *PrivateKeyJwtDef `json:"private_key_jwt,omitempty"`

	// +kubebuilder:validation:Optional
	TlsClientAuth *TlsClientAuthDef `json:"tls_client_auth,omitempty"`

	// +kubebuilder:validation:Optional
	SelfSignedTlsClientAuth *SelfSignedTlsClientAuthDef `json:"self_signed_tls_client_auth,omitempty"`
}

// CredentialIdDef represents a credential reference
type CredentialIdDef struct {
	// +kubebuilder:validation:Optional
	Id *string `json:"id,omitempty"`
}

// PrivateKeyJwtDef contains private key JWT credentials
type PrivateKeyJwtDef struct {
	// +kubebuilder:validation:Optional
	Credentials []CredentialIdDef `json:"credentials,omitempty"`
}

// TlsClientAuthDef contains TLS client auth credentials
type TlsClientAuthDef struct {
	// +kubebuilder:validation:Optional
	Credentials []CredentialIdDef `json:"credentials,omitempty"`
}

// SelfSignedTlsClientAuthDef contains self-signed TLS client auth credentials
type SelfSignedTlsClientAuthDef struct {
	// +kubebuilder:validation:Optional
	Credentials []CredentialIdDef `json:"credentials,omitempty"`
}

// SignedRequestObject contains signed request object configuration
type SignedRequestObject struct {
	// +kubebuilder:validation:Optional
	Required *bool `json:"required,omitempty"`

	// +kubebuilder:validation:Optional
	Credentials []CredentialIdDef `json:"credentials,omitempty"`
}


// Mobile contains mobile app configuration
type Mobile struct {
	// +kubebuilder:validation:Optional
	Android *MobileAndroid `json:"android,omitempty"`

	// +kubebuilder:validation:Optional
	Ios *MobileIos `json:"ios,omitempty"`
}

// MobileAndroid contains Android-specific configuration
type MobileAndroid struct {
	// +kubebuilder:validation:Optional
	AppPackageName *string `json:"app_package_name,omitempty"`

	// +kubebuilder:validation:Optional
	KeystoreHash *string `json:"keystore_hash,omitempty"`
}

// MobileIos contains iOS-specific configuration
type MobileIos struct {
	// +kubebuilder:validation:Optional
	AppBundleIdentifier *string `json:"app_bundle_identifier,omitempty"`

	// +kubebuilder:validation:Optional
	TeamId *string `json:"team_id,omitempty"`
}

// EncryptionKey represents encryption key configuration
type EncryptionKey struct {
	// +kubebuilder:validation:Optional
	Certificate *string `json:"cert,omitempty"`

	// +kubebuilder:validation:Optional
	PublicKey *string `json:"pub,omitempty"`

	// +kubebuilder:validation:Optional
	Subject *string `json:"subject,omitempty"`
}

// JwtConfiguration contains JWT configuration
type JwtConfiguration struct {
	// +kubebuilder:validation:Optional
	IsSecretEncoded *bool `json:"secret_encoded,omitempty"`

	// +kubebuilder:validation:Optional
	LifetimeInSeconds *int32 `json:"lifetime_in_seconds,omitempty"`

	// +kubebuilder:validation:Optional
	Scopes *Scopes `json:"scopes,omitempty"`

	// +kubebuilder:validation:Optional
	SigningAlgorithm *string `json:"alg,omitempty"`
}

// Scopes contains scope definitions
type Scopes struct {
	// +kubebuilder:validation:Optional
	Users *ScopeEntry `json:"users,omitempty"`

	// +kubebuilder:validation:Optional
	UsersAppMetadata *ScopeEntry `json:"users_app_metadata,omitempty"`

	// +kubebuilder:validation:Optional
	Clients *ScopeEntry `json:"clients,omitempty"`

	// +kubebuilder:validation:Optional
	ClientKeys *ScopeEntry `json:"client_keys,omitempty"`

	// +kubebuilder:validation:Optional
	Tokens *ScopeEntry `json:"tokens,omitempty"`

	// +kubebuilder:validation:Optional
	Stats *ScopeEntry `json:"stats,omitempty"`
}

// ScopeEntry contains scope actions
type ScopeEntry struct {
	// +kubebuilder:validation:Optional
	Actions []string `json:"actions,omitempty"`
}

// OidcLogoutConfig contains OIDC logout configuration
type OidcLogoutConfig struct {
	// +kubebuilder:validation:Optional
	BackchannelLogoutUrls []string `json:"backchannel_logout_urls,omitempty"`

	// +kubebuilder:validation:Optional
	BackchannelLogoutInitiators *BackchannelLogoutInitiators `json:"backchannel_logout_initiators,omitempty"`
}

// BackchannelLogoutInitiators contains backchannel logout initiator configuration
type BackchannelLogoutInitiators struct {
	// +kubebuilder:validation:Optional
	// +kubebuilder:validation:Enum=all;custom
	Mode *string `json:"mode,omitempty"`

	// +kubebuilder:validation:Optional
	// +kubebuilder:validation:Enum=rp-logout;idp-logout;password-changed;session-expired
	SelectedInitiators []string `json:"selected_initiators,omitempty"`
}

// RefreshToken contains refresh token configuration
type RefreshToken struct {
	// +kubebuilder:validation:Optional
	// +kubebuilder:validation:Enum=rotating;non-rotating
	RotationType *string `json:"rotation_type,omitempty"`

	// +kubebuilder:validation:Optional
	// +kubebuilder:validation:Enum=expiring;non-expiring
	ExpirationType *string `json:"expiration_type,omitempty"`

	// +kubebuilder:validation:Optional
	Leeway *int32 `json:"leeway,omitempty"`

	// +kubebuilder:validation:Optional
	TokenLifetime *int32 `json:"token_lifetime,omitempty"`

	// +kubebuilder:validation:Optional
	InfiniteTokenLifetime *bool `json:"infinite_token_lifetime,omitempty"`

	// +kubebuilder:validation:Optional
	IdleTokenLifetime *int32 `json:"idle_token_lifetime,omitempty"`

	// +kubebuilder:validation:Optional
	InfiniteIdleTokenLifetime *bool `json:"infinite_idle_token_lifetime,omitempty"`
}

// ClientResourceServerAssociation represents a client-resource server association
type ClientResourceServerAssociation struct {
	// +kubebuilder:validation:Optional
	Identifier *string `json:"identifier,omitempty"`

	// +kubebuilder:validation:Optional
	Scopes []string `json:"scopes,omitempty"`
}

// DefaultOrganization contains default organization settings
type DefaultOrganization struct {
	// +kubebuilder:validation:Optional
	OrganizationId *string `json:"organization_id,omitempty"`

	// +kubebuilder:validation:Optional
	// +kubebuilder:validation:Enum=client_credentials
	Flows []string `json:"flows,omitempty"`
}

