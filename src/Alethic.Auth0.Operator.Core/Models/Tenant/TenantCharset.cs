using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Core.Models.Tenant
{

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum TenantCharset
    {

        [JsonStringEnumMemberName("base20")]
        Base20,

        [JsonStringEnumMemberName("digits")]
        Digits

    }

}
