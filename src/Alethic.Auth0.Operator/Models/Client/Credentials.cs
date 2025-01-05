using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Models.Client
{

    public class Credentials
    {

        [JsonPropertyName("id")]
        public string? Id { get; set; }

    }

}
