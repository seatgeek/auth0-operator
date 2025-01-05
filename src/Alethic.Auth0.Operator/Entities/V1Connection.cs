using System.Text.Json.Serialization;

using Alethic.Auth0.Operator.Models.Connection;

using k8s.Models;

using KubeOps.Abstractions.Entities;
using KubeOps.Abstractions.Entities.Attributes;

namespace Alethic.Auth0.Operator.Entities
{

    [EntityScope(EntityScope.Namespaced)]
    [KubernetesEntity(Group = "kubernetes.auth0.com", ApiVersion = "v1", Kind = "Connection")]
    [KubernetesEntityShortNames("a0con")]
    public partial class V1Connection : CustomKubernetesEntity<V1Connection.SpecDef, V1Connection.StatusDef>
    {

        public class SpecDef
        {

            [JsonPropertyName("tenantRef")]
            [Required]
            public V1TenantRef? TenantRef { get; set; }

            [JsonPropertyName("conf")]
            public ConnectionConf? Conf { get; set; }

        }

        public class StatusDef
        {

            [JsonPropertyName("id")]
            public string? Id { get; set; }

            [JsonPropertyName("lastConf")]
            public ConnectionConf? LastConf { get; set; }

        }

    }

}
