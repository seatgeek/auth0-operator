using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Core.Models.Client
{

    public class Credentials
    {

        [JsonPropertyName("id")]
        public string? Id { get; set; }

    }

}
