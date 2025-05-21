using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ManifestTranslation
{
    internal static class Constants
    {
        public static string GlobalEndpoint = "https://hcapisapi.rp.azurehealthcareapis.com/providers/Microsoft.HealthcareApis/";
        public static string RegionalEndpointFormat = "https://hcapisapi-{0}.rp.azurehealthcareapis.com/providers/Microsoft.HealthcareApis/";

        public static string IntFeatureFlag = "Microsoft.HealthcareApis/IntTestEnv";
        public static string AKSFeatureFlag = "Microsoft.HealthcareApis/TestEnvAKS";
        public static string PreprodFeatureFlag = "Microsoft.HealthcareApis/PreprodTestEnv";

        private static List<string> RegionsToCutover = new List<string>()
        {
            "East US 2 EUAP",
            "West Central US",
            "UK South"
        };

        public static List<string> SkippedResourceTypes = new List<string>()
        {
            "workspaces/analyticsconnectors",
            "workspaces/iotconnectors/destinations",
            "services/iomtconnectors",
            "services/iomtconnectors/connections",
            "services/iomtconnectors/mappings",
            "locations/checkNameAvailability", // skipping because it has no endpoints open to public (without a ff)
            "locations",
            "validateMedtechMappings"
        };

        public static Dictionary<string, string> LocationToShortName = new Dictionary<string, string>()
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
    }
}
