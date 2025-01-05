using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Models
{

    public class Mobile
    {

        public class MobileAndroid
        {

            [JsonPropertyName("app_package_name")]
            public string? AppPackageName { get; set; }

            [JsonPropertyName("keystore_hash")]
            public string? KeystoreHash { get; set; }

        }

        public class MobileIos
        {

            [JsonPropertyName("app_bundle_identifier")]
            public string? AppBundleIdentifier { get; set; }

            [JsonPropertyName("team_id")]
            public string? TeamId { get; set; }

        }

        [JsonPropertyName("android")]
        public MobileAndroid? Android { get; set; }

        [JsonPropertyName("ios")]
        public MobileIos? Ios { get; set; }

    }

}
