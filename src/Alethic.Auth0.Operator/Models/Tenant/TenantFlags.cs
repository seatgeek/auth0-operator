using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Models.Tenant
{

    public class TenantFlags
    {

        [JsonPropertyName("allow_legacy_ro_grant_types")]
        public bool? AllowLegacyRoGrantTypes { get; set; }

        [JsonPropertyName("allow_legacy_delegation_grant_types")]
        public bool? AllowLegacyDelegationGrantTypes { get; set; }

        [JsonPropertyName("allow_legacy_tokeninfo_endpoint")]
        public bool? AllowLegacyTokeninfoEndpoint { get; set; }

        [JsonPropertyName("change_pwd_flow_v1")]
        public bool? ChangePwdFlowV1 { get; set; }

        [JsonPropertyName("disable_clickjack_protection_headers")]
        public bool? DisableClickjackProtectionHeaders { get; set; }

        [JsonPropertyName("disable_management_api_sms_obfuscation")]
        public bool? DisableManagementApiSmsObfuscation { get; set; }

        [JsonPropertyName("enable_adfs_waad_email_verification")]
        public bool? EnableAdfsWaadEmailVerification { get; set; }

        [JsonPropertyName("enable_apis_section")]
        public bool? EnableAPIsSection { get; set; }

        [JsonPropertyName("enable_client_connections")]
        public bool? EnableClientConnections { get; set; }

        [JsonPropertyName("enable_custom_domain_in_emails")]
        public bool? EnableCustomDomainInEmails { get; set; }

        [JsonPropertyName("enable_dynamic_client_registration")]
        public bool? EnableDynamicClientRegistration { get; set; }

        [JsonPropertyName("enable_idtoken_api2")]
        public bool? EnableIdTokenApi2 { get; set; }

        [JsonPropertyName("enable_legacy_profile")]
        public bool? EnableLegacyProfile { get; set; }

        [JsonPropertyName("enable_pipeline2")]
        public bool? EnablePipeline2 { get; set; }

        [JsonPropertyName("enable_public_signup_user_exists_error")]
        public bool? EnablePublicSignupUserExistsError { get; set; }

        [JsonPropertyName("enable_sso")]
        public bool? EnableSSO { get; set; }

        [JsonPropertyName("enforce_client_authentication_on_passwordless_start")]
        public bool? EnforceClientAuthenticationOnPasswordlessStart { get; set; }

        [JsonPropertyName("no_disclose_enterprise_connections")]
        public bool? NoDiscloseEnterpriseConnections { get; set; }

        [JsonPropertyName("remove_alg_from_jwks")]
        public bool? RemoveAlgFromJwks { get; set; }

        [JsonPropertyName("require_pushed_authorization_requests")]
        public bool? RequirePushedAuthorizationRequests { get; set; }

        [JsonPropertyName("revoke_refresh_token_grant")]
        public bool? RevokeRefreshTokenGrant { get; set; }

        [JsonPropertyName("trust_azure_adfs_email_verified_connection_property")]
        public bool? TrustAzureAdfsEmailVerifiedConnectionProperty { get; set; }

    }

}
