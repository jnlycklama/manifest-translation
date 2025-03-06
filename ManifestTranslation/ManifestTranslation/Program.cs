using Azure.Deployments.Core.Json;
using Microsoft.WindowsAzure.ResourceStack.Common.Extensions;
using Microsoft.WindowsAzure.ResourceStack.Common.Manifest.Entities;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Drawing;
using System.Net;
using System.Text.Json;
using System.Xml;

namespace ManifestTranslation;

class Program
{
    private static Dictionary<string, string> LocationToShortName = new Dictionary<string, string>()
    {
        { "East US 2 EUAP", "eusep" },
        { "West Central US", "wcus" },
        { "UK South", "uksouth" },
        { "East US", "eus" },
        { "Central US", "cus" },
        { "East US 2", "eastus2" },
        { "North Central US", "ncus" },
        { "South Central US", "scus" },
        { "West US 2", "westus2" },
        { "West US 3", "westus3" },
        { "Southeast Asia", "seasia" },
        { "Australia East", "aueast" },
        { "Australia Central", "auc" },
        { "Sweden Central", "swec" },
        { "North Europe", "northeu" },
        { "West Europe", "westeu" },
        { "UK West", "ukwest" },
        { "Brazil South", "brsouth" },
        { "Canada Central", "cac" },
        { "Central India", "inc" },
        { "France Central", "fc" },
        { "Germany West Central", "gc" },
        { "Japan East", "jpeast" },
        { "Switzerland North", "swin" },
        { "Korea Central", "krc" },
        { "Qatar Central", "qc" },
        { "South Africa North", "zan" }
    };

    private static List<string> RegionsToCutover = new List<string>()
    {
        "West Central US", "East Asia", "Central US", "UK South", "Australia Central"
    };

    private static List<string> DeprecatedRTs = new List<string>()
    {
        "workspaces/analyticsconnectors",
        "workspaces/iotconnectors/destinations",
        "services/iomtconnectors",
        "services/iomtconnectors/connections",
        "services/iomtconnectors/mappings",
        "locations/checkNameAvailability" // skipping because it has no endpoints open to public (without a ff)
    };

    public static void Main()
    {
        UpdateManifestToCutoverToHcapisInRegions().Wait();
        //UpdateManifestToAddRegionalEndpoints().Wait();
        //UpdateManifestToConsolidateVersions().Wait();
    }

