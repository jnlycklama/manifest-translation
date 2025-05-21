using Azure.Deployments.Core.Json;
using Microsoft.WindowsAzure.ResourceStack.Common.Extensions;
using Microsoft.WindowsAzure.ResourceStack.Common.Manifest.Entities;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ManifestTranslation
{
    internal class CutoverToNewEndpoints
    {
        private static bool ShouldCutoverGlobalEndpoints = false;

        public CutoverToNewEndpoints() { }

        public static ResourceProviderManifest UpdateManifestToCutoverToHcapisInRegions(ResourceProviderManifest manifest, List<string> regionsToCutover)
        {
            foreach (var rt in manifest.ResourceTypes)
            {
                var endpointsToBeAddedTo = rt.Endpoints.ToList();
                int endpointsIndex = 0;

                if (rt.Name == "checkNameAvailability")
                {
                    Console.WriteLine("Stop");
                }

                foreach (var endpoint in rt.Endpoints)
                {
                    foreach (var region in regionsToCutover)
                    {
                        if (Common.IsTestEndpoint(endpoint) || Common.IsGlobalEndpoint(endpoint) || Common.IsAKSEndpoint(endpoint.EndpointUri))
                        {
                            // skipping preprod routes since that environment does not exist in AKS
                            // skipping int since it is already converted
                            // skip endpoint without location
                            continue;
                        }

                        int aksEndpointIndex;
                        List<string> aksFeatureFlagsAsideFromAksFF;

                        if (!endpointsToBeAddedTo.GetAksEndpointIndex(region, endpointsIndex, endpoint?.RequiredFeatures?.ToList(), out aksEndpointIndex, out aksFeatureFlagsAsideFromAksFF))
                        {
                            // aks endpoint was not found
                            continue;
                        }

                        if (endpoint.Locations.Contains(region))
                        {
                            endpointsIndex = CollectDataAndDeleteOldEndpoints(endpointsToBeAddedTo, endpointsIndex, endpoint, region, aksEndpointIndex, aksFeatureFlagsAsideFromAksFF);
                        }
                    }

                    // handle gloabl endpoints
                    if (Common.IsGlobalEndpoint(endpoint))
                    {
                        string region = string.Empty;

                        if (Common.IsTestEndpoint(endpoint) || Common.IsAKSEndpoint(endpoint.EndpointUri))
                        {
                            // skipping preprod routes since that environment does not exist in AKS
                            // skipping int since it is already converted
                            // skip endpoint without location
                            continue;
                        }

                        int aksEndpointIndex;
                        List<string> aksFeatureFlagsAsideFromAksFF;

                        if (!endpointsToBeAddedTo.GetAksEndpointIndex(region, endpointsIndex, endpoint?.RequiredFeatures?.ToList(), out aksEndpointIndex, out aksFeatureFlagsAsideFromAksFF))
                        {
                            // aks endpoint was not found
                            continue;
                        }

                        endpointsIndex = CollectDataAndDeleteOldEndpoints(endpointsToBeAddedTo, endpointsIndex, endpoint, region, aksEndpointIndex, aksFeatureFlagsAsideFromAksFF);
                    }

                    endpointsIndex++;
                }

                foreach (var region in regionsToCutover)
                {
                    int skip = 0;
                    int aksEndpointIndex = endpointsToBeAddedTo.IndexOf(e => e.Locations.Contains(region) && e.RequiredFeatures != null && e.RequiredFeatures.Contains("Microsoft.HealthcareApis/TestEnvAKS"));

                    while (aksEndpointIndex >= 0)
                    {
                        // get the actual index, which is the index found plus the amount skipped
                        aksEndpointIndex = aksEndpointIndex + skip;

                        // Remove AKS feature flag
                        var features = endpointsToBeAddedTo[aksEndpointIndex].RequiredFeatures.ToList();
                        features.Remove("Microsoft.HealthcareApis/TestEnvAKS");
                        endpointsToBeAddedTo[aksEndpointIndex].RequiredFeatures = features.Count == 0 ? null : features.ToArray();

                        skip = aksEndpointIndex;
                        aksEndpointIndex = endpointsToBeAddedTo.Skip(skip).IndexOf(e => e.Locations.Contains(region) && e.RequiredFeatures != null && e.RequiredFeatures.Contains("Microsoft.HealthcareApis/TestEnvAKS"));
                    }
                }

                rt.Endpoints = endpointsToBeAddedTo.ToArray();
            }

            return manifest;
        }

        private static int CollectDataAndDeleteOldEndpoints(List<ResourceProviderEndpoint> endpointsToBeAddedTo, int endpointsIndex, ResourceProviderEndpoint endpoint, string region, int aksEndpointIndex, List<string> aksFeatureFlagsAsideFromAksFF)
        {
            // handle endpoint with no features or matching feature set to the aks endpoint found
            if (endpoint.RequiredFeatures == null || endpoint.RequiredFeatures.Length == 0 || endpoint.RequiredFeatures.All(e => aksFeatureFlagsAsideFromAksFF.Contains(e)))
            {
                if (endpoint.ApiVersions != null && endpoint.ApiVersions.Length > 0)
                {
                    // Merge api versions
                    var existingAksEndpoints = endpointsToBeAddedTo[aksEndpointIndex].ApiVersions;
                    var allEndpoints = existingAksEndpoints.Union(endpoint.ApiVersions);

                    // Update aks endpoint with new api versions
                    endpointsToBeAddedTo[aksEndpointIndex].ApiVersions = allEndpoints.ToArray();
                }

                // Update endpoint with this location removed
                var endpointLocations = endpoint.Locations.ToList();

                // Only do the cutover for global endpoints when ShouldCutoverGlobalEndpoints = true
                if (region != string.Empty || ShouldCutoverGlobalEndpoints)
                {
                    endpointLocations.Remove(region);
                }

                // If that was the only region in the endpoint, remove the whole endpoint. Otherwise update endpoint
                if (endpointLocations.Count == 0)
                {
                    var endpointatIndex = endpointsToBeAddedTo.ElementAt(endpointsIndex);
                    if (endpointatIndex.Locations.Length == 1 && !endpointatIndex.Locations.Contains(region))
                    {
                        Console.WriteLine("Deleting the endpoint with the wrong locations");
                    }

                    endpointsToBeAddedTo.RemoveAt(endpointsIndex);

                    // adjust index based on removal of item
                    endpointsIndex--;
                }
                else
                {
                    endpointsToBeAddedTo[endpointsIndex].Locations = endpointLocations.ToArray();
                }
            }
            else
            {
                // Create new endpoint based on this endpoint, but with single location and regional AKS endpoint
                var newEndpointForSpecificFeatureFlag = new ResourceProviderEndpoint()
                {
                    Locations = new string[] { region },
                    EndpointUri = Common.GetEndpoint(region),
                    RequiredFeatures = endpoint.RequiredFeatures,
                    Timeout = endpoint.Timeout,
                    Enabled = endpoint.Enabled,
                    ApiVersions = endpoint.ApiVersions,
                };

                // Add new endpoint for this specific feature flag
                endpointsToBeAddedTo.Add(newEndpointForSpecificFeatureFlag);

                // Update endpoint with this location removed
                var endpointLocations = endpoint.Locations.ToList();

                // Only do the cutover for global endpoints when ShouldCutoverGlobalEndpoints = true
                if (region != string.Empty || ShouldCutoverGlobalEndpoints)
                {
                    endpointLocations.Remove(region);
                }

                // If that was the only region in the endpoint, remove the whole endpoint. Otherwise update endpoint
                if (endpointLocations.Count == 0)
                {
                    var endpointatIndex = endpointsToBeAddedTo.ElementAt(endpointsIndex);
                    if (endpointatIndex.Locations.Length == 1 && !endpointatIndex.Locations.Contains(region))
                    {
                        Console.WriteLine("Deleting the endpoint with the wrong locations");
                    }

                    endpointsToBeAddedTo.RemoveAt(endpointsIndex);

                    // adjust index based on removal of item
                    endpointsIndex--;
                }
                else
                {
                    endpointsToBeAddedTo[endpointsIndex].Locations = endpointLocations.ToArray();
                }
            }

            return endpointsIndex;
        }
    }
}
