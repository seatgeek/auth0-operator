using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Alethic.Auth0.Operator.Core.Models.ResourceServer;
using Alethic.Auth0.Operator.Extensions;
using Alethic.Auth0.Operator.Helpers;
using Alethic.Auth0.Operator.Models;
using Alethic.Auth0.Operator.Options;
using Alethic.Auth0.Operator.Services;

using Auth0.ManagementApi;
using Auth0.ManagementApi.Models;
using Auth0.ManagementApi.Paging;

using k8s.Models;

using KubeOps.Abstractions.Controller;
using KubeOps.Abstractions.Queue;
using KubeOps.Abstractions.Rbac;
using KubeOps.KubernetesClient;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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

        readonly IMemoryCache _resourceServerCache;

        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        /// <param name="kube"></param>
        /// <param name="requeue"></param>
        /// <param name="cache"></param>
        /// <param name="logger"></param>
        /// <param name="options"></param>
        public V1ResourceServerController(IKubernetesClient kube, EntityRequeue<V1ResourceServer> requeue, IMemoryCache cache, ILogger<V1ResourceServerController> logger, IOptions<OperatorOptions> options) :
            base(kube, requeue, cache, logger, options)
        {
            _resourceServerCache = cache;
        }

        /// <inheritdoc />
        protected override string EntityTypeName => "A0ResourceServer";

        /// <inheritdoc />
        protected override async Task<Hashtable?> Get(IManagementApiClient api, string id, string defaultNamespace, CancellationToken cancellationToken)
        {
            try
            {
                Logger.LogInformationJson($"{EntityTypeName} fetching resource server from Auth0 with ID {id}", new
                {
                    entityTypeName = EntityTypeName,
                    resourceServerId = id,
                    operation = "fetch"
                });
                LogAuth0ApiCall($"Getting Auth0 resource server with ID: {id}", Auth0ApiCallType.Read, "A0ResourceServer", id, defaultNamespace, "retrieve_resource_server_by_id");
                var result = await api.ResourceServers.GetAsync(id, cancellationToken: cancellationToken);
                Logger.LogInformationJson($"{EntityTypeName} successfully retrieved resource server from Auth0 with ID {id}", new
                {
                    entityTypeName = EntityTypeName,
                    resourceServerId = id,
                    operation = "fetch",
                    status = "success"
                });
                return TransformToSystemTextJson<Hashtable>(result);
            }
            catch (Exception e)
            {
                Logger.LogErrorJson($"Error retrieving {EntityTypeName} with ID {id}: {e.Message}", new
                {
                    entityTypeName = EntityTypeName,
                    resourceServerId = id,
                    operation = "fetch",
                    errorMessage = e.Message,
                    status = "error"
                }, e);
                throw;
            }
        }

        /// <inheritdoc />
        protected override async Task<string?> Find(IManagementApiClient api, V1ResourceServer entity, V1ResourceServer.SpecDef spec, string defaultNamespace, CancellationToken cancellationToken)
        {
            var conf = spec.Init ?? spec.Conf;
            if (conf is null)
            {
                Logger.LogInformationJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} no configuration available for find operation", new
                {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name(),
                    operation = "find",
                    configurationStatus = "not_available"
                });
                return null;
            }

            Logger.LogInformationJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} searching Auth0 for resource server with identifier {conf.Identifier}", new
            {
                entityTypeName = EntityTypeName,
                entityNamespace = entity.Namespace(),
                entityName = entity.Name(),
                identifier = conf.Identifier,
                operation = "search_by_identifier"
            });
            var list = await GetAllResourceServersWithPagination(api, entity, cancellationToken);
            var self = list.FirstOrDefault(i => string.Equals(i.Identifier, conf.Identifier, StringComparison.OrdinalIgnoreCase));

            if (self != null)
            {
                Logger.LogInformationJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} found existing resource server with identifier {conf.Identifier} and ID {self.Id}", new
                {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name(),
                    identifier = conf.Identifier,
                    resourceServerId = self.Id,
                    operation = "search_by_identifier",
                    status = "found"
                });
            }
            else
            {
                Logger.LogWarningJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} no existing resource server found with identifier {conf.Identifier}", new
                {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name(),
                    identifier = conf.Identifier,
                    operation = "search_by_identifier",
                    status = "not_found"
                });
            }

            return self?.Id;
        }

        /// <inheritdoc />
        protected override string? ValidateCreate(ResourceServerConf conf)
        {
            return null;
        }

        /// <inheritdoc />
        protected override async Task<string> Create(IManagementApiClient api, ResourceServerConf conf, string defaultNamespace, CancellationToken cancellationToken)
        {
            Logger.LogInformationJson($"{EntityTypeName} creating resource server in Auth0 with identifier {conf.Identifier} and name {conf.Name}", new
            {
                entityTypeName = EntityTypeName,
                identifier = conf.Identifier,
                resourceServerName = conf.Name,
                operation = "create"
            });
            try
            {
                LogAuth0ApiCall($"Creating Auth0 resource server with identifier: {conf.Identifier}", Auth0ApiCallType.Write, "A0ResourceServer", conf.Name ?? "unknown", "unknown", "create_resource_server");
                var self = await api.ResourceServers.CreateAsync(TransformToNewtonsoftJson<ResourceServerConf, ResourceServerCreateRequest>(conf), cancellationToken);
                Logger.LogInformationJson($"{EntityTypeName} successfully created resource server in Auth0 with ID {self.Id} and identifier {self.Identifier}", new
                {
                    entityTypeName = EntityTypeName,
                    resourceServerId = self.Id,
                    identifier = self.Identifier,
                    operation = "create",
                    status = "success"
                });
                return self.Id;
            }
            catch (Exception ex)
            {
                Logger.LogErrorJson($"{EntityTypeName} failed to create resource server in Auth0 with identifier {conf.Identifier} and name {conf.Name}: {ex.Message}", new
                {
                    entityTypeName = EntityTypeName,
                    identifier = conf.Identifier,
                    resourceServerName = conf.Name,
                    operation = "create",
                    errorMessage = ex.Message,
                    status = "failed"
                }, ex);
                throw;
            }
        }

        /// <inheritdoc />
        protected override async Task Update(IManagementApiClient api, string id, Hashtable? last, ResourceServerConf conf, List<string> driftingFields, string defaultNamespace, ITenantApiAccess tenantApiAccess, CancellationToken cancellationToken)
        {
            Logger.LogInformationJson($"{EntityTypeName} updating resource server in Auth0 with ID {id} and identifier {conf.Identifier}", new
            {
                entityTypeName = EntityTypeName,
                resourceServerId = id,
                identifier = conf.Identifier,
                operation = "update"
            });
            try
            {
                LogAuth0ApiCall($"Updating Auth0 resource server with ID: {id}", Auth0ApiCallType.Write, "A0ResourceServer", conf.Name ?? "unknown", "unknown", "update_resource_server");
                await api.ResourceServers.UpdateAsync(id, TransformToNewtonsoftJson<ResourceServerConf, ResourceServerUpdateRequest>(conf), cancellationToken);
                Logger.LogInformationJson($"{EntityTypeName} successfully updated resource server in Auth0 with ID {id}", new
                {
                    entityTypeName = EntityTypeName,
                    resourceServerId = id,
                    operation = "update",
                    status = "success"
                });
            }
            catch (Exception ex)
            {
                Logger.LogErrorJson($"{EntityTypeName} failed to update resource server in Auth0 with ID {id} and identifier {conf.Identifier}: {ex.Message}", new
                {
                    entityTypeName = EntityTypeName,
                    resourceServerId = id,
                    identifier = conf.Identifier,
                    operation = "update",
                    errorMessage = ex.Message,
                    status = "failed"
                }, ex);
                throw;
            }
        }

        /// <inheritdoc />
        protected override Task<bool> ApplyStatus(IManagementApiClient api, V1ResourceServer entity, Hashtable lastConf, string defaultNamespace, CancellationToken cancellationToken)
        {
            var identifier = (string?)lastConf["identifier"];
            if (string.IsNullOrWhiteSpace(identifier))
                throw new InvalidOperationException($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} has missing Identifier.");

            entity.Status.Identifier = identifier;
            return base.ApplyStatus(api, entity, lastConf, defaultNamespace, cancellationToken);
        }

        /// <inheritdoc />
        protected override async Task Delete(IManagementApiClient api, string id, CancellationToken cancellationToken)
        {
            Logger.LogInformationJson($"{EntityTypeName} deleting resource server from Auth0 with ID {id}", new
            {
                entityTypeName = EntityTypeName,
                resourceServerId = id,
                operation = "delete"
            });
            try
            {
                LogAuth0ApiCall($"Deleting Auth0 resource server with ID: {id}", Auth0ApiCallType.Write, "A0ResourceServer", id, "unknown", "delete_resource_server");
                await api.ResourceServers.DeleteAsync(id, cancellationToken);
                Logger.LogInformationJson($"{EntityTypeName} successfully deleted resource server from Auth0 with ID {id}", new
                {
                    entityTypeName = EntityTypeName,
                    resourceServerId = id,
                    operation = "delete",
                    status = "success"
                });
            }
            catch (Exception ex)
            {
                Logger.LogErrorJson($"{EntityTypeName} failed to delete resource server from Auth0 with ID {id}: {ex.Message}", new
                {
                    entityTypeName = EntityTypeName,
                    resourceServerId = id,
                    operation = "delete",
                    errorMessage = ex.Message,
                    status = "failed"
                }, ex);
                throw;
            }
        }

        /// <summary>
        /// Retrieves all Auth0 resource servers across all pages using pagination with caching.
        /// </summary>
        /// <param name="api">Auth0 Management API client</param>
        /// <param name="entity">Resource server entity for tenant domain extraction</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Complete list of all resource servers</returns>
        private async Task<List<ResourceServer>> GetAllResourceServersWithPagination(IManagementApiClient api, V1ResourceServer entity, CancellationToken cancellationToken)
        {
            var tenantDomain = await GetTenantDomainForCacheSalt(entity, cancellationToken);

            return await Auth0PaginationHelper.GetAllWithPaginationAsync(
                _resourceServerCache,
                Logger,
                new ResourceServerGetRequest(),
                api.ResourceServers.GetAllAsync,
                "resource_servers",
                tenantDomain,
                cancellationToken);
        }

    }

}
