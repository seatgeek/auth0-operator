using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Alethic.Auth0.Operator.Entities;
using Alethic.Auth0.Operator.Models.Connection;

using Auth0.ManagementApi;
using Auth0.ManagementApi.Models;
using Auth0.ManagementApi.Paging;

using k8s.Models;

using KubeOps.Abstractions.Controller;
using KubeOps.Abstractions.Rbac;
using KubeOps.KubernetesClient;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Alethic.Auth0.Operator.Controllers
{

    [EntityRbac(typeof(V1Tenant), Verbs = RbacVerb.List | RbacVerb.Get)]
    [EntityRbac(typeof(V1Connection), Verbs = RbacVerb.All)]
    [EntityRbac(typeof(V1Secret), Verbs = RbacVerb.List | RbacVerb.Get)]
    [EntityRbac(typeof(Eventsv1Event), Verbs = RbacVerb.All)]
    public class V1ConnectionController :
        V1TenantEntityController<V1Connection, V1Connection.SpecDef, V1Connection.StatusDef, ConnectionConf>,
        IEntityController<V1Connection>
    {

        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        /// <param name="cache"></param>
        /// <param name="kube"></param>
        /// <param name="logger"></param>
        public V1ConnectionController(IMemoryCache cache, IKubernetesClient kube, ILogger<V1ClientController> logger) :
            base(cache, kube, logger)
        {

        }

        /// <inheritdoc />
        protected override string EntityTypeName => "Connection";

        /// <inheritdoc />
        protected override async Task<ConnectionConf?> GetApi(IManagementApiClient api, string id, CancellationToken cancellationToken)
        {
            return TransformToSystemTextJson<Connection, ConnectionConf>(await api.Connections.GetAsync(id, cancellationToken: cancellationToken));
        }

        /// <inheritdoc />
        protected override async Task<string?> FindApi(IManagementApiClient api, ConnectionConf conf, CancellationToken cancellationToken)
        {
            var list = await api.Connections.GetAllAsync(new GetConnectionsRequest() { Fields = "id,name" }, pagination: (PaginationInfo?)null, cancellationToken: cancellationToken);
            var self = list.FirstOrDefault(i => i.Name == conf.Name);
            return self?.Id;
        }

        /// <inheritdoc />
        protected override string? ValidateCreateConf(ConnectionConf conf)
        {
            return null;
        }

        /// <inheritdoc />
        protected override async Task<string> CreateApi(IManagementApiClient api, ConnectionConf conf, CancellationToken cancellationToken)
        {
            var self = await api.Connections.CreateAsync(TransformToNewtonsoftJson<ConnectionConf, ConnectionCreateRequest>(conf), cancellationToken);
            return self.Id;
        }

        /// <inheritdoc />
        protected override async Task UpdateApi(IManagementApiClient api, string id, ConnectionConf conf, CancellationToken cancellationToken)
        {
            await api.Connections.UpdateAsync(id, TransformToNewtonsoftJson<ConnectionConf, ConnectionUpdateRequest>(conf), cancellationToken);
        }

        /// <inheritdoc />
        protected override Task DeleteApi(IManagementApiClient api, string id, CancellationToken cancellationToken)
        {
            return api.Connections.DeleteAsync(id, cancellationToken);
        }

    }

}
