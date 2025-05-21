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
    private static List<string> RegionsToCutover = new List<string>()
    {
        "East US 2 EUAP",
        "West Central US",
        "UK South",
        "Central US",
        "Australia Central"
    };

    public static void Main()
    {
        // TransformManifest(AddEndpointsForHcapisapi.UpdateManifestToAddHcapisapisEndpoints).Wait();
        TransformManifest(AddEndpointsForHcapisapi.UpdateManifestToAddHcapisapisEndpoints, CutoverToNewEndpoints.UpdateManifestToCutoverToHcapisInRegions).Wait();
    }

    public static async Task TransformManifest(Func<ResourceProviderManifest, ResourceProviderManifest> addEndpointsFunction, Func<ResourceProviderManifest, List<string>, ResourceProviderManifest> cutoverToEndpointsFunction)
    {
        try
        {
            ResourceProviderManifest manifest = await Common.GetProdManifestAsync("prod.json");
            ResourceProviderManifest transformedManifest = addEndpointsFunction(manifest);
            ResourceProviderManifest cutoverManifest = cutoverToEndpointsFunction(transformedManifest, RegionsToCutover);

            Common.SaveManifestToFile(cutoverManifest, "prod2.json");
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

    public static async Task TransformManifest(Func<ResourceProviderManifest, ResourceProviderManifest> manifestTranformation)
    {
        try
        {
            ResourceProviderManifest manifest = await Common.GetProdManifestAsync("prod.json");
            ResourceProviderManifest transformedManifest = manifestTranformation(manifest);

            Common.SaveManifestToFile(transformedManifest, "prod2.json");
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