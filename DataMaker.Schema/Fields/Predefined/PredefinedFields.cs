using DataMaker.Schema.Validation;

namespace DataMaker.Schema.Fields.Predefined;

/// <summary>
/// Curated catalog of common fields. Seeded from the old Chivan.Leads.Fields.PredefinedFields
/// set and broadened beyond CRM (dates, commerce, status, media, location).
/// Deliberately hand-curated, not data-driven — plugin-style extension can
/// come later once we see which ones people actually use.
/// </summary>
public static class PredefinedFields
{
    public static IReadOnlyList<PredefinedField> All { get; } = BuildCatalog();

    private static List<PredefinedField> BuildCatalog() => new()
    {
        // ── Personal ──────────────────────────────────────────────
        new("first_name",   "Personal", "First name",   "Given name.",              "",
            names => Text("first_name", "First name", "Given name.", names, autocomplete: "given-name")),
        new("last_name",    "Personal", "Last name",    "Family name.",             "",
            names => Text("last_name", "Last name", "Family name.", names, autocomplete: "family-name")),
        new("full_name",    "Personal", "Full name",    "Complete name on one line.","",
            names => Text("full_name", "Full name", "Complete name on one line.", names, autocomplete: "name")),
        new("initials",     "Personal", "Initials",     "Short initials — uppercase.","",
            names => Text("initials", "Initials", "Short initials — uppercase.", names,
                new TextOptions { MaxLength = 10, Pattern = "^[A-Z.]+$" })),
        new("birth_date",   "Personal", "Birth date",   "Date of birth.",           "",
            names => Date("birth_date", "Birth date", "Date of birth.", names, autocomplete: "bday")),
        new("gender",       "Personal", "Gender",       "Gender identity.",         "",
            names => Choice("gender", "Gender", "Gender identity.", names,
                new[] { "female", "male", "non_binary", "prefer_not_to_say" },
                new[] { "Female", "Male", "Non-binary", "Prefer not to say" }, autocomplete: "sex")),
        new("age",          "Personal", "Age",          "Age in years.",            "",
            names => Integer("age", "Age", "Age in years.", names, min: 0, max: 150)),

        // ── Contact ───────────────────────────────────────────────
        new("email",         "Contact", "Email",          "Email address — validated.",     "",
            names => Typed(FieldTypes.Email, "email", "Email", "Email address — validated.", names, autocomplete: "email")),
        new("phone",         "Contact", "Phone",          "Phone number — validated.",      "",
            names => Typed(FieldTypes.Phone, "phone", "Phone", "Phone number — validated.", names, autocomplete: "tel")),
        new("website",       "Contact", "Website",        "URL — validated (http/https).",  "",
            names => Typed(FieldTypes.Url,   "website", "Website", "URL — validated (http/https).", names, autocomplete: "url")),
        new("social_handle", "Contact", "Social handle",  "e.g. @username.",                "",
            names => Text("social_handle", "Social handle", "e.g. @username.", names, autocomplete: "username")),

        // ── Address ───────────────────────────────────────────────
        new("address_line_1","Address", "Address line 1", "Street + house number.",         "",
            names => Text("address_line_1", "Address line 1", "Street + house number.", names, autocomplete: "address-line1")),
        new("address_line_2","Address", "Address line 2", "Apartment, suite, floor.",       "",
            names => Text("address_line_2", "Address line 2", "Apartment, suite, floor.", names, autocomplete: "address-line2")),
        new("postal_code",   "Address", "Postal code",    "ZIP / postcode.",                "",
            names => Text("postal_code", "Postal code", "ZIP / postcode.", names,
                new TextOptions { MaxLength = 12 }, autocomplete: "postal-code")),
        new("city",          "Address", "City",           "City or town.",                  "",
            names => Text("city", "City", "City or town.", names, autocomplete: "address-level2")),
        new("state_province","Address", "State / Province","State, province, or region.",   "",
            names => Text("state_province", "State/Province", "State, province, or region.", names, autocomplete: "address-level1")),
        new("country_code",  "Address", "Country code",   "ISO two-letter country code.",   "",
            names => Text("country_code", "Country code", "ISO two-letter country code.", names,
                new TextOptions { MinLength = 2, MaxLength = 2, Pattern = "^[A-Z]{2}$" }, autocomplete: "country")),

        // ── Business ──────────────────────────────────────────────
        new("company_name",    "Business", "Company name",     "Legal or trading name.",    "",
            names => Text("company_name", "Company name", "Legal or trading name.", names, autocomplete: "organization")),
        new("job_title",       "Business", "Job title",        "Role or position.",         "",
            names => Text("job_title", "Job title", "Role or position.", names, autocomplete: "organization-title")),
        new("vat_number",      "Business", "VAT number",       "Tax identifier.",           "",
            names => Text("vat_number", "VAT number", "Tax identifier.", names, autocomplete: "off")),
        new("iban",            "Business", "IBAN",             "International bank account number.", "",
            names => Text("iban", "IBAN", "International bank account number.", names,
                new TextOptions { Pattern = "^[A-Z]{2}[0-9A-Z]{13,32}$" }, autocomplete: "off")),

        // ── Commerce ──────────────────────────────────────────────
        new("price",      "Commerce", "Price",       "Amount in EUR, 2 decimals.",      "",
            names => Money("price", "Price", "Amount in EUR, 2 decimals.", names)),
        new("quantity",   "Commerce", "Quantity",    "Whole number, >= 0.",             "",
            names => Integer("quantity", "Quantity", "Whole number, >= 0.", names, min: 0)),
        new("percentage", "Commerce", "Percentage",  "Value 0–100 with 2 decimals.",    "",
            names => Decimal("percentage", "Percentage", "Value 0–100 with 2 decimals.", names,
                min: 0m, max: 100m, decimalPlaces: 2)),
        new("sku",        "Commerce", "SKU",         "Stock-keeping unit — uppercase alphanumeric.", "",
            names => Text("sku", "SKU", "Stock-keeping unit — uppercase alphanumeric.", names,
                new TextOptions { MaxLength = 32, Pattern = "^[A-Z0-9_-]+$" })),

        // ── Dates ─────────────────────────────────────────────────
        new("start_date", "Dates", "Start date", "Start of a period.",    "",
            names => Date("start_date", "Start date", "Start of a period.", names)),
        new("end_date",   "Dates", "End date",   "End of a period.",      "",
            names => Date("end_date", "End date", "End of a period.", names)),
        new("due_date",   "Dates", "Due date",   "Deadline.",             "",
            names => Date("due_date", "Due date", "Deadline.", names)),

        // ── Notes ─────────────────────────────────────────────────
        new("short_description", "Notes", "Short description", "One-line summary, 200 chars.", "",
            names => Text("short_description", "Short description", "One-line summary.", names,
                new TextOptions { MaxLength = 200 })),
        new("notes",             "Notes", "Notes",             "Free-form multi-line text.",   "",
            names => LongText("notes", "Notes", "Free-form multi-line text.", names)),

        // ── Status ────────────────────────────────────────────────
        new("active",   "Status", "Active",   "Yes / No flag.",                          "",
            names => Boolean("active", "Active", "Yes / No flag.", names, defaultValue: true)),
        new("priority", "Status", "Priority", "Low / Medium / High.",                    "",
            names => Choice("priority", "Priority", "Low / Medium / High.", names,
                new[] { "low", "medium", "high" }, new[] { "Low", "Medium", "High" })),
        new("status",   "Status", "Status",   "Draft / Active / Archived lifecycle.",    "",
            names => Choice("status", "Status", "Draft / Active / Archived lifecycle.", names,
                new[] { "draft", "active", "archived" }, new[] { "Draft", "Active", "Archived" })),

        // ── Ratings ───────────────────────────────────────────────
        new("star_rating", "Ratings", "Star rating (1–5)", "Integer rating from 1 to 5.", "",
            names => Integer("star_rating", "Star rating", "Integer rating from 1 to 5.", names,
                min: 1, max: 5)),

        // ── Media ─────────────────────────────────────────────────
        new("profile_photo", "Media", "Profile photo", "Image upload (jpg/png/webp).", "",
            names => Image("profile_photo", "Profile photo", "Image upload.", names)),
        new("document",      "Media", "Document",      "File attachment.",             "",
            names => Attachment("document", "Document", "File attachment.", names)),
        new("tags",          "Media", "Tags",          "Free-form tag set (multi-choice).", "",
            names => Typed(FieldTypes.List, "tags", "Tags", "Free-form tag set.", names)),

        // ── Location ──────────────────────────────────────────────
        new("location", "Location", "GPS location", "Latitude/longitude point.", "",
            names => Typed(FieldTypes.Geo, "location", "Location", "Latitude/longitude point.", names)),
    };

