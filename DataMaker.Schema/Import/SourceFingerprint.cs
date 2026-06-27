using System.Security.Cryptography;
using System.Text;

namespace DataMaker.Schema.Import;

/// <summary>
/// A stable identifier for an import source <i>layout</i>, derived purely from
/// its set of field/column names. Two sources with the same name set fingerprint
/// identically (order- and duplicate-independent), so a mapping saved for one
/// auto-applies to the next. Values are ignored — only names define the layout.
/// </summary>
public static class SourceFingerprint
{
    /// <summary>
    /// SHA-256 (hex) of the distinct names, sorted ordinally and joined by
    /// newlines. Empty input yields a fixed empty-set fingerprint.
    /// </summary>
    public static string Compute(IEnumerable<string> fieldNames)
    {
        ArgumentNullException.ThrowIfNull(fieldNames);

        var canonical = string.Join('\n',
            fieldNames.Where(n => !string.IsNullOrEmpty(n))
                      .Distinct(StringComparer.Ordinal)
                      .OrderBy(n => n, StringComparer.Ordinal));

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>Fingerprint a field list by its names.</summary>
    public static string Compute(IEnumerable<ImportField> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        return Compute(fields.Select(f => f.Name));
    }
}
