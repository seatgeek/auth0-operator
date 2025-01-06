using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;

using Alethic.Auth0.Operator.Core.Models.Tenant;
using Alethic.Auth0.Operator.Models;

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

    [EntityRbac(typeof(V1Tenant), Verbs = RbacVerb.All)]
    [EntityRbac(typeof(V1Secret), Verbs = RbacVerb.List | RbacVerb.Get)]
    [EntityRbac(typeof(Eventsv1Event), Verbs = RbacVerb.All)]
    public class V1TenantController :
        V1Controller<V1Tenant, V1Tenant.SpecDef, V1Tenant.StatusDef, TenantConf>,
        IEntityController<V1Tenant>
    {

        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        /// <param name="kube"></param>
        /// <param name="requeue"></param>
        /// <param name="cache"></param>
        /// <param name="logger"></param>
        public V1TenantController(IKubernetesClient kube, EntityRequeue<V1Tenant> requeue, IMemoryCache cache, ILogger<V1ClientController> logger) :
            base(kube, requeue, cache, logger)
        {

        }

        /// <inheritdoc />
        protected override string EntityTypeName => "Tenant";

        /// <inheritdoc />
        protected override async Task Reconcile(V1Tenant entity, CancellationToken cancellationToken)
        {
            var api = await GetTenantApiClientAsync(entity, cancellationToken);
            if (api == null)
                throw new InvalidOperationException($"{EntityTypeName} {entity.Namespace()}:{entity.Name()} failed to retrieve API client.");

            // update specified configuration
            if (entity.Spec.Conf is { } conf)
                await api.TenantSettings.UpdateAsync(TransformToNewtonsoftJson<TenantConf, TenantSettingsUpdateRequest>(conf), cancellationToken);

            // retrieve and copy applied settings to status
            var settings = await api.TenantSettings.GetAsync(cancellationToken: cancellationToken);
            entity.Status.LastConf = TransformToSystemTextJson<TenantSettings, IDictionary>(settings);
            entity = await Kube.UpdateStatusAsync(entity, cancellationToken);

            Logger.LogInformation("Reconciled {EntityTypeName} {Namespace}/{Name}.", EntityTypeName, entity.Namespace(), entity.Name());
            await ReconcileSuccessAsync(entity, cancellationToken);
        }

        /// <inheritdoc />
        public override Task DeletedAsync(V1Tenant entity, CancellationToken cancellationToken)
        {
            Logger.LogWarning("Unsupported operation deleting entity {Entity}.", entity);
            return Task.CompletedTask;
        }

    }

}