    // ── Factories ───────────────────────────────────────────────────

    private static FieldDefinition Text(
        string baseName, string label, string desc,
        IReadOnlyCollection<string> existing,
        TextOptions? options = null,
        string? autocomplete = null) => new()
    {
        Id = Guid.NewGuid().ToString("n"),
        Name = UniqueName(baseName, existing),
        Label = label,
        Description = desc,
        Kind = FieldTypes.Text,
        Text = options,
        Autocomplete = autocomplete,
    };

    private static FieldDefinition LongText(
        string baseName, string label, string desc,
        IReadOnlyCollection<string> existing) => new()
    {
        Id = Guid.NewGuid().ToString("n"),
        Name = UniqueName(baseName, existing),
        Label = label,
        Description = desc,
        Kind = FieldTypes.LongText,
    };

    private static FieldDefinition Typed(
        string kind,
        string baseName, string label, string desc,
        IReadOnlyCollection<string> existing,
        string? autocomplete = null) => new()
    {
        Id = Guid.NewGuid().ToString("n"),
        Name = UniqueName(baseName, existing),
        Label = label,
        Description = desc,
        Kind = kind,
        Autocomplete = autocomplete,
    };

    private static FieldDefinition Integer(
        string baseName, string label, string desc,
        IReadOnlyCollection<string> existing,
        long? min = null, long? max = null)
    {
        var rules = new List<ValidationRule>();
        if (min is not null) rules.Add(new MinValueRule(min.Value));
        if (max is not null) rules.Add(new MaxValueRule(max.Value));
        return new FieldDefinition
        {
            Id = Guid.NewGuid().ToString("n"),
            Name = UniqueName(baseName, existing),
            Label = label,
            Description = desc,
            Kind = FieldTypes.Number,
            Number = min is not null || max is not null
                ? new NumberOptions { Min = min, Max = max }
                : null,
            Validation = rules,
        };
    }

