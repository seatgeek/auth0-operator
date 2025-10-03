using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Alethic.Auth0.Operator.Core.Models.Connection;
using Alethic.Auth0.Operator.Extensions;
using Alethic.Auth0.Operator.Helpers;
using Alethic.Auth0.Operator.Models;
using Alethic.Auth0.Operator.Options;
using Alethic.Auth0.Operator.Services;

using Auth0.Core.Exceptions;
using Auth0.ManagementApi;
using Auth0.ManagementApi.Models;

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
                        connectionId,
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
                            connectionId,
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
                            connectionId,
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
                var list = await GetAllConnectionsWithPagination(api, entity, cancellationToken);
                var self = list.FirstOrDefault(i => string.Equals(i.Name, conf.Name, StringComparison.OrdinalIgnoreCase));
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
                ApplyConfToRequest(req, conf);
                req.Strategy = conf.Strategy ?? throw new InvalidOperationException("Strategy is required for connection creation.");
                req.Options = string.Equals(conf.Strategy, "auth0", StringComparison.OrdinalIgnoreCase) ? (TransformToNewtonsoftJson<ConnectionOptions, global::Auth0.ManagementApi.Models.Connections.ConnectionOptions>(JsonSerializer.Deserialize<ConnectionOptions>(JsonSerializer.Serialize(conf.Options ?? new Hashtable()))) ?? new global::Auth0.ManagementApi.Models.Connections.ConnectionOptions()) : conf.Options ?? new Hashtable();

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
        protected override async Task Update(IManagementApiClient api, string id, Hashtable? last, ConnectionConf conf, List<string> driftingFields, string defaultNamespace, ITenantApiAccess tenantApiAccess, CancellationToken cancellationToken)
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
                ApplyConfToRequest(req, conf);
                req.Name = null!; // not allowed to be changed

                // Merge Auth0's current options with desired options to preserve unmanaged fields
                var mergedOptions = MergeConnectionOptions(last, conf.Options);

                Logger.LogDebugJson($"{EntityTypeName} merged options for update", new
                {
                    entityTypeName = EntityTypeName,
                    connectionId = id,
                    desiredOptionsCount = conf.Options?.Count ?? 0,
                    auth0OptionsCount = (last?.ContainsKey("options") == true && last["options"] is Hashtable h) ? h.Count : 0,
                    mergedOptionsCount = mergedOptions.Count,
                    operation = "merge_options_for_update"
                });

                req.Options = string.Equals(conf.Strategy, "auth0", StringComparison.OrdinalIgnoreCase)
                    ? (TransformToNewtonsoftJson<ConnectionOptions, global::Auth0.ManagementApi.Models.Connections.ConnectionOptions>(
                        JsonSerializer.Deserialize<ConnectionOptions>(JsonSerializer.Serialize(mergedOptions)))
                        ?? new global::Auth0.ManagementApi.Models.Connections.ConnectionOptions())
                    : mergedOptions;

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
        /// <param name="req"></param>
        /// <param name="conf"></param>
        static void ApplyConfToRequest(ConnectionBase req, ConnectionConf conf)
        {
            req.Name = conf.Name!;
            req.DisplayName = conf.DisplayName!;
            req.Metadata = conf.Metadata ?? new Hashtable();
            req.Realms = conf.Realms ?? new string[0];
            req.IsDomainConnection = conf.IsDomainConnection ?? false;
            req.ShowAsButton = conf.ShowAsButton;
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
        /// <param name="entity">Connection entity for tenant domain extraction</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Complete list of all connections</returns>
        private async Task<List<Connection>> GetAllConnectionsWithPagination(IManagementApiClient api, V1Connection entity, CancellationToken cancellationToken)
        {
            var tenantDomain = await GetTenantDomainForCacheSalt(entity, cancellationToken);

            return await Auth0PaginationHelper.GetAllWithPaginationAsync(
                _connectionCache,
                Logger,
                new GetConnectionsRequest(),
                api.Connections.GetAllAsync,
                "connections",
                tenantDomain,
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


            return filtered;
        }

        /// <summary>
        /// Filters the options field to only compare keys present in the desired Kubernetes configuration.
        /// This prevents false drift detection for Auth0-managed fields not specified in the K8s spec.
        /// </summary>
        /// <param name="fieldName">Name of the field being compared</param>
        /// <param name="auth0Value">Value from Auth0 API (current state)</param>
        /// <param name="desiredValue">Value from Kubernetes spec (desired state)</param>
        /// <returns>Tuple of filtered values ready for comparison</returns>
        protected override (object? filteredAuth0, object? filteredDesired) FilterNestedFieldForComparison(
            string fieldName,
            object? auth0Value,
            object? desiredValue)
        {
            // Special handling for options field - only compare fields present in desired state
            if (string.Equals(fieldName, "options", StringComparison.OrdinalIgnoreCase))
            {
                if (desiredValue is not Hashtable desiredOptions)
                    return (auth0Value, desiredValue);

                if (auth0Value is not Hashtable auth0Options)
                    return (auth0Value, desiredValue);

                // Create filtered Auth0 options containing only keys from desired spec
                var filteredAuth0Options = new Hashtable();
                foreach (DictionaryEntry entry in desiredOptions)
                {
                    if (auth0Options.ContainsKey(entry.Key))
                    {
                        filteredAuth0Options[entry.Key] = auth0Options[entry.Key];
                    }
                    // Keys in desired but not in Auth0 are omitted (will be detected as "added")
                }

                Logger.LogDebugJson($"{EntityTypeName} filtered options for comparison", new
                {
                    entityTypeName = EntityTypeName,
                    desiredKeyCount = desiredOptions.Count,
                    auth0OriginalKeyCount = auth0Options.Count,
                    auth0FilteredKeyCount = filteredAuth0Options.Count,
                    operation = "filter_options_for_comparison"
                });

                return (filteredAuth0Options, desiredOptions);
            }

            return base.FilterNestedFieldForComparison(fieldName, auth0Value, desiredValue);
        }

        /// <summary>
        /// Merges Auth0's current options with desired options to preserve Auth0-managed fields.
        /// Fields in desiredOptions override corresponding fields in Auth0's current options.
        /// </summary>
        /// <param name="lastConf">Last known Auth0 configuration (from Get() call)</param>
        /// <param name="desiredOptions">Desired options from Kubernetes spec.conf</param>
        /// <returns>Merged Hashtable with Auth0 fields preserved and desired fields applied</returns>
        private static Hashtable MergeConnectionOptions(Hashtable? lastConf, Hashtable? desiredOptions)
        {
            var merged = new Hashtable();

            // Step 1: Start with Auth0's current options (base layer)
            if (lastConf?.ContainsKey("options") == true && lastConf["options"] is Hashtable currentOptions)
            {
                foreach (DictionaryEntry entry in currentOptions)
                {
                    merged[entry.Key] = entry.Value;
                }
            }

            // Step 2: Override with desired options (Kubernetes spec wins)
            if (desiredOptions is not null)
            {
                foreach (DictionaryEntry entry in desiredOptions)
                {
                    merged[entry.Key] = entry.Value;
                }
            }

            return merged;
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

        /// <inheritdoc />
        protected override Task<(bool RequiresFetch, string? Reason)> RequiresAuth0Fetch(V1Connection entity, CancellationToken cancellationToken)
        {
            return Task.FromResult<(bool RequiresFetch, string? Reason)>((true, "inspecting configuration drift between desired and actual state"));
        }
    }

}
