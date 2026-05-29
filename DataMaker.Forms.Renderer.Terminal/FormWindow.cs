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
        // Detect inline ButtonColumn(Action=Submit) anywhere in the form.
        // When present the page-level Submit becomes redundant — author
        // wired their own submit affordance — so we hide it (+ the
        // separator line above it) and reclaim those rows for the form.
        var hasInlineSubmit = FormHasInlineSubmit(_verified.Form);

        // Close (was Cancel) bottom-left, Submit bottom-right (skipped
        // when an inline submit ButtonColumn is present).
        _cancel = new Button("_Close") { X = 0, Y = Pos.AnchorEnd(1) };

        const int SubmitAnchor = 12;
        if (!hasInlineSubmit)
        {
            _submit = new Button("_Submit")
            {
                X         = Pos.AnchorEnd(SubmitAnchor),
                Y         = Pos.AnchorEnd(1),
                IsDefault = true,
            };
        }

        _status = new Label("")
        {
            X     = Pos.Right(_cancel) + 2,
            Y     = Pos.AnchorEnd(1),
            Width = hasInlineSubmit ? Dim.Fill() : Dim.Fill(SubmitAnchor + 2),
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
            // separator + 1 for the button bar.
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

        // ScrollView.Add auto-wires MouseEnter→GrabMouse(this) only for
        // children that DON'T override MouseEvent (see TGui v1
        // ScrollView.cs:230 IsOverridden check). _contentInset qualifies,
        // but TextField/CheckBox children inside it DO override and grab
        // mouse on entry — once cursor passes through inset into a
        // TextField, the inset's enter handler doesn't re-fire and the
        // scroll never grabs. Explicit grab on every mouse move inside
        // the inset keeps ScrollView as the wheel receiver regardless
        // of which child the cursor is over.
        var scroll = _scroll;
        // Maintain the scroll mouse grab via RootMouseEvent instead of
        // MouseEnter/Leave on the inset. The transition-based handlers
        // dropped the grab after a gutter scroll because:
        //   - Cursor over gutter → MouseLeave(inset) → ungrab.
        //   - Wheel in gutter scrolls content; views under cursor shift.
        //   - Cursor moves back to inset, but TGui sees no transition
        //     (the inset is "already" under it after the shift) so
        //     MouseEnter doesn't re-fire → no regrab → main wheel dead.
        // Re-evaluating grab on every mouse event keeps the routing
        // path correct regardless of how the cursor got where it is.
        Action<MouseEvent> grabKeeper = e =>
        {
            if (_scroll is null) return;
            var inScroll = _scroll.ScreenToView(e.X, e.Y);
            var inside = inScroll.X >= 0 && inScroll.Y >= 0
                      && inScroll.X < _scroll.Bounds.Width
                      && inScroll.Y < _scroll.Bounds.Height;
            if (inside)
            {
                if (Application.MouseGrabView != _scroll)
                    Application.GrabMouse(_scroll);
            }
            else if (Application.MouseGrabView == _scroll)
            {
                Application.UngrabMouse();
            }
        };
        Application.RootMouseEvent += grabKeeper;
        this.Closing += _ => Application.RootMouseEvent -= grabKeeper;

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
        _consumedRows = _renderer.Render(_contentInset) + padTop + padBottom;
        // Snap _contentInset to the rendered content size so the
        // ScrollView's ContentSize-based scrolling actually moves
        // children. Width follows the scroll viewport minus padding.
        _contentInset.Height = Dim.Sized(Math.Max(1, _consumedRows - padTop - padBottom));
        _runtime.InitialEvaluate();

        // Show the current field's error (if any) in the status bar when
        // the user focuses it — detailed message the field-level !-indicator
        // can't convey on its own.
        _runtime.FieldFocused += (_, error) =>
        {
            if (_status is not null) _status.Text = error ?? "";
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

        var errors = _runtime.Validate();
        if (errors.Count > 0)
        {
            ShowErrors(errors);
            return;
        }

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
