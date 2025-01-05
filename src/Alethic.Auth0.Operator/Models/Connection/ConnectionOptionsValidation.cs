using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Models.Connection
{

    public class ConnectionOptionsValidation
    {

        [JsonPropertyName("username")]
        public ConnectionOptionsUserName? UserName { get; set; }

    }

}