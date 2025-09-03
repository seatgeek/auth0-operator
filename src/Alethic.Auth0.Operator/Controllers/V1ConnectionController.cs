using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Alethic.Auth0.Operator.Core.Models;
using Alethic.Auth0.Operator.Core.Models.Connection;
using Alethic.Auth0.Operator.Extensions;
using Alethic.Auth0.Operator.Helpers;
using Alethic.Auth0.Operator.Models;
using Alethic.Auth0.Operator.Options;

using Auth0.Core.Exceptions;
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
    [EntityRbac(typeof(V1Connection), Verbs = RbacVerb.All)]
    [EntityRbac(typeof(V1Secret), Verbs = RbacVerb.List | RbacVerb.Get)]
    [EntityRbac(typeof(Eventsv1Event), Verbs = RbacVerb.All)]
    public class V1ConnectionController :
        V1TenantEntityController<V1Connection, V1Connection.SpecDef, V1Connection.StatusDef, ConnectionConf>,
        IEntityController<V1Connection>
    {

        readonly IMemoryCache _connectionCache;

        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        /// <param name="kube"></param>
        /// <param name="requeue"></param>
        /// <param name="cache"></param>
        /// <param name="logger"></param>
        /// <param name="options"></param>
        public V1ConnectionController(IKubernetesClient kube, EntityRequeue<V1Connection> requeue, IMemoryCache cache, ILogger<V1ConnectionController> logger, IOptions<OperatorOptions> options) :
            base(kube, requeue, cache, logger, options)
        {
            _connectionCache = cache;
        }

        /// <inheritdoc />
        protected override string EntityTypeName => "A0Connection";

        /// <inheritdoc />
        protected override async Task<Hashtable?> Get(IManagementApiClient api, string id, string defaultNamespace, CancellationToken cancellationToken)
        {
            try
            {
                Logger.LogInformationJson($"{EntityTypeName} fetching connection from Auth0 with ID {id}", new
                {
                    entityTypeName = EntityTypeName,
                    connectionId = id,
                    operation = "fetch"
                });
                LogAuth0ApiCall($"Getting Auth0 connection with ID: {id}", Auth0ApiCallType.Read, "A0Connection", id, defaultNamespace, "retrieve_connection_by_id");
                var self = await api.Connections.GetAsync(id, cancellationToken: cancellationToken);
                if (self == null)
                {
                    Logger.LogWarningJson($"{EntityTypeName} connection with ID {id} not found in Auth0", new
                    {
                        entityTypeName = EntityTypeName,
                        connectionId = id,
                        status = "not_found"
                    });
                    return null;
                }

                Logger.LogInformationJson($"{EntityTypeName} successfully retrieved connection from Auth0 with ID {id} and name {self.Name}", new
                {
                    entityTypeName = EntityTypeName,
                    connectionId = id,
                    connectionName = self.Name,
                    operation = "fetch",
                    status = "success"
                });
                var dict = new Hashtable();
                dict["id"] = self.Id;
                dict["name"] = self.Name;
                dict["display_name"] = self.DisplayName;
                dict["strategy"] = self.Strategy;
                dict["realms"] = self.Realms;
                dict["is_domain_connection"] = self.IsDomainConnection;
                dict["show_as_button"] = self.ShowAsButton;
                dict["provisioning_ticket_url"] = self.ProvisioningTicketUrl;
                // Transform Auth0's enabled_clients array to dictionary format for consistency
                var enabledClientsDict = new Dictionary<string, V1ClientReference>();
                if (self.EnabledClients != null && self.EnabledClients.Length > 0)
                {
                    foreach (var clientId in self.EnabledClients)
                    {
                        if (!string.IsNullOrEmpty(clientId))
                        {
                            enabledClientsDict[clientId] = new V1ClientReference { Id = clientId };
                        }
                    }
                }
                dict["enabled_clients"] = enabledClientsDict;
                dict["options"] = TransformToSystemTextJson<Hashtable?>(self.Options);
                dict["metadata"] = TransformToSystemTextJson<Hashtable?>(self.Metadata);
                return dict;
            }
            catch (ErrorApiException e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                Logger.LogWarningJson($"{EntityTypeName} connection with ID {id} not found in Auth0 (404)", new
                {
                    entityTypeName = EntityTypeName,
                    connectionId = id,
                    statusCode = 404,
                    status = "not_found"
                });
                return null;
            }
            catch (Exception e)
            {
                Logger.LogErrorJson($"Error retrieving {EntityTypeName} with ID {id}: {e.Message}", new
                {
                    entityTypeName = EntityTypeName,
                    connectionId = id,
                    operation = "fetch",
                    errorMessage = e.Message,
                    status = "error"
                }, e);
                throw;
            }
        }

        /// <inheritdoc />
        protected override async Task<string?> Find(IManagementApiClient api, V1Connection entity, V1Connection.SpecDef spec, string defaultNamespace, CancellationToken cancellationToken)
        {
            if (spec.Find is not null)
            {
                Logger.LogInformationJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} using find criteria for connection lookup", new
                {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name(),
                    operation = "find_using_criteria"
                });

                if (spec.Find.ConnectionId is string connectionId)
                {
                    Logger.LogInformationJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} searching Auth0 for connection with ID {connectionId}", new
                    {
                        entityTypeName = EntityTypeName,
                        entityNamespace = entity.Namespace(),
                        entityName = entity.Name(),
                        connectionId = connectionId,
                        operation = "search_by_id"
                    });
                    try
                    {
                        LogAuth0ApiCall($"Getting Auth0 connection by ID: {connectionId}", Auth0ApiCallType.Read, "A0Connection", entity.Name(), entity.Namespace(), "retrieve_connection_by_id_from_spec");
                        var connection = await api.Connections.GetAsync(connectionId, cancellationToken: cancellationToken);
                        Logger.LogInformationJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} found existing connection with ID {connectionId} and name {connection.Name}", new
                        {
                            entityTypeName = EntityTypeName,
                            entityNamespace = entity.Namespace(),
                            entityName = entity.Name(),
                            connectionId = connectionId,
                            connectionName = connection.Name,
                            operation = "search_by_id",
                            status = "found"
                        });
                        return connection.Id;
                    }
                    catch (ErrorApiException e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        Logger.LogWarningJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} could not find connection with ID {connectionId}", new
                        {
                            entityTypeName = EntityTypeName,
                            entityNamespace = entity.Namespace(),
                            entityName = entity.Name(),
                            connectionId = connectionId,
                            operation = "search_by_id",
                            status = "not_found"
                        });
                        return null;
                    }
                }

                Logger.LogInformationJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} no valid connection ID provided in find criteria", new
                {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name(),
                    operation = "find_criteria_validation",
                    issue = "no_valid_connection_id"
                });
                return null;
            }
            else
            {
                var conf = spec.Init ?? spec.Conf;
                if (conf is null || string.IsNullOrEmpty(conf.Name))
                {
                    Logger.LogInformationJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} no configuration or connection name available for find operation", new
                    {
                        entityTypeName = EntityTypeName,
                        entityNamespace = entity.Namespace(),
                        entityName = entity.Name(),
                        operation = "find_by_name",
                        issue = "no_configuration_or_name"
                    });
                    return null;
                }

                Logger.LogInformationJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} searching Auth0 for connection with name {conf.Name}", new
                {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name(),
                    connectionName = conf.Name,
                    operation = "search_by_name"
                });
                LogAuth0ApiCall($"Listing Auth0 connections to find by name: {conf.Name}", Auth0ApiCallType.Read, "A0Connection", entity.Name(), entity.Namespace(), "list_connections_by_name");
                var list = await GetAllConnectionsWithPagination(api, cancellationToken);
                var self = list.FirstOrDefault(i => i.Name == conf.Name);
                if (self is not null)
                {
                    Logger.LogInformationJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} found existing connection with name {conf.Name} and ID {self.Id}", new
                    {
                        entityTypeName = EntityTypeName,
                        entityNamespace = entity.Namespace(),
                        entityName = entity.Name(),
                        connectionName = conf.Name,
                        connectionId = self.Id,
                        operation = "search_by_name",
                        status = "found"
                    });
                }
                else
                {
                    Logger.LogWarningJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} no existing connection found with name {conf.Name}", new
                    {
                        entityTypeName = EntityTypeName,
                        entityNamespace = entity.Namespace(),
                        entityName = entity.Name(),
                        connectionName = conf.Name,
                        operation = "search_by_name",
                        status = "not_found"
                    });
                }
                return self?.Id;
            }
        }

        /// <inheritdoc />
        protected override string? ValidateCreate(ConnectionConf conf)
        {
            return null;
        }

        /// <summary>
        /// Attempts to resolve the dictionary of client references to client IDs for Auth0 API.
        /// </summary>
        /// <param name="refs"></param>
        /// <param name="defaultNamespace"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        async Task<string[]?> ResolveClientRefsToIds(IManagementApiClient api, Dictionary<string, V1ClientReference>? refs, string defaultNamespace, CancellationToken cancellationToken)
        {
            if (refs is null || refs.Count == 0)
                return Array.Empty<string>();

            var l = new List<string>(refs.Count);

            foreach (var kvp in refs)
            {
                var r = await ResolveClientRefToId(api, kvp.Value, defaultNamespace, cancellationToken);
                if (r is null)
                    throw new InvalidOperationException();

                l.Add(r);
            }

            return l.ToArray();
        }

        /// <inheritdoc />
        protected override async Task<string> Create(IManagementApiClient api, ConnectionConf conf, string defaultNamespace, CancellationToken cancellationToken)
        {
            Logger.LogInformationJson($"{EntityTypeName} creating connection in Auth0 with name: {conf.Name} and strategy: {conf.Strategy}", new
            {
                entityTypeName = EntityTypeName,
                connectionName = conf.Name,
                strategy = conf.Strategy,
                operation = "create"
            });
            try
            {
                var req = new ConnectionCreateRequest();
                await ApplyConfToRequest(api, req, conf, defaultNamespace, cancellationToken);
                req.Strategy = conf.Strategy;
                req.Options = conf.Strategy == "auth0" ? TransformToNewtonsoftJson<ConnectionOptions, global::Auth0.ManagementApi.Models.Connections.ConnectionOptions>(JsonSerializer.Deserialize<ConnectionOptions>(JsonSerializer.Serialize(conf.Options))) : conf.Options;

                LogAuth0ApiCall($"Creating Auth0 connection with name: {conf.Name}", Auth0ApiCallType.Write, "A0Connection", conf.Name ?? "unknown", "unknown", "create_connection");
                var self = await api.Connections.CreateAsync(req, cancellationToken);
                if (self is null)
                    throw new InvalidOperationException();

                Logger.LogInformationJson($"{EntityTypeName} successfully created connection in Auth0 with ID: {self.Id}, name: {conf.Name} and strategy: {conf.Strategy}", new
                {
                    entityTypeName = EntityTypeName,
                    connectionId = self.Id,
                    connectionName = conf.Name,
                    strategy = conf.Strategy,
                    operation = "create",
                    status = "success"
                });
                return self.Id;
            }
            catch (Exception ex)
            {
                Logger.LogErrorJson($"{EntityTypeName} failed to create connection in Auth0 with name: {conf.Name} and strategy: {conf.Strategy}: {ex.Message}", new
                {
                    entityTypeName = EntityTypeName,
                    connectionName = conf.Name,
                    strategy = conf.Strategy,
                    operation = "create",
                    errorMessage = ex.Message,
                    status = "failed"
                }, ex);
                throw;
            }
        }

        /// <inheritdoc />
        protected override async Task Update(IManagementApiClient api, string id, Hashtable? last, ConnectionConf conf, string defaultNamespace, CancellationToken cancellationToken)
        {
            Logger.LogInformationJson($"{EntityTypeName} updating connection in Auth0 with ID: {id}, name: {conf.Name} and strategy: {conf.Strategy}", new
            {
                entityTypeName = EntityTypeName,
                connectionId = id,
                connectionName = conf.Name,
                strategy = conf.Strategy,
                operation = "update"
            });
            try
            {
                var req = new ConnectionUpdateRequest();
                await ApplyConfToRequest(api, req, conf, defaultNamespace, cancellationToken);
                req.Name = null; // not allowed to be changed
                req.Options = conf.Strategy == "auth0" ? TransformToNewtonsoftJson<ConnectionOptions, global::Auth0.ManagementApi.Models.Connections.ConnectionOptions>(JsonSerializer.Deserialize<ConnectionOptions>(JsonSerializer.Serialize(conf.Options))) : conf.Options;

                // Handle metadata with special nulling logic (overriding what ApplyConfToRequest set)
                req.Metadata = conf.Metadata ?? new Hashtable();

                // explicitly "null out" missing metadata if previously present
                if (last is not null && last.ContainsKey("metadata") && last["metadata"] is Hashtable lastMetadata)
                {
                    if (req.Metadata == null)
                        req.Metadata = new Hashtable();

                    // Create a defensive copy of keys to avoid potential enumeration issues
                    var keysToProcess = lastMetadata.Keys.Cast<string>().ToList();
                    foreach (string key in keysToProcess)
                    {
                        if (conf.Metadata == null || !conf.Metadata.ContainsKey(key))
                            req.Metadata[key] = null; // setting null value deletes Auth0 client metadata but doesn't affect Auth0 connection metadata. Instead, it's signaling that a value was removed.
                    }
                }

                if (WasConnectionMetadataValuesRemoved(req.Metadata))
                {
                    Logger.LogWarningJson($"{EntityTypeName} connection metadata values were removed in Auth0 with ID: {id}. Need to reset the connection metadata object via a preliminary update with empty Metadata object and updated Metadata again.", new
                    {
                        entityTypeName = EntityTypeName,
                        connectionId = id,
                        connectionName = conf.Name,
                        operation = "reset_connection_metadata",
                        status = "warning",
                        metadata = req.Metadata
                    });
                    LogAuth0ApiCall($"Resetting Auth0 connection metadata with ID: {id}", Auth0ApiCallType.Write, "A0Connection", conf.Name ?? "unknown", "unknown", "reset_connection_metadata");
                    await api.Connections.UpdateAsync(id, new ConnectionUpdateRequest { Metadata = new Hashtable() }, cancellationToken);
                }

                LogAuth0ApiCall($"Updating Auth0 connection with ID: {id}", Auth0ApiCallType.Write, "A0Connection", conf.Name ?? "unknown", "unknown", "update_connection");
                await api.Connections.UpdateAsync(id, req, cancellationToken);
                Logger.LogInformationJson($"{EntityTypeName} successfully updated connection in Auth0 with ID: {id}, name: {conf.Name} and strategy: {conf.Strategy}", new
                {
                    entityTypeName = EntityTypeName,
                    connectionId = id,
                    connectionName = conf.Name,
                    strategy = conf.Strategy,
                    operation = "update",
                    status = "success"
                });
            }
            catch (Exception ex)
            {
                Logger.LogErrorJson($"{EntityTypeName} failed to update connection in Auth0 with ID: {id}, name: {conf.Name} and strategy: {conf.Strategy}: {ex.Message}", new
                {
                    entityTypeName = EntityTypeName,
                    connectionId = id,
                    connectionName = conf.Name,
                    strategy = conf.Strategy,
                    operation = "update",
                    errorMessage = ex.Message,
                    status = "failed"
                }, ex);
                throw;
            }
        }

        /// <summary>
        /// Applies the specified configuration to the request object.
        /// </summary>
        /// <param name="api"></param>
        /// <param name="req"></param>
        /// <param name="conf"></param>
        /// <param name="defaultNamespace"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        async Task ApplyConfToRequest(IManagementApiClient api, ConnectionBase req, ConnectionConf conf, string defaultNamespace, CancellationToken cancellationToken)
        {
            req.Name = conf.Name;
            req.DisplayName = conf.DisplayName;
            req.Metadata = conf.Metadata;
            req.Realms = conf.Realms;
            req.IsDomainConnection = conf.IsDomainConnection ?? false;
            req.ShowAsButton = conf.ShowAsButton;
            req.EnabledClients = conf.EnabledClients?.Select(c => c.Id).Where(id => !string.IsNullOrEmpty(id)).ToArray();
        }

        /// <inheritdoc />
        protected override async Task Delete(IManagementApiClient api, string id, CancellationToken cancellationToken)
        {
            Logger.LogInformationJson($"{EntityTypeName} deleting connection from Auth0 with ID: {id} (reason: Kubernetes entity deleted)", new
            {
                entityTypeName = EntityTypeName,
                connectionId = id,
                operation = "delete",
                reason = "kubernetes_entity_deleted"
            });
            try
            {
                LogAuth0ApiCall($"Deleting Auth0 connection with ID: {id}", Auth0ApiCallType.Write, "A0Connection", id, "unknown", "delete_connection");
                await api.Connections.DeleteAsync(id, cancellationToken);
                Logger.LogInformationJson($"{EntityTypeName} successfully deleted connection from Auth0 with ID: {id}", new
                {
                    entityTypeName = EntityTypeName,
                    connectionId = id,
                    operation = "delete",
                    status = "success"
                });
            }
            catch (Exception ex)
            {
                Logger.LogErrorJson($"{EntityTypeName} failed to delete connection from Auth0 with ID: {id}: {ex.Message}", new
                {
                    entityTypeName = EntityTypeName,
                    connectionId = id,
                    operation = "delete",
                    errorMessage = ex.Message,
                    status = "failed"
                }, ex);
                throw;
            }
        }

        /// <summary>
        /// Retrieves all Auth0 connections across all pages using pagination with caching.
        /// </summary>
        /// <param name="api">Auth0 Management API client</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Complete list of all connections</returns>
        private async Task<List<Connection>> GetAllConnectionsWithPagination(IManagementApiClient api, CancellationToken cancellationToken)
        {
            return await Auth0PaginationHelper.GetAllWithPaginationAsync(
                _connectionCache,
                Logger,
                api,
                new GetConnectionsRequest(),
                api.Connections.GetAllAsync,
                "connections",
                cancellationToken);
        }


        protected override string[] GetIncludedFields()
        {
            // Only compare these specific fields for drift detection
            return new[]
            {
                "display_name",
                "options",
                "metadata",
                "is_domain_connection",
                "show_as_button",
                "enabled_clients"
            };
        }

        protected override string[] GetExcludedFields()
        {
            // Exclude the userid_attribute field from options during comparison
            return new[]
            {
                "options.userid_attribute"
            };
        }

        /// <summary>
        /// Applies connection-specific post-processing to filtered configuration for comparison.
        /// Filters out empty metadata values since they cannot be properly removed from Auth0 connections.
        /// </summary>
        /// <param name="filtered">The already filtered configuration hashtable</param>
        /// <returns>The hashtable with connection-specific filtering applied</returns>
        protected override Hashtable PostProcessFilteredConfiguration(Hashtable filtered)
        {
            // Special handling for connection metadata: filter out empty values
            if (filtered.ContainsKey("metadata") && filtered["metadata"] is Hashtable metadata)
            {
                var filteredMetadata = new Hashtable();
                foreach (DictionaryEntry entry in metadata)
                {
                    // Only include metadata entries that have non-empty values
                    if (entry.Value is string stringValue && !string.IsNullOrEmpty(stringValue))
                    {
                        filteredMetadata[entry.Key] = entry.Value;
                    }
                    else if (entry.Value is not null && entry.Value is not string)
                    {
                        filteredMetadata[entry.Key] = entry.Value;
                    }
                }
                filtered["metadata"] = filteredMetadata;
            }

            // Normalize enabled_clients format for comparison - ensure it's always a V1ClientReference array
            if (filtered.ContainsKey("enabled_clients"))
            {
                var enabledClientsValue = filtered["enabled_clients"];
                var normalizedClients = new List<V1ClientReference>();

                // Handle V1ClientReference array format (standard)
                if (enabledClientsValue is V1ClientReference[] clientArray)
                {
                    normalizedClients.AddRange(clientArray.Where(c => !string.IsNullOrEmpty(c.Id)));
                }
                // Handle other array formats
                else if (enabledClientsValue is IEnumerable enabledClients)
                {
                    foreach (var client in enabledClients)
                    {
                        if (client is Hashtable clientHash && clientHash.ContainsKey("id"))
                        {
                            // Extract from hashtable format  
                            var clientRef = new V1ClientReference
                            {
                                Id = clientHash["id"]?.ToString(),
                                Name = clientHash["name"]?.ToString(),
                                Namespace = clientHash["namespace"]?.ToString()
                            };
                            if (!string.IsNullOrEmpty(clientRef.Id))
                            {
                                normalizedClients.Add(clientRef);
                            }
                        }
                        else if (client is string clientId && !string.IsNullOrEmpty(clientId))
                        {
                            // Convert string to V1ClientReference
                            normalizedClients.Add(new V1ClientReference { Id = clientId });
                        }
                    }
                }
                filtered["enabled_clients"] = normalizedClients.ToArray();
            }

            return filtered;
        }

        private bool WasConnectionMetadataValuesRemoved(Hashtable metadata)
        {
            foreach (DictionaryEntry entry in metadata)
            {
                if (entry.Value == null)
                    return true;
            }
            return false;
        }
    }

}
