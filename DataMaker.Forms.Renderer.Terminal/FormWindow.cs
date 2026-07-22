using DataMaker.Expressions.Engine;
using DataMaker.Forms.Signing;
using DataMaker.Forms.Renderer.Terminal.Forms;
using FOBO.Auth;
using Terminal.Gui;

namespace DataMaker.Forms.Renderer.Terminal;

/// <summary>
/// Top-level window for a verified form. Hosts the rendered form in a
/// scrollable view with a fixed button bar at the bottom. Submit runs
/// validation via <see cref="FormRuntime"/>, then sealed-box-encrypts the
/// payload and POSTs it to the sync API.
///
/// <para>
/// <b>Resize strategy.</b> Terminal.Gui v1's per-event repaint on resize
/// leaves ghost pixels outside the new window frame and races the event
/// cascade. Rather than fighting that, this window debounces resize events
/// and — once the user stops dragging — tears the whole view subtree down
/// and rebuilds it fresh. <see cref="FormState"/> persists across the
/// rebuild, so typed values survive; scroll + focus position reset to the
/// top, which is an accepted trade-off.
/// </para>
/// </summary>
internal sealed class FormWindow : Window
{
    private readonly VerifiedForm _verified;
    private readonly string _submitEndpoint;
    private readonly ColorScheme? _templateScheme;
    private readonly TokenSet? _bearer;
    private readonly FormState _state;
    private readonly HttpClient _http = new();

    // Recreated on every rebuild.
    private FormRenderer? _renderer;
    private FormRuntime?  _runtime;
    private Button?       _submit;
    private Button?       _cancel;
    private Label?        _status;
    private ScrollView?   _scroll;
    private View?         _contentInset;
    private int           _consumedRows;

    // Re-entrancy guard for the submit round-trip: set when a submit is
    // dispatched, cleared when the result comes back. Inline ButtonColumn
    // forms leave _submit null (the page-level button is suppressed), so
    // toggling the button's Enabled state alone can't block a double-submit
    // from a repeated inline click or Ctrl+S — this flag does.
    private bool          _submitting;

    // Wizard (multi-step) state — used only when the form has >1 step.
    // Every step is rendered into its own host (all register their fields with
    // the runtime); the window shows one at a time, driven by Back / Next.
    private readonly List<View>     _stepHosts  = new();
    private readonly List<int>      _stepRows   = new();
    private readonly List<string[]> _stepFields = new();
    private readonly List<Label> _stepPips = new();
    private View?   _stepBar;
    private Button? _back;
    private Button? _next;
    private int     _activeStep;
    private int     _padTop;
    private int     _padBottom;
    private bool    _showStepBar;
    private bool    _stepBarBottom;
    private ColorScheme? _pipTodo;
    private ColorScheme? _pipCurrent;
    private ColorScheme? _pipDone;

    private bool Wizard => _verified.Form.Steps.Count > 1;

    private object? _resizeDebounce;
    private bool _isResizing;
    private int  _lastPolledCols;
    private int  _lastPolledRows;
    private long _lastIndicatorPaintTicks;

    public FormWindow(
        VerifiedForm verified,
        string submitEndpoint,
        ColorScheme? templateScheme = null,
        TokenSet? bearer = null)
    {
        _verified       = verified;
        _submitEndpoint = submitEndpoint;
        _templateScheme = templateScheme;
        _bearer         = bearer;
        _state          = new FormState();

        Title = $" {_verified.Form.Name} ";
        if (templateScheme is not null) ColorScheme = templateScheme;

        // Anchor to full terminal so the Window frame tracks resizes.
        X      = 0;
        Y      = 0;
        Width  = Dim.Fill();
        Height = Dim.Fill();

        BuildContent();

        // Resize strategy:
        //   1. Poll driver dimensions every 100ms. Terminal.Gui's Resized
        //      event doesn't fire during a drag when the mouse is outside
        //      the terminal (MainLoop stays asleep), so we can't react to
        //      it in time. A recurring timer wakes the loop periodically
        //      regardless of input; if dimensions changed, we flip into
        //      resizing mode immediately.
        //   2. Application.Resized is still a trigger — redundant belt +
        //      braces so we catch a resize even if the poll hasn't ticked.
        //   3. DEBOUNCED: 120ms after the last detected resize, tear down
        //      and rebuild the full view subtree against the new dimensions.
        Application.Resized += _ => TriggerResize();

        _lastPolledCols = Application.Driver?.Cols ?? 0;
        _lastPolledRows = Application.Driver?.Rows ?? 0;
        Application.MainLoop.AddTimeout(TimeSpan.FromMilliseconds(100), _ =>
        {
            var drv = Application.Driver;
            if (drv is not null && (drv.Cols != _lastPolledCols || drv.Rows != _lastPolledRows))
            {
                _lastPolledCols = drv.Cols;
                _lastPolledRows = drv.Rows;
                TriggerResize();
            }
            return true; // keep polling for the lifetime of the window
        });
    }

    // ── Build / rebuild ────────────────────────────────────────────────

