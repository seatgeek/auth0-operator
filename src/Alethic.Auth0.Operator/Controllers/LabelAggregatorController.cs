using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Alethic.Auth0.Operator.Core.Models;
using Alethic.Auth0.Operator.Core.Models.Connection;
using Alethic.Auth0.Operator.Extensions;
using Alethic.Auth0.Operator.Models;

using k8s;
using k8s.Models;

using KubeOps.Abstractions.Controller;
using KubeOps.Abstractions.Rbac;
using KubeOps.KubernetesClient;

using Microsoft.Extensions.Logging;

namespace Alethic.Auth0.Operator.Controllers
{
    /// <summary>
    /// Controller that automatically populates A0Connection enabled_clients based on A0Client labels.
    /// Watches A0Client changes and updates corresponding A0Connection resources.
    /// </summary>
    [EntityRbac(typeof(V1Client), Verbs = RbacVerb.List | RbacVerb.Get | RbacVerb.Watch)]
    [EntityRbac(typeof(V1Connection), Verbs = RbacVerb.List | RbacVerb.Get | RbacVerb.Watch | RbacVerb.Update | RbacVerb.Patch)]
    [EntityRbac(typeof(Eventsv1Event), Verbs = RbacVerb.All)]
    public class LabelAggregatorController
    {
        const string ConnectionLabelKey = "auth0.kubernetes.com/connection";
        const string FieldManager = "auth0-operator.kubernetes.auth0.com/label-aggregator";

        readonly IKubernetesClient _kubernetesClient;
        readonly IKubernetes _k8sClient;
        readonly ILogger<LabelAggregatorController> _logger;

        /// <summary>
        /// Sanitizes a connection reference to be valid as a Kubernetes label value.
        /// Replaces invalid characters (/, :) with underscores and ensures compliance with DNS subdomain naming rules.
        /// </summary>
        /// <param name="connectionRef">The connection reference (namespace/name format)</param>
        /// <returns>A sanitized label value</returns>
        private static string SanitizeLabelValue(string connectionRef)
        {
            // Replace forward slashes and other invalid characters with underscores
            // Kubernetes label values must follow DNS subdomain naming rules
            return connectionRef.Replace('/', '_').Replace(':', '_');
        }

        /// <summary>
        /// Converts a sanitized label value back to the original connection reference format.
        /// Reverses the sanitization applied by SanitizeLabelValue.
        /// </summary>
        /// <param name="sanitizedRef">The sanitized label value</param>
        /// <returns>The original connection reference</returns>
        private static string UnsanitizeLabelValue(string sanitizedRef)
        {
            // Convert underscores back to forward slashes for namespace/name format
            // This assumes the first underscore represents the namespace separator
            var firstUnderscoreIndex = sanitizedRef.IndexOf('_');
            if (firstUnderscoreIndex > 0 && firstUnderscoreIndex < sanitizedRef.Length - 1)
            {
                return sanitizedRef.Substring(0, firstUnderscoreIndex) + '/' + sanitizedRef.Substring(firstUnderscoreIndex + 1);
            }
            return sanitizedRef;
        }

        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        /// <param name="kubernetesClient"></param>
        /// <param name="k8sClient"></param>
        /// <param name="logger"></param>
        public LabelAggregatorController(IKubernetesClient kubernetesClient, IKubernetes k8sClient, ILogger<LabelAggregatorController> logger)
        {
            _kubernetesClient = kubernetesClient;
            _k8sClient = k8sClient;
            _logger = logger;
        }

        /// <summary>
        /// Processes A0Client changes to update corresponding A0Connection enabled_clients.
        /// </summary>
        public async Task ProcessClientAsync(V1Client entity, CancellationToken cancellationToken = default)
        {
            _logger.LogInformationJson($"LabelAggregator processing A0Client {entity.Namespace()}/{entity.Name()}", new
            {
                entityType = "A0Client",
                entityNamespace = entity.Namespace(),
                entityName = entity.Name(),
                operation = "reconcile_client"
            });

            var connectionLabel = entity.GetLabel(ConnectionLabelKey);
            if (!string.IsNullOrEmpty(connectionLabel))
            {
                await ProcessClientConnectionLabel(entity, connectionLabel, cancellationToken);
            }

            // Also process removal case - find connections that might reference this client
            await ProcessClientRemoval(entity, cancellationToken);
        }


