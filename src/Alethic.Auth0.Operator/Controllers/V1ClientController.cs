using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Alethic.Auth0.Operator.Core.Models.Client;
using Alethic.Auth0.Operator.Helpers;
using Alethic.Auth0.Operator.Models;
using Alethic.Auth0.Operator.Options;
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
    [EntityRbac(typeof(V1Secret), Verbs = RbacVerb.All)]
    [EntityRbac(typeof(Eventsv1Event), Verbs = RbacVerb.All)]
    public class V1ClientController :
        V1TenantEntityController<V1Client, V1Client.SpecDef, V1Client.StatusDef, ClientConf>,
        IEntityController<V1Client>
    {
        readonly IMemoryCache _clientCache;

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
        }

        /// <inheritdoc />
        protected override string EntityTypeName => "A0Client";

        /// <inheritdoc />
        protected override async Task<Hashtable?> Get(IManagementApiClient api, string id, string defaultNamespace,
            CancellationToken cancellationToken)
        {
            try
            {
                LogAuth0ApiCall($"Getting Auth0 client with ID: {id}", "read", "A0Client", id, defaultNamespace);
                return TransformToSystemTextJson<Hashtable>(await api.Clients.GetAsync(id,
                    cancellationToken: cancellationToken));
            }
            catch (ErrorApiException e) when (e.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
            catch (Exception e)
            {
                Logger.LogError(e, "Error retrieving {EntityTypeName} with ID {Id}: {Message}", EntityTypeName, id,
                    e.Message);
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
                Logger.LogInformation(
                    "{EntityTypeName} {EntityNamespace}/{EntityName} starting client lookup with find criteria: ClientId={ClientId}, CallbackUrls=[{CallbackUrls}], MatchMode={MatchMode}",
                    EntityTypeName, entity.Namespace(), entity.Name(),
                    spec.Find.ClientId ?? "null",
                    spec.Find.CallbackUrls != null ? string.Join(", ", spec.Find.CallbackUrls) : "null",
                    spec.Find.CallbackUrlMatchMode ?? "strict");

                if (spec.Find.ClientId is string clientId)
                {
                    Logger.LogDebug(
                        "{EntityTypeName} {EntityNamespace}/{EntityName} initiating client_id lookup: {ClientId}",
                        EntityTypeName, entity.Namespace(), entity.Name(), clientId);

                    try
                    {
                        LogAuth0ApiCall($"Getting Auth0 client by client ID: {clientId}", "read", "A0Client", entity.Name(), entity.Namespace());
                        var client = await api.Clients.GetAsync(clientId, "client_id,name",
                            cancellationToken: cancellationToken);
                        Logger.LogInformation(
                            "{EntityTypeName} {EntityNamespace}/{EntityName} client_id lookup SUCCESSFUL - found existing client: {Name} (ClientId: {ClientId})",
                            EntityTypeName, entity.Namespace(), entity.Name(), client.Name, client.ClientId);
                        return client.ClientId;
                    }
                    catch (ErrorApiException e) when (e.StatusCode == HttpStatusCode.NotFound)
                    {
                        Logger.LogWarning(
                            "{EntityTypeName} {EntityNamespace}/{EntityName} client_id lookup FAILED - could not find client with id {ClientId}",
                            EntityTypeName, entity.Namespace(), entity.Name(), clientId);
                        return null;
                    }
                }

                if (spec.Find.CallbackUrls is { Length: > 0 } callbackUrls)
                {
                    Logger.LogInformation(
                        "{EntityTypeName} {EntityNamespace}/{EntityName} initiating callback URL lookup: URLs=[{CallbackUrls}], Mode={MatchMode}",
                        EntityTypeName, entity.Namespace(), entity.Name(),
                        string.Join(", ", callbackUrls),
                        spec.Find.CallbackUrlMatchMode ?? "strict");

                    var result = await FindByCallbackUrls(api, entity, callbackUrls, spec.Find.CallbackUrlMatchMode,
                        cancellationToken);

                    if (result != null)
                    {
                        Logger.LogInformation(
                            "{EntityTypeName} {EntityNamespace}/{EntityName} callback URL lookup SUCCESSFUL - found client with id: {ClientId}",
                            EntityTypeName, entity.Namespace(), entity.Name(), result);
                    }
                    else
                    {
                        Logger.LogInformation(
                            "{EntityTypeName} {EntityNamespace}/{EntityName} callback URL lookup FAILED - no matching client found",
                            EntityTypeName, entity.Namespace(), entity.Name());
                    }

                    return result;
                }

                Logger.LogInformation(
                    "{EntityTypeName} {EntityNamespace}/{EntityName} find operation completed - no valid lookup criteria provided",
                    EntityTypeName, entity.Namespace(), entity.Name());
                return null;
            }
            else
            {
                Logger.LogDebug(
                    "{EntityTypeName} {EntityNamespace}/{EntityName} no find criteria specified - falling back to name-based lookup",
                    EntityTypeName, entity.Namespace(), entity.Name());

                var conf = spec.Init ?? spec.Conf;
                if (conf is null)
                    return null;

                Logger.LogDebug(
                    "{EntityTypeName} {EntityNamespace}/{EntityName} initiating name-based lookup for client: {ClientName}",
                    EntityTypeName, entity.Namespace(), entity.Name(), conf.Name);

                LogAuth0ApiCall($"Getting all Auth0 clients for name-based lookup", "read", "A0Client", entity.Name(), entity.Namespace());
                var list = await GetAllClientsWithPagination(api, cancellationToken);
                Logger.LogDebug("{EntityTypeName} {EntityNamespace}/{EntityName} searched {Count} clients for name-based lookup",
                    EntityTypeName, entity.Namespace(), entity.Name(), list.Count);
                var self = list.FirstOrDefault(i => i.Name == conf.Name);

                if (self != null)
                {
                    Logger.LogInformation(
                        "{EntityTypeName} {EntityNamespace}/{EntityName} name-based lookup SUCCESSFUL - found client: {ClientName} (ClientId: {ClientId})",
                        EntityTypeName, entity.Namespace(), entity.Name(), conf.Name, self.ClientId);
                }
                else
                {
                    Logger.LogInformation(
                        "{EntityTypeName} {EntityNamespace}/{EntityName} name-based lookup FAILED - no client found with name: {ClientName}",
                        EntityTypeName, entity.Namespace(), entity.Name(), conf.Name);
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
                Logger.LogWarning(
                    "{EntityTypeName} {EntityNamespace}/{EntityName} targetCallbackUrls is null or empty",
                    EntityTypeName, entity.Namespace(), entity.Name());
                return null;
            }

            foreach (var url in targetCallbackUrls)
            {
                if (string.IsNullOrWhiteSpace(url))
                {
                    Logger.LogWarning(
                        "{EntityTypeName} {EntityNamespace}/{EntityName} callback URL is null or empty",
                        EntityTypeName, entity.Namespace(), entity.Name());
                    return null;
                }

                if (!Uri.TryCreate(url, UriKind.Absolute, out _))
                {
                    Logger.LogWarning(
                        "{EntityTypeName} {EntityNamespace}/{EntityName} invalid callback URL format: {CallbackUrl}",
                        EntityTypeName, entity.Namespace(), entity.Name(), url);
                    return null;
                }
            }

            var isStrictMode = !string.Equals(matchMode, "loose", StringComparison.OrdinalIgnoreCase);
            var modeName = isStrictMode ? "strict" : "loose";

            Logger.LogDebug(
                "{EntityTypeName} {EntityNamespace}/{EntityName} executing callback URL search with {Mode} mode matching against Auth0 clients",
                EntityTypeName, entity.Namespace(), entity.Name(), modeName);

            LogAuth0ApiCall($"Getting all Auth0 clients for callback URL lookup", "read", "A0Client", entity.Name(), entity.Namespace());
            var clients = await GetAllClientsWithPagination(api, cancellationToken);

            var matchingClients = clients
                .Where(client => HasMatchingCallbackUrls(client, targetCallbackUrls, isStrictMode)).ToList();

            if (matchingClients.Count == 0)
            {
                Logger.LogDebug(
                    "{EntityTypeName} {EntityNamespace}/{EntityName} no clients matched callback URL criteria ({Mode} mode)",
                    EntityTypeName, entity.Namespace(), entity.Name(), modeName);
                return null;
            }

            if (matchingClients.Count > 1)
            {
                Logger.LogWarning(
                    "{EntityTypeName} {EntityNamespace}/{EntityName} found multiple clients ({Count}) matching callback URL criteria ({Mode} mode). Target URLs: {TargetUrls}. Using first match: {ClientName} ({ClientId})",
                    EntityTypeName, entity.Namespace(), entity.Name(), matchingClients.Count, modeName,
                    string.Join(", ", targetCallbackUrls), matchingClients[0].Name, matchingClients[0].ClientId);
            }

            var selectedClient = matchingClients[0];
            Logger.LogDebug(
                "{EntityTypeName} {EntityNamespace}/{EntityName} selected client for callback URL match ({Mode} mode): {Name} ({ClientId})",
                EntityTypeName, entity.Namespace(), entity.Name(), modeName, selectedClient.Name,
                selectedClient.ClientId);

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
                return targetUrls.All(targetUrl => client.Callbacks.Contains(targetUrl));
            }
            else
            {
                // Loose mode: AT LEAST ONE target URL must be found in client's callbacks
                return targetUrls.Any(targetUrl => client.Callbacks.Contains(targetUrl));
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
            Logger.LogInformation("{EntityTypeName} creating client in Auth0 with name: {ClientName}", EntityTypeName,
                conf.Name);
            
            ClientCreateRequest createRequest;
            try
            {
                createRequest = TransformToNewtonsoftJson<ClientConf, ClientCreateRequest>(conf);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "{EntityTypeName} failed to transform configuration for client creation: {Message}", 
                    EntityTypeName, ex.Message);
                throw;
            }

            try
            {
                LogAuth0ApiCall($"Creating Auth0 client with name: {conf.Name}", "write", "A0Client", conf.Name ?? "unknown", "unknown");
                var self = await api.Clients.CreateAsync(createRequest, cancellationToken);
                var duration = DateTimeOffset.UtcNow - startTime;
                Logger.LogInformation(
                    "{EntityTypeName} successfully created client in Auth0 with ID: {ClientId} and name: {ClientName} in {Duration}ms",
                    EntityTypeName, self.ClientId, conf.Name, duration.TotalMilliseconds);
                return self.ClientId;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "{EntityTypeName} failed to create client in Auth0 with name: {ClientName}: {Message}", EntityTypeName, conf.Name, ex.Message);
                throw;
            }
        }

        /// <inheritdoc />
        protected override async Task Update(IManagementApiClient api, string id, Hashtable? last, ClientConf conf,
            string defaultNamespace, CancellationToken cancellationToken)
        {
            var startTime = DateTimeOffset.UtcNow;
            Logger.LogInformation(
                "{EntityTypeName} updating client in Auth0 with id: {ClientId} and name: {ClientName}", EntityTypeName,
                id, conf.Name);

            // transform initial request
            ClientUpdateRequest req;
            try
            {
                req = TransformToNewtonsoftJson<ClientConf, ClientUpdateRequest>(conf);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "{EntityTypeName} failed to transform configuration for client update: {Message}", 
                    EntityTypeName, ex.Message);
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
                LogAuth0ApiCall($"Updating Auth0 client with ID: {id} and name: {conf.Name}", "write", "A0Client", conf.Name ?? "unknown", "unknown");
                await api.Clients.UpdateAsync(id, req, cancellationToken);
                var duration = DateTimeOffset.UtcNow - startTime;
                Logger.LogInformation(
                    "{EntityTypeName} successfully updated client in Auth0 with id: {ClientId} and name: {ClientName} in {Duration}ms",
                    EntityTypeName, id, conf.Name, duration.TotalMilliseconds);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "{EntityTypeName} failed to update client in Auth0 with id: {ClientId} and name: {ClientName}: {Message}", EntityTypeName, id, conf.Name, ex.Message);
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
                    Logger.LogWarning("Failed to decode existing clientId from secret data: {Message}", ex.Message);
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
                    Logger.LogWarning("Failed to decode existing clientSecret from secret data: {Message}", ex.Message);
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
                    Logger.LogInformation(
                        "{EntityTypeName} {EntityNamespace}/{EntityName} referenced secret {SecretName} which does not exist: creating.",
                        EntityTypeName, entity.Namespace(), entity.Name(), entity.Spec.SecretRef.Name);
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
                        Logger.LogError(ex, "{EntityTypeName} {EntityNamespace}/{EntityName} failed to create secret {SecretName}: {Message}", 
                            EntityTypeName, entity.Namespace(), entity.Name(), entity.Spec.SecretRef.Name, ex.Message);
                        throw;
                    }
                }

                // only apply actual values if we are the owner
                if (secret.IsOwnedBy(entity))
                {
                    var updateNeeded = IsSecretUpdateNeeded(secret, desiredClientId, desiredClientSecret);

                    if (updateNeeded)
                    {
                        Logger.LogInformation(
                            "{EntityTypeName} {EntityNamespace}/{EntityName} referenced secret {SecretName}: updating due to data changes.",
                            EntityTypeName, entity.Namespace(), entity.Name(), entity.Spec.SecretRef.Name);
                        
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
                            Logger.LogInformation(
                                "{EntityTypeName} {EntityNamespace}/{EntityName} successfully updated secret {SecretName}",
                                EntityTypeName, entity.Namespace(), entity.Name(), entity.Spec.SecretRef.Name);
                        }
                        catch (Exception ex)
                        {
                            Logger.LogError(ex, "{EntityTypeName} {EntityNamespace}/{EntityName} failed to update secret {SecretName}: {Message}", 
                                EntityTypeName, entity.Namespace(), entity.Name(), entity.Spec.SecretRef.Name, ex.Message);
                            throw;
                        }
                    }
                }
                else
                {
                    Logger.LogInformation(
                        "{EntityTypeName} {EntityNamespace}/{EntityName} secret {SecretName} exists but is not owned by this client, skipping update",
                        EntityTypeName, entity.Namespace(), entity.Name(), entity.Spec.SecretRef.Name);
                }
            }
            catch (Exception e)
            {
                Logger.LogError(e,
                    "Error applying secret for {EntityTypeName} {EntityNamespace}/{EntityName}: {Message}",
                    EntityTypeName, entity.Namespace(), entity.Name(), e.Message);
                throw;
            }
        }

        /// <inheritdoc />
        protected override async Task Delete(IManagementApiClient api, string id, CancellationToken cancellationToken)
        {
            Logger.LogInformation(
                "{EntityTypeName} deleting client from Auth0 with ID: {ClientId} (reason: Kubernetes entity deleted)",
                EntityTypeName, id);
            try
            {
                LogAuth0ApiCall($"Deleting Auth0 client with ID: {id}", "write", "A0Client", id, "unknown");
                await api.Clients.DeleteAsync(id, cancellationToken);
                Logger.LogInformation("{EntityTypeName} successfully deleted client from Auth0 with ID: {ClientId}",
                    EntityTypeName, id);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "{EntityTypeName} failed to delete client from Auth0 with ID: {ClientId}: {Message}", 
                    EntityTypeName, id, ex.Message);
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
            var secretRef = await ResolveSecretRef(new V1SecretReference { Name = $"{entity.Name()}-auth0" }, entity.Namespace(), cancellationToken);
            
            if (secretRef is null)
            {
                Logger.LogWarning("{EntityTypeName} {Namespace}/{Name} missing client authentication secret.", EntityTypeName, entity.Namespace(), entity.Name());
                return true;
            }

            var secretName = secretRef.Name();
            var secretNamespace = secretRef.Namespace();
            secretNamespace = string.IsNullOrEmpty(secretNamespace) ? entity.Namespace() : secretNamespace;
            
            if (string.IsNullOrWhiteSpace(secretName) || string.IsNullOrWhiteSpace(secretNamespace))
            {
                Logger.LogWarning("{EntityTypeName} {Namespace}/{Name} has invalid client authentication secret reference.", EntityTypeName, entity.Namespace(), entity.Name());
                return true;
            }
            
            var secret = Kube.Get<V1Secret>(secretName, secretNamespace);
            if (secret is null)
            {
                Logger.LogWarning("{EntityTypeName} {Namespace}/{Name} client authentication secret {SecretNamespace}/{SecretName} not found.", EntityTypeName, entity.Namespace(), entity.Name(), secretNamespace, secretName);
                return true;
            }

            if (secret.Data is null)
            {
                Logger.LogWarning("{EntityTypeName} {Namespace}/{Name} client authentication secret {SecretNamespace}/{SecretName} has no data.", EntityTypeName, entity.Namespace(), entity.Name(), secretNamespace, secretName);
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
                Logger.LogWarning("{EntityTypeName} {Namespace}/{Name} client authentication secret {SecretNamespace}/{SecretName} is missing clientId.", EntityTypeName, entity.Namespace(), entity.Name(), secretNamespace, secretName);
                return true;
            }

            if (secret.Data.TryGetValue("clientSecret", out var clientSecretBytes) && clientSecretBytes != null)
            {
                clientSecret = Encoding.UTF8.GetString(clientSecretBytes);
            }
            if (string.IsNullOrWhiteSpace(clientSecret))
            {
                Logger.LogWarning("{EntityTypeName} {Namespace}/{Name} client authentication secret {SecretNamespace}/{SecretName} is missing clientSecret.", EntityTypeName, entity.Namespace(), entity.Name(), secretNamespace, secretName);
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

        /// <inheritdoc />

        protected override DriftDetectionMode GetDriftDetectionMode() => DriftDetectionMode.IncludeSpecificFields;

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
                "client_metadata"
            };
        }
    }
}