    private void BuildContent()
    {
        // Wizard view-lists are rebuilt from scratch each (re)build — clear any
        // stale views the previous build left behind.
        _stepHosts.Clear();
        _stepRows.Clear();
        _stepFields.Clear();
        _stepPips.Clear();

        // Detect inline ButtonColumn(Action=Submit) anywhere in the form.
        // When present the page-level Submit becomes redundant — author
        // wired their own submit affordance — so we hide it (+ the
        // separator line above it) and reclaim those rows for the form.
        var hasInlineSubmit = FormHasInlineSubmit(_verified.Form);

        // No "_" hotkeys: those fire on Alt+letter, and macOS has no usable Alt
        // (Option types special chars), so the highlighted letter was a shortcut
        // that did nothing. Buttons are reached by Tab + Enter/Space, Esc closes.
        _cancel = new Button("Close") { X = 0, Y = Pos.AnchorEnd(1) };

        const int SubmitAnchor = 12;
        const int BackAnchor   = SubmitAnchor + 11; // Back sits left of Next/Submit
        if (!hasInlineSubmit)
        {
            _submit = new Button("Submit")
            {
                X         = Pos.AnchorEnd(SubmitAnchor),
                Y         = Pos.AnchorEnd(1),
                IsDefault = !Wizard, // wizard hands Default to Next until the last step
            };
        }

        if (Wizard)
        {
            // Next occupies the same slot as Submit (only one is visible at a
            // time); Back sits to its left and is disabled on the first step.
            _next = new Button("Next ›")
            {
                X         = Pos.AnchorEnd(SubmitAnchor),
                Y         = Pos.AnchorEnd(1),
                IsDefault = true,
            };
            _back = new Button("‹ Back")
            {
                X = Pos.AnchorEnd(BackAnchor),
                Y = Pos.AnchorEnd(1),
            };
        }

        var rightReserve = Wizard ? BackAnchor + 2 : (hasInlineSubmit ? 0 : SubmitAnchor + 2);
        _status = new Label(KeyHint)
        {
            X     = Pos.Right(_cancel) + 2,
            Y     = Pos.AnchorEnd(1),
            Width = rightReserve > 0 ? Dim.Fill(rightReserve) : Dim.Fill(),
        };

        // Horizontal separator above the button bar — mirrors the Window's
        // own bottom border so the button bar feels bracketed on both sides
        // by a line. AutoSize=false so the 200-char rule clips at the
        // viewport instead of blowing out the layout.
        var separator = new Label(new string('─', 200))
        {
            X        = 0,
            Y        = Pos.AnchorEnd(2),
            Width    = Dim.Fill(),
            Height   = 1,
            AutoSize = false,
        };

        // Form.Style.Padding applies to the PAPER (form-content surface)
        // not the CHROME (window/scrollbar). ScrollView stays full-width
        // so the scrollbar sits at the window edge; the padding becomes
        // an inner-host offset that wraps the rendered form views.
        // CSS px → terminal cells via ÷12 (rows, matches SpacerColumn)
        // and ÷6 (cols, monospace cells are roughly half-width).
        var padTop    = PadToRows(_verified.Form.Style?.PaddingTop    ?? _verified.Form.Style?.Padding);
        var padBottom = PadToRows(_verified.Form.Style?.PaddingBottom ?? _verified.Form.Style?.Padding);
        var padLeft   = PadToCols(_verified.Form.Style?.PaddingLeft   ?? _verified.Form.Style?.Padding);
        var padRight  = PadToCols(_verified.Form.Style?.PaddingRight  ?? _verified.Form.Style?.Padding);

        // Cols available on the terminal — used to seed ContentSize and
        // _contentInset.Width before SyncContentSize runs on first layout.
        var initialCols = Math.Max(40, (Application.Driver?.Cols ?? 80) - 6);

        // Apply the form-resolved ColorScheme directly to the scroll so
        // the paper fills the viewport edge-to-edge (and below the
        // rendered content as the user scrolls). _contentInset sits
        // inside w/ padding offsets so labels/fields are indented from
        // the paper edge. Height stays explicit (Dim.Sized) instead of
        // Dim.Fill() — Fill collapses to viewport size and clips
        // scrolling. SyncContentSize keeps both in step on resize.
        var paperScheme = _templateScheme is not null
            ? _templateScheme
            : (_verified.Form.Style is { } fs
                ? Forms.StyleToColorScheme.Build(Colors.Dialog, fs)
                : Colors.Dialog);

        _scroll = new ScrollView
        {
            X             = 0,
            Y             = 0,
            Width         = Dim.Fill(),
            // Fill(2) leaves exactly 2 rows at the bottom: 1 for the
            // separator + 1 for the button bar. The wizard step bar lives
            // inside the scrolled content (top of the form), not as chrome.
            Height        = Dim.Fill(2),
            ShowVerticalScrollIndicator   = true,
            ShowHorizontalScrollIndicator = false,
            ColorScheme   = paperScheme,
        };

        _contentInset = new View
        {
            X           = padLeft,
            Y           = padTop,
            // Explicit Sized — Dim.Fill() against a ScrollView resolves
            // to viewport size, not ContentSize, so the inset would clip
            // any rendered child whose Y > viewport.Height and scrolling
            // would do nothing. Real values get fixed up post-render +
            // on every SyncContentSize.
            // Width = viewport - padLeft - padRight. Content starts at
            // X=padLeft so we shave padLeft from the available width
            // too — otherwise right margin collapses and looks asymmetric.
            // (No extra -1 for scrollbar: Terminal.Gui paints the bar
            // in an overlay lane outside the content viewport.)
            Width       = Dim.Sized(Math.Max(1, initialCols - padLeft - padRight)),
            Height      = Dim.Sized(1000),
            // No explicit ColorScheme — the scroll's paperScheme propagates
            // via inheritance. Setting one here causes the inset to repaint
            // its bg on every child PropertyChanged tick (Terminal.Gui v1
            // wakes on TextField input), wiping out the TextField caret.
        };
        _scroll.Add(_contentInset);

        // Mouse-wheel → scroll, WITHOUT grabbing the mouse. The previous code
        // grabbed the ScrollView on every mouse event so clicks were routed to
        // the scroll instead of the field under the cursor — which is why a
        // radio / checkbox / button couldn't be clicked most of the time. Here
        // we react only to wheel events and scroll manually; every click then
        // reaches its own control.
        Action<MouseEvent> wheel = e =>
        {
            if (_scroll is null) return;
            var up   = e.Flags.HasFlag(MouseFlags.WheeledUp);
            var down = e.Flags.HasFlag(MouseFlags.WheeledDown);
            if (!up && !down) return;

            var p = _scroll.ScreenToView(e.X, e.Y);
            var inside = p.X >= 0 && p.Y >= 0 && p.X < _scroll.Bounds.Width && p.Y < _scroll.Bounds.Height;
            if (!inside) return;

            if (up) _scroll.ScrollUp(2); else _scroll.ScrollDown(2);
            _scroll.SetNeedsDisplay();
        };
        Application.RootMouseEvent += wheel;
        this.Closing += _ => Application.RootMouseEvent -= wheel;

        // Pre-size ContentSize BEFORE rendering children so column percents
        // resolve against a real width on first layout.
        _scroll.ContentSize = new Size(initialCols, 1000);

        _runtime  = new FormRuntime(new ExpressionEngine(), _verified.Form, _state);

        // Wire ambient upload context so image / attachment bindings can
        // hit POST /submissions/upload-slot on the sync Lambda. Share-only
        // .dmf bundles (no recipient) leave Current null; FileBinding
        // then renders the picker disabled with a clear notice.
        if (_verified.AcceptsSubmissions)
        {
            var baseUri = _submitEndpoint.EndsWith('/') ? _submitEndpoint : _submitEndpoint + "/";
            DataMaker.Forms.Renderer.Terminal.Forms.Bindings.UploadContext.Current =
                new DataMaker.Forms.Renderer.Terminal.Forms.Bindings.UploadContext
                {
                    RecipientUserId    = _verified.RecipientUserId!,
                    SubmitEndpointBase = baseUri,
                    Http               = _http,
                    RecipientPublicKey = _verified.RecipientPublicKey!,
                };
        }
        else
        {
            DataMaker.Forms.Renderer.Terminal.Forms.Bindings.UploadContext.Current = null;
        }

        _renderer = new FormRenderer(
            _verified.Form,
            _state,
            fallback:      _templateScheme ?? Colors.Dialog,
            runtime:       _runtime,
            ignoreStyles:  _templateScheme is not null);
        // Wire inline ButtonColumns (Submit / Save / Reset / None) into
        // the same flow the page-level Submit button uses. None is a
        // no-op — author-supplied custom action with no built-in hook
        // (yet). Save mirrors Submit for now (terminal has no draft
        // store, so persist = submit); Reset clears the FormState back
        // to field defaults.
        _renderer.OnButtonAction = column =>
        {
            switch (column.Action)
            {
                case DataMaker.Schema.Layout.ButtonAction.Submit:
                case DataMaker.Schema.Layout.ButtonAction.Save:
                    OnSubmit();
                    break;
                case DataMaker.Schema.Layout.ButtonAction.Reset:
                    foreach (var f in _verified.Form.Fields)
                    {
                        // Coerce per kind — JSON defaults arrive as
                        // JsonElement (or double) which trips the
                        // Number validator. Same path as FormRenderer's
                        // initial seed; unwrap JsonElement first.
                        var v = f.DefaultValue;
                        if (v is System.Text.Json.JsonElement je)
                        {
                            v = je.ValueKind switch
                            {
                                System.Text.Json.JsonValueKind.String => je.GetString(),
                                System.Text.Json.JsonValueKind.Number => je.TryGetInt64(out var l) ? l : je.GetDouble(),
                                System.Text.Json.JsonValueKind.True   => true,
                                System.Text.Json.JsonValueKind.False  => false,
                                _                                     => je.GetRawText(),
                            };
                        }
                        if (v is not null)
                        {
                            try
                            {
                                v = f.Kind switch
                                {
                                    DataMaker.Schema.Fields.FieldTypes.Number  => v is long l ? l : Convert.ToInt64(v, System.Globalization.CultureInfo.InvariantCulture),
                                    DataMaker.Schema.Fields.FieldTypes.Decimal or DataMaker.Schema.Fields.FieldTypes.Money
                                                                               => v is decimal d ? d : Convert.ToDecimal(v, System.Globalization.CultureInfo.InvariantCulture),
                                    DataMaker.Schema.Fields.FieldTypes.Boolean => v is bool b ? b : Convert.ToBoolean(v, System.Globalization.CultureInfo.InvariantCulture),
                                    _                                          => v,
                                };
                            }
                            catch { }
                        }
                        _state.Set(f.Name, v);
                    }
                    _runtime?.InitialEvaluate();
                    _scroll?.SetNeedsDisplay();
                    break;
                case DataMaker.Schema.Layout.ButtonAction.None:
                default:
                    // No-op — author wired no built-in action.
                    break;
            }
        };
        _padTop    = padTop;
        _padBottom = padBottom;

        if (Wizard)
        {
            // Step bar position + visibility come from the form style, same as
            // the PDF: Top sits above the step content, Bottom renders directly
            // under the last item, ShowBar=false hides it (Back/Next still work).
            var stepBarStyle = _verified.Form.Style?.StepBar;
            _showStepBar   = stepBarStyle?.ShowBar ?? true;
            _stepBarBottom = stepBarStyle?.Position == DataMaker.Schema.Styling.StepBarPosition.Bottom;

            // The bar lives inside the scrolled content (on the paper), so it
            // flows with the form and picks up the form/template colours.
            if (_showStepBar)
            {
                _stepBar = BuildStepBar(paperScheme);
                _contentInset.Add(_stepBar);
            }

            // Render every step into its own host. Top bar pushes the content
            // down a couple of rows; bottom bar leaves the content at the top.
            // All hosts register their fields with the runtime (so cross-step
            // calc / visibility keep working); the window reveals one at a time.
            var hostTop  = StepBarTopRows();
            var idToName = _verified.Form.Fields.ToDictionary(f => f.Id, f => f.Name, StringComparer.Ordinal);
            foreach (var step in _verified.Form.Steps)
            {
                var stepHost = new View
                {
                    X = 0, Y = hostTop,
                    Width   = Dim.Fill(),
                    Height  = Dim.Sized(1),
                    Visible = false,
                };
                _contentInset.Add(stepHost);
                var rows = _renderer.RenderStepHost(stepHost, step);
                stepHost.Height = Dim.Sized(Math.Max(1, rows));
                _stepHosts.Add(stepHost);
                _stepRows.Add(rows);
                _stepFields.Add(CollectStepFields(step, idToName));
            }
            _activeStep   = Math.Clamp(_activeStep, 0, _stepHosts.Count - 1);
            _consumedRows = ContentRowsFor(_activeStep) + padTop + padBottom;
        }
        else
        {
            _consumedRows = _renderer.Render(_contentInset) + padTop + padBottom;
        }
        // Snap _contentInset to the rendered content size so the
        // ScrollView's ContentSize-based scrolling actually moves
        // children. Width follows the scroll viewport minus padding.
        _contentInset.Height = Dim.Sized(Math.Max(1, _consumedRows - padTop - padBottom));
        _runtime.InitialEvaluate();

        // Show the current field's error (if any) in the status bar when
        // the user focuses it — detailed message the field-level !-indicator
        // can't convey on its own.
        _runtime.FieldFocused += (name, error) =>
        {
            if (_status is not null) _status.Text = error ?? KeyHint;
            // Keep the focused field in view as the user tabs through — the
            // ScrollView doesn't follow focus on its own, so a Tab onto an
            // off-screen field would otherwise leave it hidden below the fold.
            if (_runtime?.EditorFor(name) is { } editor) ScrollIntoView(editor);
        };

        _scroll.ContentSize = new Size(initialCols, Math.Max(1, _consumedRows));
        _scroll.LayoutComplete += _ => SyncContentSize();
        // Kick layout once now so the scrollbar shows + responds on the
        // very first paint instead of waiting for the user to tab into
        // an off-screen field. Otherwise SyncContentSize doesn't fire
        // until the first LayoutComplete the framework drives.
        _scroll.LayoutSubviews();
        SyncContentSize();

        if (_submit is not null) _submit.Clicked += OnSubmit;
        _cancel.Clicked += () => Application.RequestStop();

        Add(_scroll, separator, _cancel, _status);
        if (_submit is not null) Add(_submit);

        if (Wizard)
        {
            // Step bar was already added into the scrolled content above; here
            // we only wire the chrome nav buttons + reveal the active step.
            if (_back is not null) { _back.Clicked += GoBack; Add(_back); }
            if (_next is not null) { _next.Clicked += GoNext; Add(_next); }
            ShowStep(_activeStep);
        }
    }

