using System.Globalization;
using System.Text.RegularExpressions;
using DataMaker.Schema.Fields;

namespace DataMaker.Schema.Validation;

/// <summary>
/// A preset validation check baked into a field type. Users don't author
/// these — they see them as read-only "always enforced" items in the designer
/// and the runtime runs them on save alongside user rules.
///
/// <para>
/// Intrinsics are derived from kind + options at read time; they are NOT
/// written to the form's JSON. That keeps storage clean and lets us tighten
/// a built-in regex (e.g. email) without migrating old forms — next load
/// picks up the new rule automatically.
/// </para>
///
/// <para>
/// <b>Custom messages:</b> intrinsics consult <see cref="FieldDefinition.Messages"/>
/// keyed by the slot ids in <see cref="IntrinsicMessageCatalog"/> before
/// falling back to the engine defaults in <see cref="IntrinsicMessages"/>.
/// </para>
/// </summary>
public sealed record IntrinsicValidator(
    string Description,
    Func<object?, FieldDefinition, string?> Validate);

public static class IntrinsicValidators
{
    // ── Regexes ─────────────────────────────────────────────────────
    // Deliberately loose. The designer advertises the *concept* (must be
    // an email / URL / etc.) rather than enforcing a draconian spec — which
    // always over-rejects in practice.

    private static readonly Regex EmailRx = new(
        @"^[^\s@]+@[^\s@]+\.[^\s@]+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex PhoneRx = new(
        @"^\+?[0-9\s\-().]{6,}$",
        RegexOptions.Compiled);

    /// <summary>
    /// Resolve the message for a given slot: custom override on the field
    /// wins, otherwise the engine default. Empty/whitespace override falls
    /// through to the default so an accidentally-blanked entry doesn't
    /// silently swallow the error.
    /// </summary>
    private static string Msg(FieldDefinition field, string slotId, string defaultMsg)
    {
        if (field.Messages is { } map
            && map.TryGetValue(slotId, out var custom)
            && !string.IsNullOrWhiteSpace(custom))
        {
            return custom;
        }
        return defaultMsg;
    }

    /// <summary>Built-in validators derived from <paramref name="field"/>. Text-shaped kinds compose their kind-specific intrinsic with the shared <see cref="TextOptionsValidators"/> block (min/max length, pattern). The flat <see cref="FieldDefinition.Required"/> bool is emitted as the first intrinsic so server-side validation gates on it.</summary>
    public static IReadOnlyList<IntrinsicValidator> GetFor(FieldDefinition field)
    {
        var list = new List<IntrinsicValidator>(6);

        if (field.Required)
            list.Add(RequiredValidator);

        switch (field.Kind)
        {
            case FieldTypes.Text:
            case FieldTypes.LongText:
                list.AddRange(TextOptionsValidators(field));
                break;
            case FieldTypes.Email:
                list.Add(EmailValidator);
                list.AddRange(TextOptionsValidators(field));
                break;
            case FieldTypes.Url:
                list.Add(UrlValidator);
                list.AddRange(TextOptionsValidators(field));
                break;
            case FieldTypes.Phone:
                list.Add(PhoneValidator);
                list.AddRange(TextOptionsValidators(field));
                break;
            case FieldTypes.Number:    list.Add(NumberValidator);          break;
            case FieldTypes.Decimal:   list.Add(DecimalValidator(field));  break;
            case FieldTypes.Money:     list.AddRange(MoneyValidators(field));      break;
            case FieldTypes.Date:      list.Add(DateValidator);            break;
            case FieldTypes.DateTime:  list.Add(DateTimeValidator);        break;
            case FieldTypes.Boolean:   list.Add(BooleanValidator);         break;
            case FieldTypes.Choice:      list.AddRange(ChoiceValidators(field));      break;
            case FieldTypes.MultiChoice: list.AddRange(MultiChoiceValidators(field)); break;
            case FieldTypes.Geo:         list.Add(GeoValidator);                       break;
            case FieldTypes.Image:       list.AddRange(AttachmentValidators(field, imageOnly: true));  break;
            case FieldTypes.Attachment:  list.AddRange(AttachmentValidators(field, imageOnly: false)); break;
            case FieldTypes.Relation:    list.AddRange(RelationValidators(field));    break;
        }

        return list;
    }

