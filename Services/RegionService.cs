// RegionService.cs — Region detection and country codes
using System.Globalization;

namespace Burudani.Services;

public static class RegionService
{
    public static readonly Dictionary<string, string> SupportedRegions = new()
    {
        ["WW"] = "Worldwide",
        ["UG"] = "Uganda",
        ["US"] = "United States",
        ["GB"] = "United Kingdom",
        ["CA"] = "Canada",
        ["AU"] = "Australia",
        ["DE"] = "Germany",
        ["FR"] = "France",
        ["KE"] = "Kenya",
        ["NG"] = "Nigeria",
        ["ZA"] = "South Africa",
        ["IN"] = "India",
        ["JP"] = "Japan",
        ["BR"] = "Brazil",
        ["MX"] = "Mexico",
        ["ES"] = "Spain",
        ["IT"] = "Italy",
        ["NL"] = "Netherlands",
        ["SE"] = "Sweden",
    };

    public static string DetectSystemRegion()
    {
        try
        {
            var region = RegionInfo.CurrentRegion;
            var code = region.TwoLetterISORegionName.ToUpperInvariant();
            if (SupportedRegions.ContainsKey(code))
                return code;
        }
        catch { }

        try
        {
            var culture = CultureInfo.CurrentCulture;
            if (culture.Name.Contains('-'))
            {
                var code = culture.Name.Split('-')[1].ToUpperInvariant();
                if (SupportedRegions.ContainsKey(code))
                    return code;
            }
        }
        catch { }

        return "US";
    }

    public static bool IsInternetAvailable()
    {
        return Connectivity.Current.NetworkAccess == NetworkAccess.Internet;
    }
}
