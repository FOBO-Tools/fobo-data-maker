using System.Text.Json.Serialization;

namespace DataMaker.Sdk;

/// <summary>
/// Source-generated JSON for the wire types, so the SDK is trim/AOT-safe — a
/// reflection-based resolver would have its property accessors stripped under
/// <c>PublishTrimmed=true</c> (e.g. inside the terminal's single-file publish).
///
/// <para>
/// <see cref="SubmissionPayload.Values"/> is <c>IReadOnlyDictionary&lt;string,
/// object?&gt;</c>; the concrete value types a coerced cell can land as are
/// registered below so the dictionary-of-object path resolves without the
/// reflection fallback. Values of other runtime types (e.g. custom image /
/// attachment refs passed through with <c>Validate = false</c>) still require
/// the caller to preserve their own types under trimming.
/// </para>
/// </summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(SubmissionEnvelope))]
[JsonSerializable(typeof(SubmissionPayload))]
[JsonSerializable(typeof(SubmissionResponse))]
[JsonSerializable(typeof(IReadOnlyDictionary<string, object?>))]
[JsonSerializable(typeof(Dictionary<string, object?>))]
[JsonSerializable(typeof(object))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(decimal))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(List<string>))]
internal sealed partial class DataMakerJsonContext : JsonSerializerContext;