    /// <summary>Rows the in-content step bar occupies: the pip row + one blank separator.</summary>
    private const int StepBarRows = 2;

    /// <summary>Rows reserved above the step content for a top-positioned bar (0 when hidden or bottom).</summary>
    private int StepBarTopRows() => _showStepBar && !_stepBarBottom ? StepBarRows : 0;

    /// <summary>Rows reserved below the step content for a bottom-positioned bar (0 when hidden or top).</summary>
    private int StepBarBottomRows() => _showStepBar && _stepBarBottom ? StepBarRows : 0;

    /// <summary>Total content rows for a step: top-bar reserve + the step's own rows + bottom-bar reserve.</summary>
    private int ContentRowsFor(int stepIndex) => StepBarTopRows() + _stepRows[stepIndex] + StepBarBottomRows();

    // ── Keyboard shortcuts (macOS-friendly: no Alt/⌘) ──────────────────
    //
    // A terminal app can't see ⌘ (the terminal keeps it) and macOS has no Alt,
    // so button hotkeys are useless here. These work everywhere: Esc closes,
    // Ctrl+N / Ctrl+B step the wizard. The focused field gets first crack at the
    // key (so a TextView's own Ctrl+N/Esc editing still works); only keys it
    // doesn't consume fall through to these shortcuts.
    // Wizard nav goes in ProcessHotKey because Terminal.Gui dispatches it BEFORE
    // focus traversal + the focused field — otherwise Ctrl+B was swallowed as a
    // focus-move and just looped through the buttons.
    public override bool ProcessHotKey(KeyEvent kb)
    {
        // Ctrl+C closes (matches the highlighted C on the Close button).
        if (IsCtrl(kb, 'c')) { Application.RequestStop(); return true; }
        // Ctrl+S submits when submitting is valid right now: a non-wizard form,
        // or the last wizard step. Works whether the submit affordance is the
        // page button OR an inline ButtonColumn (where _submit is null).
        // (If your terminal has flow-control on, Ctrl+S may be swallowed as XOFF
        // — `stty -ixon`, or Tab to the button + Enter.)
        if (IsCtrl(kb, 's') && CanSubmitNow()) { OnSubmit(); return true; }
        if (Wizard)
        {
            if (IsCtrl(kb, 'n') && _next is { Visible: true, Enabled: true }) { GoNext(); return true; }
            if (IsCtrl(kb, 'b') && _back is { Enabled: true })               { GoBack(); return true; }
        }
        return base.ProcessHotKey(kb);
    }

