using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Models.ResourceServer
{

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ConsentPolicy
    {

        [JsonPropertyName("transactional-authorization-with-mfa")]
        TransactionalAuthorizationWithMfa,

    }

}
