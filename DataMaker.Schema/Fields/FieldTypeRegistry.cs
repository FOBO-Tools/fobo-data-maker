namespace DataMaker.Schema.Fields;

/// <summary>
/// Static registry of known field types. Built-ins are registered in the
/// class initializer so every consumer sees them without explicit setup.
/// Hosts extending the type system (custom field types for third-party
/// editors) should call <see cref="Register"/> once at startup before any
/// <see cref="Form"/> gets loaded — the registry is process-global.
///
/// <para>
/// Process-global state is deliberate: the registry mirrors a closed world
/// of known types across the whole app, the same way a MIME-type table does.
/// Tests that need per-test types can register on setup — but since the
/// built-ins cover the real cases today, most tests never touch the
/// registry.
/// </para>
/// </summary>
public static class FieldTypeRegistry
{
    private static readonly Dictionary<string, FieldTypeDescriptor> _descriptors
        = new(StringComparer.Ordinal);

    static FieldTypeRegistry() => RegisterBuiltIns();

    private static void RegisterBuiltIns()
    {
        // text family — all map to SQLite TEXT, expression parameter string.
        Register(new(FieldTypes.Text,        "Text",            typeof(string),                 "TEXT"));
        Register(new(FieldTypes.LongText,    "Long text",       typeof(string),                 "TEXT"));
        Register(new(FieldTypes.Email,       "Email",           typeof(string),                 "TEXT"));
        Register(new(FieldTypes.Phone,       "Phone",           typeof(string),                 "TEXT"));
        Register(new(FieldTypes.Url,         "URL",             typeof(string),                 "TEXT"));
        Register(new(FieldTypes.Relation,    "Relation",        typeof(string),                 "TEXT"));
        Register(new(FieldTypes.Image,       "Image",           typeof(ImageRef?),              "TEXT"));
        Register(new(FieldTypes.Attachment,  "Attachment",      typeof(AttachmentRef?),         "TEXT"));
        Register(new(FieldTypes.Choice,      "Choice",          typeof(string),                 "TEXT"));
        Register(new(FieldTypes.Date,        "Date",            typeof(string),                 "TEXT"));
        Register(new(FieldTypes.DateTime,    "Date & time",     typeof(string),                 "TEXT"));

        // numeric
        Register(new(FieldTypes.Number,      "Number",          typeof(long?),                  "INTEGER"));
        Register(new(FieldTypes.Decimal,     "Decimal",         typeof(decimal?),               "NUMERIC"));
        Register(new(FieldTypes.Money,       "Money",           typeof(decimal?),               "NUMERIC"));
        Register(new(FieldTypes.Boolean,     "Yes / no",        typeof(bool?),                  "INTEGER"));

        // collections — stored as JSON string arrays in the value column.
        Register(new(FieldTypes.MultiChoice, "Multi-choice",    typeof(IReadOnlyList<string>),  "TEXT"));
        Register(new(FieldTypes.List,        "List",            typeof(IReadOnlyList<string>),  "TEXT"));

        // geo — object type so expressions can use g.Lat / g.Lng.
        Register(new(FieldTypes.Geo,         "Geo point",       typeof(Geo?),                   "TEXT"));
    }

    /// <summary>Register a field type. Replaces any existing registration for the same id.</summary>
    public static void Register(FieldTypeDescriptor descriptor)
    {
        if (descriptor is null) throw new ArgumentNullException(nameof(descriptor));
        if (string.IsNullOrWhiteSpace(descriptor.Id))
            throw new ArgumentException("FieldTypeDescriptor.Id is required.", nameof(descriptor));
        _descriptors[descriptor.Id] = descriptor;
    }

    /// <summary>Look up a descriptor, or null if the id is unknown.</summary>
    public static FieldTypeDescriptor? Get(string id) =>
        _descriptors.TryGetValue(id, out var d) ? d : null;

    /// <summary>Look up a descriptor, throwing if the id is unknown.</summary>
    public static FieldTypeDescriptor Require(string id) =>
        Get(id) ?? throw new InvalidOperationException(
            $"Unknown field type '{id}'. Register it via FieldTypeRegistry.Register before loading forms that use it.");

    /// <summary>True when <paramref name="id"/> has a registered descriptor.</summary>
    public static bool IsRegistered(string id) => _descriptors.ContainsKey(id);

    /// <summary>All currently-registered descriptors. Enumeration order is registration order.</summary>
    public static IEnumerable<FieldTypeDescriptor> All => _descriptors.Values;
}