    private static FieldDefinition Decimal(
        string baseName, string label, string desc,
        IReadOnlyCollection<string> existing,
        decimal? min = null, decimal? max = null, int? decimalPlaces = null)
    {
        var rules = new List<ValidationRule>();
        if (min is not null) rules.Add(new MinValueRule(min.Value));
        if (max is not null) rules.Add(new MaxValueRule(max.Value));
        return new FieldDefinition
        {
            Id = Guid.NewGuid().ToString("n"),
            Name = UniqueName(baseName, existing),
            Label = label,
            Description = desc,
            Kind = FieldTypes.Decimal,
            Number = new NumberOptions { Min = min, Max = max, DecimalPlaces = decimalPlaces },
            Validation = rules,
        };
    }

    private static FieldDefinition Money(
        string baseName, string label, string desc,
        IReadOnlyCollection<string> existing,
        string currency = "EUR", int decimalPlaces = 2) => new()
    {
        Id = Guid.NewGuid().ToString("n"),
        Name = UniqueName(baseName, existing),
        Label = label,
        Description = desc,
        Kind = FieldTypes.Money,
        Money = new MoneyOptions { Currency = currency, DecimalPlaces = decimalPlaces },
    };

    private static FieldDefinition Date(
        string baseName, string label, string desc,
        IReadOnlyCollection<string> existing,
        string? autocomplete = null) => new()
    {
        Id = Guid.NewGuid().ToString("n"),
        Name = UniqueName(baseName, existing),
        Label = label,
        Description = desc,
        Kind = FieldTypes.Date,
        Autocomplete = autocomplete,
    };

