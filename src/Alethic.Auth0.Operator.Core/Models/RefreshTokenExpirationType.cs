using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Core.Models
{

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum RefreshTokenExpirationType
    {

        [JsonStringEnumMemberName("expiring")]
        Expiring,

        [JsonStringEnumMemberName("non-expiring")]
        NonExpiring

    }

}