    // Esc stays in ProcessKey (after the focused view) so an open ComboBox can
    // use Esc to close its dropdown before Esc closes the whole form.
    public override bool ProcessKey(KeyEvent kb)
    {
        if (base.ProcessKey(kb)) return true;
        if (kb.Key == Key.Esc)
        {
            Application.RequestStop();
            return true;
        }
        return false;
    }

    /// <summary>Submitting is valid now: any non-wizard form, or the last step of a wizard.</summary>
    private bool CanSubmitNow() => !Wizard || _activeStep == _stepHosts.Count - 1;

    /// <summary>True if <paramref name="kb"/> is Ctrl+<paramref name="letter"/>, however the driver encodes it (control code, or letter + CtrlMask/modifier).</summary>
    private static bool IsCtrl(KeyEvent kb, char letter)
    {
        var lower = char.ToLowerInvariant(letter);
        if (kb.Key == (Key)(lower - 'a' + 1)) return true;                       // raw control code (Ctrl+N = 14)
        if (kb.IsCtrl && char.ToLowerInvariant((char)(kb.KeyValue & 0xFF)) == lower) return true;
        if (kb.Key == ((Key)char.ToUpperInvariant(letter) | Key.CtrlMask)) return true;
        return false;
    }

    /// <summary>One-line key hint shown in the status bar when there's no field error to display.</summary>
    private string KeyHint => Wizard
        ? "Tab move · Space/Enter select · Ctrl+B back · Ctrl+N next · Ctrl+S submit · Ctrl+C/Esc close"
        : "Tab move · Space/Enter select · Ctrl+S submit · Ctrl+C/Esc close";

