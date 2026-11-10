using System.Globalization;
using System.Text.RegularExpressions;

namespace PasswordGenerator.Core;

/// <summary>
/// A version in the form x.y.z or x.y.z-betaNNN. A beta ranks below the full version with the same x.y.z.
/// </summary>
public readonly partial record struct AppVersion(int Major, int Minor, int Patch, int? Beta = null) : IComparable<AppVersion>
{
    public bool IsBeta => Beta is not null;

    public static bool TryParse(string? text, out AppVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var m = Pattern().Match(text.Trim());
        if (!m.Success) return false;
        int? beta = m.Groups[4].Success ? int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture) : null;
        version = new AppVersion(
            int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
            int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
            int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture),
            beta);
        return true;
    }

    public int CompareTo(AppVersion other)
    {
        var c = Major.CompareTo(other.Major);
        if (c != 0) return c;
        c = Minor.CompareTo(other.Minor);
        if (c != 0) return c;
        c = Patch.CompareTo(other.Patch);
        if (c != 0) return c;
        if (Beta is null) return other.Beta is null ? 0 : 1;
        return other.Beta is null ? -1 : Beta.Value.CompareTo(other.Beta.Value);
    }

    public override string ToString() =>
        Beta is null
            ? string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}")
            : string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}-beta{Beta.Value:000}");

    // Accepts an optional leading "v" and ignores build metadata after "+".
    [GeneratedRegex(@"^v?(\d{1,5})\.(\d{1,5})\.(\d{1,5})(?:-beta(\d{1,3}))?(?:\+.*)?$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
