using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Auth0.ManagementApi;
using Auth0.ManagementApi.Models;
using Auth0.ManagementApi.Paging;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Alethic.Auth0.Operator.Helpers;

/// <summary>
/// Generic helper for paginated Auth0 API calls with caching.
/// Reduces code duplication across controllers.
/// </summary>
public static class Auth0PaginationHelper
{
    /// <summary>
    /// Retrieves all resources of type T from Auth0 API using pagination with caching.
    /// </summary>
    /// <typeparam name="T">The resource type (Client, ResourceServer, Connection, ClientGrant)</typeparam>
    /// <typeparam name="TRequest">The request type for the GetAllAsync call</typeparam>
    /// <param name="cache">Memory cache instance</param>
    /// <param name="logger">Logger instance</param>
    /// <param name="api">Auth0 Management API client</param>
    /// <param name="request">The request object to pass to GetAllAsync</param>
    /// <param name="getAllFunc">Function to call the specific GetAllAsync method</param>
    /// <param name="resourceTypeName">Name of the resource type for logging and cache keys</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <param name="cacheDurationMinutes">Cache duration in minutes (default: 5)</param>
    /// <returns>Complete list of all resources</returns>
    public static async Task<List<T>> GetAllWithPaginationAsync<T, TRequest>(
        IMemoryCache cache,
        ILogger logger,
        IManagementApiClient api,
        TRequest request,
        Func<TRequest, PaginationInfo, CancellationToken, Task<IPagedList<T>>> getAllFunc,
        string resourceTypeName,
        CancellationToken cancellationToken,
        int cacheDurationMinutes = 5)
        where T : class
    {
        // Create cache key based on API client to ensure tenant isolation
        var cacheKey = $"auth0_{resourceTypeName}_all_{api.GetHashCode()}";
        
        // Check cache first
        if (cache.TryGetValue(cacheKey, out List<T>? cachedResources) && cachedResources != null)
        {
            logger.LogDebug("Using cached {ResourceType} list with {Count} resources", resourceTypeName, cachedResources.Count);
            return cachedResources;
        }

        // Fetch from Auth0 API with pagination
        var allResources = new List<T>();
        var page = 0;
        const int perPage = 100; // Maximum page size allowed by Auth0 API
        IPagedList<T> resources;

        logger.LogDebug("Fetching all {ResourceType} from Auth0 API", resourceTypeName);

        do
        {
            try
            {
                var pagination = new PaginationInfo(page, perPage, true);
                resources = await getAllFunc(request, pagination, cancellationToken);

                allResources.AddRange(resources);

                logger.LogInformation("Retrieved page {Page}: {Count} {ResourceType} (total so far: {Total})",
                    page, resources.Count, resourceTypeName, allResources.Count);

                page++;

                // Add small delay between pages to respect rate limits, but only if there are more pages
                if (resources.Paging != null && resources.Paging.Start + resources.Paging.Length < resources.Paging.Total)
                {
                    await Task.Delay(50, cancellationToken); // 50ms delay between pages
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error retrieving {ResourceType} page {Page}: {Message}", resourceTypeName, page, e.Message);
                throw;
            }

        } while (resources.Paging != null && resources.Paging.Start + resources.Paging.Length < resources.Paging.Total);

        logger.LogInformation("Completed paginated {ResourceType} retrieval: {TotalResources} resources across {TotalPages} pages",
            resourceTypeName, allResources.Count, page);

        logger.LogDebug("Caching {Count} {ResourceType} for {CacheDurationMinutes} minutes", 
            allResources.Count, resourceTypeName, cacheDurationMinutes);
        cache.Set(cacheKey, allResources, TimeSpan.FromMinutes(cacheDurationMinutes));

        return allResources;
    }
}