    private static FieldDefinition Boolean(
        string baseName, string label, string desc,
        IReadOnlyCollection<string> existing,
        bool? defaultValue = null) => new()
    {
        Id = Guid.NewGuid().ToString("n"),
        Name = UniqueName(baseName, existing),
        Label = label,
        Description = desc,
        Kind = FieldTypes.Boolean,
        DefaultValue = defaultValue,
    };

    private static FieldDefinition Choice(
        string baseName, string label, string desc,
        IReadOnlyCollection<string> existing,
        string[] values, string[] labels,
        string? autocomplete = null) => new()
    {
        Id = Guid.NewGuid().ToString("n"),
        Name = UniqueName(baseName, existing),
        Label = label,
        Description = desc,
        Kind = FieldTypes.Choice,
        Choice = new ChoiceOptions
        {
            Choices = values.Zip(labels, (v, l) => new Choice { Value = v, Label = l }).ToList(),
        },
        Autocomplete = autocomplete,
    };

    private static FieldDefinition MultiChoice(
        string baseName, string label, string desc,
        IReadOnlyCollection<string> existing,
        string[] values, string[] labels, bool allowCustom = false) => new()
    {
        Id = Guid.NewGuid().ToString("n"),
        Name = UniqueName(baseName, existing),
        Label = label,
        Description = desc,
        Kind = FieldTypes.MultiChoice,
        Choice = new ChoiceOptions
        {
            Choices = values.Zip(labels, (v, l) => new Choice { Value = v, Label = l }).ToList(),
            AllowCustom = allowCustom,
        },
    };

    private static FieldDefinition Image(
        string baseName, string label, string desc,
        IReadOnlyCollection<string> existing) => new()
    {
        Id = Guid.NewGuid().ToString("n"),
        Name = UniqueName(baseName, existing),
        Label = label,
        Description = desc,
        Kind = FieldTypes.Image,
        Attachment = new AttachmentOptions
        {
            AcceptedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" },
            MaxSizeBytes = 10 * 1024 * 1024,  // 10 MB
        },
    };

    private static FieldDefinition Attachment(
        string baseName, string label, string desc,
        IReadOnlyCollection<string> existing) => new()
    {
        Id = Guid.NewGuid().ToString("n"),
        Name = UniqueName(baseName, existing),
        Label = label,
        Description = desc,
        Kind = FieldTypes.Attachment,
        Attachment = new AttachmentOptions
        {
            MaxSizeBytes = 25 * 1024 * 1024,  // 25 MB
        },
    };

    /// <summary>
    /// Return <paramref name="baseName"/> if not taken, else auto-suffix
    /// <c>_2</c>, <c>_3</c>… so adding two Email presets gives <c>email</c>
    /// and <c>email_2</c> instead of failing.
    /// </summary>
    private static string UniqueName(string baseName, IReadOnlyCollection<string> existing)
    {
        var taken = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(baseName)) return baseName;
        for (int i = 2; i < 1000; i++)
        {
            var candidate = $"{baseName}_{i}";
            if (!taken.Contains(candidate)) return candidate;
        }
        // Giving up — a collision storm this bad indicates misuse; fall back to a random suffix.
        return $"{baseName}_{Guid.NewGuid().ToString("n").Substring(0, 6)}";
    }
}
