using System.Collections;
using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Models.Connection
{

    public class ConnectionOptions
    {

        [JsonPropertyName("validation")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ConnectionOptionsValidation? Validation { get; set; }

        [JsonPropertyName("non_persistent_attrs")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string[]? NonPersistentAttributes { get; set; }

        [JsonPropertyName("precedence")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ConnectionOptionsPrecedence[]? Precedence { get; set; }

        [JsonPropertyName("attributes")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ConnectionOptionsAttributes? Attributes { get; set; }

        [JsonPropertyName("enable_script_context")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? EnableScriptContext { get; set; }

        [JsonPropertyName("enabledDatabaseCustomization")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? EnableDatabaseCustomization { get; set; }

        [JsonPropertyName("import_mode")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? ImportMode { get; set; }

        [JsonPropertyName("customScripts")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ConnectionOptionsCustomScripts? CustomScripts { get; set; }

        [JsonPropertyName("authentication_methods")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ConnectionOptionsAuthenticationMethods? AuthenticationMethods { get; set; }

        [JsonPropertyName("passkey_options")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ConnectionOptionsPasskeyOptions? PasskeyOptions { get; set; }

        [JsonPropertyName("passwordPolicy")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ConnectionOptionsPasswordPolicy? PasswordPolicy { get; set; }

        [JsonPropertyName("password_complexity_options")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ConnectionOptionsPasswordComplexityOptions? PasswordComplexityOptions { get; set; }

        [JsonPropertyName("password_history")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ConnectionOptionsPasswordHistory? PasswordHistory { get; set; }

        [JsonPropertyName("password_no_personal_info")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ConnectionOptionsPasswordNoPersonalInfo? PasswordNoPersonalInfo { get; set; }

        [JsonPropertyName("password_dictionary")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ConnectionOptionsPasswordDictionary? PasswordDictionary { get; set; }

        [JsonPropertyName("api_enable_users")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? ApiEnableUsers { get; set; }

        [JsonPropertyName("basic_profile")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? BasicProfile { get; set; }

        [JsonPropertyName("ext_admin")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? ExtAdmin { get; set; }

        [JsonPropertyName("ext_is_suspended")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? ExtIsSuspended { get; set; }

        [JsonPropertyName("ext_agreed_terms")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? ExtAgreedTerms { get; set; }

        [JsonPropertyName("ext_groups")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? ExtGroups { get; set; }

        [JsonPropertyName("ext_assigned_plans")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? ExtAssignedPlans { get; set; }

        [JsonPropertyName("ext_profile")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? ExtProfile { get; set; }

        [JsonPropertyName("disable_self_service_change_password")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? DisableSelfServiceChangePassword { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("upstream_params")]
        public IDictionary? UpstreamParams { get; set; }

        [JsonPropertyName("set_user_root_attributes")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public SetUserRootAttributes? SetUserRootAttributes { get; set; }

        [JsonPropertyName("gateway_authentication")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public GatewayAuthentication? GatewayAuthentication { get; set; }

    }

}