    // ── Required (flat field.Required bool) ─────────────────────────

    private static readonly IntrinsicValidator RequiredValidator = new(
        "Required",
        (value, field) => IsEmpty(value)
            ? Msg(field, "required", IntrinsicMessages.Required)
            : null);

    private static bool IsEmpty(object? value) => value switch
    {
        null                                                 => true,
        string s when string.IsNullOrEmpty(s)                => true,
        System.Collections.ICollection c when c.Count == 0   => true,
        SignatureRef sig when sig.IsBlank                    => true,
        _                                                    => false,
    };

    // ── TextOptions (MinLength / MaxLength / Pattern) ────────────────
    //
    // Applied to every text-shaped kind. Empty/null values pass silently —
    // pair with Required if the field must be filled.

    private static IntrinsicValidator[] TextOptionsValidators(FieldDefinition field)
    {
        var opts = field.Text;
        if (opts is null) return Array.Empty<IntrinsicValidator>();

        var list = new List<IntrinsicValidator>(3);

        if (opts.MinLength is int minLen && minLen > 0)
        {
            list.Add(new IntrinsicValidator(
                $"At least {minLen} character{(minLen == 1 ? "" : "s")}",
                (value, f) => value is string s && !string.IsNullOrEmpty(s) && s.Length < minLen
                    ? Msg(f, "text.minLength",
                          string.Format(IntrinsicMessages.TextMinLength, minLen, minLen == 1 ? "" : "s"))
                    : null));
        }

        if (opts.MaxLength is int maxLen && maxLen > 0)
        {
            list.Add(new IntrinsicValidator(
                $"At most {maxLen} character{(maxLen == 1 ? "" : "s")}",
                (value, f) => value is string s && s.Length > maxLen
                    ? Msg(f, "text.maxLength",
                          string.Format(IntrinsicMessages.TextMaxLength, maxLen, maxLen == 1 ? "" : "s"))
                    : null));
        }

        if (!string.IsNullOrWhiteSpace(opts.Pattern))
        {
            Regex? rx = null;
            try { rx = new Regex(opts.Pattern, RegexOptions.Compiled); }
            catch { /* malformed pattern — silently drop; design-time validator surfaces it */ }

            if (rx is not null)
            {
                list.Add(new IntrinsicValidator(
                    "Must match the required pattern",
                    (value, f) => value is string s && !string.IsNullOrEmpty(s) && !rx.IsMatch(s)
                        ? Msg(f, "text.pattern", IntrinsicMessages.TextPattern)
                        : null));
            }
        }

        return list.ToArray();
    }

    // ── Scalar validators ───────────────────────────────────────────

    private static readonly IntrinsicValidator EmailValidator = new(
        "Must be a valid email address",
        (value, f) => value switch
        {
            null or "" => null,
            string s when EmailRx.IsMatch(s) => null,
            _ => Msg(f, "email", IntrinsicMessages.Email),
        });

