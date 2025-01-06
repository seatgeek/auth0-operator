using System.Text.Json.Serialization;

namespace Alethic.Auth0.Operator.Core.Models.Organization
{

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum OrganizationRequireBehavior
    {

        [JsonStringEnumMemberName("no_prompt")]
        NoPrompt,

        [JsonStringEnumMemberName("pre_login_prompt")]
        PreLoginPrompt,

        [JsonStringEnumMemberName("post_login_prompt")]
        PostLoginPrompt

    }

}
