using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Alethic.Auth0.Operator.Core.Models.ClientGrant;
using Alethic.Auth0.Operator.Extensions;
using Alethic.Auth0.Operator.Helpers;
using Alethic.Auth0.Operator.Models;
using Alethic.Auth0.Operator.Options;

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
    [EntityRbac(typeof(V1Client), Verbs = RbacVerb.List | RbacVerb.Get)]
    [EntityRbac(typeof(V1ResourceServer), Verbs = RbacVerb.List | RbacVerb.Get)]
    [EntityRbac(typeof(V1ClientGrant), Verbs = RbacVerb.All)]
    [EntityRbac(typeof(Eventsv1Event), Verbs = RbacVerb.Create)]
    public class V1ClientGrantController :
        V1TenantEntityController<V1ClientGrant, V1ClientGrant.SpecDef, V1ClientGrant.StatusDef, ClientGrantConf>,
        IEntityController<V1ClientGrant>
    {

        readonly IMemoryCache _clientGrantCache;

        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        /// <param name="kube"></param>
        /// <param name="requeue"></param>
        /// <param name="cache"></param>
        /// <param name="logger"></param>
        /// <param name="options"></param>
        public V1ClientGrantController(IKubernetesClient kube, EntityRequeue<V1ClientGrant> requeue, IMemoryCache cache, ILogger<V1ClientGrantController> logger, IOptions<OperatorOptions> options) :
            base(kube, requeue, cache, logger, options)
        {
            _clientGrantCache = cache;
        }

        /// <inheritdoc />
        protected override string EntityTypeName => "A0ClientGrant";

        /// <inheritdoc />
        protected override async Task<Hashtable?> Get(IManagementApiClient api, string id, string defaultNamespace, CancellationToken cancellationToken)
        {
            try
            {
                Logger.LogInformationJson($"{EntityTypeName} fetching client grant from Auth0 with ID {id}", new {
                    entityTypeName = EntityTypeName,
                    clientGrantId = id,
                    operation = "fetch"
                });
                var list = await GetAllClientGrantsWithPagination(api, cancellationToken);
                var self = list.FirstOrDefault(i => i.Id == id);
                if (self == null)
                {
                    Logger.LogWarningJson($"{EntityTypeName} client grant with ID {id} not found in Auth0", new {
                        entityTypeName = EntityTypeName,
                        clientGrantId = id,
                        status = "not_found"
                    });
                    return null;
                }

                Logger.LogInformationJson($"{EntityTypeName} successfully retrieved client grant from Auth0 with ID {id}", new {
                    entityTypeName = EntityTypeName,
                    clientGrantId = id,
                    operation = "fetch",
                    status = "success"
                });
                return TransformToSystemTextJson<Hashtable>(self);
            }
            catch (Exception e)
            {
                Logger.LogErrorJson($"Error retrieving {EntityTypeName} with ID {id}: {e.Message}", new {
                    entityTypeName = EntityTypeName,
                    clientGrantId = id,
                    operation = "fetch",
                    errorMessage = e.Message,
                    status = "error"
                }, e);
                throw;
            }
        }

        /// <inheritdoc />
        protected override async Task<string?> Find(IManagementApiClient api, V1ClientGrant entity, V1ClientGrant.SpecDef spec, string defaultNamespace, CancellationToken cancellationToken)
        {
            var conf = spec.Init ?? spec.Conf;
            if (conf is null)
            {
                Logger.LogInformationJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} no configuration available for find operation", new {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name(),
                    operation = "find",
                    configurationStatus = "not_available"
                });
                return null;
            }

            if (conf.ClientRef is null)
            {
                Logger.LogErrorJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} missing required ClientRef", new {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name(),
                    missingField = "ClientRef",
                    operation = "find"
                });
                throw new InvalidOperationException("ClientRef is required.");
            }

            Logger.LogInformationJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} resolving client reference for find operation", new {
                entityTypeName = EntityTypeName,
                entityNamespace = entity.Namespace(),
                entityName = entity.Name(),
                operation = "resolve_client_ref"
            });
            var clientId = await ResolveClientRefToId(api, conf.ClientRef, defaultNamespace, cancellationToken);
            if (string.IsNullOrWhiteSpace(clientId))
            {
                Logger.LogErrorJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} failed to resolve ClientRef to client ID", new {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name(),
                    operation = "resolve_client_ref",
                    status = "failed"
                });
                throw new InvalidOperationException();
            }

            if (conf.Audience is null)
            {
                Logger.LogErrorJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} missing required Audience", new {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name(),
                    missingField = "Audience",
                    operation = "find"
                });
                throw new InvalidOperationException("Audience is required.");
            }

            Logger.LogInformationJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} resolving audience reference for find operation", new {
                entityTypeName = EntityTypeName,
                entityNamespace = entity.Namespace(),
                entityName = entity.Name(),
                operation = "resolve_audience_ref"
            });
            var audience = await ResolveResourceServerRefToIdentifier(api, conf.Audience, defaultNamespace, cancellationToken);
            if (string.IsNullOrWhiteSpace(audience))
            {
                Logger.LogErrorJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} failed to resolve Audience to resource server identifier", new {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name(),
                    operation = "resolve_audience_ref",
                    status = "failed"
                });
                throw new InvalidOperationException();
            }

            Logger.LogInformationJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} searching Auth0 for client grant with ClientId {clientId} and Audience {audience}", new {
                entityTypeName = EntityTypeName,
                entityNamespace = entity.Namespace(),
                entityName = entity.Name(),
                clientId = clientId,
                audience = audience,
                operation = "search_auth0"
            });
            var list = await GetAllClientGrantsWithPagination(api, cancellationToken);
            var result = list.Where(i => i.ClientId == clientId && i.Audience == audience).Select(i => i.Id).FirstOrDefault();
            
            if (result != null)
            {
                Logger.LogInformationJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} found existing client grant with ID {result}", new {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name(),
                    clientGrantId = result,
                    operation = "search_auth0",
                    status = "found"
                });
            }
            else
            {
                Logger.LogWarningJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} no existing client grant found for ClientId {clientId} and Audience {audience}", new {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name(),
                    clientId = clientId,
                    audience = audience,
                    operation = "search_auth0",
                    status = "not_found"
                });
            }
            
            return result;
        }

        /// <inheritdoc />
        protected override string? ValidateCreate(ClientGrantConf conf)
        {
            if (conf.ClientRef is null)
                return "missing a value for ClientRef";
            if (conf.Audience is null)
                return "missing a value for Audience";
            if (conf.Scope is null)
                return "missing a value for Scope";

            return null;
        }

        /// <inheritdoc />
        protected override async Task<string> Create(IManagementApiClient api, ClientGrantConf conf, string defaultNamespace, CancellationToken cancellationToken)
        {
            var req = new ClientGrantCreateRequest();
            req.ClientId = await ResolveClientRefToId(api, conf.ClientRef, defaultNamespace, cancellationToken);
            req.Audience = await ResolveResourceServerRefToIdentifier(api, conf.Audience, defaultNamespace, cancellationToken);
            req.Scope = conf.Scope?.ToList();
            req.AllowAnyOrganization = conf.AllowAnyOrganization;
            req.OrganizationUsage = Convert(conf.OrganizationUsage);

            Logger.LogInformationJson($"{EntityTypeName} creating client grant in Auth0 for ClientId {req.ClientId} and Audience {req.Audience}", new {
                entityTypeName = EntityTypeName,
                clientId = req.ClientId,
                audience = req.Audience,
                operation = "create"
            });
            try
            {
                LogAuth0ApiCall($"Creating Auth0 client grant", Auth0ApiCallType.Write, "A0ClientGrant", "unknown", "unknown", "create_client_grant");
                var self = await api.ClientGrants.CreateAsync(req, cancellationToken);
                if (self is null)
                {
                    Logger.LogErrorJson($"{EntityTypeName} failed to create client grant in Auth0 - API returned null", new {
                        entityTypeName = EntityTypeName,
                        operation = "create",
                        error = "api_returned_null"
                    });
                    throw new InvalidOperationException();
                }

                Logger.LogInformationJson($"{EntityTypeName} successfully created client grant in Auth0 with ID {self.Id}", new {
                    entityTypeName = EntityTypeName,
                    clientGrantId = self.Id,
                    operation = "create",
                    status = "success"
                });
                return self.Id;
            }
            catch (Exception ex)
            {
                Logger.LogErrorJson($"{EntityTypeName} failed to create client grant in Auth0 for ClientId {req.ClientId} and Audience {req.Audience}: {ex.Message}", new {
                    entityTypeName = EntityTypeName,
                    clientId = req.ClientId,
                    audience = req.Audience,
                    operation = "create",
                    errorMessage = ex.Message,
                    status = "failed"
                }, ex);
                throw;
            }
        }

        /// <inheritdoc />
        protected override async Task Update(IManagementApiClient api, string id, Hashtable? last, ClientGrantConf conf, string defaultNamespace, CancellationToken cancellationToken)
        {
            var req = new ClientGrantUpdateRequest();
            req.Scope = conf.Scope?.ToList();
            req.AllowAnyOrganization = conf.AllowAnyOrganization;
            req.OrganizationUsage = Convert(conf.OrganizationUsage);

            Logger.LogInformationJson($"{EntityTypeName} updating client grant in Auth0 with ID {id}", new {
                entityTypeName = EntityTypeName,
                clientGrantId = id,
                operation = "update"
            });
            try
            {
                LogAuth0ApiCall($"Updating Auth0 client grant with ID: {id}", Auth0ApiCallType.Write, "A0ClientGrant", "unknown", "unknown", "update_client_grant");
                await api.ClientGrants.UpdateAsync(id, req, cancellationToken);
                Logger.LogInformationJson($"{EntityTypeName} successfully updated client grant in Auth0 with ID {id}", new {
                    entityTypeName = EntityTypeName,
                    clientGrantId = id,
                    operation = "update",
                    status = "success"
                });
            }
            catch (Exception ex)
            {
                Logger.LogErrorJson($"{EntityTypeName} failed to update client grant in Auth0 with ID {id}: {ex.Message}", new {
                    entityTypeName = EntityTypeName,
                    clientGrantId = id,
                    operation = "update",
                    errorMessage = ex.Message,
                    status = "failed"
                }, ex);
                throw;
            }
        }

        /// <inheritdoc />
        protected override async Task Delete(IManagementApiClient api, string id, CancellationToken cancellationToken)
        {
            Logger.LogInformationJson($"{EntityTypeName} deleting client grant from Auth0 with ID {id}", new {
                entityTypeName = EntityTypeName,
                clientGrantId = id,
                operation = "delete"
            });
            try
            {
                LogAuth0ApiCall($"Deleting Auth0 client grant with ID: {id}", Auth0ApiCallType.Write, "A0ClientGrant", id, "unknown", "delete_client_grant");
                await api.ClientGrants.DeleteAsync(id, cancellationToken);
                Logger.LogInformationJson($"{EntityTypeName} successfully deleted client grant from Auth0 with ID {id}", new {
                    entityTypeName = EntityTypeName,
                    clientGrantId = id,
                    operation = "delete",
                    status = "success"
                });
            }
            catch (Exception ex)
            {
                Logger.LogErrorJson($"{EntityTypeName} failed to delete client grant from Auth0 with ID {id}: {ex.Message}", new {
                    entityTypeName = EntityTypeName,
                    clientGrantId = id,
                    operation = "delete",
                    errorMessage = ex.Message,
                    status = "failed"
                }, ex);
                throw;
            }
        }

        /// <summary>
        /// Converts a from a local model type to a request type.
        /// </summary>
        /// <param name="organizationUsage"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        global::Auth0.ManagementApi.Models.OrganizationUsage? Convert(global::Alethic.Auth0.Operator.Core.Models.OrganizationUsage? organizationUsage)
        {
            return organizationUsage switch
            {
                Core.Models.OrganizationUsage.Deny => global::Auth0.ManagementApi.Models.OrganizationUsage.Deny,
                Core.Models.OrganizationUsage.Allow => global::Auth0.ManagementApi.Models.OrganizationUsage.Allow,
                Core.Models.OrganizationUsage.Require => global::Auth0.ManagementApi.Models.OrganizationUsage.Require,
                null => null,
                _ => throw new InvalidOperationException(),
            };
        }

        /// <summary>
        /// Retrieves all Auth0 client grants across all pages using pagination with caching.
        /// </summary>
        /// <param name="api">Auth0 Management API client</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Complete list of all client grants</returns>
        private async Task<List<ClientGrant>> GetAllClientGrantsWithPagination(IManagementApiClient api, CancellationToken cancellationToken)
        {
            return await Auth0PaginationHelper.GetAllWithPaginationAsync(
                _clientGrantCache,
                Logger,
                api,
                new GetClientGrantsRequest(),
                api.ClientGrants.GetAllAsync,
                "client_grants",
                cancellationToken);
        }

    }

}
