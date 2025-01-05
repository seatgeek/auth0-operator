using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Models
{

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum Flows
    {

        [JsonStringEnumMemberName("client_credentials")]
        ClientCredentials

    }

}
