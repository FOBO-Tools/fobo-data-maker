using System.Text;
using DataMaker.Schema.Forms;

namespace DataMaker.Schema.Records;

/// <summary>
/// Streams a form's records to RFC-4180 CSV (UTF-8 with BOM so Excel
/// autodetects the encoding). Pure + UI-free — extracted from
/// <c>RecordListViewModel</c> (#81g) so it is unit-testable. Caller supplies
/// the localized <see cref="RecordValueLabels"/> (the value fallbacks) and the
/// "Updated" column header; the formatting logic itself is the shared
/// <see cref="RecordValueFormatter"/>, so CSV / grid / PDF can't drift.
/// </summary>
public static class RecordCsvExporter
{
    private const int ProgressInterval = 100;

    public static async Task<long> WriteAsync(
        Stream output,
        Form form,
        IAsyncEnumerable<Record> records,
        RecordValueLabels labels,
        string updatedColumnHeader,
        IProgress<long>? progress = null,
        CancellationToken ct = default)
    {
        await using var sw = new StreamWriter(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        sw.Write(string.Join(",", form.Fields.Select(f => CsvEscape(f.Label))));
        sw.Write($",{CsvEscape(updatedColumnHeader)}\n");

        long count = 0;
        await foreach (var record in records.WithCancellation(ct))
        {
            var cells = form.Fields.Select(f =>
                CsvEscape(RecordValueFormatter.FormatForDisplay(
                    record.Values.GetValueOrDefault(f.Name), f, labels) ?? ""));
            sw.Write(string.Join(",", cells));
            sw.Write(',');
            sw.Write(CsvEscape(record.UpdatedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm")));
            sw.Write('\n');
            count++;
            if (count % ProgressInterval == 0) progress?.Report(count);
        }

        progress?.Report(count);
        return count;
    }

    public static string CsvEscape(string value)
    {
        if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
