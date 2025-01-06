using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Alethic.Auth0.Operator.Entities;
using Alethic.Auth0.Operator.Models.ResourceServer;

using Auth0.ManagementApi;
using Auth0.ManagementApi.Models;

using k8s.Models;

using KubeOps.Abstractions.Controller;
using KubeOps.Abstractions.Rbac;
using KubeOps.KubernetesClient;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Alethic.Auth0.Operator.Controllers
{

    [EntityRbac(typeof(V1Tenant), Verbs = RbacVerb.List | RbacVerb.Get)]
    [EntityRbac(typeof(V1ResourceServer), Verbs = RbacVerb.All)]
    [EntityRbac(typeof(V1Secret), Verbs = RbacVerb.List | RbacVerb.Get)]
    [EntityRbac(typeof(Eventsv1Event), Verbs = RbacVerb.All)]
    public class V1ResourceServerController :
        V1TenantEntityController<V1ResourceServer, V1ResourceServer.SpecDef, V1ResourceServer.StatusDef, ResourceServerConf>,
        IEntityController<V1ResourceServer>
    {

        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        /// <param name="cache"></param>
        /// <param name="kube"></param>
        /// <param name="logger"></param>
        public V1ResourceServerController(IMemoryCache cache, IKubernetesClient kube, ILogger<V1ClientController> logger) :
            base(cache, kube, logger)
        {

        }

        /// <inheritdoc />
        protected override string EntityTypeName => "ResourceServer";

        /// <inheritdoc />
        protected override async Task<ResourceServerConf?> GetApi(IManagementApiClient api, string id, CancellationToken cancellationToken)
        {
            return TransformToSystemTextJson<ResourceServer, ResourceServerConf>(await api.ResourceServers.GetAsync(id, cancellationToken: cancellationToken));
        }

        /// <inheritdoc />
        protected override async Task<string?> FindApi(IManagementApiClient api, ResourceServerConf conf, CancellationToken cancellationToken)
        {
            var list = await api.ResourceServers.GetAllAsync(new ResourceServerGetRequest() { }, cancellationToken: cancellationToken);
            var self = list.FirstOrDefault(i => i.Identifier == conf.Identifier);
            return self?.Id;
        }

        /// <inheritdoc />
        protected override string? ValidateCreateConf(ResourceServerConf conf)
        {
            return null;
        }

        /// <inheritdoc />
        protected override async Task<string> CreateApi(IManagementApiClient api, ResourceServerConf conf, CancellationToken cancellationToken)
        {
            var self = await api.ResourceServers.CreateAsync(TransformToNewtonsoftJson<ResourceServerConf, ResourceServerCreateRequest>(conf), cancellationToken);
            return self.Id;
        }

        /// <inheritdoc />
        protected override async Task UpdateApi(IManagementApiClient api, string id, ResourceServerConf conf, CancellationToken cancellationToken)
        {
            await api.ResourceServers.UpdateAsync(id, TransformToNewtonsoftJson<ResourceServerConf, ResourceServerUpdateRequest>(conf), cancellationToken);
        }

        /// <inheritdoc />
        protected override Task DeleteApi(IManagementApiClient api, string id, CancellationToken cancellationToken)
        {
            return api.ResourceServers.DeleteAsync(id, cancellationToken);
        }

    }

}
