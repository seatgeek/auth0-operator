using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Alethic.Auth0.Operator.Core.Models.Tenant;
using Alethic.Auth0.Operator.Extensions;
using Alethic.Auth0.Operator.Models;
using Alethic.Auth0.Operator.Options;

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

    [EntityRbac(typeof(V1Tenant), Verbs = RbacVerb.All)]
    [EntityRbac(typeof(V1Secret), Verbs = RbacVerb.List | RbacVerb.Get)]
    [EntityRbac(typeof(Eventsv1Event), Verbs = RbacVerb.All)]
    public class V1TenantController :
        V1Controller<V1Tenant, V1Tenant.SpecDef, V1Tenant.StatusDef, TenantConf>,
        IEntityController<V1Tenant>
    {
        readonly IOptions<OperatorOptions> _options;

        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        /// <param name="kube"></param>
        /// <param name="requeue"></param>
        /// <param name="cache"></param>
        /// <param name="logger"></param>
        /// <param name="options"></param>
        public V1TenantController(IKubernetesClient kube, EntityRequeue<V1Tenant> requeue, IMemoryCache cache, ILogger<V1TenantController> logger, IOptions<OperatorOptions> options) :
            base(kube, requeue, cache, logger)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <inheritdoc />
        protected override string EntityTypeName => "A0Tenant";


        /// <summary>
        /// Override ReconcileAsync to include partition filtering.
        /// </summary>
        /// <param name="entity">The entity to reconcile</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task representing the reconciliation operation</returns>
        public override async Task ReconcileAsync(V1Tenant entity, CancellationToken cancellationToken)
        {
            if (!ShouldProcessEntityByPartition(entity, _options, EntityTypeName))
            {
                return;
            }

            await base.ReconcileAsync(entity, cancellationToken);
        }

        /// <summary>
        /// Gets the list of fields to include in drift detection for tenant configuration.
        /// </summary>
        /// <returns>Array of field names to include in comparison</returns>
        protected string[] GetIncludedFields()
        {
            return new[]
            {
                "friendly_name",
                "picture_url",
                "support_url",
                "enabled_locales",
                "idle_session_lifetime",
                "session_lifetime",
                "sandbox_version",
                "sandbox_versions_available"
            };
        }

        /// <summary>
        /// Gets the list of fields to exclude from drift detection for tenant configuration.
        /// </summary>
        /// <returns>Array of field names to exclude from comparison</returns>
        protected string[] GetExcludedFields()
        {
            return Array.Empty<string>();
        }

        /// <inheritdoc />
        protected override async Task<(bool needsRequeue, V1Tenant updatedEntity)> Reconcile(V1Tenant entity, CancellationToken cancellationToken)
        {
            Logger.LogInformationJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} starting reconciliation", new
            {
                entityTypeName = EntityTypeName,
                entityNamespace = entity.Namespace(),
                entityName = entity.Name()
            });

            var api = await GetTenantApiClientAsync(entity, cancellationToken);
            if (api == null)
            {
                Logger.LogErrorJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} failed to retrieve API client", new
                {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name()
                });
                throw new InvalidOperationException($"{EntityTypeName} {entity.Namespace()}:{entity.Name()} failed to retrieve API client.");
            }

            // Check if configuration changes need to be applied
            var isFirstReconciliation = entity.Status.LastConf is null;
            var hasLocalChanges = entity.Spec.Conf is { } currentConf && !isFirstReconciliation && HasConfigurationChanged(entity, entity.Status.LastConf, currentConf);
            var needsAuth0Fetch = hasLocalChanges || isFirstReconciliation;

            TenantSettings? settings = null;
            if (needsAuth0Fetch)
            {
                var reason = isFirstReconciliation ? "first reconciliation" : "local configuration changes detected";
                Logger.LogInformationJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} fetching tenant settings from Auth0 API (reason: {reason})", new
                {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name(),
                    fetchReason = reason
                });
                LogAuth0ApiCall($"Getting Auth0 tenant settings", Auth0ApiCallType.Read, "A0Tenant", entity.Name(), entity.Namespace(), "retrieve_tenant_settings");
                settings = await api.TenantSettings.GetAsync(cancellationToken: cancellationToken);
                if (settings is null)
                {
                    Logger.LogErrorJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} tenant settings not found in Auth0 API", new
                    {
                        entityTypeName = EntityTypeName,
                        entityNamespace = entity.Namespace(),
                        entityName = entity.Name()
                    });
                    throw new InvalidOperationException($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} cannot be loaded from API.");
                }
                Logger.LogInformationJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} successfully retrieved tenant settings from Auth0", new
                {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name()
                });
            }

            // configuration was specified and needs to be applied
            if (entity.Spec.Conf is { } conf && needsAuth0Fetch)
            {
                // For first reconciliation, check if update is actually needed by comparing with Auth0 state
                // For subsequent reconciliations, we already know changes exist from local comparison
                bool needsUpdate;
                if (isFirstReconciliation)
                {
                    var settingsHashtable = TransformToSystemTextJson<Hashtable>(settings);
                    needsUpdate = HasConfigurationChanged(entity, settingsHashtable, conf);
                    if (needsUpdate)
                    {
                        Logger.LogWarningJson($"*** {EntityTypeName} {entity.Namespace()}/{entity.Name()} DRIFT DETECTED *** First reconciliation - configuration drift detected between Auth0 and desired state - applying updates", new
                        {
                            entityTypeName = EntityTypeName,
                            entityNamespace = entity.Namespace(),
                            entityName = entity.Name(),
                            reconciliationType = "first",
                            action = "applying updates",
                            driftDetected = true
                        });
                    }
                }
                else
                {
                    needsUpdate = hasLocalChanges;
                    Logger.LogInformationJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} local configuration changes detected - applying updates to Auth0", new
                    {
                        entityTypeName = EntityTypeName,
                        entityNamespace = entity.Namespace(),
                        entityName = entity.Name(),
                        changeType = "local configuration changes",
                        action = "applying updates"
                    });
                }

                if (needsUpdate)
                {
                    // verify that no changes to enable_sso are being made
                    if (conf.Flags != null && conf.Flags.EnableSSO != null && settings?.Flags?.EnableSSO != null && conf.Flags.EnableSSO != settings.Flags.EnableSSO)
                    {
                        Logger.LogErrorJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} attempted to modify enable_sso flag from {settings.Flags.EnableSSO} to {conf.Flags.EnableSSO} - operation not allowed", new
                        {
                            entityTypeName = EntityTypeName,
                            entityNamespace = entity.Namespace(),
                            entityName = entity.Name(),
                            currentValue = settings.Flags.EnableSSO,
                            newValue = conf.Flags.EnableSSO,
                            operation = "enable_sso_modification",
                            allowed = false
                        });
                        throw new InvalidOperationException($"{EntityTypeName} {entity.Namespace()}/{entity.Name()}: updating the enable_sso flag is not allowed.");
                    }

                    Logger.LogInformationJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} updating tenant settings in Auth0", new
                    {
                        entityTypeName = EntityTypeName,
                        entityNamespace = entity.Namespace(),
                        entityName = entity.Name(),
                        operation = "update_tenant_settings"
                    });
                    try
                    {
                        // push update to Auth0
                        var req = TransformToNewtonsoftJson<TenantConf, TenantSettingsUpdateRequest>(conf);
                        req.Flags.EnableSSO = null;
                        LogAuth0ApiCall($"Updating Auth0 tenant settings", Auth0ApiCallType.Write, "A0Tenant", entity.Name(), entity.Namespace(), "update_tenant_settings");
                        settings = await api.TenantSettings.UpdateAsync(req, cancellationToken);
                        Logger.LogInformationJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} successfully updated tenant settings in Auth0", new
                        {
                            entityTypeName = EntityTypeName,
                            entityNamespace = entity.Namespace(),
                            entityName = entity.Name(),
                            operation = "update_tenant_settings",
                            status = "success"
                        });
                    }
                    catch (Exception ex)
                    {
                        Logger.LogErrorJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} failed to update tenant settings in Auth0: {ex.Message}", new
                        {
                            entityTypeName = EntityTypeName,
                            entityNamespace = entity.Namespace(),
                            entityName = entity.Name(),
                            operation = "update_tenant_settings",
                            errorMessage = ex.Message,
                            status = "failed"
                        }, ex);
                        throw;
                    }
                }
            }

            // Always retrieve final settings for status update if we made API calls
            if (needsAuth0Fetch)
            {
                Logger.LogInformationJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} retrieving final tenant settings from Auth0 for status update", new
                {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name(),
                    operation = "retrieve_final_settings"
                });
                LogAuth0ApiCall($"Getting Auth0 tenant settings for status update", Auth0ApiCallType.Read, "A0Tenant", entity.Name(), entity.Namespace(), "retrieve_tenant_settings_for_status");
                settings = await api.TenantSettings.GetAsync(cancellationToken: cancellationToken);
                entity.Status.LastConf = TransformToSystemTextJson<Hashtable>(settings);
                try
                {
                    entity = await Kube.UpdateStatusAsync(entity, cancellationToken);
                }
                catch (Exception ex)
                {
                    Logger.LogErrorJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} failed to update Kubernetes status: {ex.Message}", new
                    {
                        entityTypeName = EntityTypeName,
                        entityNamespace = entity.Namespace(),
                        entityName = entity.Name(),
                        operation = "update_kubernetes_status",
                        errorMessage = ex.Message,
                        status = "failed"
                    }, ex);
                    throw;
                }
            }

            Logger.LogInformationJson($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} reconciliation completed successfully", new
            {
                entityTypeName = EntityTypeName,
                entityNamespace = entity.Namespace(),
                entityName = entity.Name(),
                status = "completed"
            });
            await ReconcileSuccessAsync(entity, cancellationToken);

            return (false, entity);
        }

        /// <summary>
        /// Determines if the tenant configuration has changed by comparing the last known state with the desired configuration.
        /// </summary>
        /// <param name="entity">The tenant entity</param>
        /// <param name="lastConf">The last known configuration from Auth0</param>
        /// <param name="desiredConf">The desired configuration from the Kubernetes spec</param>
        /// <returns>True if changes are detected, false if configurations match</returns>
        private bool HasConfigurationChanged(V1Tenant entity, Hashtable? lastConf, TenantConf desiredConf)
        {
            try
            {
                if (lastConf is null)
                {
                    Logger.LogDebugJson($"{EntityTypeName} no previous configuration available - assuming changes exist", new
                    {
                        entityTypeName = EntityTypeName,
                        configurationStatus = "no_previous_config",
                        assumingChanges = true
                    });
                    return true;
                }

                // Convert desired configuration to Auth0 API format for comparison
                var desiredJson = TransformToNewtonsoftJson<TenantConf, object>(desiredConf);
                var desiredHashtable = TransformToSystemTextJson<Hashtable>(desiredJson);

                // Filter fields based on the specific tenant fields we track
                var filteredLast = FilterFieldsForComparison(lastConf);
                var filteredDesired = FilterFieldsForComparison(desiredHashtable);

                var foundConfigChanges = !AreHashtablesEqual(filteredLast, filteredDesired);

                if (foundConfigChanges)
                {
                    LogConfigurationDifferences(entity, filteredLast, filteredDesired);
                }

                return foundConfigChanges;
            }
            catch (Exception ex)
            {
                Logger.LogWarningJson($"{EntityTypeName} error comparing configurations, assuming changes exist: {ex.Message}", new
                {
                    entityTypeName = EntityTypeName,
                    operation = "configuration_comparison",
                    errorMessage = ex.Message,
                    assumingChanges = true
                });
                return true; // Safe fallback: assume changes exist
            }
        }

        /// <summary>
        /// Filters fields for comparison based on the tenant-specific fields we track.
        /// Always attempts to read GetIncludedFields first, then applies GetExcludedFields.
        /// </summary>
        /// <param name="config">The configuration hashtable to filter</param>
        /// <returns>A new hashtable with fields filtered according to included and excluded fields</returns>
        private Hashtable FilterFieldsForComparison(Hashtable config)
        {
            var filtered = new Hashtable();
            var includedFields = GetIncludedFields();
            var excludedFields = GetExcludedFields();

            // If included fields are specified, only include those fields
            if (includedFields.Length > 0)
            {
                var includedFieldsSet = new HashSet<string>(includedFields, StringComparer.OrdinalIgnoreCase);
                foreach (DictionaryEntry entry in config)
                {
                    if (entry.Key is string key && includedFieldsSet.Contains(key))
                    {
                        filtered[key] = entry.Value;
                    }
                }
            }
            else
            {
                // Include all fields initially
                foreach (DictionaryEntry entry in config)
                {
                    filtered[entry.Key] = entry.Value;
                }
            }

            // Then apply excluded fields if any are specified
            if (excludedFields.Length > 0)
            {
                var excludedFieldsSet = new HashSet<string>(excludedFields, StringComparer.OrdinalIgnoreCase);
                var keysToRemove = new List<object>();

                foreach (DictionaryEntry entry in filtered)
                {
                    if (entry.Key is string key && excludedFieldsSet.Contains(key))
                    {
                        keysToRemove.Add(entry.Key);
                    }
                }

                foreach (var key in keysToRemove)
                {
                    filtered.Remove(key);
                }
            }

            return filtered;
        }

        /// <summary>
        /// Performs deep comparison of two hashtables to detect configuration differences.
        /// </summary>
        /// <param name="left">First hashtable to compare</param>
        /// <param name="right">Second hashtable to compare</param>
        /// <returns>True if hashtables are equal, false otherwise</returns>
        private static bool AreHashtablesEqual(Hashtable left, Hashtable right)
        {
            if (left.Count != right.Count)
                return false;

            foreach (DictionaryEntry entry in left)
            {
                if (!right.ContainsKey(entry.Key))
                    return false;

                if (!AreValuesEqual(entry.Value, right[entry.Key]))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Compares two values for equality, handling nested hashtables and arrays.
        /// </summary>
        /// <param name="left">First value to compare</param>
        /// <param name="right">Second value to compare</param>
        /// <returns>True if values are equal, false otherwise</returns>
        private static bool AreValuesEqual(object? left, object? right)
        {
            if (ReferenceEquals(left, right))
                return true;

            if (left is null || right is null)
                return false;

            // Handle nested hashtables
            if (left is Hashtable leftHash && right is Hashtable rightHash)
                return AreHashtablesEqual(leftHash, rightHash);

            // Handle arrays
            if (left is IEnumerable leftEnum && right is IEnumerable rightEnum &&
                !(left is string) && !(right is string))
            {
                var leftArray = leftEnum.Cast<object>().ToArray();
                var rightArray = rightEnum.Cast<object>().ToArray();

                if (leftArray.Length != rightArray.Length)
                    return false;

                for (int i = 0; i < leftArray.Length; i++)
                {
                    if (!AreValuesEqual(leftArray[i], rightArray[i]))
                        return false;
                }

                return true;
            }

            // Use standard equality comparison
            return left.Equals(right);
        }

        /// <summary>
        /// Logs detected configuration differences with detailed field-by-field changes.
        /// </summary>
        /// <param name="entity">The tenant entity</param>
        /// <param name="last">Last known configuration</param>
        /// <param name="desired">Desired configuration</param>
        private void LogConfigurationDifferences(V1Tenant entity, Hashtable last, Hashtable desired)
        {
            var addedFields = new List<string>();
            var modifiedFields = new List<string>();
            var removedFields = new List<string>();

            // Check for added or modified fields
            foreach (DictionaryEntry entry in desired)
            {
                var key = entry.Key.ToString()!;
                if (!last.ContainsKey(entry.Key))
                {
                    addedFields.Add($"{key} = {FormatValueForLogging(entry.Value)}");
                }
                else if (!AreValuesEqual(entry.Value, last[entry.Key]))
                {
                    var oldValue = FormatValueForLogging(last[entry.Key]);
                    var newValue = FormatValueForLogging(entry.Value);
                    modifiedFields.Add($"{key}: {oldValue} → {newValue}");
                }
            }

            // Check for removed fields
            foreach (DictionaryEntry entry in last)
            {
                if (!desired.ContainsKey(entry.Key))
                {
                    var key = entry.Key.ToString()!;
                    removedFields.Add($"{key} = {FormatValueForLogging(entry.Value)}");
                }
            }

            // Log changes by category with detailed before/after values
            if (addedFields.Count > 0)
            {
                Logger.LogWarningJson($"*** {EntityTypeName} {entity.Namespace()}/{entity.Name()} CONFIGURATION CHANGES - ADDED fields: {string.Join(", ", addedFields)}", new
                {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name(),
                    changeType = "ADDED",
                    addedFields,
                    fieldCount = addedFields.Count
                });
            }

            if (modifiedFields.Count > 0)
            {
                Logger.LogWarningJson($"*** {EntityTypeName} {entity.Namespace()}/{entity.Name()} CONFIGURATION CHANGES - MODIFIED fields: {string.Join(", ", modifiedFields)}", new
                {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name(),
                    changeType = "MODIFIED",
                    modifiedFields,
                    fieldCount = modifiedFields.Count
                });
            }

            if (removedFields.Count > 0)
            {
                Logger.LogWarningJson($"*** {EntityTypeName} {entity.Namespace()}/{entity.Name()} CONFIGURATION CHANGES - REMOVED fields: {string.Join(", ", removedFields)}", new
                {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name(),
                    changeType = "REMOVED",
                    removedFields,
                    fieldCount = removedFields.Count
                });
            }

            // Also log a summary count
            var totalChanges = addedFields.Count + modifiedFields.Count + removedFields.Count;
            if (totalChanges > 0)
            {
                Logger.LogWarningJson($"*** {EntityTypeName} {entity.Namespace()}/{entity.Name()} CONFIGURATION DRIFT DETECTED *** {totalChanges} field changes ({addedFields.Count} added, {modifiedFields.Count} modified, {removedFields.Count} removed)", new
                {
                    entityTypeName = EntityTypeName,
                    entityNamespace = entity.Namespace(),
                    entityName = entity.Name(),
                    operation = "drift_detection",
                    totalChanges,
                    addedCount = addedFields.Count,
                    modifiedCount = modifiedFields.Count,
                    removedCount = removedFields.Count,
                    driftDetected = true
                });
            }
        }

        /// <summary>
        /// Formats a value for logging, providing detailed representation with truncation for long values.
        /// </summary>
        /// <param name="value">Value to format</param>
        /// <returns>Formatted string representation with type information</returns>
        private static string FormatValueForLogging(object? value)
        {
            if (value is null)
                return "(null)";

            // Handle different value types with more detail
            switch (value)
            {
                case string stringValue:
                    var quotedStr = $"\"{stringValue}\"";
                    return quotedStr.Length > 100 ? $"\"{stringValue[..95]}...\"" : quotedStr;

                case bool boolean:
                    return boolean.ToString().ToLowerInvariant();

                case int or long or float or double or decimal:
                    return value.ToString() ?? "(null)";

                case IEnumerable enumerable when value is not string:
                    var items = enumerable.Cast<object>().Take(5).Select(FormatValueForLogging);
                    var arrayPreview = string.Join(", ", items);
                    var count = enumerable.Cast<object>().Count();
                    return count > 5 ? $"[{arrayPreview}, ...] (total: {count} items)" : $"[{arrayPreview}]";

                case Hashtable hashtable:
                    var entries = hashtable.Cast<DictionaryEntry>().Take(3)
                        .Select(entry => $"{entry.Key}: {FormatValueForLogging(entry.Value)}");
                    var hashPreview = string.Join(", ", entries);
                    return hashtable.Count > 3 ? $"{{{hashPreview}, ...}} (total: {hashtable.Count} fields)" : $"{{{hashPreview}}}";

                default:
                    var objectStr = value.ToString() ?? "(null)";
                    var typeName = value.GetType().Name;
                    return objectStr.Length > 80 ? $"({typeName}) {objectStr[..75]}..." : $"({typeName}) {objectStr}";
            }
        }

        /// <inheritdoc />
        public override Task DeletedAsync(V1Tenant entity, CancellationToken cancellationToken)
        {
            Logger.LogWarningJson($"Unsupported operation deleting entity {entity}", new
            {
                entity = entity,
                operation = "delete",
                supported = false
            });
            return Task.CompletedTask;
        }

    }

}
