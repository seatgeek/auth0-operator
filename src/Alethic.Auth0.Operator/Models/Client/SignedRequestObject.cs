using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Models.Client
{

    public class SignedRequestObject
    {

        [JsonPropertyName("required")]
        public bool? Required { get; set; }

        [JsonPropertyName("credentials")]
        public IList<Credentials>? Credentials { get; set; }

    }

}
