using Azure.Deployments.Core.Json;
using Microsoft.WindowsAzure.ResourceStack.Common.Extensions;
using Microsoft.WindowsAzure.ResourceStack.Common.Manifest.Entities;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ManifestTranslation
{
    internal class AddEndpointsForHcapisapi
    {
        public AddEndpointsForHcapisapi() { }

        public static ResourceProviderManifest UpdateManifestToAddHcapisapisEndpoints(ResourceProviderManifest manifest)
        {
            foreach (var rt in manifest.ResourceTypes)
            {
                var endpointsToBeAddedTo = rt.Endpoints.ToList();

                var availableRegionsToApiVersions = new Dictionary<string, List<string>>();
                var regionToEndpointData = new Dictionary<string, List<ResourceProviderEndpoint>>();
                var commonVersions = new List<string>();

                foreach (var endpoint in rt.Endpoints)
                {
                    if (Common.IsAKSEndpoint(endpoint.EndpointUri))
                    {
                        // only act on sf endpoints
                        continue;
                    }

                    if (endpoint.RequiredFeatures == null || endpoint.RequiredFeatures.Length == 0)
                    {
                        // Add endpoint for each region
                        if (endpoint.Locations != null)
                        {
                            foreach (var location in endpoint.Locations)
                            {
                                if (!availableRegionsToApiVersions.ContainsKey(location))
                                {
                                    availableRegionsToApiVersions[location] = new List<string>();
                                }

                                if (endpoint.ApiVersions != null)
                                {
                                    availableRegionsToApiVersions[location].AddRange(endpoint.ApiVersions);
                                }
                            }
                        }

                        // Add api versions with no region
                        if (endpoint.Locations == null || endpoint.Locations.Length == 0 || endpoint.Locations.Contains(""))
                        {
                            if (!availableRegionsToApiVersions.ContainsKey(string.Empty))
                            {
                                availableRegionsToApiVersions[string.Empty] = new List<string>();
                            }

                            availableRegionsToApiVersions[string.Empty].AddRange(endpoint.ApiVersions);
                        }
                    }
                }

                // go through endpoints again, this time to get the ff endpoints. but only add them if the expose a unique region
                foreach (var endpoint in rt.Endpoints)
                {
                    if (Common.IsAKSEndpoint(endpoint.EndpointUri))
                    {
                        // only act on sf endpoints
                        continue;
                    }

                    if (endpoint.RequiredFeatures != null && endpoint.RequiredFeatures.Length > 0)
                    {
                        // proccess regional endpoints
                        if (!Common.IsGlobalEndpoint(endpoint))
                        {
                            foreach (var location in endpoint.Locations)
                            {
                                // Only list regions under a special ff if they are unavailble publicly otherwise
                                if (availableRegionsToApiVersions.ContainsKey(location))
                                {
                                    var newApiVersionsForRegionUnderFF = endpoint.ApiVersions.Except(availableRegionsToApiVersions[location]);

                                    if (newApiVersionsForRegionUnderFF.Count() == 0)
                                    {
                                        // if the feature flag does not expose any new api versions for this region, don't include it
                                        continue;
                                    }
                                }

                                if (!regionToEndpointData.ContainsKey(location))
                                {
                                    regionToEndpointData[location] = new List<ResourceProviderEndpoint>();
                                }

                                regionToEndpointData[location].Add(endpoint);
                            }
                        }

                        // process endpoint with no region
                        if (Common.IsGlobalEndpoint(endpoint))
                        {
                            // Only list regions under a special ff if they are unavailble publicly otherwise
                            if (availableRegionsToApiVersions.ContainsKey(string.Empty))
                            {
                                var newApiVersionsForRegionUnderFF = endpoint.ApiVersions.Except(availableRegionsToApiVersions[string.Empty]);

                                if (newApiVersionsForRegionUnderFF.Count() == 0)
                                {
                                    // if the feature flag does not expose any new api versions for this region, don't include it
                                    continue;
                                }
                            }

                            if (!regionToEndpointData.ContainsKey(string.Empty))
                            {
                                regionToEndpointData[string.Empty] = new List<ResourceProviderEndpoint>();
                            }

                            regionToEndpointData[string.Empty].Add(endpoint);
                        }
                    }
                }

                // Add publicly available regions & api versions under new endpoint
                foreach (var regionToApiVersions in availableRegionsToApiVersions)
                {
                    string endpointUri = Common.GetEndpoint(regionToApiVersions.Key);

                    if (rt.Endpoints.Any(rpe => rpe.EndpointUri == endpointUri))
                    {
                        // endpoint already exists
                        continue;
                    }

                    var endpoint = new ResourceProviderEndpoint
                    {
                        Enabled = true,
                        EndpointUri = endpointUri,
                        Locations = new string[] { regionToApiVersions.Key },
                        RequiredFeatures = new string[] { Constants.AKSFeatureFlag },
                        Timeout = TimeSpan.FromSeconds(20),
                    };

                    if (regionToApiVersions.Value != null && regionToApiVersions.Value.Count > 0)
                    {
                        endpoint.ApiVersions = regionToApiVersions.Value.ToArray();
                    }

                    endpointsToBeAddedTo.Add(endpoint);
                }

                // Add regions & apis under feature flag for new endpoint
                foreach (var regionToEndpoint in regionToEndpointData)
                {
                    string endpointUri = Common.GetEndpoint(regionToEndpoint.Key);

                    if (rt.Endpoints.Any(rpe => rpe.EndpointUri == endpointUri))
                    {
                        // endpoint already exists
                        continue;
                    }

                    foreach (var ffEndpoint in regionToEndpoint.Value)
                    {
                        var features = ffEndpoint.RequiredFeatures.ToList();
                        features.Add("Microsoft.HealthcareApis/TestEnvAKS");

                        var endpoint = new ResourceProviderEndpoint
                        {
                            Enabled = true,
                            EndpointUri = endpointUri,
                            Locations = new string[] { regionToEndpoint.Key },
                            RequiredFeatures = features.ToArray(),
                            EndpointType = ffEndpoint.EndpointType,
                            FeaturesRule = ffEndpoint.FeaturesRule,
                            Timeout = TimeSpan.FromSeconds(20),
                            ApiVersions = ffEndpoint.ApiVersions,
                        };

                        endpointsToBeAddedTo.Add(endpoint);
                    }
                }

                rt.Endpoints = endpointsToBeAddedTo.ToArray();
            }

            return manifest;
        }
    }
}
