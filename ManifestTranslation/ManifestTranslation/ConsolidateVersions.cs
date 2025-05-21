using Azure.Deployments.Core.Json;
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
    internal class ConsolidateVersions
    {
        public ConsolidateVersions() { }

        public static ResourceProviderManifest UpdateManifestToConsolidateVersions(ResourceProviderManifest manifest)
        {
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

            return manifest;
        }
    }
}
