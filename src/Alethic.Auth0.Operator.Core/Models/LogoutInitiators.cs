using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Core.Models
{

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum LogoutInitiators
    {

        [JsonStringEnumMemberName("rp-logout")]
        RpLogout,

        [JsonStringEnumMemberName("idp-logout")]
        IdpLogout,

        [JsonStringEnumMemberName("password-changed")]
        PasswordChanged,

        [JsonStringEnumMemberName("session-expired")]
        SessionExpired

    }

}
