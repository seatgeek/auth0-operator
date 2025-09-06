using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Auth0.ManagementApi.Paging;

using Alethic.Auth0.Operator.Extensions;

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
    /// Mutex dictionary for protecting cache access across concurrent operations
    /// </summary>
    static readonly ConcurrentDictionary<string, SemaphoreSlim> _cacheMutexes = new();
    /// <summary>
    /// Retrieves all resources of type T from Auth0 API using pagination with caching.
    /// </summary>
    /// <typeparam name="T">The resource type (Client, ResourceServer, Connection, ClientGrant)</typeparam>
    /// <typeparam name="TRequest">The request type for the GetAllAsync call</typeparam>
    /// <param name="cache">Memory cache instance</param>
    /// <param name="logger">Logger instance</param>
    /// <param name="request">The request object to pass to GetAllAsync</param>
    /// <param name="getAllFunc">Function to call the specific GetAllAsync method</param>
    /// <param name="resourceTypeName">Name of the resource type for logging and cache keys</param>
    /// <param name="cacheSalt">Cache salt for tenant isolation (tenant domain for per-tenant entities, fixed string for global entities)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <param name="cacheDurationMinutes">Cache duration in minutes (default: 5)</param>
    /// <returns>Complete list of all resources</returns>
    public static async Task<List<T>> GetAllWithPaginationAsync<T, TRequest>(
        IMemoryCache cache,
        ILogger logger,
        TRequest request,
        Func<TRequest, PaginationInfo, CancellationToken, Task<IPagedList<T>>> getAllFunc,
        string resourceTypeName,
        string cacheSalt,
        CancellationToken cancellationToken,
        int cacheDurationMinutes = 5)
        where T : class
    {
        // Create cache key using the provided cache salt for tenant isolation
        var cacheKey = $"auth0_{resourceTypeName}_all_{cacheSalt}";
        var mutex = _cacheMutexes.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
        
        await mutex.WaitAsync(cancellationToken);
        try
        {
            // Check cache again after acquiring mutex
            if (cache.TryGetValue(cacheKey, out List<T>? cachedResources) && cachedResources != null)
            {
                logger.LogDebugJson($"Using cached {resourceTypeName} list with {cachedResources.Count} resources", new
                {
                    resourceType = resourceTypeName,
                    resourceCount = cachedResources.Count,
                    cacheSalt,
                    operation = "cache_hit"
                });
                return cachedResources;
            }

            // Fetch from Auth0 API with pagination
            var allResources = new List<T>();
            var page = 0;
            const int perPage = 100; // Maximum page size allowed by Auth0 API
            IPagedList<T> resources;

            logger.LogDebugJson($"Fetching all {resourceTypeName} from Auth0 API", new
            {
                resourceType = resourceTypeName,
                cacheSalt,
                operation = "fetch_paginated"
            });

            do
            {
                try
                {
                    var pagination = new PaginationInfo(page, perPage, true);
                    resources = await getAllFunc(request, pagination, cancellationToken);

                    allResources.AddRange(resources);

                    logger.LogInformationJson($"Retrieved page {page}: {resources.Count} {resourceTypeName} (total so far: {allResources.Count})", new
                    {
                        resourceType = resourceTypeName,
                        page,
                        pageCount = resources.Count,
                        totalCount = allResources.Count,
                        cacheSalt,
                        operation = "fetch_page"
                    });

                    page++;

                    // Add small delay between pages to respect rate limits, but only if there are more pages
                    if (resources.Paging != null && resources.Paging.Start + resources.Paging.Length < resources.Paging.Total)
                    {
                        await Task.Delay(50, cancellationToken); // 50ms delay between pages
                    }
                }
                catch (Exception e)
                {
                    logger.LogErrorJson($"Error retrieving {resourceTypeName} page {page}: {e.Message}", new
                    {
                        resourceType = resourceTypeName,
                        page,
                        errorMessage = e.Message,
                        cacheSalt,
                        operation = "fetch_page",
                        status = "error"
                    }, e);
                    throw;
                }

            } while (resources.Paging != null && resources.Paging.Start + resources.Paging.Length < resources.Paging.Total);

            logger.LogInformationJson($"Completed paginated {resourceTypeName} retrieval: {allResources.Count} resources across {page} pages", new
            {
                resourceType = resourceTypeName,
                totalResources = allResources.Count,
                totalPages = page,
                cacheSalt,
                operation = "fetch_paginated",
                status = "completed"
            });

            logger.LogDebugJson($"Caching {allResources.Count} {resourceTypeName} for {cacheDurationMinutes} minutes", new
            {
                resourceType = resourceTypeName,
                resourceCount = allResources.Count,
                cacheDurationMinutes,
                cacheSalt,
                operation = "cache_set"
            });
            cache.Set(cacheKey, allResources, TimeSpan.FromMinutes(cacheDurationMinutes));

            return allResources;
        }
        finally
        {
            mutex.Release();
        }
    }
}