    // ── Wizard (multi-step) navigation ─────────────────────────────────

    /// <summary>Collect the field names a step owns (recursing into groups) so the Next gate can validate just that step.</summary>
    private static string[] CollectStepFields(
        DataMaker.Schema.Forms.FormStep step,
        Dictionary<string, string> idToName)
    {
        var names = new List<string>();
        foreach (var section in step.Sections)
            foreach (var row in section.Rows)
                foreach (var col in row.Columns)
                    Collect(col, names, idToName);
        return names.ToArray();

        static void Collect(DataMaker.Schema.Layout.Column col, List<string> acc, Dictionary<string, string> idToName)
        {
            switch (col)
            {
                case DataMaker.Schema.Layout.FieldColumn fc when idToName.TryGetValue(fc.FieldId, out var nm):
                    acc.Add(nm);
                    break;
                case DataMaker.Schema.Layout.GroupColumn gc:
                    foreach (var r in gc.Rows)
                        foreach (var c in r.Columns)
                            Collect(c, acc, idToName);
                    break;
            }
        }
    }

    private void GoBack()
    {
        if (_activeStep > 0) ShowStep(_activeStep - 1);
    }

    private void GoNext()
    {
        if (_runtime is null) return;

        // Validate-and-block: don't advance while the current step has errors.
        var errs = _runtime.ValidateStep(_stepFields[_activeStep]);
        if (errs.Count > 0)
        {
            if (_status is not null)
                _status.Text = $"Fix {errs.Count} field{(errs.Count == 1 ? "" : "s")} on this step to continue.";
            _scroll?.SetNeedsDisplay();
            return;
        }
        ShowStep(_activeStep + 1);
    }

