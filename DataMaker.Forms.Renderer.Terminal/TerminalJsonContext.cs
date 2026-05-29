using System.Text.Json.Serialization;

namespace DataMaker.Forms.Renderer.Terminal;

/// <summary>
/// Source-generated <see cref="JsonSerializerContext"/> for terminal-local
/// state files. Keeps the on-disk trust store readable + writable under
/// trim/AOT publishes (the reflection-based resolver would otherwise drop
/// the <see cref="TrustRecord"/> property accessors as unreachable).
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    WriteIndented = true)]
[JsonSerializable(typeof(Dictionary<string, TrustRecord>))]
[JsonSerializable(typeof(TrustRecord))]
[JsonSerializable(typeof(DataMaker.Forms.Renderer.Terminal.Forms.Bindings.NominatimRaw[]))]
[JsonSerializable(typeof(DataMaker.Forms.Renderer.Terminal.Forms.Bindings.UploadSlotRequest))]
[JsonSerializable(typeof(DataMaker.Forms.Renderer.Terminal.Forms.Bindings.UploadSlotResponse))]
internal partial class TerminalJsonContext : JsonSerializerContext;
