using Azure.Deployments.Core.Json;
using Microsoft.WindowsAzure.ResourceStack.Common.Manifest.Entities;
using Newtonsoft.Json;
using System.Text.Json;
using System.Xml;

namespace ManifestTranslation;

class Program
{
    public static void Main()
    {
        UpdateManifest().Wait();
    }

    private static async Task UpdateManifest()
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