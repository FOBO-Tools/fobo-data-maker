using System.Text.Json.Serialization;

namespace DataMaker.Forms.Signing;

/// <summary>
/// Source-generated <see cref="JsonSerializerContext"/> for the manifest +
/// FOBO-attestation payloads this project owns. Required so the Terminal
/// renderer (and any other trimmed/AOT consumer) can verify a <c>.dmf</c>
/// without the trimmer stripping property accessors that the reflection-based
/// resolver would have lazily walked at runtime.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(DmfManifest))]
[JsonSerializable(typeof(FoboAttestationPayload))]
public partial class SigningJsonContext : JsonSerializerContext;