    /// <summary>Reveal step <paramref name="index"/>: toggle host visibility, resize the scroll content, and flip the Back / Next / Submit buttons + step bar.</summary>
    private void ShowStep(int index)
    {
        if (!Wizard || _stepHosts.Count == 0) return;

        _activeStep = Math.Clamp(index, 0, _stepHosts.Count - 1);
        for (var k = 0; k < _stepHosts.Count; k++)
            _stepHosts[k].Visible = k == _activeStep;

        var content = ContentRowsFor(_activeStep);
        _consumedRows = content + _padTop + _padBottom;
        if (_contentInset is not null)
            _contentInset.Height = Dim.Sized(Math.Max(1, content));
        // A bottom bar sits directly under the active step's last item (a single
        // blank line of breathing room); a top bar stays at the content top.
        if (_stepBar is not null)
            _stepBar.Y = _stepBarBottom ? _stepRows[_activeStep] + 1 : 0;
        if (_scroll is not null)
            _scroll.ContentOffset = new Point(0, 0); // each step starts at the top
        SyncContentSize();

        var last = _activeStep == _stepHosts.Count - 1;
        if (_back is not null) _back.Enabled = _activeStep > 0;
        if (_next is not null) { _next.Visible = !last; _next.Enabled = !last; }
        if (_submit is not null)
        {
            _submit.Visible   = last;
            _submit.Enabled   = last;
            _submit.IsDefault = last;
        }
        if (_status is not null) _status.Text = KeyHint;

        UpdateStepBar();
        _stepHosts[_activeStep].FocusFirst();
        _scroll?.SetNeedsDisplay();
        SetNeedsDisplay();
    }

    /// <summary>Build the "1 ─ 2 ─ 3" progress strip. Completed steps get a filled background, the current step an accent fill + brackets.</summary>
    private View BuildStepBar(ColorScheme paper)
    {
        var nFg = paper.Normal.Foreground;
        var nBg = paper.Normal.Background;

        ColorScheme Solid(Color fg, Color bg)
        {
            var a = Application.Driver.MakeAttribute(fg, bg);
            return new ColorScheme { Normal = a, Focus = a, HotNormal = a, HotFocus = a, Disabled = a };
        }

        // Colours come straight from the active form/template scheme so the bar
        // honours the green / dark / light templates. The current step fills
        // with the theme's foreground; completed steps fill with a dimmer shade
        // of it (still clearly a filled background, just one notch back).
        _pipTodo    = paper;                       // not yet reached — plain
        _pipDone    = Solid(nBg, DimColor(nFg));   // completed — dim themed fill
        _pipCurrent = Solid(nBg, nFg);             // current   — bright themed fill

        var bar = new View { X = 0, Y = 0, Width = Dim.Fill(), Height = 1, ColorScheme = paper };
        var x   = 0;
        for (var i = 0; i < _verified.Form.Steps.Count; i++)
        {
            if (i > 0)
            {
                bar.Add(new Label(" ─ ") { X = x, Y = 0, Width = 3, Height = 1, AutoSize = false, ColorScheme = paper });
                x += 3;
            }
            var text = $" {i + 1} ";
            var pip  = new Label(text) { X = x, Y = 0, Width = text.Length, Height = 1, AutoSize = false };
            bar.Add(pip);
            _stepPips.Add(pip);
            x += text.Length;
        }
        return bar;
    }

    /// <summary>One notch dimmer version of a theme colour, for completed step pips. Unknown colours pass through unchanged.</summary>
    private static Color DimColor(Color c) => c switch
    {
        Color.White         => Color.Gray,
        Color.BrightGreen   => Color.Green,
        Color.BrightYellow  => Color.Brown,
        Color.BrightCyan    => Color.Cyan,
        Color.BrightBlue    => Color.Blue,
        Color.BrightRed     => Color.Red,
        Color.BrightMagenta => Color.Magenta,
        Color.Black         => Color.DarkGray,
        _                   => c,
    };

    /// <summary>Recolour the step-bar pips to match the active step.</summary>
    private void UpdateStepBar()
    {
        for (var i = 0; i < _stepPips.Count; i++)
        {
            var n = i + 1;
            if (i < _activeStep)      { _stepPips[i].Text = $" {n} "; _stepPips[i].ColorScheme = _pipDone; }
            else if (i == _activeStep) { _stepPips[i].Text = $"[{n}]"; _stepPips[i].ColorScheme = _pipCurrent; }
            else                       { _stepPips[i].Text = $" {n} "; _stepPips[i].ColorScheme = _pipTodo; }
            _stepPips[i].SetNeedsDisplay();
        }
        _stepBar?.SetNeedsDisplay();
    }

