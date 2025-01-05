using System.Collections.Generic;
using System.Text.Json.Serialization;

using Alethic.Auth0.Operator.Entities;

namespace Alethic.Auth0.Operator.Models.Tenant
{

    public partial class TenantConf
    {

        [JsonPropertyName("friendly_name")]
        public string? FriendlyName { get; set; }

        [JsonPropertyName("support_email")]
        public string? SupportEmail { get; set; }

        [JsonPropertyName("support_url")]
        public string? SupportUrl { get; set; }

        [JsonPropertyName("enabled_locales")]
        public List<string>? EnabledLocales { get; set; }

        [JsonPropertyName("change_password")]
        public TenantChangePassword? ChangePassword { get; set; }

        [JsonPropertyName("device_flow")]
        public TenantDeviceFlow? DeviceFlow { get; set; }

        [JsonPropertyName("flags")]
        public TenantFlags? Flags { get; set; }

        [JsonPropertyName("guardian_mfa_page")]
        public TenantGuardianMfaPage? GuardianMfaPage { get; set; }

        [JsonPropertyName("default_audience")]
        public string? DefaultAudience { get; set; }

        [JsonPropertyName("default_directory")]
        public string? DefaultDirectory { get; set; }

        [JsonPropertyName("error_page")]
        public TenantErrorPage? ErrorPage { get; set; }

        [JsonPropertyName("picture_url")]
        public string? PictureUrl { get; set; }

        [JsonPropertyName("allowed_logout_urls")]
        public string[]? AllowedLogoutUrls { get; set; }

        [JsonPropertyName("session_lifetime")]
        public float? SessionLifetime { get; set; }

        [JsonPropertyName("idle_session_lifetime")]
        public float? IdleSessionLifetime { get; set; }

        [JsonPropertyName("sandbox_version")]
        public string? SandboxVersion { get; set; }

        [JsonPropertyName("sandbox_versions_available")]
        public string[]? SandboxVersionsAvailable { get; set; }

        [JsonPropertyName("session_cookie")]
        public SessionCookie? SessionCookie { get; set; }

        [JsonPropertyName("customize_mfa_in_postlogin_action")]
        public bool? CustomizeMfaInPostLoginAction { get; set; }

        [JsonPropertyName("acr_values_supported")]
        public string[]? AcrValuesSupported { get; set; }

        [JsonPropertyName("pushed_authorization_requests_supported")]
        public bool? PushedAuthorizationRequestsSupported { get; set; }

        [JsonPropertyName("mtls")]
        public TenantMtls? Mtls { get; set; }

    }

}