        /// <summary>
        /// Processes a client's connection label and updates the target connection.
        /// </summary>
        private async Task ProcessClientConnectionLabel(V1Client client, string connectionLabel, CancellationToken cancellationToken)
        {
            var (connectionNamespace, connectionName) = ParseConnectionReference(connectionLabel, client.Namespace());
            
            _logger.LogDebugJson($"LabelAggregator parsed connection reference: {connectionNamespace}/{connectionName}", new
            {
                clientNamespace = client.Namespace(),
                clientName = client.Name(),
                connectionNamespace = connectionNamespace,
                connectionName = connectionName,
                operation = "parse_connection_ref"
            });

            var connection = await GetConnection(connectionNamespace, connectionName, cancellationToken);
            if (connection == null)
            {
                _logger.LogWarningJson($"LabelAggregator could not find target connection {connectionNamespace}/{connectionName} for client {client.Namespace()}/{client.Name()}", new
                {
                    clientNamespace = client.Namespace(),
                    clientName = client.Name(),
                    targetConnectionNamespace = connectionNamespace,
                    targetConnectionName = connectionName,
                    operation = "resolve_connection",
                    status = "not_found"
                });
                return;
            }

            await UpdateConnectionEnabledClients(connection, cancellationToken);
        }

        /// <summary>
        /// Processes client removal to clean up connection references.
        /// </summary>
        private async Task ProcessClientRemoval(V1Client client, CancellationToken cancellationToken)
        {
            // Find all connections that might have this client in enabled_clients
            var connections = await _kubernetesClient.ListAsync<V1Connection>(cancellationToken: cancellationToken);
            
            foreach (var connection in connections)
            {
                await UpdateConnectionEnabledClients(connection, cancellationToken);
            }
        }


        /// <summary>
        /// Updates the enabled_clients field using Server-Side Apply to manage only label-based clients.
        /// </summary>
        private async Task UpdateConnectionEnabledClients(V1Connection connection, CancellationToken cancellationToken)
        {
            var connectionRef = $"{connection.Namespace()}.{connection.Name()}";
            var sanitizedConnectionRef = SanitizeLabelValue(connectionRef);
            
            // Find all clients with labels pointing to this connection
            var labelSelector = $"{ConnectionLabelKey}={sanitizedConnectionRef}";
            var clients = await _kubernetesClient.ListAsync<V1Client>(
                labelSelector: labelSelector,
                cancellationToken: cancellationToken);

            var labelBasedClients = new List<V1ClientReference>();
            
            foreach (var client in clients)
            {
                if (!string.IsNullOrEmpty(client.Status?.Id))
                {
                    labelBasedClients.Add(new V1ClientReference
                    {
                        Id = client.Status.Id,
                        Name = client.Name(),
                        Namespace = client.Namespace()
                    });
                }
            }

            _logger.LogInformationJson($"LabelAggregator found {labelBasedClients.Count} clients with connection label for {connectionRef}", new
            {
                connectionNamespace = connection.Namespace(),
                connectionName = connection.Name(),
                labelBasedClientCount = labelBasedClients.Count,
                operation = "aggregate_clients"
            });

            // Apply using Server-Side Apply with our field manager to own only the label-managed portion
            await ApplyLabelManagedClients(connection, labelBasedClients, cancellationToken);
        }

        /// <summary>
        /// Applies label-managed clients using Server-Side Apply to own only the portion managed by labels.
        /// With proper CRD merge hints (x-kubernetes-list-type: map), this allows kubectl users to manage 
        /// other enabled_clients independently without conflicts.
        /// </summary>
        private async Task ApplyLabelManagedClients(V1Connection connection, List<V1ClientReference> labelBasedClients, CancellationToken cancellationToken)
        {
            try
            {
                // Create JSON patch containing only our label-managed clients
                // The Kubernetes API server will merge this with manual clients automatically
                var enabledClientsJson = string.Join(",", labelBasedClients.Select(c =>
                    $@"{{""id"":""{c.Id}"",""name"":""{c.Name}"",""namespace"":""{c.Namespace}""}}"));

                var jsonPatch = $@"{{
  ""apiVersion"": ""kubernetes.auth0.com/v1"",
  ""kind"": ""A0Connection"",
  ""metadata"": {{
    ""name"": ""{connection.Name()}"",
    ""namespace"": ""{connection.Namespace()}""
  }},
  ""spec"": {{
    ""conf"": {{
      ""enabled_clients"": [{enabledClientsJson}]
    }}
  }}
}}";