    private static readonly IntrinsicValidator UrlValidator = new(
        "Must be a valid URL (http / https)",
        (value, f) =>
        {
            if (value is not string s || string.IsNullOrEmpty(s)) return null;
            return Uri.TryCreate(s, UriKind.Absolute, out var u)
                   && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps)
                ? null : Msg(f, "url", IntrinsicMessages.Url);
        });

    private static readonly IntrinsicValidator PhoneValidator = new(
        "Must be a valid phone number",
        (value, f) => value switch
        {
            null or "" => null,
            string s when PhoneRx.IsMatch(s) => null,
            _ => Msg(f, "phone", IntrinsicMessages.Phone),
        });

    private static readonly IntrinsicValidator NumberValidator = new(
        "Must be a whole number",
        (value, f) => value switch
        {
            null or "" => null,
            long or int => null,
            string s when long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) => null,
            _ => Msg(f, "number", IntrinsicMessages.Number),
        });

    private static IntrinsicValidator DecimalValidator(FieldDefinition field) => new(
        field.Number?.DecimalPlaces is int dp && dp >= 0
            ? $"Must be a decimal number (max {dp} decimal places)"
            : "Must be a decimal number",
        (value, f) => value switch
        {
            null or "" => null,
            decimal or double or int or long => null,
            string s when decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out _) => null,
            _ => Msg(f, "decimal", IntrinsicMessages.DecimalNumber),
        });

    private static IntrinsicValidator[] MoneyValidators(FieldDefinition field)
    {
        var currency = field.Money?.Currency ?? "EUR";
        var dp = field.Money?.DecimalPlaces ?? 2;
        return new[]
        {
            new IntrinsicValidator(
                $"Must be a valid amount in {currency} ({dp} decimal places)",
                (value, f) => value switch
                {
                    null or "" => null,
                    decimal or double or int or long => null,
                    string s when decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out _) => null,
                    _ => Msg(f, "money", IntrinsicMessages.Money),
                }),
        };
    }

    // The `field` discard parameter is named (not `_`) so the `out _` in the
    // DateTime.TryParse / TryParseExact calls inside the switch arms doesn't
    // collide with the lambda's discard — the compiler binds a stray `_` to
    // the closest discard in scope and reads `out _` as the lambda's
    // FieldDefinition param without this rename.
    private static readonly IntrinsicValidator DateValidator = new(
        "Must be a valid date (YYYY-MM-DD)",
        (value, f) => value switch
        {
            null or "" => null,
            // Inline-edit pickers (records grid) bind their native CLR type, not
            // the stored "yyyy-MM-dd" string — a typed date is trivially valid.
            DateOnly or DateTime or DateTimeOffset => null,
            string s when DateTime.TryParseExact(s, "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out _) => null,
            _ => Msg(f, "date", IntrinsicMessages.Date),
        });

    private static readonly IntrinsicValidator DateTimeValidator = new(
        "Must be a valid ISO-8601 date-time",
        (value, f) => value switch
        {
            null or "" => null,
            DateOnly or DateTime or DateTimeOffset => null,
            string s when DateTime.TryParse(s, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out _) => null,
            _ => Msg(f, "datetime", IntrinsicMessages.DateTime),
        });

    private static readonly IntrinsicValidator BooleanValidator = new(
        "Must be true or false",
        (value, f) => value switch
        {
            null or "" or bool => null,
            string s when bool.TryParse(s, out _) => null,
            _ => Msg(f, "boolean", IntrinsicMessages.Boolean),
        });

    // ── Choice / MultiChoice ────────────────────────────────────────

    private static IntrinsicValidator[] ChoiceValidators(FieldDefinition field)
    {
        var opts = field.Choice;
        if (opts is null || opts.Choices.Count == 0 || opts.AllowCustom)
            return Array.Empty<IntrinsicValidator>();

        var allowed = opts.Choices.Select(c => c.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var labels  = string.Join(", ", opts.Choices.Select(c => c.Label));
        return new[]
        {
            new IntrinsicValidator(
                $"Must be one of: {labels}",
                (value, f) => value is null or "" || (value is string s && allowed.Contains(s))
                    ? null : Msg(f, "choice", IntrinsicMessages.Choice)),
        };
    }

    private static IntrinsicValidator[] MultiChoiceValidators(FieldDefinition field)
    {
        var opts = field.Choice;
        if (opts is null || opts.Choices.Count == 0 || opts.AllowCustom)
            return Array.Empty<IntrinsicValidator>();

        var allowed = opts.Choices.Select(c => c.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var labels  = string.Join(", ", opts.Choices.Select(c => c.Label));
        return new[]
        {
            new IntrinsicValidator(
                $"Each item must be one of: {labels}",
                (value, f) =>
                {
                    if (value is null) return null;
                    if (value is IEnumerable<string> items)
                        return items.All(allowed.Contains) ? null : Msg(f, "multichoice", IntrinsicMessages.MultiChoice);
                    return Msg(f, "multichoice", IntrinsicMessages.MultiChoice);
                }),
        };
    }

    // ── Geo ─────────────────────────────────────────────────────────

    private static readonly IntrinsicValidator GeoValidator = new(
        "Must be a valid latitude/longitude pair",
        (value, f) => value switch
        {
            null or ""                                             => null,
            Geo g when g.Lat is < -90 or > 90                      => Msg(f, "geo.lat", IntrinsicMessages.GeoLat),
            Geo g when g.Lng is < -180 or > 180                    => Msg(f, "geo.lng", IntrinsicMessages.GeoLng),
            Geo                                                    => null,
            _                                                      => Msg(f, "geo", IntrinsicMessages.Geo),
        });

    // ── Image / Attachment ──────────────────────────────────────────

    private static IntrinsicValidator[] AttachmentValidators(FieldDefinition field, bool imageOnly)
    {
        var list = new List<IntrinsicValidator>();
        var opts = field.Attachment;

        if (imageOnly)
        {
            var imageExts = opts?.AcceptedExtensions is { Length: > 0 } ex
                ? ex
                : new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".heic" };
            list.Add(new IntrinsicValidator(
                $"Must be an image file ({string.Join(", ", imageExts)})",
                (value, f) => ExtensionMatches(value, imageExts)
                    ? null : Msg(f, "attachment.ext", IntrinsicMessages.AttachmentExt)));
        }
        else if (opts?.AcceptedExtensions is { Length: > 0 } exts)
        {
            list.Add(new IntrinsicValidator(
                $"Allowed extensions: {string.Join(", ", exts)}",
                (value, f) => ExtensionMatches(value, exts)
                    ? null : Msg(f, "attachment.ext", IntrinsicMessages.AttachmentExt)));
        }

        if (opts?.MaxSizeBytes is long max && max > 0)
        {
            var mb = max / (1024.0 * 1024.0);
            list.Add(new IntrinsicValidator(
                $"Max size: {mb:0.#} MB",
                (value, f) => null /* enforced at upload time, not from value */));
        }

        return list.ToArray();
    }

    private static bool ExtensionMatches(object? value, string[] exts)
    {
        if (value is null or "") return true;

        // Picked images/attachments are stored as typed ImageRef/AttachmentRef
        // records now (FileName + Mime + bytes). The old code path checked
        // Path.GetExtension(value as string) and rejected anything that
        // wasn't a string — which is every freshly picked file. Pull the
        // filename out of the typed shape first, then fall back to the
        // legacy string (a data URI or filename).
        string? candidate = value switch
        {
            ImageRef ir       => ir.FileName ?? ExtensionFromMime(ir.Mime),
            AttachmentRef ar  => ar.FileName ?? ExtensionFromMime(ar.Mime),
            string s          => s,
            _                 => null,
        };
        if (string.IsNullOrEmpty(candidate)) return false;

        var ext = Path.GetExtension(candidate);
        return ext.Length > 0 && exts.Any(e =>
            string.Equals(ext, e.StartsWith('.') ? e : "." + e, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Recover a synthetic ".ext" from a MIME so legacy ImageRefs with no
    /// FileName (just a data URI) still pass the extension check. Returns
    /// null for unknown mimes — caller treats that as "fail closed".
    /// </summary>
    private static string? ExtensionFromMime(string? mime) => mime switch
    {
        "image/png"        => "x.png",
        "image/jpeg"       => "x.jpg",
        "image/gif"        => "x.gif",
        "image/webp"       => "x.webp",
        "image/bmp"        => "x.bmp",
        "image/heic"       => "x.heic",
        "image/svg+xml"    => "x.svg",
        "application/pdf"  => "x.pdf",
        _                  => null,
    };

    // ── Relation ────────────────────────────────────────────────────

    private static IntrinsicValidator[] RelationValidators(FieldDefinition field)
    {
        var target = field.Relation?.TargetFormId;
        if (string.IsNullOrEmpty(target)) return Array.Empty<IntrinsicValidator>();

        return new[]
        {
            new IntrinsicValidator(
                $"Must reference an existing record in '{target}'",
                // Referential integrity is a storage-layer check — it needs the
                // record store. Designer shows the description; runtime wires
                // the check in when the record entry screen lands.
                (value, field) => null),
        };
    }
}
