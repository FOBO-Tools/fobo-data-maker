using System.Text.Json.Serialization;

namespace FOBO.Auth;

/// <summary>
/// Source-generated <see cref="JsonSerializerContext"/> for every type this
/// library serializes. Keeping JSON metadata source-generated (rather than
/// reflection-based) lets consumers ship trimmed / AOT-compiled binaries
/// without their <c>TokenSet</c> / OAuth response shapes silently being
/// stripped.
///
/// <para>
/// Every <see cref="System.Text.Json.JsonSerializer"/> call in this project
/// must route through one of the <see cref="Default"/>-rooted typeinfos so
/// the trimmer has a static reachability path to the property accessors.
/// </para>
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    WriteIndented = true)]
[JsonSerializable(typeof(TokenSet))]
[JsonSerializable(typeof(TokenEndpointResponse))]
internal partial class AuthJsonContext : JsonSerializerContext;
