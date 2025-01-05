using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Models.Client
{

    public class ClientAuthenticationMethods
    {

        public class CredentialIdDef
        {

            [JsonPropertyName("id")]
            public string? Id { get; set; }

        }

        public class PrivateKeyJwtDef
        {

            [JsonPropertyName("credentials")]
            public IList<CredentialIdDef>? Credentials { get; set; }

        }

        public class TlsClientAuthDef
        {

            [JsonPropertyName("credentials")]
            public IList<CredentialIdDef>? Credentials { get; set; }

        }
        public class SelfSignedTlsClientAuthDef
        {

            [JsonPropertyName("credentials")]
            public IList<CredentialIdDef>? Credentials { get; set; }

        }

        [JsonPropertyName("private_key_jwt")]
        public PrivateKeyJwtDef? PrivateKeyJwt { get; set; }

        [JsonPropertyName("tls_client_auth")]
        public TlsClientAuthDef? TlsClientAuth { get; set; }

        [JsonPropertyName("self_signed_tls_client_auth")]
        public SelfSignedTlsClientAuthDef? SelfSignedTlsClientAuth { get; set; }

    }

}
