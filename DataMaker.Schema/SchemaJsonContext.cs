using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DataMaker.Schema.Backups;
using DataMaker.Schema.Charts;
using DataMaker.Schema.Coachmarks;
using DataMaker.Schema.Dashboards;
using DataMaker.Schema.Fields;
using DataMaker.Schema.Forms;
using DataMaker.Schema.Layout;
using DataMaker.Schema.Styling;
using DataMaker.Schema.Validation;

namespace DataMaker.Schema;

/// <summary>
/// Source-generated <see cref="JsonSerializerContext"/> for every shape in
/// the schema graph. Registering <see cref="Form"/> here is what lets the
/// trimmer keep all the property accessors reachable from a Form in the
/// Terminal binary — without source-gen the reflection resolver walks the
/// graph lazily, so the trimmer has no way to see what's needed and silently
/// strips constructors/getters/setters that the deserializer then can't find.
///
/// <para>
/// <b>Polymorphic types</b> (<see cref="Column"/>'s field/group split,
/// <see cref="ValidationRule"/>'s discriminator) are listed explicitly: the
/// custom <see cref="ColumnJsonConverter"/> reads
/// <see cref="System.Text.Json.JsonSerializerOptions.GetTypeInfo"/> at
/// runtime, so the concrete subclasses must be registered or the converter
/// gets back a missing typeinfo. <see cref="ValidationRule"/> uses STJ's
/// built-in <c>$kind</c> discriminator and source-gen wires the derived
/// types automatically.
/// </para>
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(Form))]
[JsonSerializable(typeof(FieldDefinition))]
[JsonSerializable(typeof(DataMaker.Schema.Cards.CardLayout))]
[JsonSerializable(typeof(FormStep))]
[JsonSerializable(typeof(Section))]
[JsonSerializable(typeof(Row))]
[JsonSerializable(typeof(Column))]
[JsonSerializable(typeof(FieldColumn))]
[JsonSerializable(typeof(GroupColumn))]
[JsonSerializable(typeof(RichTextColumn))]
[JsonSerializable(typeof(ImageColumn))]
[JsonSerializable(typeof(DividerColumn))]
[JsonSerializable(typeof(HeadingColumn))]
[JsonSerializable(typeof(ButtonColumn))]
[JsonSerializable(typeof(SpacerColumn))]
[JsonSerializable(typeof(ButtonDefaults))]
[JsonSerializable(typeof(StepBarStyle))]
[JsonSerializable(typeof(Style))]
[JsonSerializable(typeof(FormStyle))]
[JsonSerializable(typeof(StylePalette))]
[JsonSerializable(typeof(ThemePreset))]
[JsonSerializable(typeof(List<ThemePreset>))]
[JsonSerializable(typeof(ChartDefinition))]
[JsonSerializable(typeof(List<ChartDefinition>))]
[JsonSerializable(typeof(ChartValue))]
[JsonSerializable(typeof(List<ChartValue>))]
[JsonSerializable(typeof(ChartFilter))]
[JsonSerializable(typeof(List<ChartFilter>))]
[JsonSerializable(typeof(DateGranularity))]
[JsonSerializable(typeof(DateGranularity?))]
[JsonSerializable(typeof(List<DateGranularity?>))]
[JsonSerializable(typeof(Dashboard))]
[JsonSerializable(typeof(DashboardTile))]
[JsonSerializable(typeof(List<DashboardTile>))]
[JsonSerializable(typeof(MarkdownTilePayload))]
[JsonSerializable(typeof(ChartTilePayload))]
[JsonSerializable(typeof(LatestRecordsTilePayload))]
[JsonSerializable(typeof(BackupManifest))]
[JsonSerializable(typeof(BackupSettings))]
[JsonSerializable(typeof(CloudBackupListResponse))]
[JsonSerializable(typeof(CloudBackupQuotaResponse))]
[JsonSerializable(typeof(CloudBackupDownloadResponse))]
[JsonSerializable(typeof(AgentSettings))]
[JsonSerializable(typeof(CoachmarkSettings))]
[JsonSerializable(typeof(ValidationRule))]
[JsonSerializable(typeof(Choice))]
[JsonSerializable(typeof(Geo))]
[JsonSerializable(typeof(List<FieldDefinition>))]
[JsonSerializable(typeof(List<FormStep>))]
[JsonSerializable(typeof(List<Section>))]
[JsonSerializable(typeof(List<Row>))]
[JsonSerializable(typeof(List<Column>))]
[JsonSerializable(typeof(List<Choice>))]
[JsonSerializable(typeof(List<ValidationRule>))]
[JsonSerializable(typeof(Dictionary<string, object?>))]
[JsonSerializable(typeof(IReadOnlyDictionary<string, object?>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
// FieldDefinition.DefaultValue is `object?`. STJ stores incoming JSON
// primitives as JsonElement on the read side and needs the typeinfo to
// echo them back on the write side; without these, source-gen throws
// "JsonTypeInfo metadata for type 'System.Text.Json.JsonElement' was not
// provided" the first time we round-trip a form whose author set a
// default value.
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(JsonObject))]
[JsonSerializable(typeof(JsonNode))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(decimal))]
[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(DateTimeOffset))]
public partial class SchemaJsonContext : JsonSerializerContext;
