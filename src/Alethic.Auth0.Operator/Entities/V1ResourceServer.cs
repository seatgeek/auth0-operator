using System.Text.Json.Serialization;

using Alethic.Auth0.Operator.Models.ResourceServer;

using k8s.Models;

using KubeOps.Abstractions.Entities;
using KubeOps.Abstractions.Entities.Attributes;

namespace Alethic.Auth0.Operator.Entities
{

    [EntityScope(EntityScope.Namespaced)]
    [KubernetesEntity(Group = "kubernetes.auth0.com", ApiVersion = "v1", Kind = "ResourceServer")]
    public partial class V1ResourceServer : CustomKubernetesEntity<V1ResourceServer.SpecDef, V1ResourceServer.StatusDef>
    {

        public class SpecDef
        {

            [JsonPropertyName("tenantRef")]
            [Required]
            public V1TenantRef? TenantRef { get; set; }

            [JsonPropertyName("conf")]
            public ResourceServerConf? Conf { get; set; }

        }

        public class StatusDef
        {

            [JsonPropertyName("id")]
            public string? Id { get; set; }

            [JsonPropertyName("lastConf")]
            public ResourceServerConf? LastConf { get; set; }

        }

    }

}
