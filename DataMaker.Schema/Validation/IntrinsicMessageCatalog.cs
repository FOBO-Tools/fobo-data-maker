using DataMaker.Schema.Fields;

namespace DataMaker.Schema.Validation;

/// <summary>
/// One customizable error-message slot exposed by a field. The designer's
/// "Error messages" dialog renders one row per slot returned by
/// <see cref="IntrinsicMessageCatalog.For"/>; <see cref="Default"/> shows as
/// the placeholder, the user's entry overrides it.
/// </summary>
public sealed record MessageSlot(string Id, string Label, string Default);

/// <summary>
/// Lists every customizable error-message slot a field exposes — the flat
/// <see cref="FieldDefinition.Required"/> bool plus every intrinsic derived
/// from kind + options. Source of truth for <c>ValidationMessagesDialog</c>
/// and the WordPress per-form override admin.
///
/// <para>
/// Authored <see cref="ValidationRule"/>s carry their own <c>Message</c> field
/// and are not listed here — they appear in the rules dialog.
/// </para>
/// </summary>
public static class IntrinsicMessageCatalog
{
    public static IReadOnlyList<MessageSlot> For(FieldDefinition field)
    {
        var slots = new List<MessageSlot>(6);

        if (field.Required)
            slots.Add(new MessageSlot("required", "Required", IntrinsicMessages.Required));

        // TextOptions intrinsics (text-shaped kinds). Each option present
        // contributes one slot; numeric headers carry the parameter so the
        // designer reads "Minimum length (5)" not just "Minimum length".
        if (field.Text is { } t)
        {
            if (t.MinLength is int minLen && minLen > 0)
                slots.Add(new MessageSlot("text.minLength",
                    $"Minimum length ({minLen})",
                    string.Format(IntrinsicMessages.TextMinLength, minLen, minLen == 1 ? "" : "s")));

            if (t.MaxLength is int maxLen && maxLen > 0)
                slots.Add(new MessageSlot("text.maxLength",
                    $"Maximum length ({maxLen})",
                    string.Format(IntrinsicMessages.TextMaxLength, maxLen, maxLen == 1 ? "" : "s")));

            if (!string.IsNullOrWhiteSpace(t.Pattern))
                slots.Add(new MessageSlot("text.pattern", "Pattern match", IntrinsicMessages.TextPattern));
        }

        // Kind-specific intrinsics.
        switch (field.Kind)
        {
            case FieldTypes.Email:
                slots.Add(new MessageSlot("email", "Email format", IntrinsicMessages.Email));
                break;
            case FieldTypes.Url:
                slots.Add(new MessageSlot("url", "URL format", IntrinsicMessages.Url));
                break;
            case FieldTypes.Phone:
                slots.Add(new MessageSlot("phone", "Phone format", IntrinsicMessages.Phone));
                break;
            case FieldTypes.Number:
                slots.Add(new MessageSlot("number", "Whole number", IntrinsicMessages.Number));
                break;
            case FieldTypes.Decimal:
                slots.Add(new MessageSlot("decimal", "Decimal number", IntrinsicMessages.DecimalNumber));
                break;
            case FieldTypes.Money:
                slots.Add(new MessageSlot("money", "Monetary amount", IntrinsicMessages.Money));
                break;
            case FieldTypes.Date:
                slots.Add(new MessageSlot("date", "Date", IntrinsicMessages.Date));
                break;
            case FieldTypes.DateTime:
                slots.Add(new MessageSlot("datetime", "Date-time", IntrinsicMessages.DateTime));
                break;
            case FieldTypes.Boolean:
                slots.Add(new MessageSlot("boolean", "Boolean", IntrinsicMessages.Boolean));
                break;
            case FieldTypes.Choice:
                if (field.Choice is { Choices.Count: > 0, AllowCustom: false })
                    slots.Add(new MessageSlot("choice", "Allowed choice", IntrinsicMessages.Choice));
                break;
            case FieldTypes.MultiChoice:
                if (field.Choice is { Choices.Count: > 0, AllowCustom: false })
                    slots.Add(new MessageSlot("multichoice", "Allowed choices", IntrinsicMessages.MultiChoice));
                break;
            case FieldTypes.Geo:
                slots.Add(new MessageSlot("geo.lat", "Latitude range", IntrinsicMessages.GeoLat));
                slots.Add(new MessageSlot("geo.lng", "Longitude range", IntrinsicMessages.GeoLng));
                slots.Add(new MessageSlot("geo",     "Geo point",       IntrinsicMessages.Geo));
                break;
            case FieldTypes.Image:
            case FieldTypes.Attachment:
                slots.Add(new MessageSlot("attachment.ext", "File extension", IntrinsicMessages.AttachmentExt));
                break;
        }

        return slots;
    }
}

/// <summary>
/// Engine-default English messages. Single source so the designer's
/// PlaceholderText, the C# runtime's fallback, and the JS renderer's fallback
/// agree. Renderers may translate via a per-field override (<see cref="FieldDefinition.Messages"/>)
/// or per-site (WordPress per-form overrides).
/// </summary>
public static class IntrinsicMessages
{
    public const string Required          = "Required";
    public const string TextMinLength     = "Must be at least {0} character{1}.";
    public const string TextMaxLength     = "Must be at most {0} character{1}.";
    public const string TextPattern       = "Value does not match the required pattern.";
    public const string Email             = "Not a valid email address.";
    public const string Url               = "Not a valid URL.";
    public const string Phone             = "Not a valid phone number.";
    public const string Number            = "Not a whole number.";
    public const string DecimalNumber     = "Not a valid decimal number.";
    public const string Money             = "Not a valid monetary amount.";
    public const string Date              = "Not a valid date.";
    public const string DateTime          = "Not a valid date-time.";
    public const string Boolean           = "Not a boolean.";
    public const string Choice            = "Value is not in the allowed list.";
    public const string MultiChoice       = "Some items are not in the allowed list.";
    public const string GeoLat            = "Latitude must be between -90 and 90.";
    public const string GeoLng            = "Longitude must be between -180 and 180.";
    public const string Geo               = "Not a valid geo point.";
    public const string AttachmentExt     = "File extension not allowed.";
}

/// <summary>
/// Form-level customizable messages. Mirrors <see cref="IntrinsicMessages"/>
/// but for slots that live on <see cref="DataMaker.Schema.Forms.Form.Messages"/>
/// instead of per-field. Kept here so the single English-defaults source is
/// re-discovered by every consumer (designer placeholder, renderer fallback,
/// WP override admin).
/// </summary>
public static class FormMessages
{
    public const string ValidationBanner = "Please fix the highlighted fields before submitting.";
}

/// <summary>Form-level message slots, in display order. Used by the form-properties UI + WP admin to render one input per slot.</summary>
public static class FormMessageCatalog
{
    public static IReadOnlyList<MessageSlot> All { get; } = new[]
    {
        new MessageSlot("validationBanner", "Validation banner", FormMessages.ValidationBanner),
    };
}
