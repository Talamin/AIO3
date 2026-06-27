using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using AIO3.Core.Settings;
using robotManager.Helpful;
using WMemory = wManager.Wow.Memory;
using Math = System.Math;                       // robotManager.Helpful also defines Math / Mouse
using Mouse = System.Windows.Input.Mouse;

namespace AIO3.Overlay
{
    /// <summary>
    /// Native transparent settings overlay drawn OVER the WoW client (borderless / windowed mode). A real window,
    /// not a WoW UI frame — the alternative to the Lua <see cref="SettingsOverlay"/>. It generates controls from
    /// the same <see cref="Setting"/> list and binds two-way DIRECTLY to those objects, so an edit takes effect on
    /// the next rotation tick with no Lua bridge and no per-tick polling. Minimizable in-game to a small token.
    ///
    /// Code-only WPF (no XAML / pack URIs): the fightclass is loaded from a byte[] (no assembly Location), so
    /// resource-based WPF would be fragile. Runs on its own STA thread with a Dispatcher. EVERYTHING is guarded —
    /// if WPF can't start in this process, it logs and the Lua panel keeps working (Phase 1 runs both in parallel).
    /// </summary>
    internal sealed class NativeOverlay
    {
        private readonly string _title;
        private readonly IReadOnlyList<Setting> _settings;
        private readonly Func<string> _activeSpec;
        private readonly string _profile; // per-character key for persisting the overlay's own position/state
        private readonly Func<string> _status; // live HUD line provided by the host (computed on its thread)
        private Thread _uiThread;
        private OverlayWindow _window;
        private volatile bool _dirty; // a control edited a setting → the host should persist

        /// <summary>True once the overlay window is actually up. The host suppresses the Lua panel while this holds
        /// (running both lets the Lua Poll clobber a native edit), and falls back to the Lua panel if it never does.</summary>
        public volatile bool IsActive;

        public NativeOverlay(string title, IReadOnlyList<Setting> settings, Func<string> activeSpec,
                             string profile = null, Func<string> status = null)
        {
            _title = title;
            _settings = settings;
            _activeSpec = activeSpec ?? (() => null);
            _profile = profile;
            _status = status;
            Start();
        }

        /// <summary>True once if a control edited a setting since the last call — the host saves when it is.
        /// Mirrors the Lua overlay's <c>Poll()</c> "changed" return so Main persists edits the same way.</summary>
        public bool TakeDirty()
        {
            if (!_dirty) return false;
            _dirty = false;
            return true;
        }

        private void Start()
        {
            try
            {
                _uiThread = new Thread(Run) { IsBackground = true, Name = "AIO3-Overlay" };
                _uiThread.SetApartmentState(ApartmentState.STA);
                _uiThread.Start();
            }
            catch (Exception e)
            {
                Logging.WriteError("[AIO3] native overlay thread failed to start: " + e.Message);
            }
        }

        private void Run()
        {
            try
            {
                _window = new OverlayWindow(_title, _settings, _activeSpec, () => _dirty = true, _profile, _status);
                _window.Show();
                IsActive = true; // the host now suppresses the Lua panel so its Poll can't clobber native edits
                Logging.Write("[AIO3] Native settings overlay ready (over the game).");
                Dispatcher.Run(); // pump this STA thread's message/dispatcher loop until shutdown
            }
            catch (Exception e)
            {
                // WPF couldn't start in this byte[]-loaded fightclass context — fall back to the Lua panel.
                Logging.WriteError("[AIO3] native overlay unavailable (the in-game panel still works): " + e);
            }
        }

        public void Dispose()
        {
            try
            {
                Dispatcher d = _window?.Dispatcher;
                if (d != null)
                {
                    d.Invoke(() => { try { _window.Shutdown(); } catch { } });
                    d.InvokeShutdown();
                }
            }
            catch { /* tearing down — never throw */ }
        }
    }

    /// <summary>The WPF window itself: a transparent, topmost, borderless panel that tracks the WoW window and
    /// renders the settings. Built entirely in code.</summary>
    internal sealed class OverlayWindow : Window
    {
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd); // WoW minimized?
        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080; // keep the overlay out of the alt-tab list