    /// <summary>
    /// True if any column in the form has an explicit
    /// <see cref="DataMaker.Schema.Layout.ButtonAction.Submit"/> action.
    /// Used by <see cref="BuildContent"/> to suppress the redundant
    /// page-level Submit button.
    /// </summary>
    private static bool FormHasInlineSubmit(DataMaker.Schema.Forms.Form form)
    {
        foreach (var step in form.Steps)
            foreach (var section in step.Sections)
                foreach (var row in section.Rows)
                    foreach (var col in row.Columns)
                        if (ColumnHasSubmit(col)) return true;
        return false;

        static bool ColumnHasSubmit(DataMaker.Schema.Layout.Column col) => col switch
        {
            DataMaker.Schema.Layout.ButtonColumn b when b.Action == DataMaker.Schema.Layout.ButtonAction.Submit => true,
            DataMaker.Schema.Layout.GroupColumn  g => g.Rows.Any(r => r.Columns.Any(ColumnHasSubmit)),
            _ => false,
        };
    }

    /// <summary>CSS px → terminal rows via the same ÷12 heuristic SpacerColumn uses.</summary>
    private static int PadToRows(double? px) => px is null ? 0 : (int)Math.Ceiling(px.Value / 12.0);

    /// <summary>CSS px → terminal columns. Slightly wider char cells in most monospace fonts;
    /// using ÷6 so left/right padding is more visible than vertical (matches reading rhythm).</summary>
    private static int PadToCols(double? px) => px is null ? 0 : (int)Math.Ceiling(px.Value / 6.0);

    private void ClearContent()
    {
        // Disconnect the old runtime from FormState.ValueChanged so it stops
        // reacting to state changes the new runtime will handle.
        _runtime?.Dispose();
        _runtime  = null;
        _renderer = null;

        // RemoveAll tears down every subview in the Window, releasing their
        // event subscriptions. Fresh views in BuildContent rewire.
        RemoveAll();
        _scroll       = null;
        _contentInset = null;
        _submit = null;
        _cancel = null;
        _status = null;
    }

    private void TriggerResize()
    {
        _isResizing = true;
        _lastIndicatorPaintTicks = 0; // force an immediate first paint
        SetNeedsDisplay();
        ScheduleRebuild();
    }

    private void ScheduleRebuild()
    {
        if (_resizeDebounce is not null)
            Application.MainLoop.RemoveTimeout(_resizeDebounce);

        // Hold the resizing indicator visible for at least 1 second from
        // the last detected resize. Every new dimension change resets this
        // timer, so continuous drags stay in resize mode the whole time;
        // even a tiny nudge keeps the indicator up long enough to read.
        _resizeDebounce = Application.MainLoop.AddTimeout(
            TimeSpan.FromMilliseconds(1000),
            _ =>
            {
                _resizeDebounce = null;
                _isResizing    = false;   // resume normal Redraw path
                ClearContent();
                Clear();                  // wipe our frame before the fresh subtree paints
                BuildContent();
                LayoutSubviews();
                SetNeedsDisplay();
                Application.Refresh();
                return false;
            });
    }

    /// <summary>
    /// Short-circuit the draw path during a resize drag: instead of walking
    /// the form subtree (whose layout is momentarily stale w.r.t. the new
    /// terminal dimensions), paint a centered "Resizing…" indicator over a
    /// blanked frame. Runs on Terminal.Gui's normal repaint cycle, so it
    /// doesn't depend on MainLoop.Invoke being drained — important because
    /// the loop doesn't wake during a drag if the mouse is outside the
    /// terminal's hit area.
    /// </summary>
    public override void Redraw(Rect bounds)
    {
        if (_isResizing)
        {
            PaintResizeIndicator();
            return;
        }
        base.Redraw(bounds);
    }

    private void PaintResizeIndicator()
    {
        var drv = Application.Driver;
        if (drv is null) return;

        // Throttle to 5 Hz. Terminal.Gui may call Redraw many times per second
        // while _isResizing is true (poll timer ticks + any other events);
        // repainting the whole screen each time causes visible flicker.
        // 200ms feedback still feels live to the user.
        var nowTicks = Environment.TickCount64;
        if (nowTicks - _lastIndicatorPaintTicks < 200) return;
        _lastIndicatorPaintTicks = nowTicks;

        try
        {
            drv.SetAttribute(global::Terminal.Gui.Attribute.Make(Color.White, Color.Black));

            var cols = drv.Cols;
            var rows = drv.Rows;

            // Row-at-a-time via AddStr instead of cell-at-a-time AddRune.
            // Typical 80×24 terminal: ~24 driver calls vs ~2000 — the
            // difference between smooth and visibly flickery.
            var blankRow = NStack.ustring.Make(new string(' ', cols));
            for (var r = 0; r < rows; r++)
            {
                drv.Move(0, r);
                drv.AddStr(blankRow);
            }

            var msg = $"Resizing…  {cols} × {rows}";
            var x = Math.Max(0, (cols - msg.Length) / 2);
            drv.Move(x, rows / 2);
            drv.AddStr(NStack.ustring.Make(msg));
        }
        catch { /* driver transient; next Redraw will retry */ }
    }

