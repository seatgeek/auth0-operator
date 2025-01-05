using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Entities
{

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum TenantCharset
    {

        [EnumMember(Value = "base20")]
        Base20,

        [EnumMember(Value = "digits")]
        Digits

    }

}
