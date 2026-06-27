using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using DataMaker.Schema.Fields;
using DataMaker.Schema.Forms;

namespace DataMaker.Schema.Records;

/// <summary>
/// Streams a form's records to a minimal Open XML <c>.xlsx</c> (no ClosedXML /
/// EPPlus dependency — the spec is small enough that a few hundred lines of
/// literal XML do the job). Streaming end-to-end: one record at a time onto the
/// ZipArchive entry stream, so memory stays bounded regardless of row count.
///
/// <para>Pure + UI-free — extracted from <c>RecordListViewModel</c> (#81g) so it
/// is unit-testable. Caller supplies the localized <see cref="RecordValueLabels"/>
/// for the value fallbacks; typed kinds (number / decimal / money / date /
/// datetime / boolean) emit native cells, everything else falls back to a
/// formatted inline string via the shared <see cref="RecordValueFormatter"/>.</para>
/// </summary>
public static class RecordXlsxExporter
{
    private const int ProgressInterval = 100;

    // OOXML cell-format indices into XlsxStylesXml's cellXfs. Keep in sync
    // with the cellXfs ordering below.
    private const string XlsxStyleHeader   = " s=\"1\"";
    private const string XlsxStyleDate     = " s=\"2\"";
    private const string XlsxStyleDateTime = " s=\"3\"";
    private const string XlsxStyleTitle    = " s=\"4\"";
    private const string XlsxStyleMoney    = " s=\"5\"";

    // styles.xml — 6 cell formats:
    //   0 default, 1 header (white bold on deep blue, centered),
    //   2 date (yyyy-mm-dd), 3 datetime (yyyy-mm-dd hh:mm),
    //   4 title (bold 14pt slate), 5 money (#,##0.00).
    // Fonts: 0 body, 1 header (white bold), 2 title (slate bold 14).
    // Fills: 0 none, 1 gray125, 2 deep-blue header (#1F4E79).
    private const string XlsxStylesXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
          "<numFmts count=\"2\">" +
            "<numFmt numFmtId=\"164\" formatCode=\"yyyy-mm-dd\"/>" +
            "<numFmt numFmtId=\"165\" formatCode=\"yyyy-mm-dd&quot; &quot;hh:mm\"/>" +
          "</numFmts>" +
          "<fonts count=\"3\">" +
            "<font><sz val=\"11\"/><color rgb=\"FF1F2937\"/><name val=\"Calibri\"/></font>" +
            "<font><b/><sz val=\"11\"/><color rgb=\"FFFFFFFF\"/><name val=\"Calibri\"/></font>" +
            "<font><b/><sz val=\"14\"/><color rgb=\"FF1F2937\"/><name val=\"Calibri\"/></font>" +
          "</fonts>" +
          "<fills count=\"3\">" +
            "<fill><patternFill patternType=\"none\"/></fill>" +
            "<fill><patternFill patternType=\"gray125\"/></fill>" +
            "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF1F4E79\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
          "</fills>" +
          "<borders count=\"1\"><border/></borders>" +
          "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
          "<cellXfs count=\"6\">" +
            "<xf numFmtId=\"0\"   fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
            "<xf numFmtId=\"0\"   fontId=\"1\" fillId=\"2\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyAlignment=\"1\"><alignment horizontal=\"left\" vertical=\"center\"/></xf>" +
            "<xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
            "<xf numFmtId=\"165\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
            "<xf numFmtId=\"0\"   fontId=\"2\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +
            "<xf numFmtId=\"4\"   fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
          "</cellXfs>" +
          "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
        "</styleSheet>";

    private const string XlsxContentTypesXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
          "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
          "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
          "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
          "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
          "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
          "<Override PartName=\"/xl/tables/table1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.table+xml\"/>" +
        "</Types>";

    private const string XlsxRootRelsXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
          "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
        "</Relationships>";

    private const string XlsxWorkbookXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" " +
                 "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
          "<sheets><sheet name=\"Records\" sheetId=\"1\" r:id=\"rId1\"/></sheets>" +
        "</workbook>";

    private const string XlsxWorkbookRelsXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
          "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
          "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
        "</Relationships>";

    private const string XlsxSheetRelsXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
          "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/table\" Target=\"../tables/table1.xml\"/>" +
        "</Relationships>";

    public static async Task<long> WriteAsync(
        Stream output,
        Form form,
        IAsyncEnumerable<Record> records,
        RecordValueLabels labels,
        IProgress<long>? progress = null,
        CancellationToken ct = default)
    {
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: false);

        WriteStaticEntry(zip, "[Content_Types].xml",                XlsxContentTypesXml);
        WriteStaticEntry(zip, "_rels/.rels",                        XlsxRootRelsXml);
        WriteStaticEntry(zip, "xl/workbook.xml",                    XlsxWorkbookXml);
        WriteStaticEntry(zip, "xl/_rels/workbook.xml.rels",         XlsxWorkbookRelsXml);
        WriteStaticEntry(zip, "xl/styles.xml",                      XlsxStylesXml);
        WriteStaticEntry(zip, "xl/worksheets/_rels/sheet1.xml.rels", XlsxSheetRelsXml);

        var lastColIx     = form.Fields.Count + 1; // +1 for the trailing Updated column
        var lastColLetter = ColumnLetter(lastColIx);
        var title         = string.IsNullOrWhiteSpace(form.Name) ? "Records" : form.Name;

        // Sheet stream — written incrementally so memory stays bounded
        // regardless of row count. Must close BEFORE table1.xml is opened
        // (one ZipArchive entry stream open at a time).
        long count;
        {
            var sheetEntry = zip.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.Optimal);
            await using var sheetStream = sheetEntry.Open();
            await using var sw = new StreamWriter(sheetStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            sw.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sw.Write("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">");
            // Freeze panes below row 2 (title + header stay visible while
            // scrolling through long data sets).
            sw.Write("<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"2\" topLeftCell=\"A3\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>");
            sw.Write("<sheetData>");

            // Row 1 — title. The cell carries the value + style; the
            // merge directive lower down (mergeCells) spans it across
            // the table width.
            sw.Write("<row r=\"1\" ht=\"22\" customHeight=\"1\">");
            WriteStyledStringCell(sw, "A", 1, title, XlsxStyleTitle);
            for (var i = 2; i <= lastColIx; i++) // emit empty cells in the merge so the row paints with no gap
                sw.Write($"<c r=\"{ColumnLetter(i)}1\"{XlsxStyleTitle}/>");
            sw.Write("</row>");

            // Row 2 — column headers (table header row).
            sw.Write("<row r=\"2\">");
            var col = 1;
            foreach (var f in form.Fields)
                WriteStyledStringCell(sw, ColumnLetter(col++), 2, f.Label, XlsxStyleHeader);
            WriteStyledStringCell(sw, ColumnLetter(col), 2, "Updated", XlsxStyleHeader);
            sw.Write("</row>");

            // Data rows — start at row 3.
            var rowIndex = 3u;
            count = 0;
            await foreach (var record in records.WithCancellation(ct))
            {
                sw.Write($"<row r=\"{rowIndex}\">");
                col = 1;
                foreach (var f in form.Fields)
                    WriteTypedCell(sw, ColumnLetter(col++), rowIndex, record.Values.GetValueOrDefault(f.Name), f, labels);
                // Updated column — emit as DateTime from the underlying
                // Record so it sorts correctly in Excel instead of being
                // a sortable-only-as-string display value.
                WriteDateTimeCell(sw, ColumnLetter(col), rowIndex, record.UpdatedAt);
                sw.Write("</row>");
                rowIndex++;
                count++;
                if (count % ProgressInterval == 0) progress?.Report(count);
            }

            sw.Write("</sheetData>");
            // mergeCells must come AFTER sheetData per the schema.
            sw.Write($"<mergeCells count=\"1\"><mergeCell ref=\"A1:{lastColLetter}1\"/></mergeCells>");
            // tableParts binds the table.xml part to this worksheet.
            sw.Write("<tableParts count=\"1\"><tablePart r:id=\"rId1\"/></tableParts>");
            sw.Write("</worksheet>");
        }

        // table1.xml — now that we know the actual row count, scope the
        // table range to cover the header (row 2) + every data row.
        WriteStaticEntry(zip, "xl/tables/table1.xml",
            BuildTableXml(form, lastColLetter, dataRowCount: count));

        progress?.Report(count);
        return count;
    }

