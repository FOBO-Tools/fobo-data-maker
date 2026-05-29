namespace DataMaker.Sdk;

/// <summary>Validate + coerce caller values against a form's field schema.</summary>
public static class Validation
{
    /// <summary>
    /// Returns the coerced values (only known, non-empty, input-kind fields)
    /// and any issues. Unknown keys are rejected unless <paramref name="allowUnknown"/>;
    /// read-only kinds (calc/heading/signature) are rejected; required fields
    /// must be present and non-empty.
    /// </summary>
    public static (Dictionary<string, object?> Values, List<ValidationIssue> Issues) Validate(
        IReadOnlyList<FieldDescriptor> fields,
        IReadOnlyDictionary<string, object?> input,
        bool allowUnknown = false)
    {
        var issues = new List<ValidationIssue>();
        var values = new Dictionary<string, object?>();
        var byKey = fields.ToDictionary(f => f.Key, f => f);

        foreach (var (key, raw) in input)
        {
            if (!byKey.TryGetValue(key, out var field))
            {
                if (!allowUnknown)
                    issues.Add(new ValidationIssue(key, null, "unknown field — not in the form schema"));
                continue;
            }
            if (!FieldKinds.IsInput(field.Kind))
            {
                var nk = FieldKinds.Normalize(field.Kind);
                issues.Add(new ValidationIssue(key, nk, $"field is read-only ({nk}) and cannot be submitted"));
                continue;
            }
            if (IsEmpty(raw)) continue; // handled by the required pass below

            var (value, error) = FieldKinds.Coerce(field.Kind, raw, field);
            if (error is not null)
            {
                issues.Add(new ValidationIssue(key, FieldKinds.Normalize(field.Kind), error));
                continue;
            }
            values[key] = value;
        }

        foreach (var field in fields)
        {
            if (!field.Required) continue;
            if (FieldKinds.NonInput.Contains(FieldKinds.Normalize(field.Kind))) continue;
            if (!values.ContainsKey(field.Key))
                issues.Add(new ValidationIssue(field.Key, FieldKinds.Normalize(field.Kind), "required field is missing"));
        }

        return (values, issues);
    }

    private static bool IsEmpty(object? v) => v switch
    {
        null => true,
        string s => s.Trim().Length == 0,
        System.Collections.ICollection c => c.Count == 0,
        _ => false,
    };
}