    /// <summary>
    /// Scroll the ScrollView just enough to bring <paramref name="view"/> fully
    /// into the viewport. Computes the field's row in content coordinates by
    /// summing Frame.Y up the parent chain (so it's agnostic to how deeply the
    /// editor is nested in row / column / group frames), then nudges the
    /// ScrollView by the shortfall.
    /// </summary>
    private void ScrollIntoView(View view)
    {
        if (_scroll is null || _contentInset is null) return;

        // Field top in content coordinates: walk up to the content host,
        // accumulating each frame's offset.
        var contentY = 0;
        for (var v = view; v is not null && v != _contentInset && v != _scroll; v = v.SuperView)
            contentY += v.Frame.Y;

        var fieldBottom = contentY + Math.Max(1, view.Frame.Height);   // exclusive

        // ContentOffset.Y is ≤ 0 (negative as the user scrolls down); the
        // first visible content row is its negation.
        var viewportTop = -_scroll.ContentOffset.Y;
        var viewportH   = _scroll.Bounds.Height;

        if (contentY < viewportTop)
            _scroll.ScrollUp(viewportTop - contentY);                  // field above the fold
        else if (fieldBottom > viewportTop + viewportH)
            _scroll.ScrollDown(fieldBottom - (viewportTop + viewportH)); // field below the fold
        else
            return;                                                    // already fully visible

        _scroll.SetNeedsDisplay();
    }

    private void SyncContentSize()
    {
        if (_scroll is null) return;

        // Driver.Cols is the live terminal width regardless of where we are
        // in the layout cycle; _scroll.Bounds can be stale during a resize.
        var cols = Application.Driver?.Cols ?? _scroll.Bounds.Width + 4;
        var width  = Math.Max(1, cols - 4);
        var height = Math.Max(1, _consumedRows);
        if (_scroll.ContentSize.Width == width && _scroll.ContentSize.Height == height) return;

        _scroll.ContentSize = new Size(width, height);
        if (_scroll.ContentOffset.X != 0)
            _scroll.ContentOffset = new Point(0, _scroll.ContentOffset.Y);

        // Keep _contentInset's explicit width in sync with the scroll's
        // content width — minus padLeft + padRight. (Scrollbar lives
        // in an overlay outside the content viewport, no extra reserve.)
        if (_contentInset is not null)
        {
            var padLeft  = PadToCols(_verified.Form.Style?.PaddingLeft  ?? _verified.Form.Style?.Padding);
            var padRight = PadToCols(_verified.Form.Style?.PaddingRight ?? _verified.Form.Style?.Padding);
            _contentInset.Width = Dim.Sized(Math.Max(1, width - padLeft - padRight));
        }

        _scroll.LayoutSubviews();
        _scroll.SetNeedsDisplay();
    }

    // ── Submit flow ────────────────────────────────────────────────────

    private void OnSubmit()
    {
        // _submit may be null when the form has its own inline
        // ButtonColumn(Submit) — page-level button is suppressed. The
        // submit FLOW still runs, just without toggling the absent
        // button's Enabled state.
        if (_runtime is null || _status is null) return;
        if (_submitting) return;   // a submit is already in flight — ignore the repeat

        var errors = _runtime.Validate();
        if (errors.Count > 0)
        {
            ShowErrors(errors);
            return;
        }

        _submitting = true;
        if (_submit is not null) _submit.Enabled = false;
        _status.Text    = "Submitting…";

        Task.Run(async () =>
        {
            var result = await FormSubmitter.SubmitAsync(
                _verified, _state, _submitEndpoint, _http, _bearer);
            Application.MainLoop.Invoke(() => OnSubmitResult(result));
        });
    }

    private void ShowErrors(IReadOnlyDictionary<string, string> errors)
    {
        if (_status is null) return;

        // Light up every invalid field's red-! indicator so the user can
        // see which ones need fixing once they dismiss the dialog — not
        // just the fields they happened to tab through.
        _runtime?.SyncAllErrorIndicators();

        var body = string.Join('\n', errors.Select(e => $"• {e.Key}: {e.Value}"));
        InfoDialog.Show(" Cannot submit — please fix the following ", body, isError: true);
        _status.Text = $"{errors.Count} error(s) — see dialog";
    }

    private void OnSubmitResult(SubmitResult result)
    {
        _submitting = false;   // round-trip done — allow a fresh submit (e.g. after a failure)
        if (_status is null) return;
        if (_submit is not null) _submit.Enabled = true;

        if (result.Success)
        {
            InfoDialog.Show(
                " Submitted ",
                $"Your submission has been sent.\nServer id: {result.SubmissionId}");
            Application.RequestStop();
            return;
        }

        _status.Text = "Submit failed — see dialog";
        InfoDialog.Show(" Submission failed ", result.Error ?? "Unknown error.", isError: true);
    }
}
