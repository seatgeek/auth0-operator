using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Core.Models.Connection
{

    public partial class ConnectionFind
    {

        [JsonPropertyName("connectionId")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ConnectionId { get; set; }

    }

}
