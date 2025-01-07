using System.Collections;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Alethic.Auth0.Operator.Core.Models.Client;
using Alethic.Auth0.Operator.Models;

using Auth0.ManagementApi;
using Auth0.ManagementApi.Models;

using k8s.Models;

using KubeOps.Abstractions.Controller;
using KubeOps.Abstractions.Queue;
using KubeOps.Abstractions.Rbac;
using KubeOps.KubernetesClient;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Alethic.Auth0.Operator.Controllers
{

    [EntityRbac(typeof(V1Tenant), Verbs = RbacVerb.List | RbacVerb.Get)]
    [EntityRbac(typeof(V1Client), Verbs = RbacVerb.All)]
    [EntityRbac(typeof(V1Secret), Verbs = RbacVerb.List | RbacVerb.Get)]
    [EntityRbac(typeof(Eventsv1Event), Verbs = RbacVerb.All)]
    public class V1ClientController :
        V1TenantEntityController<V1Client, V1Client.SpecDef, V1Client.StatusDef, ClientConf>,
        IEntityController<V1Client>
    {

        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        /// <param name="kube"></param>
        /// <param name="requeue"></param>
        /// <param name="cache"></param>
        /// <param name="logger"></param>
        public V1ClientController(IKubernetesClient kube, EntityRequeue<V1Client> requeue, IMemoryCache cache, ILogger<V1ClientController> logger) :
            base(kube, requeue, cache, logger)
        {

        }

        /// <inheritdoc />
        protected override string EntityTypeName => "Client";

        /// <inheritdoc />
        protected override async Task<IDictionary?> GetApi(IManagementApiClient api, string id, string defaultNamespace, CancellationToken cancellationToken)
        {
            return TransformToSystemTextJson<Client, IDictionary>(await api.Clients.GetAsync(id, cancellationToken: cancellationToken));
        }

        /// <inheritdoc />
        protected override async Task<string?> FindApi(IManagementApiClient api, ClientConf conf, string defaultNamespace, CancellationToken cancellationToken)
        {
            var list = await api.Clients.GetAllAsync(new GetClientsRequest() { Fields = "client_id,name" }, cancellationToken: cancellationToken);
            var self = list.FirstOrDefault(i => i.Name == conf.Name);
            return self?.ClientId;
        }

        /// <inheritdoc />
        protected override string? ValidateCreateConf(ClientConf conf)
        {
            if (conf.ApplicationType == null)
                return "missing a value for application type";

            return null;
        }

        /// <inheritdoc />
        protected override async Task<string> CreateApi(IManagementApiClient api, ClientConf conf, string defaultNamespace, CancellationToken cancellationToken)
        {
            var self = await api.Clients.CreateAsync(TransformToNewtonsoftJson<ClientConf, ClientCreateRequest>(conf), cancellationToken);
            return self.ClientId;
        }

        /// <inheritdoc />
        protected override async Task UpdateApi(IManagementApiClient api, string id, ClientConf conf, string defaultNamespace, CancellationToken cancellationToken)
        {
            await api.Clients.UpdateAsync(id, TransformToNewtonsoftJson<ClientConf, ClientUpdateRequest>(conf), cancellationToken);
        }

        /// <inheritdoc />
        protected override Task DeleteApi(IManagementApiClient api, string id, CancellationToken cancellationToken)
        {
            return api.Clients.DeleteAsync(id, cancellationToken);
        }

    }

}