        private static readonly Brush PanelBg = Freeze(new SolidColorBrush(Color.FromArgb(235, 22, 24, 30)));
        private static readonly Brush BarBg = Freeze(new SolidColorBrush(Color.FromArgb(255, 34, 38, 48)));
        private static readonly Brush Accent = Freeze(new SolidColorBrush(Color.FromArgb(255, 120, 170, 255)));
        private static readonly Brush Fg = Freeze(new SolidColorBrush(Color.FromArgb(255, 226, 230, 238)));
        private static readonly Brush Divider = Freeze(new SolidColorBrush(Color.FromArgb(40, 120, 130, 150)));
        private static readonly Brush RowHover = Freeze(new SolidColorBrush(Color.FromArgb(36, 255, 255, 255)));
        private static readonly Brush InputBg = Freeze(new SolidColorBrush(Color.FromArgb(255, 40, 44, 54)));

        private const double FullW = 340, FullH = 470, TokenW = 74, PillW = 214, TokenH = 26, PillH = 42;

        private readonly List<(Setting setting, FrameworkElement row)> _rows = new List<(Setting, FrameworkElement)>();
        private TextBox _search;
        private TextBlock _tokenLine1, _tokenLine2; // the minimized pill's two rows (spec+target / current cast)

        private readonly IReadOnlyList<Setting> _settings;
        private readonly Func<string> _activeSpec;
        private readonly Action _onChanged;
        private readonly DispatcherTimer _timer;

        private Border _panel;   // the full panel
        private Border _token;   // the minimized token
        private TabControl _tabs;
        private string _builtForSpec = ""; // the spec the tabs were last built for (set in the ctor + on spec change)
        private bool _minimized;
        private bool _dragging;
        private Point _tokenDown;   // token press point (screen) for the click-vs-drag test
        private bool _tokenMoved;
        private int _offX = 24, _offY = 80; // overlay position relative to the WoW window's top-left (in DIPs)
        private IntPtr _hwnd;
        private readonly string _stateFile; // persisted position + minimized state, per character
        private double _dpiScale = 1.0;      // device px per DIP (so positioning is correct above 100% scaling)
        private readonly Func<string> _status; // live HUD line (host-provided)
        private TextBlock _statusText;

        public OverlayWindow(string title, IReadOnlyList<Setting> settings, Func<string> activeSpec, Action onChanged,
                             string profile = null, Func<string> status = null)
        {
            _settings = settings;
            _activeSpec = activeSpec;
            _onChanged = onChanged;
            _status = status;
            _stateFile = StatePath(profile);
            LoadState(); // restore the saved offset + minimized state before building

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.Manual;
            ShowActivated = false;
            FontSize = 12; // normalize the base font (the inherited system default can render large on some displays)
            Width = FullW;
            Height = FullH;
            Left = 200; Top = 200; // a visible default; the first track repositions it over the WoW window

            BuildChrome(title);
            BuildControls(_activeSpec());
            SetMinimized(_minimized); // apply the restored minimized state

            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(66) };
            _timer.Tick += Track;
            _timer.Start();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _hwnd = new WindowInteropHelper(this).Handle;
            try { SetWindowLong(_hwnd, GWL_EXSTYLE, GetWindowLong(_hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW); } catch { }
            try
            {
                var src = PresentationSource.FromVisual(this);
                if (src?.CompositionTarget != null) _dpiScale = src.CompositionTarget.TransformToDevice.M11;
                if (_dpiScale <= 0) _dpiScale = 1.0;
            }
            catch { _dpiScale = 1.0; }
        }

        // --- persisted overlay state (position offset + minimized) per character ---

        private static string StatePath(string profile)
        {
            try
            {
                if (string.IsNullOrEmpty(profile)) profile = "default";
                foreach (char c in Path.GetInvalidFileNameChars()) profile = profile.Replace(c, '_');
                return Path.Combine(Others.GetCurrentDirectory, "Settings", "AIO3", profile + ".overlay");
            }
            catch { return null; }
        }

        private void LoadState()
        {
            try
            {
                if (_stateFile == null || !File.Exists(_stateFile)) return;
                string[] p = File.ReadAllText(_stateFile).Split(',');
                if (p.Length >= 3 && int.TryParse(p[0], out int x) && int.TryParse(p[1], out int y))
                {
                    _offX = x; _offY = y; _minimized = p[2] == "1";
                }
            }
            catch { }
        }

        private void SaveState()
        {
            try
            {
                if (_stateFile == null) return;
                Directory.CreateDirectory(Path.GetDirectoryName(_stateFile));
                File.WriteAllText(_stateFile, _offX + "," + _offY + "," + (_minimized ? "1" : "0"));
            }
            catch { }
        }

