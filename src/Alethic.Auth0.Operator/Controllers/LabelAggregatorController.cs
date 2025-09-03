using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Alethic.Auth0.Operator.Core.Models;
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
    public class LabelAggregatorController : IEntityController<V1Client>
    {
        const string ConnectionLabelKey = "auth0.kubernetes.com/connection";
        const string FieldManager = "auth0-operator.kubernetes.auth0.com/label-aggregator";

        readonly IKubernetesClient _kubernetesClient;
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
        /// <param name="logger"></param>
        public LabelAggregatorController(IKubernetesClient kubernetesClient, ILogger<LabelAggregatorController> logger)
        {
            _kubernetesClient = kubernetesClient;
            _logger = logger;
        }

        /// <summary>
        /// Reconciles A0Client changes to update corresponding A0Connection enabled_clients.
        /// </summary>
        public async Task ReconcileAsync(V1Client entity, CancellationToken cancellationToken = default)
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
        /// Updates the enabled_clients field using Server-Side Apply to merge with user entries.
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

            // Apply using Server-Side Apply with our field manager
            await ApplyEnabledClientsField(connection, labelBasedClients, cancellationToken);
        }

        /// <summary>
        /// Applies the enabled_clients field by merging label-discovered clients with existing ones.
        /// This implementation reads the current connection and merges rather than using SSA to avoid conflicts.
        /// </summary>
        private async Task ApplyEnabledClientsField(V1Connection connection, List<V1ClientReference> labelBasedClients, CancellationToken cancellationToken)
        {
            try
            {
                // Get the current connection to merge with existing enabled_clients
                var currentConnection = await _kubernetesClient.GetAsync<V1Connection>(
                    connection.Name(), 
                    connection.Namespace(), 
                    cancellationToken);

                if (currentConnection?.Spec?.Conf == null)
                {
                    _logger.LogWarningJson($"LabelAggregator could not retrieve current connection {connection.Namespace()}/{connection.Name()} for enabled_clients update", new
                    {
                        connectionNamespace = connection.Namespace(),
                        connectionName = connection.Name(),
                        operation = "get_current_connection",
                        status = "failed"
                    });
                    return;
                }

                // Merge existing enabled_clients (user-configured) with label-based ones
                var mergedClients = new Dictionary<string, V1ClientReference>();
                
                // Add existing user-configured clients first
                if (currentConnection.Spec.Conf.EnabledClients != null)
                {
                    foreach (var clientRef in currentConnection.Spec.Conf.EnabledClients)
                    {
                        if (!string.IsNullOrEmpty(clientRef.Id))
                        {
                            mergedClients[clientRef.Id] = clientRef;
                        }
                    }
                }

                // Add label-based clients (these take precedence for the same IDs)
                foreach (var clientRef in labelBasedClients)
                {
                    if (!string.IsNullOrEmpty(clientRef.Id))
                    {
                        mergedClients[clientRef.Id] = clientRef;
                    }
                }

                // Update the connection spec with array format
                currentConnection.Spec.Conf.EnabledClients = mergedClients.Values.ToArray();
                
                await _kubernetesClient.UpdateAsync(currentConnection, cancellationToken);

                _logger.LogInformationJson($"LabelAggregator successfully merged enabled_clients for connection {connection.Namespace()}/{connection.Name()}", new
                {
                    connectionNamespace = connection.Namespace(),
                    connectionName = connection.Name(),
                    totalClientCount = mergedClients.Count,
                    labelBasedClientCount = labelBasedClients.Count,
                    operation = "merge_enabled_clients",
                    status = "success"
                });
            }
            catch (Exception ex)
            {
                _logger.LogErrorJson($"LabelAggregator failed to merge enabled_clients for connection {connection.Namespace()}/{connection.Name()}: {ex.Message}", new
                {
                    connectionNamespace = connection.Namespace(),
                    connectionName = connection.Name(),
                    operation = "merge_enabled_clients",
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

        public Task DeletedAsync(V1Client entity, CancellationToken cancellationToken = default)
        {
            return ProcessClientRemoval(entity, cancellationToken);
        }

    }
}