package v1

import (
	metav1 "k8s.io/apimachinery/pkg/apis/meta/v1"
	"k8s.io/apimachinery/pkg/runtime"
)

// A0Tenant is the Schema for the a0tenants API
// +kubebuilder:object:root=true
// +kubebuilder:subresource:status
// +kubebuilder:resource:shortName=a0tenant
// +genclient
type A0Tenant struct {
	metav1.TypeMeta   `json:",inline"`
	metav1.ObjectMeta `json:"metadata,omitempty"`

	Spec   A0TenantSpec   `json:"spec,omitempty"`
	Status A0TenantStatus `json:"status,omitempty"`
}

// A0TenantList contains a list of A0Tenant
// +kubebuilder:object:root=true
type A0TenantList struct {
	metav1.TypeMeta `json:",inline"`
	metav1.ListMeta `json:"metadata,omitempty"`
	Items           []A0Tenant `json:"items"`
}

// A0TenantSpec defines the desired state of A0Tenant
type A0TenantSpec struct {
	// Policy defines the allowed operations for this tenant
	// +kubebuilder:validation:Optional
	Policy []V1EntityPolicyType `json:"policy,omitempty"`

	// Name is the name of the tenant
	// +kubebuilder:validation:Required
	Name string `json:"name"`

	// Auth contains authentication configuration for the tenant
	// +kubebuilder:validation:Required
	Auth *TenantAuth `json:"auth"`

	// Init specifies the initial configuration when creating a new tenant
	// +kubebuilder:validation:Optional
	Init *TenantConf `json:"init,omitempty"`

	// Conf specifies the desired configuration for the tenant
	// +kubebuilder:validation:Required
	Conf *TenantConf `json:"conf"`
}

// A0TenantStatus defines the observed state of A0Tenant
type A0TenantStatus struct {
	// LastConf contains the last applied configuration
	// +kubebuilder:validation:Optional
	// +kubebuilder:pruning:PreserveUnknownFields
	LastConf *runtime.RawExtension `json:"lastConf,omitempty"`
}

// TenantAuth contains authentication configuration for accessing Auth0 Management API
type TenantAuth struct {
	// Domain is the Auth0 tenant domain
	// +kubebuilder:validation:Required
	Domain *string `json:"domain"`

	// SecretRef references a Kubernetes Secret containing Auth0 credentials
	// +kubebuilder:validation:Required
	SecretRef *V1SecretReference `json:"secretRef"`
}

// TenantConf defines the configuration for an Auth0 tenant
type TenantConf struct {
	// FriendlyName is the human-readable name of the tenant
	// +kubebuilder:validation:Optional
	FriendlyName *string `json:"friendly_name,omitempty"`

	// SupportEmail is the support email for the tenant
	// +kubebuilder:validation:Optional
	SupportEmail *string `json:"support_email,omitempty"`

	// SupportUrl is the support URL for the tenant
	// +kubebuilder:validation:Optional
	SupportUrl *string `json:"support_url,omitempty"`

	// EnabledLocales lists the enabled locales for the tenant
	// +kubebuilder:validation:Optional
	EnabledLocales []string `json:"enabled_locales,omitempty"`

	// ChangePassword contains change password configuration
	// +kubebuilder:validation:Optional
	ChangePassword *TenantChangePassword `json:"change_password,omitempty"`

	// GuardianMfaPage contains Guardian MFA page configuration
	// +kubebuilder:validation:Optional
	GuardianMfaPage *TenantGuardianMfaPage `json:"guardian_mfa_page,omitempty"`

	// DefaultAudience is the default audience for API authorization
	// +kubebuilder:validation:Optional
	DefaultAudience *string `json:"default_audience,omitempty"`

	// DefaultDirectory is the default directory for the tenant
	// +kubebuilder:validation:Optional
	DefaultDirectory *string `json:"default_directory,omitempty"`

	// ErrorPage contains custom error page configuration
	// +kubebuilder:validation:Optional
	ErrorPage *TenantErrorPage `json:"error_page,omitempty"`

	// DeviceFlow contains device flow configuration
	// +kubebuilder:validation:Optional
	DeviceFlow *TenantDeviceFlow `json:"device_flow,omitempty"`

	// Flags contains feature flags for the tenant
	// +kubebuilder:validation:Optional
	Flags *TenantFlags `json:"flags,omitempty"`

	// SessionCookie contains session cookie configuration
	// +kubebuilder:validation:Optional
	SessionCookie *SessionCookie `json:"session_cookie,omitempty"`

	// Sessions contains session configuration
	// +kubebuilder:validation:Optional
	// +kubebuilder:pruning:PreserveUnknownFields
	Sessions *runtime.RawExtension `json:"sessions,omitempty"`

	// SandboxVersion specifies the sandbox version
	// +kubebuilder:validation:Optional
	SandboxVersion *string `json:"sandbox_version,omitempty"`

	// IdleSessionLifetime specifies idle session lifetime in hours
	// +kubebuilder:validation:Optional
	// +kubebuilder:validation:Minimum=1
	IdleSessionLifetime *int32 `json:"idle_session_lifetime,omitempty"`

	// SessionLifetime specifies session lifetime in hours
	// +kubebuilder:validation:Optional
	// +kubebuilder:validation:Minimum=1
	SessionLifetime *int32 `json:"session_lifetime,omitempty"`

	// AllowedLogoutUrls lists allowed logout URLs for the tenant
	// +kubebuilder:validation:Optional
	AllowedLogoutUrls []string `json:"allowed_logout_urls,omitempty"`

	// SessionCookieMode specifies the session cookie mode
	// +kubebuilder:validation:Optional
	// +kubebuilder:validation:Enum=persistent;non-persistent
	SessionCookieMode *string `json:"session_cookie_mode,omitempty"`
}