    private static async Task UpdateManifestToCutoverToHcapisInRegions()
    {
        try
        {
            // Open the text file using a stream reader.
            using (StreamReader reader = new StreamReader("prod.json"))
            {
                // Read the entire stream asynchronously and await the result.
                string text = await reader.ReadToEndAsync();

                var options = new JsonSerializerOptions
                {
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                };
                ResourceProviderManifest manifest = JsonConvert.DeserializeObject<ResourceProviderManifest>(text, SerializerSettings.SerializerMediaTypeSettings);

                foreach (var rt in manifest.ResourceTypes)
                {
                    if (DeprecatedRTs.Contains(rt.Name))
                    {
                        // do not add to deprecated resources
                        continue;
                    }

                    var endpointsToBeAddedTo = rt.Endpoints.ToList();
                    int endpointsIndex = 0;

                    foreach (var endpoint in rt.Endpoints)
                    {
                        foreach (var region in RegionsToCutover)
                        {
                            if (endpoint.RequiredFeatures != null && endpoint.RequiredFeatures.Length == 1 && endpoint.RequiredFeatures.Contains("Microsoft.HealthcareApis/<Put PreProd Feature Flag here>"))
                            {
                                // skipping preprod routes since that environment does not exist in AKS
                                continue;
                            }

                            int aksEndpointIndex = endpointsToBeAddedTo.IndexOf(e => e.Locations.Contains(region) && e.RequiredFeatures != null && e.RequiredFeatures.Contains("Microsoft.HealthcareApis/<Put AKS Feature Flag here>"));

                            if (aksEndpointIndex < 0)
                            {
                                Console.WriteLine($"RT {rt.Name} does not have AKS endpoint for region {region}");
                                continue;
                            }

                            // Get any additional feature flags declared on AKS endpoint
                            var aksFeatureFlagsAsideFromAksFF = endpointsToBeAddedTo[aksEndpointIndex].RequiredFeatures.ToList();
                            aksFeatureFlagsAsideFromAksFF.Remove("Microsoft.HealthcareApis/<Put AKS Feature Flag here>");

                            if (endpoint.Locations == null || endpoint.Locations.Length == 0 || endpoint.Locations.Contains("") || aksEndpointIndex == endpointsIndex)
                            {
                                // no locations specified, or skip the aks endpoint for data collection
                                continue;
                            }

                            if (endpoint.Locations.Contains(region))
                            {
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
                                    endpointLocations.Remove(region);

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
                                        EndpointUri = $"https://<endpointprefix>-{LocationToShortName[region]}.<endpointsuffix>",
                                        RequiredFeatures = endpoint.RequiredFeatures,
                                        Timeout = endpoint.Timeout,
                                        Enabled = endpoint.Enabled,
                                        ApiVersions = endpoint.ApiVersions,
                                    };

                                    // Add new endpoint for this specific feature flag
                                    endpointsToBeAddedTo.Add(newEndpointForSpecificFeatureFlag);

                                    // Update endpoint with this location removed
                                    var endpointLocations = endpoint.Locations.ToList();
                                    endpointLocations.Remove(region);

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
                            }
                        }

                        endpointsIndex++;
                    }

                    foreach (var region in RegionsToCutover)
                    {
                        int aksEndpointIndex = endpointsToBeAddedTo.IndexOf(e => e.Locations.Contains(region) && e.RequiredFeatures != null && e.RequiredFeatures.Contains("Microsoft.HealthcareApis/TestEnvAKS"));

                        if (aksEndpointIndex < 0)
                        {
                            continue;
                        }

                        // Remove AKS feature flag
                        var features = endpointsToBeAddedTo[aksEndpointIndex].RequiredFeatures.ToList();
                        features.Remove("Microsoft.HealthcareApis/<Put AKS Feature Flag here>");
                        endpointsToBeAddedTo[aksEndpointIndex].RequiredFeatures = features.Count == 0 ? null : features.ToArray();
                    }

                    rt.Endpoints = endpointsToBeAddedTo.ToArray();
                }

                var serializer = SerializerSettings.SerializerMediaTypeSettings;
                serializer.Formatting = Newtonsoft.Json.Formatting.Indented;
                string json = JsonConvert.SerializeObject(manifest, serializer);

                File.WriteAllText("prod2.json", json);
            }
        }
        catch (IOException e)
        {
            Console.WriteLine("An error occurred while reading the file:");
            Console.WriteLine(e.Message);
        }
        catch (Exception ex)
        {
            Console.WriteLine("An error occurred while reading the file:");
            Console.WriteLine(ex.Message);
        }
    }

    private static async Task UpdateManifestToAddRegionalEndpoints()
    {
        try
        {
            // Open the text file using a stream reader.
            using (StreamReader reader = new StreamReader("prod.json"))
            {
                // Read the entire stream asynchronously and await the result.
                string text = await reader.ReadToEndAsync();

                var options = new JsonSerializerOptions
                {
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                };
                ResourceProviderManifest manifest = JsonConvert.DeserializeObject<ResourceProviderManifest>(text, SerializerSettings.SerializerMediaTypeSettings);

                foreach (var rt in manifest.ResourceTypes)
                {
                    if (DeprecatedRTs.Contains(rt.Name))
                    {
                        // do not add to deprecated resources
                        continue;
                    }

                    var endpointsToBeAddedTo = rt.Endpoints.ToList();

                    var availableRegionsToApiVersions = new Dictionary<string, List<string>>();
                    var regionToEndpointData = new Dictionary<string, List<ResourceProviderEndpoint>>();
                    var commonVersions = new List<string>();
                    foreach (var endpoint in rt.Endpoints)
                    {
                        if (endpoint.Locations == null || endpoint.Locations.Length == 0 || endpoint.Locations.Contains(""))
                        {
                            // no locations specified
                            continue;
                        }

                        if (endpoint.RequiredFeatures == null || endpoint.RequiredFeatures.Length == 0)
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
                    }

                    // go through endpoints again, this time to get the ff endpoints. but only add them if the expose a unique region
                    foreach (var endpoint in rt.Endpoints)
                    {
                        if (endpoint.Locations == null || endpoint.Locations.Length == 0 || endpoint.Locations.Contains(""))
                        {
                            // no locations specified
                            continue;
                        }

                        if (endpoint.RequiredFeatures != null && endpoint.RequiredFeatures.Length > 0)
                        {
                            foreach (var location in endpoint.Locations)
                            {
                                // Only list regions under a special ff if they are unavailble publicly otherwise
                                if (availableRegionsToApiVersions.ContainsKey(location))
                                {
                                    continue;
                                }

                                if (!regionToEndpointData.ContainsKey(location))
                                {
                                    regionToEndpointData[location] = new List<ResourceProviderEndpoint>();
                                }

                                regionToEndpointData[location].Add(endpoint);
                            }
                        }
                    }

                    foreach (var regionToApiVersions in availableRegionsToApiVersions)
                    {
                        string endpointUri = $"https://<endpointprefix>-{LocationToShortName[regionToApiVersions.Key]}.<endpointsuffix>";
                        if (rt.Endpoints.Any(rpe => rpe.EndpointUri == endpointUri))
                        {
                            // endpoint already exists
                            continue;
                        }

                        if (regionToApiVersions.Key == "South Africa North")
                        {
                            // not supported yet
                            continue;
                        }

                        var endpoint = new ResourceProviderEndpoint
                        {
                            Enabled = true,
                            EndpointUri = endpointUri,
                            Locations = new string[] { regionToApiVersions.Key },
                            RequiredFeatures = new string[] { "<AKS Feature Flag>" },
                            Timeout = TimeSpan.FromSeconds(20),
                        };

                        if (regionToApiVersions.Value != null && regionToApiVersions.Value.Count > 0)
                        {
                            endpoint.ApiVersions = regionToApiVersions.Value.ToArray();
                        }

                        endpointsToBeAddedTo.Add(endpoint);
                    }

                    foreach (var regionToEndpoint in regionToEndpointData)
                    {
                        string endpointUri = $"https://hcapisapi-{LocationToShortName[regionToEndpoint.Key]}.<endpointURL>/providers/Microsoft.HealthcareApis/";
                        if (rt.Endpoints.Any(rpe => rpe.EndpointUri == endpointUri))
                        {
                            // endpoint already exists
                            continue;
                        }


                        if (regionToEndpoint.Key == "South Africa North")
                        {
                            // not supported yet
                            continue;
                        }

                        var features = regionToEndpoint.Value.SelectMany(f => f.RequiredFeatures).ToList();
                        features.Add("<AKS Feature Flag>");

                        var endpoint = new ResourceProviderEndpoint
                        {
                            Enabled = true,
                            EndpointUri = $"https://hcapisapi-{LocationToShortName[regionToEndpoint.Key]}.<endpointURL>/providers/Microsoft.HealthcareApis/",
                            Locations = new string[] { regionToEndpoint.Key },
                            RequiredFeatures = features.ToArray(),
                            EndpointType = regionToEndpoint.Value.FirstOrDefault()?.EndpointType,
                            FeaturesRule = new FeaturesRule() { RequiredFeaturesPolicy = FeaturesPolicy.All },
                            Timeout = TimeSpan.FromSeconds(20),
                        };

                        if (regionToEndpoint.Value.Count > 1)
                        {
                            Console.WriteLine($"More than one feature flag for region {regionToEndpoint.Key}. Endpoint type may be incorrect");
                        }
                        else if (regionToEndpoint.Value.Count == 0)
                        {
                            Console.WriteLine($"Zero ff for region {regionToEndpoint.Key}. Endpoint type may be incorrect");
                        }

                        var apiVersions = regionToEndpoint.Value.SelectManyArray(e => e.ApiVersions);

                        if (apiVersions != null && apiVersions.Length > 0)
                        {
                            endpoint.ApiVersions = apiVersions;
                        }

                        endpointsToBeAddedTo.Add(endpoint);
                    }

                    rt.Endpoints = endpointsToBeAddedTo.ToArray();
                }

                var serializer = SerializerSettings.SerializerMediaTypeSettings;
                serializer.Formatting = Newtonsoft.Json.Formatting.Indented;
                string json = JsonConvert.SerializeObject(manifest, serializer);

                File.WriteAllText("prod2.json", json);
            }
        }
        catch (IOException e)
        {
            Console.WriteLine("An error occurred while reading the file:");
            Console.WriteLine(e.Message);
        }
        catch (Exception ex)
        {
            Console.WriteLine("An error occurred while reading the file:");
            Console.WriteLine(ex.Message);
        }
    }

    private static async Task UpdateManifestToConsolidateVersions()
    {
        try
        {
            // Open the text file using a stream reader.
            using (StreamReader reader = new StreamReader("prod.json"))
            {
                // Read the entire stream asynchronously and await the result.
                string text = await reader.ReadToEndAsync();

                var options = new JsonSerializerOptions
                {
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                };
                ResourceProviderManifest manifest = JsonConvert.DeserializeObject<ResourceProviderManifest>(text, SerializerSettings.SerializerMediaTypeSettings);

                foreach (var rt in manifest.ResourceTypes)
                {
                    var commonVersions = new List<string>();
                    foreach (var endpoint in rt.Endpoints)
                    {
                        if (commonVersions.Count == 0)
                        {
                            commonVersions.AddRange(endpoint.ApiVersions);
                        }
                        else
                        {
                            commonVersions = new List<string>(commonVersions.Intersect(endpoint.ApiVersions));
                        }
                    }

                    rt.CommonApiVersions = commonVersions.ToArray();

                    foreach (var endpoint in rt.Endpoints)
                    {
                        var uniqueApiVersions = endpoint.ApiVersions.Except(rt.CommonApiVersions).ToArray();
                        endpoint.ApiVersions = uniqueApiVersions.Length > 0 ? uniqueApiVersions : null;
                    }
                }

                var serializer = SerializerSettings.SerializerMediaTypeSettings;
                serializer.Formatting = Newtonsoft.Json.Formatting.Indented;
                string json = JsonConvert.SerializeObject(manifest, serializer);

                File.WriteAllText("prod2.json", json);
            }
        }
        catch (IOException e)
        {
            Console.WriteLine("An error occurred while reading the file:");
            Console.WriteLine(e.Message);
        }
        catch (Exception ex)
        {
            Console.WriteLine("An error occurred while reading the file:");
            Console.WriteLine(ex.Message);
        }
    }
}