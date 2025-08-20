using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Core.Models.Client
{

    public partial class ClientFind
    {

        [JsonPropertyName("id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ClientId { get; set; }

    }

}
