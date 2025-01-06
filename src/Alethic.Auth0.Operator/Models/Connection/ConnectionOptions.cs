using System.Collections;
using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Models.Connection
{

    public class ConnectionOptions
    {

        [JsonPropertyName("validation")]
        public ConnectionOptionsValidation? Validation { get; set; }

        [JsonPropertyName("non_persistent_attrs")]
        public string[]? NonPersistentAttributes { get; set; }

        [JsonPropertyName("precedence")]
        public ConnectionOptionsPrecedence[]? Precedence { get; set; }

        [JsonPropertyName("attributes")]
        public ConnectionOptionsAttributes? Attributes { get; set; }

        [JsonPropertyName("enable_script_context")]
        public bool? EnableScriptContext { get; set; }

        [JsonPropertyName("enabledDatabaseCustomization")]
        public bool? EnableDatabaseCustomization { get; set; }

        [JsonPropertyName("import_mode")]
        public bool? ImportMode { get; set; }

        [JsonPropertyName("customScripts")]
        public ConnectionOptionsCustomScripts? CustomScripts { get; set; }

        [JsonPropertyName("authentication_methods")]
        public ConnectionOptionsAuthenticationMethods? AuthenticationMethods { get; set; }

        [JsonPropertyName("passkey_options")]
        public ConnectionOptionsPasskeyOptions? PasskeyOptions { get; set; }

        [JsonPropertyName("passwordPolicy")]
        public ConnectionOptionsPasswordPolicy? PasswordPolicy { get; set; }

        [JsonPropertyName("password_complexity_options")]
        public ConnectionOptionsPasswordComplexityOptions? PasswordComplexityOptions { get; set; }

        [JsonPropertyName("password_history")]
        public ConnectionOptionsPasswordHistory? PasswordHistory { get; set; }

        [JsonPropertyName("password_no_personal_info")]
        public ConnectionOptionsPasswordNoPersonalInfo? PasswordNoPersonalInfo { get; set; }

        [JsonPropertyName("password_dictionary")]
        public ConnectionOptionsPasswordDictionary? PasswordDictionary { get; set; }

        [JsonPropertyName("api_enable_users")]
        public bool? ApiEnableUsers { get; set; }

        [JsonPropertyName("basic_profile")]
        public bool? BasicProfile { get; set; }

        [JsonPropertyName("ext_admin")]
        public bool? ExtAdmin { get; set; }

        [JsonPropertyName("ext_is_suspended")]
        public bool? ExtIsSuspended { get; set; }

        [JsonPropertyName("ext_agreed_terms")]
        public bool? ExtAgreedTerms { get; set; }

        [JsonPropertyName("ext_groups")]
        public bool? ExtGroups { get; set; }

        [JsonPropertyName("ext_assigned_plans")]
        public bool? ExtAssignedPlans { get; set; }

        [JsonPropertyName("ext_profile")]
        public bool? ExtProfile { get; set; }

        [JsonPropertyName("disable_self_service_change_password")]
        public bool? DisableSelfServiceChangePassword { get; set; }

        [JsonPropertyName("upstream_params")]
        public IDictionary? UpstreamParams { get; set; }

        [JsonPropertyName("set_user_root_attributes")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public SetUserRootAttributes? SetUserRootAttributes { get; set; }

        [JsonPropertyName("gateway_authentication")]
        public GatewayAuthentication? GatewayAuthentication { get; set; }

    }

}