// TenantChangePassword contains change password configuration
type TenantChangePassword struct {
	// Enabled indicates whether change password is enabled
	// +kubebuilder:validation:Optional
	Enabled *bool `json:"enabled,omitempty"`

	// Html contains custom HTML for the change password page
	// +kubebuilder:validation:Optional
	Html *string `json:"html,omitempty"`
}

// TenantGuardianMfaPage contains Guardian MFA page configuration
type TenantGuardianMfaPage struct {
	// Enabled indicates whether custom Guardian MFA page is enabled
	// +kubebuilder:validation:Optional
	Enabled *bool `json:"enabled,omitempty"`

	// Html contains custom HTML for the Guardian MFA page
	// +kubebuilder:validation:Optional
	Html *string `json:"html,omitempty"`
}

// TenantErrorPage contains custom error page configuration
type TenantErrorPage struct {
	// Html contains custom HTML for error pages
	// +kubebuilder:validation:Optional
	Html *string `json:"html,omitempty"`

	// ShowLogLink indicates whether to show log links in error pages
	// +kubebuilder:validation:Optional
	ShowLogLink *bool `json:"show_log_link,omitempty"`

	// Url is the URL for custom error pages
	// +kubebuilder:validation:Optional
	Url *string `json:"url,omitempty"`
}

// TenantDeviceFlow contains device flow configuration
type TenantDeviceFlow struct {
	// Charset specifies the character set for device codes
	// +kubebuilder:validation:Optional
	// +kubebuilder:validation:Enum=base20;digits
	Charset *string `json:"charset,omitempty"`

	// Mask specifies the format mask for device codes
	// +kubebuilder:validation:Optional
	Mask *string `json:"mask,omitempty"`
}

// TenantFlags contains feature flags for the tenant
type TenantFlags struct {
	// ChangePasswordFlowV1 enables change password flow v1
	// +kubebuilder:validation:Optional
	ChangePasswordFlowV1 *bool `json:"change_pwd_flow_v1,omitempty"`

	// EnableClientConnections enables client connections
	// +kubebuilder:validation:Optional
	EnableClientConnections *bool `json:"enable_client_connections,omitempty"`

	// EnableApisSection enables the APIs section
	// +kubebuilder:validation:Optional
	EnableApisSection *bool `json:"enable_apis_section,omitempty"`

	// EnablePipelineV2 enables pipeline v2
	// +kubebuilder:validation:Optional
	EnablePipelineV2 *bool `json:"enable_pipeline2,omitempty"`

	// EnableDynamicClientRegistration enables dynamic client registration
	// +kubebuilder:validation:Optional
	EnableDynamicClientRegistration *bool `json:"enable_dynamic_client_registration,omitempty"`

	// EnableCustomDomainInEmails enables custom domain in emails
	// +kubebuilder:validation:Optional
	EnableCustomDomainInEmails *bool `json:"enable_custom_domain_in_emails,omitempty"`

	// UniversalLogin enables universal login
	// +kubebuilder:validation:Optional
	UniversalLogin *bool `json:"universal_login,omitempty"`

	// EnableLegacyLogsSearchV2 enables legacy logs search v2
	// +kubebuilder:validation:Optional
	EnableLegacyLogsSearchV2 *bool `json:"enable_legacy_logs_search_v2,omitempty"`

	// DisableClickjackProtectionHeaders disables clickjack protection headers
	// +kubebuilder:validation:Optional
	DisableClickjackProtectionHeaders *bool `json:"disable_clickjack_protection_headers,omitempty"`

	// EnablePublicSignupUserExistsError enables public signup user exists error
	// +kubebuilder:validation:Optional
	EnablePublicSignupUserExistsError *bool `json:"enable_public_signup_user_exists_error,omitempty"`

	// UseScope describes what scope to use
	// +kubebuilder:validation:Optional
	UseScope *string `json:"use_scope,omitempty"`

	// AllowLegacyDelegationGrantTypes allows legacy delegation grant types
	// +kubebuilder:validation:Optional
	AllowLegacyDelegationGrantTypes *bool `json:"allow_legacy_delegation_grant_types,omitempty"`

	// AllowLegacyROGrantTypes allows legacy resource owner grant types
	// +kubebuilder:validation:Optional
	AllowLegacyROGrantTypes *bool `json:"allow_legacy_ro_grant_types,omitempty"`

	// AllowLegacyTokenInfoEndpoint allows legacy tokeninfo endpoint
	// +kubebuilder:validation:Optional
	AllowLegacyTokenInfoEndpoint *bool `json:"allow_legacy_tokeninfo_endpoint,omitempty"`

	// EnableLegacyProfile enables legacy profile
	// +kubebuilder:validation:Optional
	EnableLegacyProfile *bool `json:"enable_legacy_profile,omitempty"`

	// EnableIdTokenAPI enables ID token API
	// +kubebuilder:validation:Optional
	EnableIdTokenAPI *bool `json:"enable_idtoken_api2,omitempty"`

	// EnableIdTokenImplicitGrant enables ID token implicit grant
	// +kubebuilder:validation:Optional
	EnableIdTokenImplicitGrant *bool `json:"enable_impersonation,omitempty"`
}

// SessionCookie contains session cookie configuration
type SessionCookie struct {
	// Mode specifies the session cookie mode
	// +kubebuilder:validation:Optional
	// +kubebuilder:validation:Enum=persistent;non-persistent
	Mode *string `json:"mode,omitempty"`
}
