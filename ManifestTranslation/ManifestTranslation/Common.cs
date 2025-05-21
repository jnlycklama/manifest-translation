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
    internal static class Common
    {
        public async static Task<ResourceProviderManifest> GetProdManifestAsync(string fileName)
        {
            // Open the text file using a stream reader.
            using StreamReader reader = new StreamReader(fileName);

            // Read the entire stream asynchronously and await the result.
            string text = await reader.ReadToEndAsync();

            var options = new JsonSerializerOptions
            {
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            };
            
            return JsonConvert.DeserializeObject<ResourceProviderManifest>(text, SerializerSettings.SerializerMediaTypeSettings);
        }

        public static void SaveManifestToFile(ResourceProviderManifest manifest, string fileName)
        {
            var serializer = SerializerSettings.SerializerMediaTypeSettings;
            serializer.Formatting = Newtonsoft.Json.Formatting.Indented;
            string json = JsonConvert.SerializeObject(manifest, serializer);

            File.WriteAllText(fileName, json);
        }

        public static string GetEndpoint(string location)
        {
            if (location == string.Empty)
            {
                return Constants.GlobalEndpoint;
            }

            return string.Format(Constants.RegionalEndpointFormat, Constants.LocationToShortName[location]);
        }

        public static bool IsAKSEndpoint(string endpoint)
        {
            return endpoint.StartsWith("https://hcapisapi");
        }
        
        public static bool IsTestEndpoint(ResourceProviderEndpoint endpoint)
        {
            return endpoint.RequiredFeatures != null && endpoint.RequiredFeatures.Length == 1 && (endpoint.RequiredFeatures.Contains(Constants.PreprodFeatureFlag) || endpoint.RequiredFeatures.Contains(Constants.IntFeatureFlag));
        }

        public static bool IsGlobalEndpoint(ResourceProviderEndpoint endpoint)
        {
            return endpoint.Locations == null || endpoint.Locations.Length == 0 || endpoint.Locations.Contains("");
        }

        public static bool GetAksEndpointIndex(this List<ResourceProviderEndpoint> endpoints, string region, int currentIndex, List<string> currentRequiredFeatures, out int aksEndpointIndex, out List<string> aksFeatureFlags)
        {
            aksFeatureFlags = new List<string>();
            aksEndpointIndex = -1;

            // find the index of the existing AKS endpoint, if it is enabled in this region and still has a feature flag on it
            int matchingEndpoints = endpoints.Count(e => e.Locations.Contains(region) && e.RequiredFeatures != null && e.RequiredFeatures.Contains(Constants.AKSFeatureFlag));

            if (matchingEndpoints == 0)
            {
                Console.WriteLine($"RT does not have AKS endpoint for region {region}. Run 'AddEndpointsForHcapisapi' before this.");
                return false;
            }

            if (matchingEndpoints == 1 || currentRequiredFeatures == null)
            {
                aksEndpointIndex = endpoints.IndexOf(e => e.Locations.Contains(region) && e.RequiredFeatures != null && e.RequiredFeatures.Contains(Constants.AKSFeatureFlag));
            }
            else if (currentRequiredFeatures != null)
            {
                // there are mutliple endpoints to match on, so choose the one with the same feature flag set
                aksEndpointIndex = endpoints.IndexOf(e => e.Locations.Contains(region) && e.RequiredFeatures != null && e.RequiredFeatures.Contains(Constants.AKSFeatureFlag) && currentRequiredFeatures.All(f => e.RequiredFeatures.Contains(f)));
            }

            if (aksEndpointIndex < 0)
            {
                // endpoint is not found
                // aksEndpointIndex = endpoints.IndexOf(e => e.Locations.Contains(region) && e.RequiredFeatures != null && e.RequiredFeatures.Contains(Constants.AKSFeatureFlag));
                return false;
            }

            if (aksEndpointIndex == currentIndex)
            {
                // dont get index if its the current index
                return false;
            }

            aksFeatureFlags = endpoints[aksEndpointIndex].RequiredFeatures.ToList();
            aksFeatureFlags.Remove(Constants.AKSFeatureFlag);

            return true;
        }
    }
}
