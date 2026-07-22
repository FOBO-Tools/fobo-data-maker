using Terminal.Gui;

namespace DataMaker.Forms.Renderer.Terminal.Forms;

/// <summary>
/// Fills the gaps in Terminal.Gui v1's default key bindings so selection
/// controls behave consistently: Space AND Enter both confirm, and the arrow
/// keys both move. Out of the box a RadioGroup binds Space (but not Enter) to
/// select and leaves Down-arrow bound to no command at all, while a CheckBox
/// binds Space (but not Enter) to toggle — so Enter falls through to the page's
/// default button (Next/Submit) instead of acting on the field, and Down-arrow
/// does nothing. These extensions make Enter behave like Space and restore
/// Down-arrow.
/// </summary>
internal static class Keys
{
    public static RadioGroup WithStandardKeys(this RadioGroup group)
    {
        group.AddKeyBinding(Key.Enter, Command.Accept);       // Enter selects, like Space
        group.AddKeyBinding(Key.CursorDown, Command.LineDown); // Down moves (TG v1 leaves it unbound)
        return group;
    }

    public static CheckBox WithStandardKeys(this CheckBox box)
    {
        box.AddKeyBinding(Key.Enter, Command.ToggleChecked);  // Enter toggles, like Space
        return box;
    }

    /// <summary>
    /// Give each label a UNIQUE hotkey by inserting an explicit '_' before the
    /// first letter/digit not already claimed in the set. Terminal.Gui otherwise
    /// auto-picks the first uppercase letter, so "Very satisfied" and "Very
    /// dissatisfied" both grab V and collide. Labels that already carry an
    /// explicit '_' marker are respected as-is. Falls back to the plain label
    /// when every character is already taken.
    /// </summary>
    /// <summary>
    /// Hotkey letters owned by the chrome buttons (Close, Next, Back, Submit).
    /// Answer hotkeys must avoid these — otherwise pressing the letter triggers
    /// the button (which grabs the bare letter via cold-key dispatch) instead of
    /// selecting the option. These letters also back the Ctrl+C/N/B shortcuts.
    /// </summary>
    private static readonly char[] ReservedHotkeys = { 'c', 'n', 'b', 's' };

    public static string[] AssignUniqueHotkeys(IReadOnlyList<string> labels)
    {
        var used   = new HashSet<char>(ReservedHotkeys);
        var result = new string[labels.Count];

        // Honour author-supplied '_' markers first so the auto pass avoids them.
        for (var i = 0; i < labels.Count; i++)
        {
            var lbl = labels[i] ?? "";
            var us  = lbl.IndexOf('_');
            if (us >= 0 && us + 1 < lbl.Length)
            {
                used.Add(char.ToLowerInvariant(lbl[us + 1]));
                result[i] = lbl;
            }
        }

        for (var i = 0; i < labels.Count; i++)
        {
            if (result[i] is not null) continue;
            var lbl = labels[i] ?? "";

            var pos = -1;
            for (var j = 0; j < lbl.Length; j++)
            {
                var c = lbl[j];
                if (char.IsLetterOrDigit(c) && used.Add(char.ToLowerInvariant(c))) { pos = j; break; }
            }
            result[i] = pos >= 0 ? lbl.Insert(pos, "_") : lbl;
        }
        return result;
    }
}
