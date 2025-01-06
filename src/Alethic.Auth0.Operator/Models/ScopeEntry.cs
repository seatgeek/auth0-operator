using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Models
{

    public class ScopeEntry
    {

        [JsonPropertyName("actions")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string[]? Actions { get; set; }

    }

}
