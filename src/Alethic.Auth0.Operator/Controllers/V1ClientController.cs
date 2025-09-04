using System;
using System.Collections;
using System.Collections.Concurrent;
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
        readonly IMemoryCache _clientCache;
        readonly IMemoryCache _connectionCache;
        static readonly ConcurrentDictionary<string, SemaphoreSlim> _connectionUpdateMutexes = new();

        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        /// <param name="kube"></param>
        /// <param name="requeue"></param>
        /// <param name="cache"></param>
        /// <param name="logger"></param>
        /// <param name="options"></param>
        public V1ClientController(IKubernetesClient kube, EntityRequeue<V1Client> requeue, IMemoryCache cache,
            ILogger<V1ClientController> logger, IOptions<OperatorOptions> options) :
            base(kube, requeue, cache, logger, options)
        {
            _clientCache = cache;
            _connectionCache = cache;
        }

        /// <inheritdoc />
        protected override string EntityTypeName => "A0Client";

        /// <inheritdoc />
        protected override async Task<Hashtable?> Get(IManagementApiClient api, string id, string defaultNamespace,
            CancellationToken cancellationToken)
        {
            try
            {
                LogAuth0ApiCall($"Getting Auth0 client with ID: {id}", Auth0ApiCallType.Read, "A0Client", id, defaultNamespace, "retrieve_client_by_id");
                return TransformToSystemTextJson<Hashtable>(await api.Clients.GetAsync(id,
                    cancellationToken: cancellationToken));
            }
            catch (ErrorApiException e) when (e.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
            catch (Exception e)
            {
                Logger.LogErrorJson($"Error retrieving {EntityTypeName} with ID {id}: {e.Message}", new {
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
                // Log the beginning of find operation with all criteria
                Logger.LogInformationJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} starting client lookup with find criteria: ClientId={spec.Find.ClientId ?? "null"}, CallbackUrls=[{(spec.Find.CallbackUrls != null ? string.Join(", ", spec.Find.CallbackUrls) : "null")}], MatchMode={spec.Find.CallbackUrlMatchMode ?? "strict"}", new {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name(),
                    clientId = spec.Find.ClientId,
                    callbackUrls = spec.Find.CallbackUrls,
                    matchMode = spec.Find.CallbackUrlMatchMode ?? "strict"
                });

                if (spec.Find.ClientId is string clientId)
                {
                    Logger.LogDebugJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} initiating client_id lookup: {clientId}", new {
                        entityTypeName = EntityTypeName,
                        entityNamespace = entity.Namespace(),
                        entityName = entity.Name(),
                        clientId = clientId
                    });

                    try
                    {
                        LogAuth0ApiCall($"Getting Auth0 client by client ID: {clientId}", Auth0ApiCallType.Read, "A0Client", entity.Name(), entity.Namespace(), "retrieve_client_by_clientid");
                        var client = await api.Clients.GetAsync(clientId, "client_id,name",
                            cancellationToken: cancellationToken);
                        Logger.LogInformationJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} client_id lookup SUCCESSFUL - found existing client: {client.Name} (ClientId: {client.ClientId})", new {
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
                        Logger.LogWarningJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} client_id lookup FAILED - could not find client with id {clientId}", new {
                            entityTypeName = EntityTypeName,
                            entityNamespace = entity.Namespace(),
                            entityName = entity.Name(),
                            searchedClientId = clientId
                        });
                        return null;
                    }
                }

                if (spec.Find.CallbackUrls is { Length: > 0 } callbackUrls)
                {
                    Logger.LogInformationJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} initiating callback URL lookup: URLs=[{string.Join(", ", callbackUrls)}], Mode={spec.Find.CallbackUrlMatchMode ?? "strict"}", new {
                        entityTypeName = EntityTypeName,
                        entityNamespace = entity.Namespace(),
                        entityName = entity.Name(),
                        callbackUrls = callbackUrls,
                        matchMode = spec.Find.CallbackUrlMatchMode ?? "strict"
                    });

                    var result = await FindByCallbackUrls(api, entity, callbackUrls, spec.Find.CallbackUrlMatchMode,
                        cancellationToken);

                    if (result != null)
                    {
                        Logger.LogInformationJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} callback URL lookup SUCCESSFUL - found client with id: {result}", new {
                            entityTypeName = EntityTypeName,
                            entityNamespace = entity.Namespace(),
                            entityName = entity.Name(),
                            foundClientId = result
                        });
                    }
                    else
                    {
                        Logger.LogInformationJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} callback URL lookup FAILED - no matching client found", new {
                            entityTypeName = EntityTypeName,
                            entityNamespace = entity.Namespace(),
                            entityName = entity.Name()
                        });
                    }

                    return result;
                }

                Logger.LogInformationJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} find operation completed - no valid lookup criteria provided", new {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name()
                });
                return null;
            }
            else
            {
                Logger.LogDebugJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} no find criteria specified - falling back to name-based lookup", new {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name()
                });

                var conf = spec.Init ?? spec.Conf;
                if (conf is null)
                    return null;

                Logger.LogDebugJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} initiating name-based lookup for client: {conf.Name}", new {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name(),
                    clientName = conf.Name
                });

                LogAuth0ApiCall($"Getting all Auth0 clients for name-based lookup", Auth0ApiCallType.Read, "A0Client", entity.Name(), entity.Namespace(), "retrieve_all_clients_for_name_lookup");
                var list = await GetAllClientsWithPagination(api, cancellationToken);
                Logger.LogDebugJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} searched {list.Count} clients for name-based lookup", new {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name(),
                    clientCount = list.Count
                });
                var self = list.FirstOrDefault(i => string.Equals(i.Name, conf.Name, StringComparison.OrdinalIgnoreCase));

                if (self != null)
                {
                    Logger.LogInformationJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} name-based lookup SUCCESSFUL - found client: {conf.Name} (ClientId: {self.ClientId})", new {
                        entityTypeName = EntityTypeName,
                        entityNamespace = entity.Namespace(),
                        entityName = entity.Name(),
                        foundClientName = conf.Name,
                        foundClientId = self.ClientId
                    });
                }
                else
                {
                    Logger.LogInformationJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} name-based lookup FAILED - no client found with name: {conf.Name}", new {
                        entityTypeName = EntityTypeName,
                        entityNamespace = entity.Namespace(),
                        entityName = entity.Name(),
                        searchedClientName = conf.Name
                    });
                }

                return self?.ClientId;
            }
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
                Logger.LogWarningJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} targetCallbackUrls is null or empty", new {
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
                    Logger.LogWarningJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} callback URL is null or empty", new {
                        entityTypeName = EntityTypeName,
                        entityNamespace = entity.Namespace(),
                        entityName = entity.Name()
                    });
                    return null;
                }

                if (!Uri.TryCreate(url, UriKind.Absolute, out _))
                {
                    Logger.LogWarningJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} invalid callback URL format: {url}", new {
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

            Logger.LogDebugJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} executing callback URL search with {modeName} mode matching against Auth0 clients", new {
                entityTypeName = EntityTypeName,
                entityNamespace = entity.Namespace(),
                entityName = entity.Name(),
                searchMode = modeName
            });

            LogAuth0ApiCall($"Getting all Auth0 clients for callback URL lookup", Auth0ApiCallType.Read, "A0Client", entity.Name(), entity.Namespace(), "retrieve_all_clients_for_callback_lookup");
            var clients = await GetAllClientsWithPagination(api, cancellationToken);

            var matchingClients = clients
                .Where(client => HasMatchingCallbackUrls(client, targetCallbackUrls, isStrictMode)).ToList();

            if (matchingClients.Count == 0)
            {
                Logger.LogDebugJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} no clients matched callback URL criteria ({modeName} mode)", new {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name(),
                    searchMode = modeName
                });
                return null;
            }

            if (matchingClients.Count > 1)
            {
                Logger.LogWarningJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} found multiple clients ({matchingClients.Count}) matching callback URL criteria ({modeName} mode). Target URLs: {string.Join(", ", targetCallbackUrls)}. Using first match: {matchingClients[0].Name} ({matchingClients[0].ClientId})", new {
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
            Logger.LogDebugJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} selected client for callback URL match ({modeName} mode): {selectedClient.Name} ({selectedClient.ClientId})", new {
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
        /// Always fetches client_id, name, and callbacks fields and caches for 5 minutes.
        /// </summary>
        /// <param name="api">Auth0 Management API client</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Complete list of all clients with client_id, name, and callbacks fields</returns>
        private async Task<List<Client>> GetAllClientsWithPagination(IManagementApiClient api, CancellationToken cancellationToken)
        {
            var request = new GetClientsRequest()
            { 
                Fields = "client_id,name,callbacks",
                IncludeFields = true 
            };

            return await Auth0PaginationHelper.GetAllWithPaginationAsync(
                _clientCache,
                Logger,
                api,
                request,
                api.Clients.GetAllAsync,
                "clients",
                cancellationToken);
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
                return targetUrls.All(targetUrl => client.Callbacks.Contains(targetUrl, StringComparer.OrdinalIgnoreCase));
            }
            else
            {
                // Loose mode: AT LEAST ONE target URL must be found in client's callbacks
                return targetUrls.Any(targetUrl => client.Callbacks.Contains(targetUrl, StringComparer.OrdinalIgnoreCase));
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
            Logger.LogInformationJson($"{EntityTypeName} creating client in Auth0 with name: {conf.Name}", new {
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
                Logger.LogErrorJson($"{EntityTypeName} failed to transform configuration for client creation: {ex.Message}", new {
                    entityTypeName = EntityTypeName,
                    errorMessage = ex.Message
                }, ex);
                throw;
            }

            try
            {
                LogAuth0ApiCall($"Creating Auth0 client with name: {conf.Name}", Auth0ApiCallType.Write, "A0Client", conf.Name ?? "unknown", "unknown", "create_client");
                var self = await api.Clients.CreateAsync(createRequest, cancellationToken);
                var duration = DateTimeOffset.UtcNow - startTime;
                Logger.LogInformationJson($"{EntityTypeName} successfully created client in Auth0 with ID: {self.ClientId} and name: {conf.Name} in {duration.TotalMilliseconds}ms", new {
                    entityTypeName = EntityTypeName,
                    clientId = self.ClientId,
                    clientName = conf.Name,
                    durationMs = duration.TotalMilliseconds
                });
                return self.ClientId;
            }
            catch (Exception ex)
            {
                Logger.LogErrorJson($"{EntityTypeName} failed to create client in Auth0 with name: {conf.Name}: {ex.Message}", new {
                    entityTypeName = EntityTypeName,
                    clientName = conf.Name,
                    errorMessage = ex.Message
                }, ex);
                throw;
            }
        }

        /// <inheritdoc />
        protected override async Task Update(IManagementApiClient api, string id, Hashtable? last, ClientConf conf,
            string defaultNamespace, ITenantApiAccess tenantApiAccess, CancellationToken cancellationToken)
        {
            var startTime = DateTimeOffset.UtcNow;
            Logger.LogInformationJson($"{EntityTypeName} updating client in Auth0 with id: {id} and name: {conf.Name}", new {
                entityTypeName = EntityTypeName,
                clientId = id,
                clientName = conf.Name
            });

            // transform initial request
            ClientUpdateRequest req;
            try
            {
                req = TransformToNewtonsoftJson<ClientConf, ClientUpdateRequest>(conf);
            }
            catch (Exception ex)
            {
                Logger.LogErrorJson($"{EntityTypeName} failed to transform configuration for client update: {ex.Message}", new {
                    entityTypeName = EntityTypeName,
                    errorMessage = ex.Message
                }, ex);
                throw;
            }

            // explicitely null out missing metadata if previously present
            if (last is not null && last.ContainsKey("client_metadata") && conf.ClientMetaData != null && last["client_metadata"] is Hashtable lastMetadata)
            {
                // Create a defensive copy of keys to avoid potential enumeration issues
                var keysToProcess = lastMetadata.Keys.Cast<string>().ToList();
                foreach (string key in keysToProcess)
                    if (conf.ClientMetaData.ContainsKey(key) == false)
                        req.ClientMetaData[key] = null;
            }

            try
            {
                LogAuth0ApiCall($"Updating Auth0 client with ID: {id} and name: {conf.Name}", Auth0ApiCallType.Write, "A0Client", conf.Name ?? "unknown", "unknown", "update_client");
                await api.Clients.UpdateAsync(id, req, cancellationToken);

                // Reconcile enabled connections if they are specified in the configuration
                if (conf.EnabledConnections != null)
                {
                    await ReconcileEnabledConnections(tenantApiAccess, id, conf.EnabledConnections, defaultNamespace, cancellationToken);
                }

                var duration = DateTimeOffset.UtcNow - startTime;
                Logger.LogInformationJson($"{EntityTypeName} successfully updated client in Auth0 with id: {id} and name: {conf.Name} in {duration.TotalMilliseconds}ms", new {
                    entityTypeName = EntityTypeName,
                    clientId = id,
                    clientName = conf.Name,
                    durationMs = duration.TotalMilliseconds
                });
            }
            catch (Exception ex)
            {
                Logger.LogErrorJson($"{EntityTypeName} failed to update client in Auth0 with id: {id} and name: {conf.Name}: {ex.Message}", new {
                    entityTypeName = EntityTypeName,
                    clientId = id,
                    clientName = conf.Name,
                    errorMessage = ex.Message
                }, ex);
                throw;
            }
        }

        /// <inheritdoc />
        protected override async Task ApplyStatus(IManagementApiClient api, V1Client entity, Hashtable lastConf,
            string defaultNamespace, CancellationToken cancellationToken)
        {
            if (lastConf is not null)
            {

                // Always attempt to apply secret if secretRef is specified
                // Extract the client ID and secret from the lastConf (Auth0 Management API response)
                if (entity.Spec.SecretRef is not null)
                {
                    var desiredClientId = lastConf.ContainsKey("client_id") ? (string?)lastConf["client_id"] : null;
                    var desiredClientSecret = lastConf.ContainsKey("client_secret") ? (string?)lastConf["client_secret"] : null;

                    if (!string.IsNullOrEmpty(desiredClientId) && !string.IsNullOrEmpty(desiredClientSecret))
                    {
                        await ApplySecret(entity, defaultNamespace, desiredClientId, desiredClientSecret, cancellationToken);
                    }
                }

                if (lastConf.ContainsKey("client_id"))
                    lastConf.Remove("client_id");
                if (lastConf.ContainsKey("client_secret"))
                    lastConf.Remove("client_secret");
            }

            await base.ApplyStatus(api, entity, lastConf, defaultNamespace, cancellationToken);
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
            var currentClientId = secret.StringData?.TryGetValue("clientId", out var existingClientId) == true ? existingClientId : null;
            var currentClientSecret = secret.StringData?.TryGetValue("clientSecret", out var existingClientSecret) == true ? existingClientSecret : null;

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
                    Logger.LogWarningJson($"Failed to decode existing clientId from secret data: {ex.Message}", new {
                        errorMessage = ex.Message
                    });
                    return true; // If we can't decode, assume update is needed
                }
            }

            if (secret.StringData?.ContainsKey("clientSecret") != true && secret.Data?.ContainsKey("clientSecret") == true)
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
                    Logger.LogWarningJson($"Failed to decode existing clientSecret from secret data: {ex.Message}", new {
                        errorMessage = ex.Message
                    });
                    return true; // If we can't decode, assume update is needed
                }
            }

            var clientIdChanged = !string.Equals(currentClientId ?? "", desiredClientId ?? "", StringComparison.Ordinal);
            var clientSecretChanged = !string.Equals(currentClientSecret ?? "", desiredClientSecret ?? "", StringComparison.Ordinal);

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
                    entity.Spec.SecretRef.NamespaceProperty ?? defaultNamespace, cancellationToken);
                
                if (secret is null)
                {
                    Logger.LogInformationJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} referenced secret {entity.Spec.SecretRef.Name} which does not exist: creating.", new {
                        entityTypeName = EntityTypeName,
                        entityNamespace = entity.Namespace(),
                        entityName = entity.Name(),
                        secretName = entity.Spec.SecretRef.Name
                    });
                    try
                    {
                        secret = await Kube.CreateAsync(
                            new V1Secret(
                                    metadata: new V1ObjectMeta(
                                        namespaceProperty: entity.Spec.SecretRef.NamespaceProperty ?? defaultNamespace,
                                        name: entity.Spec.SecretRef.Name))
                                .WithOwnerReference(entity),
                            cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogErrorJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} failed to create secret {entity.Spec.SecretRef.Name}: {ex.Message}", new {
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
                        Logger.LogInformationJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} referenced secret {entity.Spec.SecretRef.Name}: updating due to data changes.", new {
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
                            Logger.LogInformationJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} successfully updated secret {entity.Spec.SecretRef.Name}", new {
                                entityTypeName = EntityTypeName,
                                entityNamespace = entity.Namespace(),
                                entityName = entity.Name(),
                                secretName = entity.Spec.SecretRef.Name
                            });
                        }
                        catch (Exception ex)
                        {
                            Logger.LogErrorJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} failed to update secret {entity.Spec.SecretRef.Name}: {ex.Message}", new {
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
                    Logger.LogInformationJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} secret {entity.Spec.SecretRef.Name} exists but is not owned by this client, skipping update", new {
                        entityTypeName = EntityTypeName,
                        entityNamespace = entity.Namespace(),
                        entityName = entity.Name(),
                        secretName = entity.Spec.SecretRef.Name
                    });
                }
            }
            catch (Exception e)
            {
                Logger.LogErrorJson($"Error applying secret for {EntityTypeName} {entity.Namespace()}/{entity.Name()}: {e.Message}", new {
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
            Logger.LogInformationJson($"{EntityTypeName} deleting client from Auth0 with ID: {id} (reason: Kubernetes entity deleted)", new {
                entityTypeName = EntityTypeName,
                clientId = id,
                reason = "Kubernetes entity deleted"
            });
            try
            {
                LogAuth0ApiCall($"Deleting Auth0 client with ID: {id}", Auth0ApiCallType.Write, "A0Client", id, "unknown", "delete_client");
                await api.Clients.DeleteAsync(id, cancellationToken);
                Logger.LogInformationJson($"{EntityTypeName} successfully deleted client from Auth0 with ID: {id}", new {
                    entityTypeName = EntityTypeName,
                    clientId = id
                });
            }
            catch (Exception ex)
            {
                Logger.LogErrorJson($"{EntityTypeName} failed to delete client from Auth0 with ID: {id}: {ex.Message}", new {
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
                Logger.LogWarningJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} missing client authentication secret.", new {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name()
                });
                return true;
            }

            var secretName = secretRef.Name();
            var secretNamespace = secretRef.Namespace();
            secretNamespace = string.IsNullOrEmpty(secretNamespace) ? entity.Namespace() : secretNamespace;
            
            if (string.IsNullOrWhiteSpace(secretName) || string.IsNullOrWhiteSpace(secretNamespace))
            {
                Logger.LogWarningJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} has invalid client authentication secret reference.", new {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name()
                });
                return true;
            }
            
            var secret = Kube.Get<V1Secret>(secretName, secretNamespace);
            if (secret is null)
            {
                Logger.LogWarningJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} client authentication secret {secretNamespace}/{secretName} not found.", new {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name(),
                    secretNamespace = secretNamespace,
                    secretName = secretName
                });
                return true;
            }

            if (secret.Data is null)
            {
                Logger.LogWarningJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} client authentication secret {secretNamespace}/{secretName} has no data.", new {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name(),
                    secretNamespace = secretNamespace,
                    secretName = secretName
                });
                return true;
            }
            
            string clientId = null;
            string clientSecret = null;
            
            // Verify that both fields clientId and clientSecret are present and non-empty
            // secret.Data contains base64-encoded byte arrays, so we need to decode them to strings
            if (secret.Data.TryGetValue("clientId", out var clientIdBytes) && clientIdBytes != null)
            {
                clientId = Encoding.UTF8.GetString(clientIdBytes);
            }
            if (string.IsNullOrWhiteSpace(clientId))
            {
                Logger.LogWarningJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} client authentication secret {secretNamespace}/{secretName} is missing clientId.", new {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name(),
                    secretNamespace = secretNamespace,
                    secretName = secretName
                });
                return true;
            }

            if (secret.Data.TryGetValue("clientSecret", out var clientSecretBytes) && clientSecretBytes != null)
            {
                clientSecret = Encoding.UTF8.GetString(clientSecretBytes);
            }
            if (string.IsNullOrWhiteSpace(clientSecret))
            {
                Logger.LogWarningJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} client authentication secret {secretNamespace}/{secretName} is missing clientSecret.", new {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name(),
                    secretNamespace = secretNamespace,
                    secretName = secretName
                });
                return true;
            }
            
            return false;
        }

        /// <inheritdoc />
        protected override async Task<(bool RequiresFetch, string? Reason)> RequiresAuth0Fetch(V1Client entity, CancellationToken cancellationToken)
        {
            var secretRequiresRefresh = await ClientAuth0SecretRequiresRefresh(entity, cancellationToken);
            if (secretRequiresRefresh)
            {
                return (true, "client auth0 secret requires refresh");
            }

            return (false, null);
        }

        /// <summary>
        /// Gets a connection from cache with lazy loading and 15-minute timeout.
        /// </summary>
        /// <param name="api">Auth0 Management API client</param>
        /// <param name="connectionId">Connection ID to retrieve</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Connection object or null if not found</returns>
        async Task<Connection?> GetConnectionFromCache(IManagementApiClient api, string connectionId, CancellationToken cancellationToken)
        {
            var cacheKey = $"connection:{connectionId}";
            
            if (_connectionCache.TryGetValue(cacheKey, out Connection? cachedConnection))
            {
                return cachedConnection;
            }

            try
            {
                Logger.LogInformationJson($"{EntityTypeName} loading connection from Auth0 for caching: {connectionId}", new
                {
                    entityTypeName = EntityTypeName,
                    connectionId = connectionId,
                    operation = "cache_load"
                });

                LogAuth0ApiCall($"Getting Auth0 connection for cache: {connectionId}", Auth0ApiCallType.Read, "A0Connection", connectionId, "unknown", "cache_connection_lookup");
                var connection = await api.Connections.GetAsync(connectionId, cancellationToken: cancellationToken);

                if (connection != null)
                {
                    var cacheOptions = new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15)
                    };
                    _connectionCache.Set(cacheKey, connection, cacheOptions);
                    
                    Logger.LogInformationJson($"{EntityTypeName} cached connection: {connectionId} ({connection.Name})", new
                    {
                        entityTypeName = EntityTypeName,
                        connectionId = connectionId,
                        connectionName = connection.Name,
                        operation = "cache_store"
                    });
                }

                return connection;
            }
            catch (ErrorApiException e) when (e.StatusCode == HttpStatusCode.NotFound)
            {
                Logger.LogWarningJson($"{EntityTypeName} connection not found for caching: {connectionId}", new
                {
                    entityTypeName = EntityTypeName,
                    connectionId = connectionId,
                    operation = "cache_load",
                    status = "not_found"
                });
                return null;
            }
        }

        /// <summary>
        /// Invalidates connection cache when a connection is modified.
        /// </summary>
        /// <param name="connectionId">Connection ID to invalidate</param>
        void InvalidateConnectionCache(string connectionId)
        {
            var cacheKey = $"connection:{connectionId}";
            _connectionCache.Remove(cacheKey);
            
            Logger.LogInformationJson($"{EntityTypeName} invalidated connection cache: {connectionId}", new
            {
                entityTypeName = EntityTypeName,
                connectionId = connectionId,
                operation = "cache_invalidate"
            });
        }

        /// <summary>
        /// Gets enabled connections for a specific client using the direct Auth0 Management API endpoint
        /// Uses GET /api/v2/clients/{id}/connections which is more efficient than fetching all connections
        /// </summary>
        /// <param name="tenantApiAccess">Tenant API access for credentials and tokens</param>
        /// <param name="clientId">The client ID to get connections for</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>List of connections enabled for the client</returns>
        private async Task<List<Connection>> GetClientConnectionsAsync(ITenantApiAccess tenantApiAccess, string clientId, CancellationToken cancellationToken)
        {
            LogAuth0ApiCall($"Getting enabled connections for client: {clientId}", Auth0ApiCallType.Read, "A0Connection", "client_direct", "unknown", "get_client_connections_direct");
            
            // Ensure we have a valid access token
            if (!tenantApiAccess.HasValidToken)
            {
                await tenantApiAccess.RegenerateTokenAsync(cancellationToken);
            }
            var accessToken = tenantApiAccess.AccessToken!;
            
            // Use the direct Auth0 Management API endpoint: GET /api/v2/clients/{id}/connections
            var requestUri = new Uri(tenantApiAccess.BaseUri, $"clients/{clientId}/connections");
            
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            
            try
            {
                var response = await httpClient.GetAsync(requestUri, cancellationToken);
                
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    // Token might be expired, try to regenerate
                    Logger.LogInformationJson($"{EntityTypeName} received 401 Unauthorized, regenerating token for client {clientId}", new
                    {
                        entityTypeName = EntityTypeName,
                        clientId = clientId,
                        operation = "token_regeneration"
                    });
                    
                    var newToken = await tenantApiAccess.RegenerateTokenAsync(cancellationToken);
                    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", newToken);
                    
                    response = await httpClient.GetAsync(requestUri, cancellationToken);
                }
                
                response.EnsureSuccessStatusCode();
                
                var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
                var response_data = Newtonsoft.Json.JsonConvert.DeserializeObject<ClientConnectionsResponse>(jsonContent);
                var connections = response_data?.Connections;
                
                Logger.LogInformationJson($"{EntityTypeName} retrieved {connections?.Count ?? 0} connections directly for client {clientId}", new
                {
                    entityTypeName = EntityTypeName,
                    clientId = clientId,
                    connectionCount = connections?.Count ?? 0,
                    operation = "get_client_connections_direct",
                    baseUri = tenantApiAccess.BaseUri.ToString()
                });
                
                return connections ?? new List<Connection>();
            }
            catch (Exception ex)
            {
                Logger.LogErrorJson($"{EntityTypeName} failed to get connections for client {clientId}: {ex.Message}", new
                {
                    entityTypeName = EntityTypeName,
                    clientId = clientId,
                    operation = "get_client_connections_direct",
                    errorMessage = ex.Message
                }, ex);
                throw;
            }
        }
        

        async Task ReconcileEnabledConnections(ITenantApiAccess tenantApiAccess, string clientId, V1ConnectionReference[]? enabledConnectionRefs, string defaultNamespace, CancellationToken cancellationToken)
        {
            try
            {
                Logger.LogInformationJson($"{EntityTypeName} getting current enabled connections for client {clientId}", new
                {
                    entityTypeName = EntityTypeName,
                    clientId = clientId,
                    operation = "get_current_connections"
                });

                var currentConnections = await GetClientConnectionsAsync(tenantApiAccess, clientId, cancellationToken);
                var currentConnectionIds = currentConnections.Select(c => c.Id).Where(id => !string.IsNullOrEmpty(id)).ToHashSet();

                var desiredConnectionIds = new HashSet<string>();
                IManagementApiClient? api = null;
                
                if (enabledConnectionRefs is { Length: > 0 })
                {
                    var resolvedIds = await ResolveConnectionRefsToIds(enabledConnectionRefs, defaultNamespace, cancellationToken);
                    if (resolvedIds is not null)
                    {
                        foreach (var connectionId in resolvedIds)
                        {
                            desiredConnectionIds.Add(connectionId);
                        }
                    }
                }

                Logger.LogInformationJson($"{EntityTypeName} reconciling connections for client {clientId}", new
                {
                    entityTypeName = EntityTypeName,
                    clientId = clientId,
                    currentCount = currentConnectionIds.Count,
                    desiredCount = desiredConnectionIds.Count,
                    operation = "reconcile_connections"
                });

                var connectionsToAdd = desiredConnectionIds.Except(currentConnectionIds).ToList();
                var connectionsToRemove = currentConnectionIds.Except(desiredConnectionIds).ToList();

                // For add/remove operations we need an API client
                if ((connectionsToAdd.Count > 0 || connectionsToRemove.Count > 0) && api == null)
                {
                    // Ensure we have a valid access token
                    if (!tenantApiAccess.HasValidToken)
                    {
                        await tenantApiAccess.RegenerateTokenAsync(cancellationToken);
                    }
                    
                    // Create a legacy ManagementApiClient for backward compatibility with existing update methods
                    api = new ManagementApiClient(tenantApiAccess.AccessToken!, tenantApiAccess.BaseUri);
                }

                // Add missing connections
                if (connectionsToAdd.Count > 0)
                {
                    Logger.LogInformationJson($"{EntityTypeName} adding {connectionsToAdd.Count} connections for client {clientId}", new
                    {
                        entityTypeName = EntityTypeName,
                        clientId = clientId,
                        connectionsToAdd = connectionsToAdd.ToArray(),
                        operation = "add_connections"
                    });

                    foreach (var connectionId in connectionsToAdd)
                    {
                        await AddClientToConnection(api!, connectionId, clientId, cancellationToken);
                    }
                }

                // Remove extra connections
                if (connectionsToRemove.Count > 0)
                {
                    Logger.LogInformationJson($"{EntityTypeName} removing {connectionsToRemove.Count} connections for client {clientId}", new
                    {
                        entityTypeName = EntityTypeName,
                        clientId = clientId,
                        connectionsToRemove = connectionsToRemove.ToArray(),
                        operation = "remove_connections"
                    });

                    foreach (var connectionId in connectionsToRemove)
                    {
                        await RemoveClientFromConnection(api!, connectionId, clientId, cancellationToken);
                    }
                }

                if (connectionsToAdd.Count == 0 && connectionsToRemove.Count == 0)
                {
                    Logger.LogInformationJson($"{EntityTypeName} connections already in desired state for client {clientId}", new
                    {
                        entityTypeName = EntityTypeName,
                        clientId = clientId,
                        connectionCount = currentConnectionIds.Count,
                        operation = "reconcile_connections",
                        status = "no_changes_needed"
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.LogErrorJson($"{EntityTypeName} failed to reconcile enabled connections for client {clientId}: {ex.Message}", new
                {
                    entityTypeName = EntityTypeName,
                    clientId = clientId,
                    operation = "reconcile_connections",
                    errorMessage = ex.Message,
                    status = "failed"
                }, ex);
                throw;
            }
        }

        /// <summary>
        /// Adds a client ID to a connection's enabled_clients field with mutex protection.
        /// </summary>
        /// <param name="api">Auth0 Management API client</param>
        /// <param name="connectionId">Connection ID to update</param>
        /// <param name="clientId">Client ID to add to enabled_clients</param>
        /// <param name="cancellationToken">Cancellation token</param>
        async Task AddClientToConnection(IManagementApiClient api, string connectionId, string clientId, CancellationToken cancellationToken)
        {
            var mutex = _connectionUpdateMutexes.GetOrAdd(connectionId, _ => new SemaphoreSlim(1, 1));
            
            await mutex.WaitAsync(cancellationToken);
            try
            {
                // Preliminary read to get current state
                Logger.LogInformationJson($"{EntityTypeName} reading current connection state before update: {connectionId}", new
                {
                    entityTypeName = EntityTypeName,
                    connectionId = connectionId,
                    clientId = clientId,
                    operation = "preliminary_read"
                });

                LogAuth0ApiCall($"Getting Auth0 connection for enabled_clients update: {connectionId}", Auth0ApiCallType.Read, "A0Connection", connectionId, "unknown", "preliminary_connection_read");
                var currentConnection = await api.Connections.GetAsync(connectionId, cancellationToken: cancellationToken);
                
                if (currentConnection is null)
                {
                    Logger.LogWarningJson($"{EntityTypeName} connection not found for enabled_clients update: {connectionId}", new
                    {
                        entityTypeName = EntityTypeName,
                        connectionId = connectionId,
                        clientId = clientId,
                        operation = "preliminary_read",
                        status = "not_found"
                    });
                    return;
                }

                // Get current enabled clients 
                LogAuth0ApiCall($"Getting Auth0 connection enabled clients: {connectionId}", Auth0ApiCallType.Read, "A0Connection", connectionId, "unknown", "get_connection_enabled_clients");
#pragma warning disable CS0618 // Type or member is obsolete
                var enabledClientIds = currentConnection.EnabledClients?.ToList() ?? new List<string>();
#pragma warning restore CS0618 // Type or member is obsolete

                if (enabledClientIds.Contains(clientId))
                {
                    Logger.LogInformationJson($"{EntityTypeName} client {clientId} already enabled for connection {connectionId}", new
                    {
                        entityTypeName = EntityTypeName,
                        connectionId = connectionId,
                        clientId = clientId,
                        operation = "enabled_clients_check",
                        status = "already_enabled"
                    });
                    return;
                }

                // Add client to enabled_clients
                enabledClientIds.Add(clientId);

                var updateRequest = new ConnectionUpdateRequest
                {
#pragma warning disable CS0618 // Type or member is obsolete
                    EnabledClients = enabledClientIds.ToArray()
#pragma warning restore CS0618 // Type or member is obsolete
                };

                Logger.LogInformationJson($"{EntityTypeName} updating connection {connectionId} to include client {clientId} in enabled_clients", new
                {
                    entityTypeName = EntityTypeName,
                    connectionId = connectionId,
                    clientId = clientId,
                    operation = "update_enabled_clients"
                });

                LogAuth0ApiCall($"Updating Auth0 connection enabled_clients: {connectionId}", Auth0ApiCallType.Write, "A0Connection", connectionId, "unknown", "update_connection_enabled_clients");
                await api.Connections.UpdateAsync(connectionId, updateRequest, cancellationToken);

                // Invalidate connection cache
                InvalidateConnectionCache(connectionId);

                Logger.LogInformationJson($"{EntityTypeName} successfully updated connection {connectionId} enabled_clients to include client {clientId}", new
                {
                    entityTypeName = EntityTypeName,
                    connectionId = connectionId,
                    clientId = clientId,
                    operation = "update_enabled_clients",
                    status = "success"
                });
            }
            catch (Exception ex)
            {
                Logger.LogErrorJson($"{EntityTypeName} failed to update connection {connectionId} enabled_clients for client {clientId}: {ex.Message}", new
                {
                    entityTypeName = EntityTypeName,
                    connectionId = connectionId,
                    clientId = clientId,
                    operation = "update_enabled_clients",
                    errorMessage = ex.Message,
                    status = "failed"
                }, ex);
                throw;
            }
            finally
            {
                mutex.Release();
            }
        }

        /// <summary>
        /// Removes a client ID from a connection's enabled_clients field with mutex protection.
        /// </summary>
        /// <param name="api">Auth0 Management API client</param>
        /// <param name="connectionId">Connection ID to update</param>
        /// <param name="clientId">Client ID to remove from enabled_clients</param>
        /// <param name="cancellationToken">Cancellation token</param>
        async Task RemoveClientFromConnection(IManagementApiClient api, string connectionId, string clientId, CancellationToken cancellationToken)
        {
            var mutex = _connectionUpdateMutexes.GetOrAdd(connectionId, _ => new SemaphoreSlim(1, 1));
            
            await mutex.WaitAsync(cancellationToken);
            try
            {
                // Preliminary read to get current state
                Logger.LogInformationJson($"{EntityTypeName} reading current connection state before removing client: {connectionId}", new
                {
                    entityTypeName = EntityTypeName,
                    connectionId = connectionId,
                    clientId = clientId,
                    operation = "preliminary_read"
                });

                LogAuth0ApiCall($"Getting Auth0 connection for enabled_clients removal: {connectionId}", Auth0ApiCallType.Read, "A0Connection", connectionId, "unknown", "preliminary_connection_read");
                var currentConnection = await api.Connections.GetAsync(connectionId, cancellationToken: cancellationToken);
                
                if (currentConnection is null)
                {
                    Logger.LogWarningJson($"{EntityTypeName} connection not found for enabled_clients removal: {connectionId}", new
                    {
                        entityTypeName = EntityTypeName,
                        connectionId = connectionId,
                        clientId = clientId,
                        operation = "preliminary_read",
                        status = "not_found"
                    });
                    return;
                }

                // Check if client is in enabled_clients
#pragma warning disable CS0618 // Type or member is obsolete
                var enabledClientIds = currentConnection.EnabledClients?.ToList() ?? new List<string>();
#pragma warning restore CS0618 // Type or member is obsolete
                if (!enabledClientIds.Contains(clientId))
                {
                    Logger.LogInformationJson($"{EntityTypeName} client {clientId} not currently enabled for connection {connectionId}", new
                    {
                        entityTypeName = EntityTypeName,
                        connectionId = connectionId,
                        clientId = clientId,
                        operation = "enabled_clients_check",
                        status = "not_enabled"
                    });
                    return;
                }

                // Remove client from enabled_clients
                enabledClientIds.Remove(clientId);

                var updateRequest = new ConnectionUpdateRequest
                {
#pragma warning disable CS0618 // Type or member is obsolete
                    EnabledClients = enabledClientIds.ToArray()
#pragma warning restore CS0618 // Type or member is obsolete
                };

                Logger.LogInformationJson($"{EntityTypeName} updating connection {connectionId} to remove client {clientId} from enabled_clients", new
                {
                    entityTypeName = EntityTypeName,
                    connectionId = connectionId,
                    clientId = clientId,
                    operation = "remove_enabled_clients"
                });

                LogAuth0ApiCall($"Updating Auth0 connection enabled_clients removal: {connectionId}", Auth0ApiCallType.Write, "A0Connection", connectionId, "unknown", "update_connection_remove_clients");
                await api.Connections.UpdateAsync(connectionId, updateRequest, cancellationToken);

                // Invalidate connection cache
                InvalidateConnectionCache(connectionId);

                Logger.LogInformationJson($"{EntityTypeName} successfully removed client {clientId} from connection {connectionId} enabled_clients", new
                {
                    entityTypeName = EntityTypeName,
                    connectionId = connectionId,
                    clientId = clientId,
                    operation = "remove_enabled_clients",
                    status = "success"
                });
            }
            catch (Exception ex)
            {
                Logger.LogErrorJson($"{EntityTypeName} failed to remove client {clientId} from connection {connectionId} enabled_clients: {ex.Message}", new
                {
                    entityTypeName = EntityTypeName,
                    connectionId = connectionId,
                    clientId = clientId,
                    operation = "remove_enabled_clients",
                    errorMessage = ex.Message,
                    status = "failed"
                }, ex);
                throw;
            }
            finally
            {
                mutex.Release();
            }
        }

        /// <summary>
        /// Resolves connection references to connection IDs.
        /// </summary>
        /// <param name="refs">Connection references</param>
        /// <param name="defaultNamespace">Default namespace</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Array of resolved connection IDs</returns>
        async Task<string[]?> ResolveConnectionRefsToIds(V1ConnectionReference[]? refs, string defaultNamespace, CancellationToken cancellationToken)
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
        async Task<string?> ResolveConnectionRefToId(V1ConnectionReference connectionRef, string defaultNamespace, CancellationToken cancellationToken)
        {
            if (!string.IsNullOrEmpty(connectionRef.Id))
            {
                return connectionRef.Id;
            }

            if (!string.IsNullOrEmpty(connectionRef.Name))
            {
                var connectionNamespace = connectionRef.Namespace ?? defaultNamespace;
                var connection = await Kube.GetAsync<V1Connection>(connectionRef.Name, connectionNamespace, cancellationToken);
                
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
    }

    /// <summary>
    /// Wrapper class for Auth0 client connections API response
    /// </summary>
    internal class ClientConnectionsResponse
    {
        [Newtonsoft.Json.JsonProperty("connections")]
        public List<Connection> Connections { get; set; } = new List<Connection>();
    }
}