using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Alethic.Auth0.Operator.Core.Models;
using Alethic.Auth0.Operator.Core.Models.Client;
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
using KubeOps.Abstractions.Entities;
using KubeOps.Abstractions.Queue;
using KubeOps.Abstractions.Rbac;
using KubeOps.KubernetesClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Alethic.Auth0.Operator.Controllers
{
    [EntityRbac(typeof(V1Tenant), Verbs = RbacVerb.List | RbacVerb.Get)]
    [EntityRbac(typeof(V1Client), Verbs = RbacVerb.All)]
    [EntityRbac(typeof(V1Connection), Verbs = RbacVerb.List | RbacVerb.Get)]
    [EntityRbac(typeof(V1Secret), Verbs = RbacVerb.All)]
    [EntityRbac(typeof(Eventsv1Event), Verbs = RbacVerb.All)]
    public class V1ClientController :
        V1TenantEntityController<V1Client, V1Client.SpecDef, V1Client.StatusDef, ClientConf>,
        IEntityController<V1Client>
    {
        readonly IMemoryCache _cache;
        readonly IHttpClientFactory _httpClientFactory;

        public V1ClientController(IKubernetesClient kube, EntityRequeue<V1Client> requeue, IMemoryCache cache,
            ILogger<V1ClientController> logger, IOptions<OperatorOptions> options, IHttpClientFactory httpClientFactory) :
            base(kube, requeue, cache, logger, options)
        {
            _cache = cache;
            _httpClientFactory = httpClientFactory;
        }

        /// <inheritdoc />
        protected override string EntityTypeName => "A0Client";

        /// <inheritdoc />
        protected override async Task<Hashtable?> Get(IManagementApiClient api, string id, string defaultNamespace,
            CancellationToken cancellationToken)
        {
            try
            {
                LogAuth0Read($"Getting Auth0 client with ID: {id}", "A0Client", id,
                    defaultNamespace, "retrieve_client_by_id");
                return TransformToSystemTextJson<Hashtable>(await api.Clients.GetAsync(id,
                    cancellationToken: cancellationToken));
            }
            catch (ErrorApiException e) when (e.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
            catch (Exception e)
            {
                Logger.LogErrorJson($"Error retrieving {EntityTypeName} with ID {id}: {e.Message}", new
                {
                    entityTypeName = EntityTypeName,
                    id = id,
                    errorMessage = e.Message
                }, e);
                throw;
            }
        }

        /// <inheritdoc />
        protected override async Task<string?> Find(IManagementApiClient api, V1Client entity, V1Client.SpecDef spec,
            string defaultNamespace, CancellationToken cancellationToken)
        {
            if (spec.Find is not null)
            {
                return await FindClientUsingCriteria(api, entity, spec.Find, cancellationToken);
            }

            return await FindClientByName(api, entity, spec, cancellationToken);
        }

        private async Task<string?> FindClientUsingCriteria(IManagementApiClient api, V1Client entity,
            ClientFind findCriteria, CancellationToken cancellationToken)
        {
            LogFindOperationStart(entity, findCriteria);

            if (findCriteria.ClientId is string clientId)
            {
                return await FindClientByClientId(api, entity, clientId, cancellationToken);
            }

            if (findCriteria.CallbackUrls is { Length: > 0 } callbackUrls)
            {
                return await FindClientByCallbackUrls(api, entity, callbackUrls, findCriteria.CallbackUrlMatchMode,
                    cancellationToken);
            }

            Logger.LogInformationJson(
                $"{EntityTypeName} {entity.Namespace()}/{entity.Name()} find operation completed - no valid lookup criteria provided",
                new
                {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name()
                });
            return null;
        }

        private void LogFindOperationStart(V1Client entity, ClientFind findCriteria)
        {
            Logger.LogInformationJson(
                $"{EntityTypeName} {entity.Namespace()}/{entity.Name()} starting client lookup with find criteria: ClientId={findCriteria.ClientId ?? "null"}, CallbackUrls=[{(findCriteria.CallbackUrls != null ? string.Join(", ", findCriteria.CallbackUrls) : "null")}], MatchMode={findCriteria.CallbackUrlMatchMode ?? "strict"}",
                new
                {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name(),
                    clientId = findCriteria.ClientId,
                    callbackUrls = findCriteria.CallbackUrls,
                    matchMode = findCriteria.CallbackUrlMatchMode ?? "strict"
                });
        }

        private async Task<string?> FindClientByClientId(IManagementApiClient api, V1Client entity, string clientId,
            CancellationToken cancellationToken)
        {
            Logger.LogDebugJson(
                $"{EntityTypeName} {entity.Namespace()}/{entity.Name()} initiating client_id lookup: {clientId}", new
                {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name(),
                    clientId
                });

            try
            {
                LogAuth0Read($"Getting Auth0 client by client ID: {clientId}", "A0Client",
                    entity.Name(), entity.Namespace(), "retrieve_client_by_clientid");
                var client = await api.Clients.GetAsync(clientId, "client_id,name",
                    cancellationToken: cancellationToken);
                Logger.LogInformationJson(
                    $"{EntityTypeName} {entity.Namespace()}/{entity.Name()} client_id lookup SUCCESSFUL - found existing client: {client.Name} (ClientId: {client.ClientId})",
                    new
                    {
                        entityTypeName = EntityTypeName,
                        entityNamespace = entity.Namespace(),
                        entityName = entity.Name(),
                        foundClientName = client.Name,
                        foundClientId = client.ClientId
                    });
                return client.ClientId;
            }
            catch (ErrorApiException e) when (e.StatusCode == HttpStatusCode.NotFound)
            {
                Logger.LogWarningJson(
                    $"{EntityTypeName} {entity.Namespace()}/{entity.Name()} client_id lookup FAILED - could not find client with id {clientId}",
                    new
                    {
                        entityTypeName = EntityTypeName,
                        entityNamespace = entity.Namespace(),
                        entityName = entity.Name(),
                        searchedClientId = clientId
                    });
                return null;
            }
        }

        private async Task<string?> FindClientByCallbackUrls(IManagementApiClient api, V1Client entity,
            string[] callbackUrls, string? matchMode, CancellationToken cancellationToken)
        {
            Logger.LogInformationJson(
                $"{EntityTypeName} {entity.Namespace()}/{entity.Name()} initiating callback URL lookup: URLs=[{string.Join(", ", callbackUrls)}], Mode={matchMode ?? "strict"}",
                new
                {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name(),
                    callbackUrls,
                    matchMode = matchMode ?? "strict"
                });

            var result = await FindByCallbackUrls(api, entity, callbackUrls, matchMode, cancellationToken);

            if (result != null)
            {
                Logger.LogInformationJson(
                    $"{EntityTypeName} {entity.Namespace()}/{entity.Name()} callback URL lookup SUCCESSFUL - found client with id: {result}",
                    new
                    {
                        entityTypeName = EntityTypeName,
                        entityNamespace = entity.Namespace(),
                        entityName = entity.Name(),
                        foundClientId = result
                    });
            }
            else
            {
                Logger.LogInformationJson(
                    $"{EntityTypeName} {entity.Namespace()}/{entity.Name()} callback URL lookup FAILED - no matching client found",
                    new
                    {
                        entityTypeName = EntityTypeName,
                        entityNamespace = entity.Namespace(),
                        entityName = entity.Name()
                    });
            }

            return result;
        }

        private async Task<string?> FindClientByName(IManagementApiClient api, V1Client entity, V1Client.SpecDef spec,
            CancellationToken cancellationToken)
        {
            Logger.LogDebugJson(
                $"{EntityTypeName} {entity.Namespace()}/{entity.Name()} no find criteria specified - falling back to name-based lookup",
                new
                {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name()
                });

            var conf = spec.Init ?? spec.Conf;
            if (conf is null)
                return null;

            if (conf.Name is null)
            {
                return null;
            }

            Logger.LogDebugJson(
                $"{EntityTypeName} {entity.Namespace()}/{entity.Name()} initiating name-based lookup for client: {conf.Name}",
                new
                {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name(),
                    clientName = conf.Name
                });

            LogAuth0Read($"Getting all Auth0 clients for name-based lookup", "A0Client",
                entity.Name(), entity.Namespace(), "retrieve_all_clients_for_name_lookup");
            var list = await GetAllClientsWithPagination(api, entity, cancellationToken);
            Logger.LogDebugJson(
                $"{EntityTypeName} {entity.Namespace()}/{entity.Name()} searched {list.Count} clients for name-based lookup",
                new
                {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name(),
                    clientCount = list.Count
                });
            var self = list.FirstOrDefault(i => string.Equals(i.Name, conf.Name, StringComparison.OrdinalIgnoreCase));

            return LogNameBasedLookupResult(entity, conf.Name, self);
        }

        private string? LogNameBasedLookupResult(V1Client entity, string clientName, Client? foundClient)
        {
            if (foundClient != null)
            {
                Logger.LogInformationJson(
                    $"{EntityTypeName} {entity.Namespace()}/{entity.Name()} name-based lookup SUCCESSFUL - found client: {clientName} (ClientId: {foundClient.ClientId})",
                    new
                    {
                        entityTypeName = EntityTypeName,
                        entityNamespace = entity.Namespace(),
                        entityName = entity.Name(),
                        foundClientName = clientName,
                        foundClientId = foundClient.ClientId
                    });
            }
            else
            {
                Logger.LogInformationJson(
                    $"{EntityTypeName} {entity.Namespace()}/{entity.Name()} name-based lookup FAILED - no client found with name: {clientName}",
                    new
                    {
                        entityTypeName = EntityTypeName,
                        entityNamespace = entity.Namespace(),
                        entityName = entity.Name(),
                        searchedClientName = clientName
                    });
            }

            return foundClient?.ClientId;
        }

        /// <summary>
        /// Finds an Auth0 client by callback URLs using loose or strict matching mode.
        /// </summary>
        /// <param name="api">Auth0 Management API client</param>
        /// <param name="entity">Kubernetes entity for logging</param>
        /// <param name="targetCallbackUrls">Array of callback URLs to match</param>
        /// <param name="matchMode">Matching mode: "loose" (partial match) or "strict" (all URLs must match)</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Client ID if found, null otherwise</returns>
        private async Task<string?> FindByCallbackUrls(IManagementApiClient api, V1Client entity,
            string[] targetCallbackUrls, string? matchMode, CancellationToken cancellationToken)
        {
            if (targetCallbackUrls == null || targetCallbackUrls.Length == 0)
            {
                Logger.LogWarningJson(
                    $"{EntityTypeName} {entity.Namespace()}/{entity.Name()} targetCallbackUrls is null or empty", new
                    {
                        entityTypeName = EntityTypeName,
                        entityNamespace = entity.Namespace(),
                        entityName = entity.Name()
                    });
                return null;
            }

            foreach (var url in targetCallbackUrls)
            {
                if (string.IsNullOrWhiteSpace(url))
                {
                    Logger.LogWarningJson(
                        $"{EntityTypeName} {entity.Namespace()}/{entity.Name()} callback URL is null or empty", new
                        {
                            entityTypeName = EntityTypeName,
                            entityNamespace = entity.Namespace(),
                            entityName = entity.Name()
                        });
                    return null;
                }

                if (!Uri.TryCreate(url, UriKind.Absolute, out _))
                {
                    Logger.LogWarningJson(
                        $"{EntityTypeName} {entity.Namespace()}/{entity.Name()} invalid callback URL format: {url}", new
                        {
                            entityTypeName = EntityTypeName,
                            entityNamespace = entity.Namespace(),
                            entityName = entity.Name(),
                            invalidUrl = url
                        });
                    return null;
                }
            }

            var isStrictMode = !string.Equals(matchMode, "loose", StringComparison.OrdinalIgnoreCase);
            var modeName = isStrictMode ? "strict" : "loose";

            Logger.LogDebugJson(
                $"{EntityTypeName} {entity.Namespace()}/{entity.Name()} executing callback URL search with {modeName} mode matching against Auth0 clients",
                new
                {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name(),
                    searchMode = modeName
                });

            LogAuth0Read($"Getting all Auth0 clients for callback URL lookup", "A0Client",
                entity.Name(), entity.Namespace(), "retrieve_all_clients_for_callback_lookup");
            var clients = await GetAllClientsWithPagination(api, entity, cancellationToken);

            var matchingClients = clients
                .Where(client => HasMatchingCallbackUrls(client, targetCallbackUrls, isStrictMode)).ToList();

            if (matchingClients.Count == 0)
            {
                Logger.LogDebugJson(
                    $"{EntityTypeName} {entity.Namespace()}/{entity.Name()} no clients matched callback URL criteria ({modeName} mode)",
                    new
                    {
                        entityTypeName = EntityTypeName,
                        entityNamespace = entity.Namespace(),
                        entityName = entity.Name(),
                        searchMode = modeName
                    });
                return null;
            }

            if (matchingClients.Count > 1)
            {
                Logger.LogWarningJson(
                    $"{EntityTypeName} {entity.Namespace()}/{entity.Name()} found multiple clients ({matchingClients.Count}) matching callback URL criteria ({modeName} mode). Target URLs: {string.Join(", ", targetCallbackUrls)}. Using first match: {matchingClients[0].Name} ({matchingClients[0].ClientId})",
                    new
                    {
                        entityTypeName = EntityTypeName,
                        entityNamespace = entity.Namespace(),
                        entityName = entity.Name(),
                        matchingClientCount = matchingClients.Count,
                        searchMode = modeName,
                        targetUrls = targetCallbackUrls,
                        selectedClientName = matchingClients[0].Name,
                        selectedClientId = matchingClients[0].ClientId
                    });
            }

            var selectedClient = matchingClients[0];
            Logger.LogDebugJson(
                $"{EntityTypeName} {entity.Namespace()}/{entity.Name()} selected client for callback URL match ({modeName} mode): {selectedClient.Name} ({selectedClient.ClientId})",
                new
                {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name(),
                    searchMode = modeName,
                    selectedClientName = selectedClient.Name,
                    selectedClientId = selectedClient.ClientId
                });

            return selectedClient.ClientId;
        }


        /// <summary>
        /// Retrieves all Auth0 clients across all pages using pagination with caching.
        /// Always fetches client_id, name, and callbacks fields and caches for 15 minutes.
        /// </summary>
        /// <param name="api">Auth0 Management API client</param>
        /// <param name="entity">Client entity for tenant domain extraction</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Complete list of all clients with client_id, name, and callbacks fields</returns>
        private async Task<List<Client>> GetAllClientsWithPagination(IManagementApiClient api, V1Client entity,
            CancellationToken cancellationToken)
        {
            var request = new GetClientsRequest()
            {
                Fields = "client_id,name,callbacks",
                IncludeFields = true
            };

            var tenantDomain = await GetTenantDomainForCacheSalt(entity, cancellationToken);

            return await Auth0PaginationHelper.GetAllWithPaginationAsync(
                _cache,
                Logger,
                request,
                api.Clients.GetAllAsync,
                "clients",
                tenantDomain,
                cancellationToken,
                cacheDurationMinutes: 15);
        }

        /// <summary>
        /// Checks if a client has matching callback URLs based on the specified matching mode.
        /// </summary>
        /// <param name="client">Auth0 client to check</param>
        /// <param name="targetUrls">Target callback URLs to match</param>
        /// <param name="isStrictMode">True for strict mode (all URLs must match), false for loose mode (any URL match)</param>
        /// <returns>True if the client matches, false otherwise</returns>
        private static bool HasMatchingCallbackUrls(Client client, string[] targetUrls, bool isStrictMode)
        {
            if (client.Callbacks == null || client.Callbacks.Length == 0)
                return false;

            if (isStrictMode)
            {
                // Strict mode: ALL target URLs must be found in client's callbacks
                return targetUrls.All(targetUrl =>
                    client.Callbacks.Contains(targetUrl, StringComparer.OrdinalIgnoreCase));
            }
            else
            {
                // Loose mode: AT LEAST ONE target URL must be found in client's callbacks
                return targetUrls.Any(targetUrl =>
                    client.Callbacks.Contains(targetUrl, StringComparer.OrdinalIgnoreCase));
            }
        }

        /// <inheritdoc />
        protected override string? ValidateCreate(ClientConf conf)
        {
            if (conf.ApplicationType == null)
                return "missing a value for application type";

            return null;
        }

        /// <inheritdoc />
        protected override async Task<string> Create(IManagementApiClient api, ClientConf conf, string defaultNamespace,
            CancellationToken cancellationToken)
        {
            var startTime = DateTimeOffset.UtcNow;
            Logger.LogInformationJson($"{EntityTypeName} creating client in Auth0 with name: {conf.Name}", new
            {
                entityTypeName = EntityTypeName,
                clientName = conf.Name
            });

            ClientCreateRequest createRequest;
            try
            {
                createRequest = TransformToNewtonsoftJson<ClientConf, ClientCreateRequest>(conf);
            }
            catch (Exception ex)
            {
                Logger.LogErrorJson(
                    $"{EntityTypeName} failed to transform configuration for client creation: {ex.Message}", new
                    {
                        entityTypeName = EntityTypeName,
                        errorMessage = ex.Message
                    }, ex);
                throw;
            }

            try
            {
                LogAuth0Write($"Creating Auth0 client with name: {conf.Name}", "A0Client",
                    conf.Name ?? "unknown", "unknown", "create_client", DriftLogContext.FirstReconciliation());
                var self = await api.Clients.CreateAsync(createRequest, cancellationToken);
                var duration = DateTimeOffset.UtcNow - startTime;
                Logger.LogInformationJson(
                    $"{EntityTypeName} successfully created client in Auth0 with ID: {self.ClientId} and name: {conf.Name} in {duration.TotalMilliseconds}ms",
                    new
                    {
                        entityTypeName = EntityTypeName,
                        clientId = self.ClientId,
                        clientName = conf.Name,
                        durationMs = duration.TotalMilliseconds
                    });
                return self.ClientId;
            }
            catch (Exception ex)
            {
                Logger.LogErrorJson(
                    $"{EntityTypeName} failed to create client in Auth0 with name: {conf.Name}: {ex.Message}", new
                    {
                        entityTypeName = EntityTypeName,
                        clientName = conf.Name,
                        errorMessage = ex.Message
                    }, ex);
                throw;
            }
        }

        /// <inheritdoc />
        protected override async Task Update(IManagementApiClient api, string id, Hashtable? last, ClientConf conf,
            IReadOnlyList<DriftField> driftFields, string defaultNamespace, string entityName, ITenantApiAccess tenantApiAccess, DriftLogContext driftContext, CancellationToken cancellationToken)
        {
            var startTime = DateTimeOffset.UtcNow;
            var driftingFieldNames = driftFields.Select(d => d.FieldPath).ToArray();
            Logger.LogInformationJson($"{EntityTypeName} updating client in Auth0 with id: {id} and name: {conf.Name}",
                new
                {
                    entityTypeName = EntityTypeName,
                    clientId = id,
                    clientName = conf.Name,
                    driftingFields = driftingFieldNames
                });

            // Determine what needs to be updated based on drifting fields
            var needsClientUpdate = driftingFieldNames.Any(field =>
                !string.Equals(field, "enabled_connections", StringComparison.OrdinalIgnoreCase));
            var needsConnectionUpdate = driftingFieldNames.Any(field =>
                string.Equals(field, "enabled_connections", StringComparison.OrdinalIgnoreCase));

            Logger.LogInformationJson($"{EntityTypeName} selective update analysis for client {id}: needsClientUpdate={needsClientUpdate}, needsConnectionUpdate={needsConnectionUpdate}",
                new
                {
                    entityTypeName = EntityTypeName,
                    clientId = id,
                    needsClientUpdate,
                    needsConnectionUpdate,
                    driftingFields = driftingFieldNames
                });

            // Update client properties if any non-enabled_connections fields have drifted
            if (needsClientUpdate)
            {
                await UpdateClientProperties(api, id, last, conf, driftContext, cancellationToken);
            }
            else
            {
                Logger.LogInformationJson($"{EntityTypeName} skipping client property update for {id} - only enabled_connections has drifted",
                    new
                    {
                        entityTypeName = EntityTypeName,
                        clientId = id,
                        reason = "only_enabled_connections_drifted"
                    });
            }

            // Update enabled connections if enabled_connections field has drifted
            if (needsConnectionUpdate)
            {
                await ReconcileEnabledConnections(tenantApiAccess, id, conf.EnabledConnections, defaultNamespace,
                    entityName, cancellationToken);
            }
            else
            {
                Logger.LogInformationJson($"{EntityTypeName} skipping enabled connections update for {id} - enabled_connections field has not drifted",
                    new
                    {
                        entityTypeName = EntityTypeName,
                        clientId = id,
                        reason = "enabled_connections_not_drifted"
                    });
            }

            var duration = DateTimeOffset.UtcNow - startTime;
            Logger.LogInformationJson(
                $"{EntityTypeName} successfully completed selective update for client {id} in {duration.TotalMilliseconds}ms (client={needsClientUpdate}, connections={needsConnectionUpdate})",
                new
                {
                    entityTypeName = EntityTypeName,
                    clientId = id,
                    clientName = conf.Name,
                    durationMs = duration.TotalMilliseconds,
                    updatedClient = needsClientUpdate,
                    updatedConnections = needsConnectionUpdate
                });
        }

        /// <summary>
        /// Updates the client properties (excluding enabled_connections) in Auth0.
        /// </summary>
        /// <param name="api">Auth0 Management API client</param>
        /// <param name="id">Client ID</param>
        /// <param name="last">Last known configuration from Auth0</param>
        /// <param name="conf">Desired client configuration</param>
        /// <param name="cancellationToken">Cancellation token</param>
        private async Task UpdateClientProperties(IManagementApiClient api, string id, Hashtable? last, ClientConf conf, DriftLogContext driftContext, CancellationToken cancellationToken)
        {
            Logger.LogInformationJson($"{EntityTypeName} updating client properties for {id}",
                new
                {
                    entityTypeName = EntityTypeName,
                    clientId = id,
                    operation = "update_client_properties"
                });

            // Transform configuration to Auth0 client update request
            ClientUpdateRequest req;
            try
            {
                req = TransformToNewtonsoftJson<ClientConf, ClientUpdateRequest>(conf);
            }
            catch (Exception ex)
            {
                Logger.LogErrorJson(
                    $"{EntityTypeName} failed to transform configuration for client update: {ex.Message}", new
                    {
                        entityTypeName = EntityTypeName,
                        errorMessage = ex.Message
                    }, ex);
                throw;
            }

            // Explicitly null out missing metadata if previously present
            if (last is not null && last.ContainsKey("client_metadata") && conf.ClientMetaData != null &&
                last["client_metadata"] is Hashtable lastMetadata)
            {
                // Create a defensive copy of keys to avoid potential enumeration issues
                var keysToProcess = lastMetadata.Keys.Cast<string>().ToList();
                foreach (string key in keysToProcess)
                    if (conf.ClientMetaData.ContainsKey(key) == false)
                        req.ClientMetaData[key] = null;
            }

            try
            {
                LogAuth0Write($"Updating Auth0 client properties with ID: {id}",
                    "A0Client", conf.Name ?? "unknown", "unknown", "update_client_properties", driftContext);
                await api.Clients.UpdateAsync(id, req, cancellationToken);

                Logger.LogInformationJson(
                    $"{EntityTypeName} successfully updated client properties for {id}",
                    new
                    {
                        entityTypeName = EntityTypeName,
                        clientId = id,
                        operation = "update_client_properties",
                        status = "success"
                    });
            }
            catch (Exception ex)
            {
                Logger.LogErrorJson(
                    $"{EntityTypeName} failed to update client properties for {id}: {ex.Message}",
                    new
                    {
                        entityTypeName = EntityTypeName,
                        clientId = id,
                        operation = "update_client_properties",
                        errorMessage = ex.Message
                    }, ex);
                throw;
            }
        }

        /// <inheritdoc />
        protected override async Task<bool> ApplyStatus(IManagementApiClient api, V1Client entity, Hashtable lastConf,
            string defaultNamespace, CancellationToken cancellationToken)
        {
            bool needsSecretCreationRetry = false;

            if (lastConf is not null)
            {
                // Always attempt to apply secret if secretRef is specified
                // Extract the client ID and secret from the lastConf (Auth0 Management API response)
                if (entity.Spec.SecretRef is not null)
                {
                    var desiredClientId = lastConf.ContainsKey("client_id") ? (string?)lastConf["client_id"] : null;
                    var desiredClientSecret = lastConf.ContainsKey("client_secret")
                        ? (string?)lastConf["client_secret"]
                        : null;

                    if (!string.IsNullOrEmpty(desiredClientId) && !string.IsNullOrEmpty(desiredClientSecret))
                    {
                        await ApplySecret(entity, defaultNamespace, desiredClientId, desiredClientSecret,
                            cancellationToken);
                    }
                    else
                    {
                        needsSecretCreationRetry = true;
                    }
                }

                if (lastConf.ContainsKey("client_id"))
                    lastConf.Remove("client_id");
                if (lastConf.ContainsKey("client_secret"))
                    lastConf.Remove("client_secret");
            }

            await base.ApplyStatus(api, entity, lastConf ?? new Hashtable(), defaultNamespace, cancellationToken);
            return needsSecretCreationRetry;
        }

        /// <summary>
        /// Checks if secret data needs to be updated by comparing existing values with desired values.
        /// </summary>
        /// <param name="secret">The existing secret</param>
        /// <param name="desiredClientId">Desired clientId value</param>
        /// <param name="desiredClientSecret">Desired clientSecret value</param>
        /// <returns>True if update is needed, false if data is already correct</returns>
        bool IsSecretUpdateNeeded(V1Secret secret, string? desiredClientId, string? desiredClientSecret)
        {
            // Check StringData first (takes precedence over Data)
            var currentClientId = secret.StringData?.TryGetValue("clientId", out var existingClientId) == true
                ? existingClientId
                : null;
            var currentClientSecret =
                secret.StringData?.TryGetValue("clientSecret", out var existingClientSecret) == true
                    ? existingClientSecret
                    : null;

            // If StringData is null or empty, check base64-encoded Data
            if (secret.StringData?.ContainsKey("clientId") != true && secret.Data?.ContainsKey("clientId") == true)
            {
                try
                {
                    if (secret.Data.TryGetValue("clientId", out var clientIdBytes) && clientIdBytes != null)
                    {
                        currentClientId = System.Text.Encoding.UTF8.GetString(clientIdBytes);
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarningJson($"Failed to decode existing clientId from secret data: {ex.Message}", new
                    {
                        errorMessage = ex.Message
                    });
                    return true; // If we can't decode, assume update is needed
                }
            }

            if (secret.StringData?.ContainsKey("clientSecret") != true &&
                secret.Data?.ContainsKey("clientSecret") == true)
            {
                try
                {
                    if (secret.Data.TryGetValue("clientSecret", out var clientSecretBytes) && clientSecretBytes != null)
                    {
                        currentClientSecret = System.Text.Encoding.UTF8.GetString(clientSecretBytes);
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarningJson($"Failed to decode existing clientSecret from secret data: {ex.Message}", new
                    {
                        errorMessage = ex.Message
                    });
                    return true; // If we can't decode, assume update is needed
                }
            }

            var clientIdChanged =
                !string.Equals(currentClientId ?? "", desiredClientId ?? "", StringComparison.Ordinal);
            var clientSecretChanged = !string.Equals(currentClientSecret ?? "", desiredClientSecret ?? "",
                StringComparison.Ordinal);

            return clientIdChanged || clientSecretChanged;
        }

        /// <summary>
        /// Applies the client secret.
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="defaultNamespace"></param>
        /// <param name="desiredClientId">The client ID from Auth0 Management API</param>
        /// <param name="desiredClientSecret">The client secret from Auth0 Management API</param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        async Task ApplySecret(V1Client entity, string defaultNamespace,
            string? desiredClientId, string? desiredClientSecret, CancellationToken cancellationToken)
        {
            try
            {
                if (entity.Spec.SecretRef is null)
                    return;

                // Query the Kubernetes secret
                var secret = await ResolveSecretRef(entity.Spec.SecretRef,
                    string.IsNullOrEmpty(entity.Spec.SecretRef.NamespaceProperty) ? defaultNamespace : entity.Spec.SecretRef.NamespaceProperty, cancellationToken);

                if (secret is null)
                {
                    var resolvedNamespace = string.IsNullOrEmpty(entity.Spec.SecretRef.NamespaceProperty) ? defaultNamespace : entity.Spec.SecretRef.NamespaceProperty;

                    Logger.LogWarningJson(
                        $"*** SECRET MISSING *** {EntityTypeName} {entity.Namespace()}/{entity.Name()} referenced secret {entity.Spec.SecretRef.Name} which does not exist - creating secret in namespace {resolvedNamespace}",
                        new
                        {
                            entityTypeName = EntityTypeName,
                            entityNamespace = entity.Namespace(),
                            entityName = entity.Name(),
                            secretName = entity.Spec.SecretRef.Name,
                            secretNamespace = resolvedNamespace,
                            operation = "secret_creation_required"
                        });
                    try
                    {
                        secret = await Kube.CreateAsync(
                            new V1Secret(
                                    metadata: new V1ObjectMeta(
                                        namespaceProperty: resolvedNamespace,
                                        name: entity.Spec.SecretRef.Name))
                                .WithOwnerReference(entity),
                            cancellationToken);

                        Logger.LogWarningJson(
                            $"*** SECRET CREATED *** Successfully created secret {entity.Spec.SecretRef.Name} in namespace {resolvedNamespace} for {EntityTypeName} {entity.Namespace()}/{entity.Name()}",
                            new
                            {
                                entityTypeName = EntityTypeName,
                                entityNamespace = entity.Namespace(),
                                entityName = entity.Name(),
                                secretName = entity.Spec.SecretRef.Name,
                                secretNamespace = resolvedNamespace,
                                operation = "secret_created_successfully"
                            });
                    }
                    catch (Exception ex)
                    {
                        Logger.LogErrorJson(
                            $"{EntityTypeName} {entity.Namespace()}/{entity.Name()} failed to create secret {entity.Spec.SecretRef.Name}: {ex.Message}",
                            new
                            {
                                entityTypeName = EntityTypeName,
                                entityNamespace = entity.Namespace(),
                                entityName = entity.Name(),
                                secretName = entity.Spec.SecretRef.Name,
                                errorMessage = ex.Message
                            }, ex);
                        throw;
                    }
                }

                // only apply actual values if we are the owner
                if (secret.IsOwnedBy(entity))
                {
                    var updateNeeded = IsSecretUpdateNeeded(secret, desiredClientId, desiredClientSecret);

                    if (updateNeeded)
                    {
                        Logger.LogInformationJson(
                            $"{EntityTypeName} {entity.Namespace()}/{entity.Name()} referenced secret {entity.Spec.SecretRef.Name}: updating due to data changes.",
                            new
                            {
                                entityTypeName = EntityTypeName,
                                entityNamespace = entity.Namespace(),
                                entityName = entity.Name(),
                                secretName = entity.Spec.SecretRef.Name
                            });

                        secret.StringData ??= new Dictionary<string, string>();

                        if (desiredClientId is not null)
                        {
                            secret.StringData["clientId"] = desiredClientId;
                        }

                        if (desiredClientSecret is not null)
                        {
                            secret.StringData["clientSecret"] = desiredClientSecret;
                        }

                        try
                        {
                            secret = await Kube.UpdateAsync(secret, cancellationToken);
                            Logger.LogInformationJson(
                                $"{EntityTypeName} {entity.Namespace()}/{entity.Name()} successfully updated secret {entity.Spec.SecretRef.Name}",
                                new
                                {
                                    entityTypeName = EntityTypeName,
                                    entityNamespace = entity.Namespace(),
                                    entityName = entity.Name(),
                                    secretName = entity.Spec.SecretRef.Name
                                });
                        }
                        catch (Exception ex)
                        {
                            Logger.LogErrorJson(
                                $"{EntityTypeName} {entity.Namespace()}/{entity.Name()} failed to update secret {entity.Spec.SecretRef.Name}: {ex.Message}",
                                new
                                {
                                    entityTypeName = EntityTypeName,
                                    entityNamespace = entity.Namespace(),
                                    entityName = entity.Name(),
                                    secretName = entity.Spec.SecretRef.Name,
                                    errorMessage = ex.Message
                                }, ex);
                            throw;
                        }
                    }
                }
                else
                {
                    Logger.LogInformationJson(
                        $"{EntityTypeName} {entity.Namespace()}/{entity.Name()} secret {entity.Spec.SecretRef.Name} exists but is not owned by this client, skipping update",
                        new
                        {
                            entityTypeName = EntityTypeName,
                            entityNamespace = entity.Namespace(),
                            entityName = entity.Name(),
                            secretName = entity.Spec.SecretRef.Name
                        });
                }
            }
            catch (Exception e)
            {
                Logger.LogErrorJson(
                    $"Error applying secret for {EntityTypeName} {entity.Namespace()}/{entity.Name()}: {e.Message}", new
                    {
                        entityTypeName = EntityTypeName,
                        entityNamespace = entity.Namespace(),
                        entityName = entity.Name(),
                        errorMessage = e.Message
                    }, e);
                throw;
            }
        }

        /// <inheritdoc />
        protected override async Task Delete(IManagementApiClient api, string id, CancellationToken cancellationToken)
        {
            Logger.LogInformationJson(
                $"{EntityTypeName} deleting client from Auth0 with ID: {id} (reason: Kubernetes entity deleted)", new
                {
                    entityTypeName = EntityTypeName,
                    clientId = id,
                    reason = "Kubernetes entity deleted"
                });
            try
            {
                LogAuth0Write($"Deleting Auth0 client with ID: {id}", "A0Client", id,
                    "unknown", "delete_client", DriftLogContext.FinalizerDelete());
                await api.Clients.DeleteAsync(id, cancellationToken);
                Logger.LogInformationJson($"{EntityTypeName} successfully deleted client from Auth0 with ID: {id}", new
                {
                    entityTypeName = EntityTypeName,
                    clientId = id
                });
            }
            catch (Exception ex)
            {
                Logger.LogErrorJson($"{EntityTypeName} failed to delete client from Auth0 with ID: {id}: {ex.Message}",
                    new
                    {
                        entityTypeName = EntityTypeName,
                        clientId = id,
                        errorMessage = ex.Message
                    }, ex);
                throw;
            }
        }

        /// <summary>
        /// Checks if the client's Auth0 secret requires refresh by verifying the existence and validity of the secret.
        /// </summary>
        /// <param name="entity">The client entity</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if the secret requires refresh, false if it exists and is valid</returns>
        private async Task<bool> ClientAuth0SecretRequiresRefresh(V1Client entity, CancellationToken cancellationToken)
        {
            var secretRef = await ResolveSecretRef(entity.Spec.SecretRef, entity.Namespace(), cancellationToken);

            if (secretRef is null)
            {
                LogMissingClientSecret(entity);
                return true;
            }

            var (secretName, secretNamespace) = GetSecretNameAndNamespace(secretRef, entity);

            if (string.IsNullOrWhiteSpace(secretName) || string.IsNullOrWhiteSpace(secretNamespace))
            {
                LogInvalidSecretReference(entity);
                return true;
            }

            var secret = Kube.Get<V1Secret>(secretName, secretNamespace);
            if (secret is null)
            {
                LogSecretNotFound(entity, secretName, secretNamespace);
                return true;
            }

            return ValidateSecretData(entity, secret, secretName, secretNamespace);
        }

        private void LogMissingClientSecret(V1Client entity)
        {
            Logger.LogWarningJson(
                $"{EntityTypeName} {entity.Namespace()}/{entity.Name()} missing client authentication secret.", new
                {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name()
                });
        }

        private (string secretName, string secretNamespace) GetSecretNameAndNamespace(V1Secret secretRef,
            V1Client entity)
        {
            var secretName = secretRef.Name();
            var secretNamespace = secretRef.Namespace();
            secretNamespace = string.IsNullOrEmpty(secretNamespace) ? entity.Namespace() : secretNamespace;
            return (secretName, secretNamespace);
        }

        private void LogInvalidSecretReference(V1Client entity)
        {
            Logger.LogWarningJson(
                $"{EntityTypeName} {entity.Namespace()}/{entity.Name()} has invalid client authentication secret reference.",
                new
                {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name()
                });
        }

        private void LogSecretNotFound(V1Client entity, string secretName, string secretNamespace)
        {
            Logger.LogWarningJson(
                $"{EntityTypeName} {entity.Namespace()}/{entity.Name()} client authentication secret {secretNamespace}/{secretName} not found.",
                new
                {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name(),
                    secretNamespace = secretNamespace,
                    secretName = secretName
                });
        }

        private bool ValidateSecretData(V1Client entity, V1Secret secret, string secretName, string secretNamespace)
        {
            if (secret.Data is null)
            {
                LogSecretHasNoData(entity, secretName, secretNamespace);
                return true;
            }

            var clientId = ExtractClientIdFromSecret(secret, entity, secretName, secretNamespace);
            if (string.IsNullOrWhiteSpace(clientId))
            {
                return true;
            }

            var clientSecret = ExtractClientSecretFromSecret(secret, entity, secretName, secretNamespace);
            if (string.IsNullOrWhiteSpace(clientSecret))
            {
                return true;
            }

            return false;
        }

        private void LogSecretHasNoData(V1Client entity, string secretName, string secretNamespace)
        {
            Logger.LogWarningJson(
                $"{EntityTypeName} {entity.Namespace()}/{entity.Name()} client authentication secret {secretNamespace}/{secretName} has no data.",
                new
                {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name(),
                    secretNamespace,
                    secretName
                });
        }

        private string? ExtractClientIdFromSecret(V1Secret secret, V1Client entity, string secretName,
            string secretNamespace)
        {
            string? clientId = null;
            if (secret.Data.TryGetValue("clientId", out var clientIdBytes) && clientIdBytes != null)
            {
                clientId = Encoding.UTF8.GetString(clientIdBytes);
            }

            if (string.IsNullOrWhiteSpace(clientId))
            {
                Logger.LogWarningJson(
                    $"{EntityTypeName} {entity.Namespace()}/{entity.Name()} client authentication secret {secretNamespace}/{secretName} is missing clientId.",
                    new
                    {
                        entityTypeName = EntityTypeName,
                        entityNamespace = entity.Namespace(),
                        entityName = entity.Name(),
                        secretNamespace,
                        secretName
                    });
            }

            return clientId;
        }

        private string? ExtractClientSecretFromSecret(V1Secret secret, V1Client entity, string secretName,
            string secretNamespace)
        {
            string? clientSecret = null;
            if (secret.Data.TryGetValue("clientSecret", out var clientSecretBytes) && clientSecretBytes != null)
            {
                clientSecret = Encoding.UTF8.GetString(clientSecretBytes);
            }

            if (string.IsNullOrWhiteSpace(clientSecret))
            {
                Logger.LogWarningJson(
                    $"{EntityTypeName} {entity.Namespace()}/{entity.Name()} client authentication secret {secretNamespace}/{secretName} is missing clientSecret.",
                    new
                    {
                        entityTypeName = EntityTypeName,
                        entityNamespace = entity.Namespace(),
                        entityName = entity.Name(),
                        secretNamespace,
                        secretName
                    });
            }

            return clientSecret;
        }

        /// <inheritdoc />
        protected override Task<(bool RequiresFetch, string? Reason)> RequiresAuth0Fetch(V1Client entity,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<(bool RequiresFetch, string? Reason)>((true,
                "inspecting configuration drift between desired and actual state"));
        }

        /// <summary>
        /// Gets enabled connections for a specific client using the direct Auth0 Management API endpoint
        /// Uses GET /api/v2/clients/{id}/connections which is more efficient than fetching all connections
        /// </summary>
        /// <param name="tenantApiAccess">Tenant API access for credentials and tokens</param>
        /// <param name="clientId">The client ID to get connections for</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>List of connections enabled for the client</returns>
        private async Task<List<Connection>> GetClientConnectionsAsync(ITenantApiAccess tenantApiAccess,
            string clientId, string defaultNamespace, CancellationToken cancellationToken)
        {
            LogAuth0Read($"Getting enabled connections for client: {clientId}",
                "A0Connection", clientId, defaultNamespace, "get_client_connections_direct");

            // Use the direct Auth0 Management API endpoint: GET /api/v2/clients/{id}/connections
            var requestUri = new Uri(tenantApiAccess.BaseUri, $"clients/{clientId}/connections");

            using var response = await SendWithTokenAndRetryAsync(tenantApiAccess,
                () => new HttpRequestMessage(HttpMethod.Get, requestUri),
                operation: "get_client_connections",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var failureBody = await ReadBodySafelyAsync(response, cancellationToken);
                await ThrowFromHttpFailureAsync(response, failureBody, "get_client_connections",
                    connectionId: "-", clientId, cancellationToken);
            }

            {
                var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
                var responseData =
                    Newtonsoft.Json.JsonConvert.DeserializeObject<ClientConnectionsResponse>(jsonContent);
                var connections = responseData?.Connections;

                Logger.LogInformationJson(
                    $"{EntityTypeName} retrieved {connections?.Count ?? 0} connections directly for client {clientId}",
                    new
                    {
                        entityTypeName = EntityTypeName,
                        clientId,
                        connectionCount = connections?.Count ?? 0,
                        operation = "get_client_connections_direct",
                        baseUri = tenantApiAccess.BaseUri.ToString()
                    });

                return connections ?? new List<Connection>();
            }
        }


        internal async Task ReconcileEnabledConnections(ITenantApiAccess tenantApiAccess, string clientId,
            V1ConnectionReference[]? enabledConnectionRefs, string defaultNamespace,
            string entityName, CancellationToken cancellationToken)
        {
            try
            {
                var (currentConnectionIds, desiredConnectionIds) =
                    await GetConnectionSetsForReconciliation(tenantApiAccess, clientId, enabledConnectionRefs,
                        defaultNamespace, cancellationToken);
                var (connectionsToAdd, connectionsToRemove) =
                    CalculateConnectionDifferences(currentConnectionIds, desiredConnectionIds);

                await ApplyConnectionChanges(tenantApiAccess, clientId, connectionsToAdd, connectionsToRemove,
                    defaultNamespace, entityName, cancellationToken);
                LogReconciliationResult(clientId, currentConnectionIds.Count, connectionsToAdd.Count,
                    connectionsToRemove.Count);
            }
            catch (Exception ex)
            {
                LogReconciliationError(clientId, ex);
                throw;
            }
        }

        private async Task<(HashSet<string> currentConnectionIds, HashSet<string> desiredConnectionIds)>
            GetConnectionSetsForReconciliation(
                ITenantApiAccess tenantApiAccess, string clientId, V1ConnectionReference[]? enabledConnectionRefs,
                string defaultNamespace, CancellationToken cancellationToken)
        {
            Logger.LogInformationJson($"{EntityTypeName} getting current enabled connections for client {clientId}", new
            {
                entityTypeName = EntityTypeName,
                clientId,
                operation = "get_current_connections"
            });

            var currentConnections = await GetClientConnectionsAsync(tenantApiAccess, clientId, defaultNamespace, cancellationToken);
            var currentConnectionIds =
                currentConnections.Select(c => c.Id).Where(id => !string.IsNullOrEmpty(id)).ToHashSet();

            var desiredConnectionIds = new HashSet<string>();
            if (enabledConnectionRefs is { Length: > 0 })
            {
                var resolvedIds =
                    await ResolveConnectionRefsToIds(enabledConnectionRefs, defaultNamespace, cancellationToken);
                if (resolvedIds is not null)
                {
                    foreach (var connectionId in resolvedIds)
                    {
                        desiredConnectionIds.Add(connectionId);
                    }
                }
            }

            return (currentConnectionIds, desiredConnectionIds);
        }

        private (List<string> connectionsToAdd, List<string> connectionsToRemove) CalculateConnectionDifferences(
            HashSet<string> currentConnectionIds, HashSet<string> desiredConnectionIds)
        {
            Logger.LogInformationJson($"{EntityTypeName} calculating connection differences", new
            {
                entityTypeName = EntityTypeName,
                currentCount = currentConnectionIds.Count,
                desiredCount = desiredConnectionIds.Count,
                operation = "calculate_differences"
            });

            var connectionsToAdd = desiredConnectionIds.Except(currentConnectionIds).ToList();
            var connectionsToRemove = currentConnectionIds.Except(desiredConnectionIds).ToList();

            return (connectionsToAdd, connectionsToRemove);
        }

        private async Task ApplyConnectionChanges(ITenantApiAccess tenantApiAccess, string clientId,
            List<string> connectionsToAdd, List<string> connectionsToRemove,
            string entityNamespace, string entityName,
            CancellationToken cancellationToken)
        {
            foreach (var connectionId in connectionsToAdd)
                await EnableClientOnConnectionAsync(tenantApiAccess, connectionId, clientId,
                    entityNamespace, entityName, cancellationToken);

            foreach (var connectionId in connectionsToRemove)
                await DisableClientOnConnectionAsync(tenantApiAccess, connectionId, clientId,
                    entityNamespace, entityName, cancellationToken);
        }

        private void LogReconciliationResult(string clientId, int currentConnectionCount, int addedCount,
            int removedCount)
        {
            if (addedCount == 0 && removedCount == 0)
            {
                Logger.LogInformationJson(
                    $"{EntityTypeName} connections already in desired state for client {clientId}", new
                    {
                        entityTypeName = EntityTypeName,
                        clientId,
                        connectionCount = currentConnectionCount,
                        operation = "reconcile_connections",
                        status = "no_changes_needed"
                    });
            }
        }

        private void LogReconciliationError(string clientId, Exception ex)
        {
            Logger.LogErrorJson(
                $"{EntityTypeName} failed to reconcile enabled connections for client {clientId}: {ex.Message}", new
                {
                    entityTypeName = EntityTypeName,
                    clientId,
                    operation = "reconcile_connections",
                    errorMessage = ex.Message,
                    status = "failed"
                }, ex);
        }

        /// <summary>PATCH /api/v2/connections/{connectionId}/clients with <c>[{client_id, status:true}]</c> — enables a single client on a connection.</summary>
        /// <remarks>
        /// CR-4 contract: callers must verify <see cref="V1EntityPolicyType.Update"/> is in
        /// <c>entity.Spec.Policy</c> before invoking. The canonical gate lives at
        /// <see cref="V1TenantEntityController{TEntity,TConf}.ApplyUpdatesIfNeeded"/>. Bypassing
        /// this is a programmer bug.
        /// </remarks>
        internal Task EnableClientOnConnectionAsync(ITenantApiAccess tenantApiAccess, string connectionId,
            string clientId, string entityNamespace, string entityName,
            CancellationToken cancellationToken)
        {
            var driftContext = BuildMembershipDriftContext(
                clientId, connectionId,
                changeType: DriftChangeType.Added,
                reasonVerb: "enable");

            return PatchConnectionClientsAsync(tenantApiAccess, connectionId, clientId,
                status: true,
                operation: "enable_client_on_connection",
                entityNamespace: entityNamespace,
                entityName: entityName,
                driftContext: driftContext,
                cancellationToken);
        }

        /// <summary>PATCH /api/v2/connections/{connectionId}/clients with <c>[{client_id, status:false}]</c> — disables a single client on a connection.</summary>
        /// <remarks>
        /// CR-4 contract: callers must verify <see cref="V1EntityPolicyType.Update"/> is in
        /// <c>entity.Spec.Policy</c> before invoking. The canonical gate lives at
        /// <see cref="V1TenantEntityController{TEntity,TConf}.ApplyUpdatesIfNeeded"/>. Bypassing
        /// this is a programmer bug.
        /// </remarks>
        internal Task DisableClientOnConnectionAsync(ITenantApiAccess tenantApiAccess, string connectionId,
            string clientId, string entityNamespace, string entityName,
            CancellationToken cancellationToken)
        {
            var driftContext = BuildMembershipDriftContext(
                clientId, connectionId,
                changeType: DriftChangeType.Removed,
                reasonVerb: "disable");

            return PatchConnectionClientsAsync(tenantApiAccess, connectionId, clientId,
                status: false,
                operation: "disable_client_on_connection",
                entityNamespace: entityNamespace,
                entityName: entityName,
                driftContext: driftContext,
                cancellationToken);
        }

        /// <summary>
        /// Builds the synthetic <see cref="DriftLogContext"/> describing a single
        /// client/connection membership transition. The "drift" here is itself the membership
        /// change — modeled as one <see cref="DriftField"/> on
        /// <c>spec.conf.enabled_connections</c>. The connection ID lives in
        /// <see cref="DriftField.BeforeValue"/> / <see cref="DriftField.AfterValue"/>, not in
        /// the field path — otherwise Datadog log-search facets keyed on
        /// <c>@driftFields[*].fieldPath</c> would have unbounded cardinality (one bucket per
        /// connection ID ever processed) instead of a stable handful of conceptual operations.
        /// </summary>
        private static DriftLogContext BuildMembershipDriftContext(string clientId, string connectionId,
            DriftChangeType changeType, string reasonVerb)
        {
            var field = new DriftField(
                FieldPath: "spec.conf.enabled_connections",
                ChangeType: changeType,
                BeforeValue: changeType == DriftChangeType.Added ? null : connectionId,
                AfterValue: changeType == DriftChangeType.Added ? connectionId : null);

            return new DriftLogContext(
                ReconciliationType.Drift,
                new[] { field },
                $"client {clientId} connection-membership change: {reasonVerb} {connectionId}");
        }

        /// <summary>
        /// Issues a single <c>PATCH /api/v2/connections/{connectionId}/clients</c> with a one-row
        /// body <c>[{client_id, status}]</c> — Auth0's replacement for the deprecated
        /// <c>enabled_clients</c> field. Success is HTTP 204 (No Content); any other status surfaces
        /// via <see cref="ThrowFromHttpFailureAsync"/>. No benign-4xx handling is required because
        /// the endpoint is idempotent and returns 204 regardless of prior membership state.
        /// </summary>
        private async Task PatchConnectionClientsAsync(ITenantApiAccess tenantApiAccess, string connectionId,
            string clientId, bool status, string operation,
            string entityNamespace, string entityName,
            DriftLogContext driftContext,
            CancellationToken cancellationToken)
        {
            var requestUri = new Uri(tenantApiAccess.BaseUri, $"connections/{connectionId}/clients");
            var payload = Newtonsoft.Json.JsonConvert.SerializeObject(new[]
            {
                new { client_id = clientId, status }
            });

            // Funnel every membership-change write through LogAuth0Write so it shows up in the
            // Datadog "Auth0 writes" tally (@auth0ApiCallType:write) alongside every other write,
            // and carries the same DriftLogContext payload shape. The post-success info-log below
            // stays purely as a success/no-op outcome marker — the drift context lives on the
            // pre-write log to keep a single source of truth.
            LogAuth0Write(
                message: $"{operation} for client {clientId} on connection {connectionId}",
                entityType: EntityTypeName,
                entityName: entityName,
                entityNamespace: entityNamespace,
                purpose: operation,
                driftContext: driftContext);

            using var response = await SendWithTokenAndRetryAsync(tenantApiAccess,
                () => new HttpRequestMessage(HttpMethod.Patch, requestUri)
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json")
                },
                operation, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                Logger.LogInformationJson(
                    $"{EntityTypeName} {entityNamespace}/{entityName} {operation} succeeded for client {clientId} on connection {connectionId}",
                    new
                    {
                        entityTypeName = EntityTypeName,
                        entityNamespace,
                        entityName,
                        connectionId,
                        clientId,
                        operation,
                        status = "success",
                    });
                return;
            }

            var body = await ReadBodySafelyAsync(response, cancellationToken);

            // H3: symmetric failure log. Mirrors the pre-write LogAuth0Write above so the
            // Datadog "Auth0 writes" widget keyed on @auth0ApiCallType:write has both a
            // before-call (Warning) and an after-failure (Error) line for every failed PATCH.
            //
            // reconciliationType is serialized via ToString().ToLowerInvariant() — not as the raw
            // enum — so it matches what _structuredLogJsonOptions emits in LogAuth0Write (where the
            // boxed-enum path runs through the camelCase [JsonConverter] attribute). Without this,
            // LogErrorJson's serializer would emit "reconciliationType":1 here while the success
            // path emits "reconciliationType":"drift", silently breaking any Datadog dashboard keyed
            // on the string value.
            Logger.LogErrorJson(
                $"{EntityTypeName} {entityNamespace}/{entityName} {operation} for client {clientId} on connection {connectionId} FAILED with HTTP {(int)response.StatusCode}",
                new
                {
                    entityTypeName = EntityTypeName,
                    entityNamespace,
                    entityName,
                    connectionId,
                    clientId,
                    operation,
                    status = "failed",
                    httpStatusCode = (int)response.StatusCode,
                    reconciliationType = driftContext.ReconciliationType.ToString().ToLowerInvariant(),
                    driftReason = driftContext.DriftReason,
                });
            await ThrowFromHttpFailureAsync(response, body, operation, connectionId, clientId, cancellationToken);
        }

        /// <summary>
        /// Sends an authenticated HTTP request, evicting and refreshing the bearer token once on a 401 response.
        /// </summary>
        private async Task<HttpResponseMessage> SendWithTokenAndRetryAsync(ITenantApiAccess tenantApiAccess,
            Func<HttpRequestMessage> requestFactory, string operation, CancellationToken cancellationToken)
        {
            var httpClient = _httpClientFactory.CreateClient();

            HttpRequestMessage Build(string token)
            {
                var req = requestFactory();
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                return req;
            }

            var response = await httpClient.SendAsync(Build(await tenantApiAccess.GetAccessTokenAsync(cancellationToken)),
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                response.Dispose();
                await tenantApiAccess.InvalidateAccessTokenAsync(cancellationToken);
                response = await httpClient.SendAsync(Build(await tenantApiAccess.GetAccessTokenAsync(cancellationToken)),
                    cancellationToken);
            }

            return response;
        }

        /// <summary>
        /// Reads the response body, swallowing IO/decode errors so callers can pass the body
        /// through both the benign-error code-check path and the failure-throw path without
        /// re-reading the stream (which Auth0 has already exhausted on the wire).
        /// </summary>
        private static async Task<string> ReadBodySafelyAsync(HttpResponseMessage response,
            CancellationToken cancellationToken)
        {
            try
            {
                return await response.Content.ReadAsStringAsync(cancellationToken);
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Throws an exception that preserves the status code and Auth0 body for the upstream retry+backoff wrapper.
        /// 429 maps to <see cref="RateLimitApiException"/> so the existing rate-limit handler reschedules per <c>X-RateLimit-Reset</c>.
        /// All other non-success statuses map to <see cref="ErrorApiException"/> (mirroring
        /// <c>Auth0.Core.Exceptions.ApiException.CreateSpecificExceptionAsync</c>) so the tier-1
        /// catch in <see cref="V1Controller{TEntity,TConf}"/> reads <c>StatusCode</c>+<c>ApiError</c>
        /// directly and labels the Event accordingly instead of falling into the generic
        /// "Unknown error" branch (CR-1).
        /// Callers own the response lifetime (via <c>using</c>); this method does not dispose it.
        /// </summary>
        private Task ThrowFromHttpFailureAsync(HttpResponseMessage response, string body, string operation,
            string connectionId, string clientId, CancellationToken cancellationToken)
        {
            var statusCode = (int)response.StatusCode;
            var apiError = ParseApiErrorOrEmpty(body);

            if (statusCode == 429)
            {
                var rateLimit = RateLimit.Parse(response.Headers);
                throw new RateLimitApiException(rateLimit, apiError);
            }

            throw new ErrorApiException(response.StatusCode, apiError);
        }

        /// <summary>
        /// Deserializes a JSON Auth0 error body into an <see cref="ApiError"/>, swallowing
        /// malformed bodies so we still propagate a typed exception with the correct
        /// <see cref="HttpStatusCode"/> upstream.
        /// </summary>
        private static ApiError ParseApiErrorOrEmpty(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return new ApiError();
            try
            {
                return JsonConvert.DeserializeObject<ApiError>(body) ?? new ApiError();
            }
            catch
            {
                return new ApiError();
            }
        }

        /// <summary>
        /// Resolves connection references to connection IDs.
        /// </summary>
        /// <param name="refs">Connection references</param>
        /// <param name="defaultNamespace">Default namespace</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Array of resolved connection IDs</returns>
        async Task<string[]?> ResolveConnectionRefsToIds(V1ConnectionReference[]? refs, string defaultNamespace,
            CancellationToken cancellationToken)
        {
            if (refs is null)
                return Array.Empty<string>();

            var resolvedIds = new List<string>(refs.Length);

            foreach (var connectionRef in refs)
            {
                var connectionId = await ResolveConnectionRefToId(connectionRef, defaultNamespace, cancellationToken);
                if (connectionId is null)
                    throw new InvalidOperationException($"Failed to resolve connection reference: {connectionRef}");

                resolvedIds.Add(connectionId);
            }

            return resolvedIds.ToArray();
        }

        /// <summary>
        /// Resolves a single connection reference to a connection ID.
        /// </summary>
        /// <param name="connectionRef">Connection reference</param>
        /// <param name="defaultNamespace">Default namespace</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Resolved connection ID or null if not found</returns>
        async Task<string?> ResolveConnectionRefToId(V1ConnectionReference connectionRef, string defaultNamespace,
            CancellationToken cancellationToken)
        {
            if (!string.IsNullOrEmpty(connectionRef.Id))
            {
                return connectionRef.Id;
            }

            if (!string.IsNullOrEmpty(connectionRef.Name))
            {
                var connectionNamespace = string.IsNullOrEmpty(connectionRef.Namespace) ? defaultNamespace : connectionRef.Namespace;
                var connection =
                    await Kube.GetAsync<V1Connection>(connectionRef.Name, connectionNamespace, cancellationToken);

                if (connection?.Status?.Id is not null)
                {
                    return connection.Status.Id;
                }
            }

            return null;
        }

        protected override string[] GetIncludedFields()
        {
            // Only compare these specific fields for drift detection
            return new[]
            {
                "name",
                "app_type",
                "grant_types",
                "callbacks",
                "allowed_logout_urls",
                "client_metadata",
                "enabled_connections"
            };
        }

        /// <summary>
        /// Enriches the Auth0 client state with actual enabled connections data using IManagementApiClient.
        /// This overload extracts necessary information from the IManagementApiClient to make direct API calls.
        /// </summary>
        /// <param name="clientState">The current client state hashtable from Auth0</param>
        /// <param name="api">The Auth0 Management API client</param>
        /// <param name="clientId">The client ID to get connections for</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The enriched client state with enabled_connections field</returns>
        public async Task<Hashtable> EnrichWithEnabledConnections(Hashtable clientState,
            ITenantApiAccess tenantApiAccess, string clientId, string defaultNamespace,
            CancellationToken cancellationToken)
        {
            try
            {
                Logger.LogInformationJson(
                    $"{EntityTypeName} enriching client state with enabled connections for client {clientId} using IManagementApiClient",
                    new
                    {
                        entityTypeName = EntityTypeName,
                        clientId,
                        operation = "enrich_enabled_connections_api"
                    });

                // Get the current enabled connections from Auth0 using direct HTTP call
                var enabledConnections = await GetClientConnectionsAsync(tenantApiAccess, clientId, defaultNamespace, cancellationToken);

                // Create a list of connection IDs to match the expected format in the client configuration
                // Need to populate `clientState["enabled_connections"]` with an array of hashtables, each containing key="id" and value= the connection ID
                var enabledConnectionsList = enabledConnections
                    .Where(conn => !string.IsNullOrEmpty(conn.Id))
                    .Select(conn => new Hashtable { { "id", conn.Id } })
                    .ToList();

                // Add the enabled_connections field to the client state
                clientState["enabled_connections"] = enabledConnectionsList;

                Logger.LogInformationJson(
                    $"{EntityTypeName} successfully enriched client state with {enabledConnectionsList.Count} enabled connections for client {clientId}",
                    new
                    {
                        entityTypeName = EntityTypeName,
                        clientId,
                        connectionCount = enabledConnectionsList.Count,
                        operation = "enrich_enabled_connections_api",
                        status = "success"
                    });

                return clientState;
            }
            catch (Exception ex)
            {
                Logger.LogErrorJson(
                    $"{EntityTypeName} failed to enrich client state with enabled connections for client {clientId}: {ex.Message}",
                    new
                    {
                        entityTypeName = EntityTypeName,
                        clientId,
                        operation = "enrich_enabled_connections_api",
                        errorMessage = ex.Message,
                        status = "failed"
                    }, ex);

                // Return the original state if enrichment fails to avoid breaking reconciliation
                return clientState;
            }
        }

        /// <summary>
        /// Overrides the base method to enrich client state with actual enabled connections data.
        /// This ensures that connection drift is properly detected by including real connection data from Auth0.
        /// </summary>
        /// <param name="entity">The V1Client entity being processed</param>
        /// <param name="api">The Auth0 Management API client</param>
        /// <param name="tenantApiAccess">Tenant API access for credentials and tokens</param>
        /// <param name="auth0State">The current Auth0 state hashtable</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The enriched Auth0 state with enabled_connections field</returns>
        protected override async Task<Hashtable> EnrichAuth0State(V1Client entity, IManagementApiClient api,
            ITenantApiAccess tenantApiAccess, Hashtable auth0State, CancellationToken cancellationToken)
        {
            try
            {
                Logger.LogInformationJson(
                    $"{EntityTypeName} enriching Auth0 state with enabled connections for client {entity.Status.Id}",
                    new
                    {
                        entityTypeName = EntityTypeName,
                        clientId = entity.Status.Id,
                        operation = "enrich_auth0_state"
                    });

                if (entity.Status.Id is null)
                {
                    Logger.LogWarningJson(
                        $"{EntityTypeName} {entity.Namespace()}/{entity.Name()} has no ID in status, skipping enrichment of enabled connections",
                        new { entityTypeName = EntityTypeName, entityNamespace = entity.Namespace(), entityName = entity.Name() });
                    return auth0State;
                }

                // Enrich with enabled connections using the existing method
                return await EnrichWithEnabledConnections(auth0State, tenantApiAccess, entity.Status.Id, entity.Namespace(), cancellationToken);
            }
            catch (Exception ex)
            {
                Logger.LogWarningJson(
                    $"Failed to enrich Auth0 state with enabled connections for {EntityTypeName} {entity.Namespace()}/{entity.Name()}: {ex.Message}",
                    new
                    {
                        entityTypeName = EntityTypeName,
                        entityNamespace = entity.Namespace(),
                        entityName = entity.Name(),
                        errorMessage = ex.Message,
                        operation = "enrich_auth0_state"
                    });

                // Return original state if enrichment fails to avoid breaking reconciliation
                return auth0State;
            }
        }

        /// <summary>
        /// Overrides the base reset method to clear client-specific status fields and secret data.
        /// </summary>
        /// <param name="entity">The client entity to reset</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task representing the operation</returns>
        protected override async Task<V1Client> ResetEntityStatusForNewTenant(V1Client entity, CancellationToken cancellationToken)
        {
            // Clear client-specific status fields
            entity.Status.Id = null;
            entity.Status.LastConf = null;

            Logger.LogInformationJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} cleared client-specific status fields for new tenant", new
            {
                entityTypeName = EntityTypeName,
                entityNamespace = entity.Namespace(),
                entityName = entity.Name(),
                operation = "reset_client_status"
            });

            // Delete the associated secret for tenant change
            await DeleteSecretForTenantChange(entity, cancellationToken);

            // Update the entity in Kubernetes to persist the changes and return updated entity
            return await UpdateKubernetesStatus(entity, "tenant_change_reset", cancellationToken);
        }

        /// <summary>
        /// Deletes the secret when tenant reference changes to remove stale credentials.
        /// A fresh secret will be created with new client credentials during reconciliation.
        /// </summary>
        /// <param name="entity">The client entity</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task representing the operation</returns>
        private async Task DeleteSecretForTenantChange(V1Client entity, CancellationToken cancellationToken)
        {
            if (entity.Spec.SecretRef is null)
            {
                Logger.LogDebugJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} has no secretRef - skipping secret deletion for tenant change", new
                {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name(),
                    operation = "skip_secret_delete"
                });
                return;
            }

            try
            {
                var secret = await ResolveSecretRef(entity.Spec.SecretRef,
                    string.IsNullOrEmpty(entity.Spec.SecretRef.NamespaceProperty) ? entity.Namespace() : entity.Spec.SecretRef.NamespaceProperty,
                    cancellationToken);

                if (secret is null)
                {
                    Logger.LogDebugJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} referenced secret {entity.Spec.SecretRef.Name} does not exist - nothing to delete", new
                    {
                        entityTypeName = EntityTypeName,
                        entityNamespace = entity.Namespace(),
                        entityName = entity.Name(),
                        secretName = entity.Spec.SecretRef.Name,
                        operation = "secret_not_found_for_delete"
                    });
                    return;
                }

                // Only delete if we are the owner of the secret
                if (!secret.IsOwnedBy(entity))
                {
                    Logger.LogWarningJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} cannot delete secret {entity.Spec.SecretRef.Name} - not owned by this entity", new
                    {
                        entityTypeName = EntityTypeName,
                        entityNamespace = entity.Namespace(),
                        entityName = entity.Name(),
                        secretName = entity.Spec.SecretRef.Name,
                        operation = "secret_not_owned"
                    });
                    return;
                }

                Logger.LogWarningJson($"*** SECRET DELETION *** {EntityTypeName} {entity.Namespace()}/{entity.Name()} deleting secret {entity.Spec.SecretRef.Name} due to tenant reference change - stale credentials must be removed", new
                {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name(),
                    secretName = entity.Spec.SecretRef.Name,
                    secretNamespace = secret.Namespace(),
                    operation = "secret_delete_for_tenant_change"
                });

                await Kube.DeleteAsync<V1Secret>(secret.Name(), secret.Namespace(), cancellationToken);

                Logger.LogInformationJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} successfully deleted secret {entity.Spec.SecretRef.Name} for tenant change - new secret will be created with fresh credentials", new
                {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name(),
                    secretName = entity.Spec.SecretRef.Name,
                    secretNamespace = secret.Namespace(),
                    operation = "secret_delete_complete"
                });

            }
            catch (Exception ex)
            {
                Logger.LogErrorJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} failed to delete secret {entity.Spec.SecretRef?.Name} during tenant change: {ex.Message}", new
                {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name(),
                    secretName = entity.Spec.SecretRef?.Name,
                    errorMessage = ex.Message,
                    operation = "secret_delete_failed"
                }, ex);

                // Don't throw - secret deletion failure shouldn't block tenant change handling
            }
        }
    }
}