                var patch = new V1Patch(jsonPatch, V1Patch.PatchType.ApplyPatch);

                // Use Server-Side Apply without force - SSA merge hints handle field ownership per item
                await _k8sClient.CustomObjects.PatchNamespacedCustomObjectAsync(
                    body: patch,
                    group: "kubernetes.auth0.com",
                    version: "v1", 
                    namespaceParameter: connection.Namespace(),
                    plural: "a0connections",
                    name: connection.Name(),
                    fieldManager: FieldManager,
                    force: false,           // No force needed - per-item ownership via CRD merge hints
                    cancellationToken: cancellationToken
                );

                var connectionNamespace = connection.Namespace();
                var connectionName = connection.Name();
                _logger.LogInformationJson($"LabelAggregator applied {labelBasedClients.Count} label-managed clients to connection {connectionNamespace}/{connectionName}", new
                {
                    connectionNamespace = connectionNamespace,
                    connectionName = connectionName,
                    labelBasedClientCount = labelBasedClients.Count,
                    operation = "apply_label_managed_clients",
                    status = "success"
                });
            }
            catch (Exception ex)
            {
                var connectionNamespace = connection.Namespace();
                var connectionName = connection.Name();
                _logger.LogErrorJson($"LabelAggregator failed to apply label-managed clients for connection {connectionNamespace}/{connectionName}: {ex.Message}", new
                {
                    connectionNamespace = connectionNamespace,
                    connectionName = connectionName,
                    operation = "apply_label_managed_clients",
                    errorMessage = ex.Message,
                    status = "failed"
                }, ex);
                throw;
            }
        }

        /// <summary>
        /// Parses connection reference in format "namespace/name" or "name".
        /// Handles both sanitized (underscore-separated) and unsanitized (slash-separated) formats for backward compatibility.
        /// </summary>
        private static (string Namespace, string Name) ParseConnectionReference(string connectionRef, string defaultNamespace)
        {
            // Try to parse as unsanitized format first (contains /)
            if (connectionRef.Contains('.'))
            {
                var parts = connectionRef.Split('.', StringSplitOptions.RemoveEmptyEntries);
                return parts.Length switch
                {
                    1 => (defaultNamespace, parts[0]),
                    2 => (parts[0], parts[1]),
                    _ => throw new ArgumentException($"Invalid connection reference format: {connectionRef}. Expected 'namespace/name' or 'name'.")
                };
            }
            
            // Try to parse as sanitized format (contains _ but no /)
            if (connectionRef.Contains('_'))
            {
                var unsanitized = UnsanitizeLabelValue(connectionRef);
                var parts = unsanitized.Split('/', StringSplitOptions.RemoveEmptyEntries);
                return parts.Length switch
                {
                    1 => (defaultNamespace, parts[0]),
                    2 => (parts[0], parts[1]),
                    _ => throw new ArgumentException($"Invalid connection reference format: {connectionRef}. Expected 'namespace/name' or 'name'.")
                };
            }
            
            // No special characters, assume it's just a name
            return (defaultNamespace, connectionRef);
        }

        /// <summary>
        /// Retrieves a connection by namespace and name.
        /// </summary>
        private async Task<V1Connection?> GetConnection(string connectionNamespace, string connectionName, CancellationToken cancellationToken)
        {
            try
            {
                return await _kubernetesClient.GetAsync<V1Connection>(connectionName, connectionNamespace, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarningJson($"LabelAggregator could not retrieve connection {connectionNamespace}/{connectionName}: {ex.Message}", new
                {
                    connectionNamespace = connectionNamespace,
                    connectionName = connectionName,
                    operation = "get_connection",
                    errorMessage = ex.Message
                });
                return null;
            }
        }

        public Task ProcessClientDeletionAsync(V1Client entity, CancellationToken cancellationToken = default)
        {
            return ProcessClientRemoval(entity, cancellationToken);
        }

    }
}