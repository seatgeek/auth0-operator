using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Models.ResourceServer
{

    public partial class ResourceServerConf
    {

        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("identifier")]
        public string? Identifier { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("scopes")]
        public List<ResourceServerScope>? Scopes { get; set; }

        [JsonPropertyName("signing_alg")]
        public SigningAlgorithm? SigningAlgorithm { get; set; }

        [JsonPropertyName("signing_secret")]
        public string? SigningSecret { get; set; }

        [JsonPropertyName("token_lifetime")]
        public int? TokenLifetime { get; set; }

        [JsonPropertyName("token_lifetime_for_web")]
        public int? TokenLifetimeForWeb { get; set; }

        [JsonPropertyName("allow_offline_access")]
        public bool? AllowOfflineAccess { get; set; }

        [JsonPropertyName("skip_consent_for_verifiable_first_party_clients")]
        public bool? SkipConsentForVerifiableFirstPartyClients { get; set; }

        [JsonPropertyName("verificationLocation")]
        public string? VerificationLocation { get; set; }

        [JsonPropertyName("token_dialect")]
        public TokenDialect? TokenDialect { get; set; }

        [JsonPropertyName("enforce_policies")]
        public bool? EnforcePolicies { get; set; }

        [JsonPropertyName("consent_policy")]
        public ConsentPolicy? ConsentPolicy { get; set; }

        [JsonPropertyName("authorization_details")]
        public IList<ResourceServerAuthorizationDetail>? AuthorizationDetails { get; set; }

        [JsonPropertyName("token_encryption")]
        public TokenEncryption? TokenEncryption { get; set; }

        [JsonPropertyName("proof_of_possession")]
        public ProofOfPossession? ProofOfPossession { get; set; }

    }

}
