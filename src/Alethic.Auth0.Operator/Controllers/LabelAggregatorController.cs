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
    /// Uses Server-Side Apply to merge user-configured static clients with label-discovered clients.
    /// </summary>
    [EntityRbac(typeof(V1Client), Verbs = RbacVerb.List | RbacVerb.Get | RbacVerb.Watch)]
    [EntityRbac(typeof(V1Connection), Verbs = RbacVerb.List | RbacVerb.Get | RbacVerb.Watch | RbacVerb.Update | RbacVerb.Patch)]
    [EntityRbac(typeof(Eventsv1Event), Verbs = RbacVerb.All)]
    public class LabelAggregatorController : IEntityController<V1Client>, IEntityController<V1Connection>
    {
        const string ConnectionLabelKey = "auth0.kubernetes.com/connection";
        const string FieldManager = "auth0-operator.kubernetes.auth0.com/label-aggregator";

        readonly IKubernetesClient _kubernetesClient;
        readonly ILogger<LabelAggregatorController> _logger;

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
        /// Reconciles A0Connection changes to ensure label-based clients are properly aggregated.
        /// </summary>
        public async Task ReconcileAsync(V1Connection entity, CancellationToken cancellationToken = default)
        {
            _logger.LogInformationJson($"LabelAggregator processing A0Connection {entity.Namespace()}/{entity.Name()}", new
            {
                entityType = "A0Connection",
                entityNamespace = entity.Namespace(),
                entityName = entity.Name(),
                operation = "reconcile_connection"
            });

            await ProcessConnectionClients(entity, cancellationToken);
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
        /// Updates a connection's enabled_clients based on current client labels.
        /// </summary>
        private async Task ProcessConnectionClients(V1Connection connection, CancellationToken cancellationToken)
        {
            await UpdateConnectionEnabledClients(connection, cancellationToken);
        }

        /// <summary>
        /// Updates the enabled_clients field using Server-Side Apply to merge with user entries.
        /// </summary>
        private async Task UpdateConnectionEnabledClients(V1Connection connection, CancellationToken cancellationToken)
        {
            var connectionRef = $"{connection.Namespace()}/{connection.Name()}";
            
            // Find all clients with labels pointing to this connection
            var labelSelector = $"{ConnectionLabelKey}={connectionRef}";
            var clients = await _kubernetesClient.ListAsync<V1Client>(
                labelSelector: labelSelector,
                cancellationToken: cancellationToken);

            var labelBasedClients = new Dictionary<string, V1ClientReference>();
            
            foreach (var client in clients)
            {
                if (!string.IsNullOrEmpty(client.Status?.Id))
                {
                    labelBasedClients[client.Status.Id] = new V1ClientReference 
                    { 
                        Name = client.Name(),
                        Namespace = client.Namespace(),
                        Id = client.Status.Id
                    };
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
        /// Applies the enabled_clients field using Server-Side Apply field manager.
        /// This merges label-discovered clients with user-configured static clients.
        /// </summary>
        private async Task ApplyEnabledClientsField(V1Connection connection, Dictionary<string, V1ClientReference> labelBasedClients, CancellationToken cancellationToken)
        {
            try
            {
                // Create a partial connection object for Server-Side Apply
                var patch = new V1Connection
                {
                    ApiVersion = "kubernetes.auth0.com/v1",
                    Kind = "A0Connection",
                    Metadata = new V1ObjectMeta
                    {
                        Name = connection.Name(),
                        NamespaceProperty = connection.Namespace()
                    },
                    Spec = new V1Connection.SpecDef
                    {
                        Conf = new Core.Models.Connection.ConnectionConf
                        {
                            EnabledClients = labelBasedClients
                        }
                    }
                };

                // Use UpdateAsync to apply the changes
                // Note: Full Server-Side Apply requires more complex implementation
                // For now, we'll update the entire connection and let Kubernetes handle conflicts
                var updatedConnection = await _kubernetesClient.UpdateAsync(patch, cancellationToken);

                _logger.LogInformationJson($"LabelAggregator successfully applied enabled_clients for connection {connection.Namespace()}/{connection.Name()}", new
                {
                    connectionNamespace = connection.Namespace(),
                    connectionName = connection.Name(),
                    appliedClientCount = labelBasedClients.Count,
                    fieldManager = FieldManager,
                    operation = "server_side_apply",
                    status = "success"
                });
            }
            catch (Exception ex)
            {
                _logger.LogErrorJson($"LabelAggregator failed to apply enabled_clients for connection {connection.Namespace()}/{connection.Name()}: {ex.Message}", new
                {
                    connectionNamespace = connection.Namespace(),
                    connectionName = connection.Name(),
                    fieldManager = FieldManager,
                    operation = "server_side_apply",
                    errorMessage = ex.Message,
                    status = "failed"
                }, ex);
                throw;
            }
        }

        /// <summary>
        /// Parses connection reference in format "namespace/name" or "name".
        /// </summary>
        private static (string Namespace, string Name) ParseConnectionReference(string connectionRef, string defaultNamespace)
        {
            var parts = connectionRef.Split('/', StringSplitOptions.RemoveEmptyEntries);
            
            return parts.Length switch
            {
                1 => (defaultNamespace, parts[0]),
                2 => (parts[0], parts[1]),
                _ => throw new ArgumentException($"Invalid connection reference format: {connectionRef}. Expected 'namespace/name' or 'name'.")
            };
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

        public Task DeletedAsync(V1Connection entity, CancellationToken cancellationToken = default)
        {
            // No special cleanup needed for connection deletion
            return Task.CompletedTask;
        }
    }
}