        /// <summary>Stop the timer and close, from the UI thread.</summary>
        public void Shutdown()
        {
            _timer?.Stop();
            Close();
        }

        // --- chrome: the full panel (title bar + tabs) and the minimized token, one visible at a time ---

        private void BuildChrome(string title)
        {
            // Title bar
            var titleText = new TextBlock
            {
                Text = "AIO3 — " + title, Foreground = Fg, FontWeight = FontWeights.Bold, FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0)
            };
            var min = BarButton("–"); // en-dash "minimize"
            min.Click += (s, e) => SetMinimized(true);
            min.HorizontalAlignment = HorizontalAlignment.Right;
            min.Margin = new Thickness(0, 0, 6, 0);

            var bar = new Grid { Background = BarBg, Height = 26 };
            bar.Children.Add(titleText);
            bar.Children.Add(min);
            bar.MouseLeftButtonDown += (s, e) => Drag();

            // Live status line (HUD): spec | target HP% → action. Hidden when the host provides no status.
            _statusText = new TextBlock
            {
                Foreground = Accent, FontSize = 11, Margin = new Thickness(8, 3, 8, 1),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Visibility = _status != null ? Visibility.Visible : Visibility.Collapsed
            };

            // Filter box: type to show only matching settings (across all tabs).
            _search = new TextBox
            {
                Margin = new Thickness(6, 4, 6, 2), Padding = new Thickness(4, 1, 4, 1),
                Background = InputBg, Foreground = Fg, BorderBrush = Divider, CaretBrush = Fg,
                ToolTip = "Filter settings — type part of a setting's name"
            };
            _search.TextChanged += (s, e) => ApplyFilter();

            _tabs = new TabControl { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Margin = new Thickness(4, 2, 4, 4) };
            _tabs.SelectionChanged += (s, e) => ApplyTabAccent();

            var dock = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(bar, Dock.Top);
            DockPanel.SetDock(_statusText, Dock.Top);
            DockPanel.SetDock(_search, Dock.Top);
            dock.Children.Add(bar);
            dock.Children.Add(_statusText);
            dock.Children.Add(_search);
            dock.Children.Add(_tabs);

