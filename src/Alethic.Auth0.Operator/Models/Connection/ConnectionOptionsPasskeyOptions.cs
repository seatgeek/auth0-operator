using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Models.Connection
{

    public class ConnectionOptionsPasskeyOptions
    {

        [JsonPropertyName("challenge_ui")]
        public ChallengeUi? ChallengeUi { get; set; }

        [JsonPropertyName("progressive_enrollment_enabled")]
        public bool? ProgressiveEnrollmentEnabled { get; set; }

        [JsonPropertyName("local_enrollment_enabled")]
        public bool? LocalEnrollmentEnabled { get; set; }

    }

}