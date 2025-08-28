using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Alethic.Auth0.Operator.Core.Models.Tenant;
using Alethic.Auth0.Operator.Models;

using Auth0.ManagementApi.Models;

using k8s.Models;

using KubeOps.Abstractions.Controller;
using KubeOps.Abstractions.Queue;
using KubeOps.Abstractions.Rbac;
using KubeOps.KubernetesClient;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Alethic.Auth0.Operator.Controllers
{

    [EntityRbac(typeof(V1Tenant), Verbs = RbacVerb.All)]
    [EntityRbac(typeof(V1Secret), Verbs = RbacVerb.List | RbacVerb.Get)]
    [EntityRbac(typeof(Eventsv1Event), Verbs = RbacVerb.All)]
    public class V1TenantController :
        V1Controller<V1Tenant, V1Tenant.SpecDef, V1Tenant.StatusDef, TenantConf>,
        IEntityController<V1Tenant>
    {

        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        /// <param name="kube"></param>
        /// <param name="requeue"></param>
        /// <param name="cache"></param>
        /// <param name="logger"></param>
        public V1TenantController(IKubernetesClient kube, EntityRequeue<V1Tenant> requeue, IMemoryCache cache, ILogger<V1TenantController> logger) :
            base(kube, requeue, cache, logger)
        {

        }

        /// <inheritdoc />
        protected override string EntityTypeName => "A0Tenant";

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

        /// <inheritdoc />
        protected override async Task Reconcile(V1Tenant entity, CancellationToken cancellationToken)
        {
            Logger.LogInformation("{EntityTypeName} {Namespace}/{Name} starting reconciliation", EntityTypeName, entity.Namespace(), entity.Name());
            
            var api = await GetTenantApiClientAsync(entity, cancellationToken);
            if (api == null)
            {
                Logger.LogError("{EntityTypeName} {Namespace}/{Name} failed to retrieve API client", EntityTypeName, entity.Namespace(), entity.Name());
                throw new InvalidOperationException($"{EntityTypeName} {entity.Namespace()}:{entity.Name()} failed to retrieve API client.");
            }

            // Check if configuration changes need to be applied
            var isFirstReconciliation = entity.Status.LastConf is null;
            var hasLocalChanges = entity.Spec.Conf is { } currentConf && !isFirstReconciliation && HasConfigurationChanged(entity.Status.LastConf, currentConf);
            var needsAuth0Fetch = hasLocalChanges || isFirstReconciliation;
            
            TenantSettings? settings = null;
            if (needsAuth0Fetch)
            {
                var reason = isFirstReconciliation ? "first reconciliation" : "local configuration changes detected";
                Logger.LogInformation("{EntityTypeName} {Namespace}/{Name} fetching tenant settings from Auth0 API (reason: {Reason})", EntityTypeName, entity.Namespace(), entity.Name(), reason);
                settings = await api.TenantSettings.GetAsync(cancellationToken: cancellationToken);
                if (settings is null)
                {
                    Logger.LogError("{EntityTypeName} {Namespace}/{Name} tenant settings not found in Auth0 API", EntityTypeName, entity.Namespace(), entity.Name());
                    throw new InvalidOperationException($"{EntityTypeName} {entity.Namespace()}/{entity.Name()} cannot be loaded from API.");
                }
                Logger.LogInformation("{EntityTypeName} {Namespace}/{Name} successfully retrieved tenant settings from Auth0", EntityTypeName, entity.Namespace(), entity.Name());
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
                    needsUpdate = HasConfigurationChanged(settingsHashtable, conf);
                    if (needsUpdate)
                    {
                        Logger.LogInformation("{EntityTypeName} {Namespace}/{Name} first reconciliation - configuration drift detected between Auth0 and desired state - applying updates", EntityTypeName, entity.Namespace(), entity.Name());
                    }
                }
                else
                {
                    needsUpdate = hasLocalChanges;
                    Logger.LogInformation("{EntityTypeName} {Namespace}/{Name} local configuration changes detected - applying updates to Auth0", EntityTypeName, entity.Namespace(), entity.Name());
                }

                if (needsUpdate)
                {
                    // verify that no changes to enable_sso are being made
                    if (conf.Flags != null && conf.Flags.EnableSSO != null && settings?.Flags?.EnableSSO != null && conf.Flags.EnableSSO != settings.Flags.EnableSSO)
                    {
                        Logger.LogError("{EntityTypeName} {Namespace}/{Name} attempted to modify enable_sso flag from {CurrentValue} to {NewValue} - operation not allowed", 
                            EntityTypeName, entity.Namespace(), entity.Name(), settings.Flags.EnableSSO, conf.Flags.EnableSSO);
                        throw new InvalidOperationException($"{EntityTypeName} {entity.Namespace()}/{entity.Name()}: updating the enable_sso flag is not allowed.");
                    }

                    Logger.LogInformation("{EntityTypeName} {Namespace}/{Name} updating tenant settings in Auth0", EntityTypeName, entity.Namespace(), entity.Name());
                    // push update to Auth0
                    var req = TransformToNewtonsoftJson<TenantConf, TenantSettingsUpdateRequest>(conf);
                    req.Flags.EnableSSO = null;
                    settings = await api.TenantSettings.UpdateAsync(req, cancellationToken);
                    Logger.LogInformation("{EntityTypeName} {Namespace}/{Name} successfully updated tenant settings in Auth0", EntityTypeName, entity.Namespace(), entity.Name());
                }
            }

            // Always retrieve final settings for status update if we made API calls
            if (needsAuth0Fetch)
            {
                Logger.LogInformation("{EntityTypeName} {Namespace}/{Name} retrieving final tenant settings from Auth0 for status update", EntityTypeName, entity.Namespace(), entity.Name());
                settings = await api.TenantSettings.GetAsync(cancellationToken: cancellationToken);
                entity.Status.LastConf = TransformToSystemTextJson<Hashtable>(settings);
                entity = await Kube.UpdateStatusAsync(entity, cancellationToken);
            }

            Logger.LogInformation("{EntityTypeName} {Namespace}/{Name} reconciliation completed successfully", EntityTypeName, entity.Namespace(), entity.Name());
            await ReconcileSuccessAsync(entity, cancellationToken);
        }

        /// <summary>
        /// Determines if the tenant configuration has changed by comparing the last known state with the desired configuration.
        /// </summary>
        /// <param name="lastConf">The last known configuration from Auth0</param>
        /// <param name="desiredConf">The desired configuration from the Kubernetes spec</param>
        /// <returns>True if changes are detected, false if configurations match</returns>
        private bool HasConfigurationChanged(Hashtable? lastConf, TenantConf desiredConf)
        {
            try
            {
                if (lastConf is null)
                {
                    Logger.LogDebug("{EntityTypeName} no previous configuration available - assuming changes exist", EntityTypeName);
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
                    LogConfigurationDifferences(filteredLast, filteredDesired);
                }
                
                return foundConfigChanges;
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "{EntityTypeName} error comparing configurations, assuming changes exist: {Message}", EntityTypeName, ex.Message);
                return true; // Safe fallback: assume changes exist
            }
        }

        /// <summary>
        /// Filters fields for comparison based on the tenant-specific fields we track.
        /// </summary>
        /// <param name="config">The configuration hashtable to filter</param>
        /// <returns>A new hashtable with only the tracked tenant fields</returns>
        private Hashtable FilterFieldsForComparison(Hashtable config)
        {
            var filtered = new Hashtable();
            var includedFields = new HashSet<string>(GetIncludedFields(), StringComparer.OrdinalIgnoreCase);
            
            foreach (DictionaryEntry entry in config)
            {
                if (entry.Key is string key && includedFields.Contains(key))
                {
                    filtered[key] = entry.Value;
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
        /// <param name="last">Last known configuration</param>
        /// <param name="desired">Desired configuration</param>
        private void LogConfigurationDifferences(Hashtable last, Hashtable desired)
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
                Logger.LogInformation("{EntityTypeName} configuration changes - ADDED fields: {AddedFields}", 
                    EntityTypeName, string.Join(", ", addedFields));
            }

            if (modifiedFields.Count > 0)
            {
                Logger.LogInformation("{EntityTypeName} configuration changes - MODIFIED fields: {ModifiedFields}", 
                    EntityTypeName, string.Join(", ", modifiedFields));
            }

            if (removedFields.Count > 0)
            {
                Logger.LogInformation("{EntityTypeName} configuration changes - REMOVED fields: {RemovedFields}", 
                    EntityTypeName, string.Join(", ", removedFields));
            }

            // Also log a summary count
            var totalChanges = addedFields.Count + modifiedFields.Count + removedFields.Count;
            if (totalChanges > 0)
            {
                Logger.LogInformation("{EntityTypeName} configuration drift detected: {TotalChanges} field changes ({AddedCount} added, {ModifiedCount} modified, {RemovedCount} removed)", 
                    EntityTypeName, totalChanges, addedFields.Count, modifiedFields.Count, removedFields.Count);
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
            Logger.LogWarning("Unsupported operation deleting entity {Entity}.", entity);
            return Task.CompletedTask;
        }

    }

}