            _panel = new Border
            {
                Background = PanelBg, CornerRadius = new CornerRadius(6),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 60, 66, 80)), BorderThickness = new Thickness(1),
                Child = dock
            };

            // Minimized token / status pill: two rows — spec + target on top, the current cast below. Falls back to
            // a single "AIO3" line when no status func is set.
            _tokenLine1 = new TextBlock
            {
                Text = "AIO3", Foreground = Fg, FontWeight = FontWeights.Bold, FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Center
            };
            _tokenLine2 = new TextBlock
            {
                Foreground = Accent, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = HorizontalAlignment.Center, Visibility = Visibility.Collapsed
            };
            var tokenStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 1, 6, 1) };
            tokenStack.Children.Add(_tokenLine1);
            tokenStack.Children.Add(_tokenLine2);
            _token = new Border
            {
                Background = BarBg, CornerRadius = new CornerRadius(5), Child = tokenStack,
                BorderBrush = Accent, BorderThickness = new Thickness(1), Visibility = Visibility.Collapsed,
                ToolTip = "AIO3 — click to open, drag to move"
            };
            // Token: click to expand, drag to move. A small move threshold tells a click from a drag (a plain
            // DragMove on press ate the click).
            _token.MouseLeftButtonDown += (s, e) =>
            {
                _tokenDown = PointToScreen(e.GetPosition(this));
                _tokenMoved = false;
                _token.CaptureMouse();
                e.Handled = true;
            };
            _token.MouseMove += (s, e) =>
            {
                if (e.LeftButton != MouseButtonState.Pressed || !_token.IsMouseCaptured) return;
                Point now = PointToScreen(e.GetPosition(this));
                if (!_tokenMoved && (Math.Abs(now.X - _tokenDown.X) > 4 || Math.Abs(now.Y - _tokenDown.Y) > 4))
                {
                    _tokenMoved = true;
                    _token.ReleaseMouseCapture();
                    Drag(); // drag the window; recomputes the offset on release
                }
            };
            _token.MouseLeftButtonUp += (s, e) =>
            {
                if (_token.IsMouseCaptured) _token.ReleaseMouseCapture();
                if (!_tokenMoved) SetMinimized(false); // a click (no drag) → expand
                e.Handled = true;
            };

            var root = new Grid();
            root.Children.Add(_panel);
            root.Children.Add(_token);
            Content = root;
        }

        private Button BarButton(string glyph) => new Button
        {
            Content = glyph, Width = 20, Height = 18, Foreground = Fg, Background = Brushes.Transparent,
            BorderThickness = new Thickness(0), FontWeight = FontWeights.Bold, Cursor = System.Windows.Input.Cursors.Hand
        };

        private void SetMinimized(bool min)
        {
            _minimized = min;
            _panel.Visibility = min ? Visibility.Collapsed : Visibility.Visible;
            _token.Visibility = min ? Visibility.Visible : Visibility.Collapsed;
            // A wider/taller pill when it shows the live status, else the compact "AIO3" token.
            Width = min ? (_status != null ? PillW : TokenW) : FullW;
            Height = min ? (_status != null ? PillH : TokenH) : FullH;
            if (min) UpdateToken(); // fill the pill straight away (don't wait for the next track tick)
            SaveState();
        }

        private void UpdateToken()
        {
            if (_tokenLine1 == null) return;
            string s = _status != null ? _status() : null;
            if (string.IsNullOrEmpty(s))
            {
                _tokenLine1.Text = "AIO3"; _tokenLine2.Text = ""; _tokenLine2.Visibility = Visibility.Collapsed;
                return;
            }
            int nl = s.IndexOf('\n');
            _tokenLine1.Text = nl >= 0 ? s.Substring(0, nl) : s;
            _tokenLine2.Text = nl >= 0 ? s.Substring(nl + 1) : "";
            _tokenLine2.Visibility = string.IsNullOrEmpty(_tokenLine2.Text) ? Visibility.Collapsed : Visibility.Visible;
        }

        private void Drag()
        {
            try { _dragging = true; DragMove(); }
            catch { }
            finally
            {
                _dragging = false;
                // remember where the user put it, as an offset (in DIPs) from the WoW window's top-left
                if (TryGetWowRect(out RECT r))
                {
                    _offX = (int)(Left - r.Left / _dpiScale);
                    _offY = (int)(Top - r.Top / _dpiScale);
                }
                SaveState();
            }
        }

        // --- controls: generated from the Setting list, grouped into Category tabs, filtered by active spec ---

        private void BuildControls(string spec)
        {
            int selected = _tabs.SelectedIndex;
            _tabs.Items.Clear();
            _rows.Clear();
            foreach (var group in _settings.Where(s => s.AppliesTo(spec))
                                           .GroupBy(s => string.IsNullOrEmpty(s.Category) ? "General" : s.Category))
            {
                var groupSettings = group.ToList();
                string cat = group.Key;
                var stack = new StackPanel { Margin = new Thickness(2) };
                foreach (Setting setting in groupSettings)
                {
                    // Each row gets a hover highlight + a subtle divider + a tooltip; kept in _rows so the filter can hide it.
                    var holder = new Border
                    {
                        Child = Row(setting),
                        Padding = new Thickness(6, 1, 6, 1),
                        BorderBrush = Divider,
                        BorderThickness = new Thickness(0, 0, 0, 1),
                        ToolTip = Hint(setting)
                    };
                    holder.MouseEnter += (s, e) => holder.Background = RowHover;
                    holder.MouseLeave += (s, e) => holder.Background = Brushes.Transparent;
                    stack.Children.Add(holder);
                    _rows.Add((setting, holder));
                }

                // Per-tab "reset to defaults".
                var reset = new Button
                {
                    Content = "↺ Reset", HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(6, 6, 6, 2), Padding = new Thickness(8, 1, 8, 1),
                    Foreground = Fg, Background = BarBg, BorderThickness = new Thickness(0), Cursor = Cursors.Hand,
                    ToolTip = "Reset this tab's settings to their defaults"
                };
                reset.Click += (s, e) =>
                {
                    foreach (Setting st in groupSettings) st.Reset();
                    Changed("reset " + cat, "defaults");
                    BuildControls(_builtForSpec);
                };
                stack.Children.Add(reset);

                var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = stack };
                _tabs.Items.Add(new TabItem
                {
                    // Set size AND colour on the header TEXT directly: TabItem.FontSize/Foreground don't reach the
                    // header in the active theme. ApplyTabAccent() then accents the selected tab.
                    Header = new TextBlock { Text = cat, FontSize = 11, Foreground = Fg },
                    Content = scroll,
                    Padding = new Thickness(7, 1, 7, 1)
                });
            }
            _builtForSpec = spec ?? "";
            if (selected >= 0 && selected < _tabs.Items.Count) _tabs.SelectedIndex = selected;
            ApplyTabAccent();
            ApplyFilter();
        }

        /// <summary>The hover tooltip for a setting: its Description when set, else a derived hint (an int's range,
        /// a choice's options) so even un-described settings get a useful tooltip.</summary>
        private static string Hint(Setting s)
        {
            if (!string.IsNullOrEmpty(s.Description)) return s.Description;
            switch (s)
            {
                case IntSetting i: return i.Label + ": " + i.Min + "–" + i.Max + " (step " + i.Step + ")";
                case ChoiceSetting c: return c.Label + ": " + string.Join(" / ", c.Options);
                default: return s.Label;
            }
        }

        /// <summary>Apply a control edit: log it (so the WRobot log confirms the change, like the Lua panel did)
        /// and flag the host to persist + reconcile. The rotation reads the new value on its next tick.</summary>
        private void Changed(string label, object value)
        {
            try { Logging.Write("[AIO3] " + label + " = " + value); } catch { }
            _onChanged();
        }

        private UIElement Row(Setting setting)
        {
            switch (setting)
            {
                case ToggleSetting t:
                {
                    var cb = new CheckBox { Content = t.Label, Foreground = Fg, IsChecked = t.Value, Margin = new Thickness(2, 4, 2, 4) };
                    cb.Checked += (s, e) => { t.Value = true; Changed(t.Label, true); };
                    cb.Unchecked += (s, e) => { t.Value = false; Changed(t.Label, false); };
                    return cb;
                }
                case IntSetting i:
                {
                    var label = new TextBlock { Foreground = Fg, Margin = new Thickness(2, 3, 2, 1) };
                    void Refresh() => label.Text = i.Label + ": " + i.Value;
                    Refresh();
                    var slider = new Slider
                    {
                        Minimum = i.Min, Maximum = i.Max, Value = i.Value, TickFrequency = Math.Max(1, i.Step),
                        IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0)
                    };
                    void Set(int v)
                    {
                        v = Math.Max(i.Min, Math.Min(i.Max, v));
                        if (v == i.Value) return;
                        i.Value = v; slider.Value = v; Refresh(); Changed(i.Label, v);
                    }
                    slider.ValueChanged += (s, e) => Set((int)Math.Round(e.NewValue));
                    var minus = StepButton("−"); minus.Click += (s, e) => Set(i.Value - i.Step);
                    var plus = StepButton("+"); plus.Click += (s, e) => Set(i.Value + i.Step);

                    var stepRow = new DockPanel { Margin = new Thickness(0, 0, 0, 3) };
                    DockPanel.SetDock(minus, Dock.Left);
                    DockPanel.SetDock(plus, Dock.Right);
                    stepRow.Children.Add(minus);
                    stepRow.Children.Add(plus);
                    stepRow.Children.Add(slider); // fills the middle

                    var box = new StackPanel();
                    box.Children.Add(label);
                    box.Children.Add(stepRow);
                    return box;
                }
                case ChoiceSetting c:
                {
                    var label = new TextBlock { Text = c.Label, Foreground = Fg, Margin = new Thickness(2, 3, 2, 1) };
                    var combo = new ComboBox { ItemsSource = c.Options, SelectedItem = c.Value, Margin = new Thickness(2, 0, 2, 3) };
                    combo.SelectionChanged += (s, e) =>
                    {
                        if (combo.SelectedItem is string v && v != c.Value) { c.Value = v; Changed(c.Label, v); }
                    };
                    var box = new StackPanel();
                    box.Children.Add(label);
                    box.Children.Add(combo);
                    return box;
                }
                default:
                    return new TextBlock { Text = setting.Label, Foreground = Fg };
            }
        }

        private Button StepButton(string glyph) => new Button
        {
            Content = glyph, Width = 22, Height = 20, Foreground = Fg, Background = BarBg,
            BorderThickness = new Thickness(0), FontWeight = FontWeights.Bold, Cursor = Cursors.Hand,
            Margin = new Thickness(1, 0, 1, 0)
        };

        /// <summary>Accent the selected tab's label (the default theme doesn't make it pop on the dark strip).</summary>
        private void ApplyTabAccent()
        {
            foreach (TabItem ti in _tabs.Items.OfType<TabItem>())
                if (ti.Header is TextBlock tb) tb.Foreground = ti.IsSelected ? Accent : Fg;
        }

        /// <summary>Filter the rows by the search text (a setting-name substring); empty shows everything.</summary>
        private void ApplyFilter()
        {
            string q = _search?.Text?.Trim() ?? "";
            bool all = q.Length == 0;
            foreach (var (setting, row) in _rows)
                row.Visibility = all || (setting.Label != null && setting.Label.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                    ? Visibility.Visible : Visibility.Collapsed;
        }

        // --- tracking: glue the overlay to the WoW window; show only while WoW or the overlay is foreground ---

        private bool _loggedRect;
        private void Track(object sender, EventArgs e)
        {
            try
            {
                // Hide only when WoW is gone or minimized — otherwise stay visible. Deliberately NOT gated on WoW
                // being the FOREGROUND window: that made the overlay vanish whenever you clicked another window,
                // which was more annoying than useful. It stays on top while WoW is open; minimize it to the token
                // to get it out of the way.
                IntPtr wow = WowHandle();
                if (wow == IntPtr.Zero || IsIconic(wow)) { Visibility = Visibility.Hidden; return; }
                if (Visibility != Visibility.Visible) Visibility = Visibility.Visible;
                if (!Topmost) Topmost = true;

                // Refresh the live status — in the expanded header (one line), or the minimized pill (two rows).
                if (_minimized) UpdateToken();
                else if (_status != null && _statusText != null) _statusText.Text = _status().Replace("\n", "  →  ");

                // Glue to the WoW window. GetWindowRect is in DEVICE pixels; the window's Left/Top/Width/Height are
                // DIPs, so divide by the DPI scale (1.0 at 100%) — otherwise it's mis-placed above 100% scaling.
                if (!_dragging && TryGetWowRect(out RECT r))
                {
                    double s = _dpiScale;
                    double wowL = r.Left / s, wowT = r.Top / s, wowR = r.Right / s, wowB = r.Bottom / s;
                    double wowH = wowB - wowT;

                    // Fit the expanded panel inside the WoW window: cap its height to the space below the top offset
                    // (the tabs scroll), so it never hangs off the bottom of whatever size the WoW window is.
                    if (!_minimized)
                        Height = Math.Max(180, Math.Min(FullH, wowH - _offY - 10));

                    // Anchor at the offset, then clamp fully inside the WoW window's bounds.
                    double left = wowL + _offX, top = wowT + _offY;
                    left = Math.Max(wowL + 2, Math.Min(left, wowR - Width - 2));
                    top = Math.Max(wowT + 2, Math.Min(top, wowB - Height - 2));
                    Left = left; Top = top;

                    if (!_loggedRect)
                    {
                        _loggedRect = true;
                        Logging.Write($"[AIO3] overlay tracked WoW rect {(int)(wowR - wowL)}x{(int)wowH} dpi={s:0.##} → placed at {(int)Left},{(int)Top}");
                    }
                }
                else if (!_loggedRect)
                {
                    _loggedRect = true;
                    Logging.Write("[AIO3] overlay: WoW window rect unavailable (handle=" + WowHandle() + ") → showing at the default spot.");
                }

                // rebuild the controls when the active spec changes (level-up / respec / manual override)
                string spec = _activeSpec() ?? "";
                if (spec != _builtForSpec) BuildControls(spec);
            }
            catch (Exception ex)
            {
                if (!_loggedRect) { _loggedRect = true; Logging.WriteError("[AIO3] overlay track error: " + ex.Message); }
            }
        }

        private static IntPtr WowHandle()
        {
            try { return WMemory.WowMemory?.Memory?.WindowHandle ?? IntPtr.Zero; }
            catch { return IntPtr.Zero; }
        }

        private static bool TryGetWowRect(out RECT rect)
        {
            rect = default(RECT);
            IntPtr h = WowHandle();
            return h != IntPtr.Zero && GetWindowRect(h, out rect) && rect.Right > rect.Left;
        }

        private static Brush Freeze(SolidColorBrush b) { b.Freeze(); return b; }
    }
}