    /// <summary>
    /// Build the OOXML &lt;table&gt; part. Range covers header + data
    /// rows; column names mirror the form's field labels (sanitized to
    /// be unique because Excel rejects duplicate tableColumn names).
    /// Uses the built-in TableStyleMedium2 — banded rows + a slate
    /// header that complements our custom header style above. The table
    /// gives Excel autofilter, structured references, and the "Insert
    /// Table" affordance for free.
    /// </summary>
    private static string BuildTableXml(Form form, string lastColLetter, long dataRowCount)
    {
        var headerRow = 2u;
        var lastRow   = headerRow + (uint)Math.Max(dataRowCount, 1); // require >=1 row for valid table ref
        var range     = $"A{headerRow}:{lastColLetter}{lastRow}";

        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append("<table xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" ");
        sb.Append($"id=\"1\" name=\"Records\" displayName=\"Records\" ref=\"{range}\" headerRowCount=\"1\" totalsRowShown=\"0\">");
        sb.Append($"<autoFilter ref=\"{range}\"/>");

        var colCount = form.Fields.Count + 1;
        sb.Append($"<tableColumns count=\"{colCount}\">");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ix   = 1;
        foreach (var f in form.Fields)
            sb.Append($"<tableColumn id=\"{ix++}\" name=\"{WebUtility.HtmlEncode(UniqueTableColumnName(f.Label, seen))}\"/>");
        sb.Append($"<tableColumn id=\"{ix}\" name=\"{WebUtility.HtmlEncode(UniqueTableColumnName("Updated", seen))}\"/>");
        sb.Append("</tableColumns>");
        sb.Append("<tableStyleInfo name=\"TableStyleMedium2\" showFirstColumn=\"0\" showLastColumn=\"0\" showRowStripes=\"1\" showColumnStripes=\"0\"/>");
        sb.Append("</table>");
        return sb.ToString();
    }

    /// <summary>
    /// Excel rejects duplicate tableColumn names. Append " (2)" / " (3)"
    /// when a label collides with a sibling.
    /// </summary>
    private static string UniqueTableColumnName(string raw, HashSet<string> seen)
    {
        var name = string.IsNullOrWhiteSpace(raw) ? "Column" : raw.Trim();
        var candidate = name;
        var n = 2;
        while (!seen.Add(candidate))
            candidate = $"{name} ({n++})";
        return candidate;
    }

    private static void WriteStaticEntry(ZipArchive zip, string name, string content)
    {
        var entry  = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var s = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        s.Write(bytes, 0, bytes.Length);
    }

    /// <summary>
    /// Inline-string cell with an optional style index. Used for the
    /// title row, the header row, and any field whose kind doesn't
    /// project to a typed XLSX cell (string-ish / composite).
    /// </summary>
    private static void WriteStyledStringCell(TextWriter w, string column, uint row, string value, string styleAttr)
    {
        var safe = WebUtility.HtmlEncode(value);
        w.Write($"<c r=\"{column}{row}\"{styleAttr} t=\"inlineStr\"><is><t xml:space=\"preserve\">{safe}</t></is></c>");
    }

    /// <summary>
    /// Type-aware cell emitter. Numbers go out as <c>t="n"</c> with a
    /// numeric &lt;v&gt; so Excel coerces / formats them; dates go out
    /// as the Excel serial number with the matching date cellXf; bools
    /// as <c>t="b"</c> with 0/1. Unhandled kinds fall back to formatted
    /// inline strings via <see cref="RecordValueFormatter.FormatForDisplay"/>
    /// so composite kinds (image / attachment / geo / list / etc.) keep
    /// a readable label in the cell.
    /// </summary>
    private static void WriteTypedCell(TextWriter w, string column, uint row, object? raw, FieldDefinition field, RecordValueLabels labels)
    {
        if (raw is null) return; // empty cell — Excel renders blank.
        var cellRef = column + row;

        switch (field.Kind)
        {
            case FieldTypes.Number:
                if (TryFormatInt64(raw, out var iStr))
                    w.Write($"<c r=\"{cellRef}\" t=\"n\"><v>{iStr}</v></c>");
                else
                    WriteStyledStringCell(w, column, row, raw.ToString() ?? "", "");
                return;

            case FieldTypes.Decimal:
                if (TryFormatDecimal(raw, out var dStr))
                    w.Write($"<c r=\"{cellRef}\" t=\"n\"><v>{dStr}</v></c>");
                else
                    WriteStyledStringCell(w, column, row, raw.ToString() ?? "", "");
                return;

            case FieldTypes.Money:
                if (TryFormatDecimal(raw, out var mStr))
                    w.Write($"<c r=\"{cellRef}\" t=\"n\"{XlsxStyleMoney}><v>{mStr}</v></c>");
                else
                    WriteStyledStringCell(w, column, row, raw.ToString() ?? "", "");
                return;

            case FieldTypes.Date:
                if (TryToExcelSerial(raw, out var dateSerial))
                    w.Write($"<c r=\"{cellRef}\"{XlsxStyleDate}><v>{dateSerial.ToString("0.######", CultureInfo.InvariantCulture)}</v></c>");
                else
                    WriteStyledStringCell(w, column, row, raw.ToString() ?? "", "");
                return;

            case FieldTypes.DateTime:
                if (TryToExcelSerial(raw, out var dtSerial))
                    w.Write($"<c r=\"{cellRef}\"{XlsxStyleDateTime}><v>{dtSerial.ToString("0.######", CultureInfo.InvariantCulture)}</v></c>");
                else
                    WriteStyledStringCell(w, column, row, raw.ToString() ?? "", "");
                return;

            case FieldTypes.Boolean:
            {
                var b = raw switch
                {
                    bool bb                                     => bb,
                    string s when bool.TryParse(s, out var sb)  => sb,
                    _                                            => false,
                };
                w.Write($"<c r=\"{cellRef}\" t=\"b\"><v>{(b ? 1 : 0)}</v></c>");
                return;
            }

            default:
                WriteStyledStringCell(w, column, row, RecordValueFormatter.FormatForDisplay(raw, field, labels) ?? "", "");
                return;
        }
    }

    private static void WriteDateTimeCell(TextWriter w, string column, uint row, DateTimeOffset value)
    {
        var serial = (value.LocalDateTime - ExcelEpoch).TotalDays;
        w.Write($"<c r=\"{column}{row}\"{XlsxStyleDateTime}><v>{serial.ToString("0.######", CultureInfo.InvariantCulture)}</v></c>");
    }

    private static readonly DateTime ExcelEpoch = new DateTime(1899, 12, 30, 0, 0, 0, DateTimeKind.Unspecified);

    private static bool TryFormatInt64(object raw, out string formatted)
    {
        try
        {
            formatted = Convert.ToInt64(raw, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
            return true;
        }
        catch { formatted = ""; return false; }
    }

    private static bool TryFormatDecimal(object raw, out string formatted)
    {
        try
        {
            formatted = Convert.ToDecimal(raw, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
            return true;
        }
        catch { formatted = ""; return false; }
    }

    private static bool TryToExcelSerial(object raw, out double serial)
    {
        DateTime dt;
        switch (raw)
        {
            case DateTime d:        dt = d; break;
            case DateTimeOffset dt2: dt = dt2.LocalDateTime; break;
            case string s when DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed):
                dt = parsed; break;
            default: serial = 0; return false;
        }
        serial = (dt - ExcelEpoch).TotalDays;
        return true;
    }

    private static string ColumnLetter(int n)
    {
        var sb = new StringBuilder();
        while (n > 0)
        {
            n--;
            sb.Insert(0, (char)('A' + (n % 26)));
            n /= 26;
        }
        return sb.ToString();
    }
}
