using System.Collections;
using System.Text.Json.Serialization;

using Alethic.Auth0.Operator.Models.Organization;

namespace Alethic.Auth0.Operator.Models.Client
{

    public partial class ClientConf
    {

        [JsonPropertyName("signing_keys")]
        public SigningKey[]? SigningKeys { get; set; }

        [JsonPropertyName("app_type")]
        public ClientApplicationType? ApplicationType { get; set; }

        [JsonPropertyName("token_endpoint_auth_method")]
        public TokenEndpointAuthMethod? TokenEndpointAuthMethod { get; set; }

        [JsonPropertyName("client_authentication_methods")]
        public ClientAuthenticationMethods? ClientAuthenticationMethods { get; set; }

        [JsonPropertyName("signed_request_object")]
        public SignedRequestObject? SignedRequestObject { get; set; }

        [JsonPropertyName("addons")]
        public Addons? AddOns { get; set; }

        [JsonPropertyName("allowed_clients")]
        public string[]? AllowedClients { get; set; }

        [JsonPropertyName("allowed_logout_urls")]
        public string[]? AllowedLogoutUrls { get; set; }

        [JsonPropertyName("allowed_origins")]
        public string[]? AllowedOrigins { get; set; }

        [JsonPropertyName("web_origins")]
        public string[]? WebOrigins { get; set; }

        [JsonPropertyName("initiate_login_uri")]
        public string? InitiateLoginUri { get; set; }

        [JsonPropertyName("callbacks")]
        public string[]? Callbacks { get; set; }

        [JsonPropertyName("client_aliases")]
        public string[]? ClientAliases { get; set; }

        [JsonPropertyName("client_metadata")]
        public IDictionary? ClientMetaData { get; set; }

        [JsonPropertyName("client_secret")]
        public string? ClientSecret { get; set; }

        [JsonPropertyName("custom_login_page_on")]
        public bool? IsCustomLoginPageOn { get; set; }

        [JsonPropertyName("is_first_party")]
        public bool? IsFirstParty { get; set; }

        [JsonPropertyName("custom_login_page")]
        public string? CustomLoginPage { get; set; }

        [JsonPropertyName("custom_login_page_preview")]
        public string? CustomLoginPagePreview { get; set; }

        [JsonPropertyName("encryption_key")]
        public EncryptionKey? EncryptionKey { get; set; }

        [JsonPropertyName("form_template")]
        public string? FormTemplate { get; set; }

        [JsonPropertyName("grant_types")]
        public string[]? GrantTypes { get; set; }

        [JsonPropertyName("jwt_configuration")]
        public JwtConfiguration? JwtConfiguration { get; set; }

        [JsonPropertyName("mobile")]
        public Mobile? Mobile { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("logo_uri")]
        public string? LogoUri { get; set; }

        [JsonPropertyName("oidc_conformant")]
        public bool? OidcConformant { get; set; }

        [JsonPropertyName("oidc_logout")]
        public OidcLogoutConfig? OidcLogout { get; set; }

        [JsonPropertyName("resource_servers")]
        public ClientResourceServerAssociation[]? ResourceServers { get; set; }

        [JsonPropertyName("sso")]
        public bool? Sso { get; set; }

        [JsonPropertyName("refresh_token")]
        public RefreshToken? RefreshToken { get; set; }

        [JsonPropertyName("organization_usage")]
        public OrganizationUsage? OrganizationUsage { get; set; }

        [JsonPropertyName("organization_require_behavior")]
        public OrganizationRequireBehavior? OrganizationRequireBehavior { get; set; }

        [JsonPropertyName("cross_origin_authentication")]
        public bool? CrossOriginAuthentication { get; set; }

        [JsonPropertyName("require_pushed_authorization_requests")]
        public bool? RequirePushedAuthorizationRequests { get; set; }

        [JsonPropertyName("default_organization")]
        public DefaultOrganization? DefaultOrganization { get; set; }

        [JsonPropertyName("compliance_level")]
        public ComplianceLevel? ComplianceLevel { get; set; }

        [JsonPropertyName("require_proof_of_possession")]
        public bool? RequireProofOfPossession { get; set; }

    }

}
