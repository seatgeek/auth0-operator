using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Core.Models.Connection
{

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ChallengeUi
    {

        [JsonStringEnumMemberName("both")]
        Both,

        [JsonStringEnumMemberName("autofill")]
        AutoFill,

        [JsonStringEnumMemberName("button")]
        Button

    }

}
