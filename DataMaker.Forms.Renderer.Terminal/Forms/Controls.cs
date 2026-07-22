using System;
using NStack;
using Terminal.Gui;

namespace DataMaker.Forms.Renderer.Terminal.Forms;

/// <summary>
/// A RadioGroup whose letter-hotkey only fires while the group has focus.
///
/// <para>Terminal.Gui dispatches a RadioGroup's letter as a global "cold key" to
/// EVERY RadioGroup in the view tree — including the hidden wizard steps. So
/// pressing a letter would select an option in whichever (often hidden) group
/// claimed that letter first, and the question you're actually on wouldn't
/// respond. Scoping to <see cref="View.HasFocus"/> means the key only ever acts
/// on the group you've tabbed to.</para>
/// </summary>
internal sealed class FocusScopedRadioGroup : RadioGroup
{
    public FocusScopedRadioGroup(ustring[] radioLabels) : base(radioLabels) { }

    public override bool ProcessColdKey(KeyEvent kb) => HasFocus && base.ProcessColdKey(kb);
}

/// <summary>
/// A CheckBox you can toggle by its letter while its GROUP has focus.
///
/// <para>Terminal.Gui's CheckBox only toggles on <c>Alt</c>+letter (dead on
/// macOS) and has no bare-letter handler at all — so plain letters never worked.
/// This adds the bare-letter toggle, scoped to the containing view's focus so a
/// hidden wizard step's option (or another question's) can't swallow the key.</para>
/// </summary>
internal sealed class GroupScopedCheckBox : CheckBox
{
    public GroupScopedCheckBox(ustring text, bool isChecked) : base(text, isChecked) { }

    public override bool ProcessColdKey(KeyEvent kb)
    {
        if (!(SuperView?.HasFocus ?? false)) return false;

        var v = kb.KeyValue;
        if (v > 0 && v < 0x10000 && char.IsLetterOrDigit((char)v)
            && char.ToUpperInvariant((char)v) == HotChar())
        {
            // TG's Checked setter does NOT raise Toggled (only ToggleChecked()
            // does) — so flipping it alone changes the glyph but never tells the
            // binding, and the pick is silently dropped on submit. Flip + raise.
            var prev = Checked;
            Checked = !prev;
            OnToggled(prev);
            return true;
        }
        return false;
    }

    /// <summary>This box's hotkey character (the one after '_', else the first letter/digit), upper-cased.</summary>
    private char HotChar()
    {
        var t  = Text?.ToString() ?? "";
        var us = t.IndexOf('_');
        if (us >= 0 && us + 1 < t.Length) return char.ToUpperInvariant(t[us + 1]);
        foreach (var c in t)
            if (char.IsLetterOrDigit(c)) return char.ToUpperInvariant(c);
        return '\0';
    }
}
