using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using WeaponDamageCalc.Models;
using WeaponDamageCalc.Services;
using WeaponDamageCalc.Tools;

namespace WeaponDamageCalc;

public partial class Form1 : Form
{
    private List<WeaponData> weapons = null!;
    private WeaponData? currentWeaponLeft = null;
    private WeaponData? currentWeaponRight = null;
    private static string? lastWikiUser = null;
    private static string? lastWikiPw = null;

    #nullable disable
    //放上面会爆warn

    private bool updatingControls = false;
    private bool initializing = true;

    private const double SliderMin = 0.0;
    private const double SliderMax = 7.5;
    private const double SliderStep = 0.01;
    private const double DistanceDivisor = 12.7;//本来500HU=31.25英尺 但MCV的好像不一样 sb英制单位

    private string lastScriptsDir = "";
    private bool refreshing = false;
    private int saveLock = 0;
    private bool isTopmost = false;
    private bool showingAltStats = false;
    private WeaponScriptService.AltStatMode currentAltStatMode = WeaponScriptService.AltStatMode.Dov;
    private bool _undoInProgress;
    private System.Windows.Forms.Timer _undoTimer = null!;
    private bool _undoPending;

    private WeaponData _snapshotLeft = null!;
    private WeaponData _snapshotRight = null!;

    private string _rapidStartLeft = null!;
    private string _rapidStartRight = null!;
    private DateTime _rapidDeadlineL;
    private DateTime _rapidDeadlineR;
    private const int RapidSettleMs = 300;

    private class UndoEntry
    {
        public string LeftScriptName = null!;
        public string RightScriptName = null!;
        public WeaponData LeftData = null!;
        public WeaponData RightData = null!;
        public bool ShowingAltStats;
        public WeaponScriptService.AltStatMode AltMode;
    }

    private LinkedList<UndoEntry> _undoStack = new();
    private LinkedList<UndoEntry> _redoStack = new();
    private const int MaxUndo = 100;

    private PanelRenderer spreadRenderer = null!;
    private PanelRenderer recoilRenderer = null!;

    private bool lastFocusLeft = true;
    public static bool ForceDarkMode = false;
    public static bool ForceLightMode = false;
    private bool _darkMode = false;

    private int hotkeyId = 9001;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    private const uint MOD_CONTROL = 0x0002;
    private const uint VK_T = 0x54;

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    private const int SW_RESTORE = 9;

    #region 初始化

    public Form1()
    {
        LogService.Info("Form1 constructor started");
        try
        {
            this.Text = "Keyvalues Mangler™ 5000";
            this.Size = new Size(1366, 768);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;

            string csvPath = Path.Combine(AppContext.BaseDirectory, "weapons.csv");
            weapons = File.Exists(csvPath) ? CsvService.LoadWeapons(csvPath) : new List<WeaponData>();
            LogService.Info($"Weapons loaded: {weapons.Count}");

            _undoTimer = new System.Windows.Forms.Timer { Interval = 300 };
            _undoTimer.Tick += (_, _) => { _undoTimer.Stop(); if (_undoPending) { PushUndo(); _undoPending = false; } };

            InitCenterPanels();
            InitLeftPanel(weapons);
            InitRightPanel(weapons);
            InitC64Labels();
            InitTopButtons();
            MarkPanelControls();
            if (SystemUsesDarkMode())
                ApplyDarkMode();

            this.KeyPreview = true;
            this.KeyDown += Form1_KeyDown;
            this.FormClosing += Form1_FormClosing;

            this.Shown += (s, e) =>
            {
                var screen = Screen.FromControl(this);
                float scale = Math.Min((float)screen.WorkingArea.Width / 1366f, (float)screen.WorkingArea.Height / 768f);
                scale = scale < 1.0f ? Math.Max(scale, 1280f / 1366f) : 1.0f;
                this.Scale(new SizeF(scale, scale));
                int w = (int)(1366 * scale), h = (int)(768 * scale);
                this.Size = new Size(Math.Min(w, screen.WorkingArea.Width), Math.Min(h, screen.WorkingArea.Height));

                bool needDrag = screen.Bounds.Width < 1280 || screen.Bounds.Height < 720;
                LogService.Info($"Scale: {scale:F3}, screen: {screen.WorkingArea.Width}x{screen.WorkingArea.Height}, needDrag={needDrag}");
                if (needDrag)
                {
                    var panel = new Panel { Location = Point.Empty, Size = new Size(w, h), AutoScroll = true };
                    EnableDoubleBuffering(panel);
                    foreach (var c in this.Controls.Cast<Control>().ToList()) { this.Controls.Remove(c); panel.Controls.Add(c); }
                    this.Controls.Add(panel);
                    //收缩panel至实际控件底部 消除多余空白
                    int bottom = 0;
                    foreach (Control c in panel.Controls) bottom = Math.Max(bottom, c.Bottom);
                    if (bottom < panel.Height) panel.Height = bottom;

                    bool dragging = false;
                    Point last = Point.Empty, off = Point.Empty;
                    int mx = Math.Min(this.ClientSize.Width - panel.Width, 0), my = Math.Min(this.ClientSize.Height - panel.Height, 0);
                    void Bind(Control p)
                    {
                        p.MouseDown += (_, me) => { dragging = true; last = p.PointToScreen(me.Location); };
                        p.MouseUp += (_, _) => dragging = false;
                        p.MouseMove += (_, me) =>
                        {
                            if (!dragging) return;
                            var cur = p.PointToScreen(me.Location);
                            off.X = Math.Clamp(off.X + cur.X - last.X, mx, 0);
                            off.Y = Math.Clamp(off.Y + cur.Y - last.Y, my, 0);
                            panel.Location = off;
                            last = cur;
                        };
                        foreach (Control c in p.Controls) Bind(c);
                    }
                    Bind(panel);
                    //跨屏幕拖动时重算可拖动范围
                    this.Resize += (_, _) =>
                    {
                        mx = Math.Min(this.ClientSize.Width - panel.Width, 0);
                        my = Math.Min(this.ClientSize.Height - panel.Height, 0);
                        off.X = Math.Clamp(off.X, mx, 0);
                        off.Y = Math.Clamp(off.Y, my, 0);
                        panel.Location = off;
                    };
                }

                var originalTitle = this.Text;
                this.Text = "Keyvalues Mangler™ 5000 — Ctrl+T to toggle topmost/minimize";
                var titleTimer = new System.Windows.Forms.Timer { Interval = 1919 };
                titleTimer.Tick += (_, _) => { this.Text = originalTitle; titleTimer.Stop(); titleTimer.Dispose(); };
                titleTimer.Start();

                if (weapons.Count > 0)
                {
                    cmbWeaponsL.SelectedIndexChanged -= WeaponSelectedL;
                    cmbWeaponsR.SelectedIndexChanged -= WeaponSelectedR;

                    cmbWeaponsL.DataSource = null;
                    cmbWeaponsL.DataSource = new List<WeaponData>(weapons);
                    cmbWeaponsL.DisplayMember = "PrintName";
                    cmbWeaponsL.SelectedIndex = 0;

                    cmbWeaponsR.DataSource = null;
                    cmbWeaponsR.DataSource = new List<WeaponData>(weapons);
                    cmbWeaponsR.DisplayMember = "PrintName";
                    cmbWeaponsR.SelectedIndex = 0;

                    cmbWeaponsL.SelectedIndexChanged += WeaponSelectedL;
                    cmbWeaponsR.SelectedIndexChanged += WeaponSelectedR;

                    initializing = false;
                    if (cmbWeaponsL.SelectedItem is WeaponData wL)
                    {
                        currentWeaponLeft = wL;
                        LoadWeaponToControls(wL, true);
                    }
                    if (cmbWeaponsR.SelectedItem is WeaponData wR)
                    {
                        currentWeaponRight = wR;
                        LoadWeaponToControls(wR, false);
                    }
                    StoreSnapshot();
                    UpdateAllDamage();
                    pnlSpread.Invalidate();
                    pnlRecoil.Invalidate();

                    UpdateC64Labels(true);
                }
                initializing = false;
                RegisterHotKey(this.Handle, hotkeyId, MOD_CONTROL, VK_T);
                LogService.Info("Form1 ready");
            };

            this.FormClosed += (s, e) => UnregisterHotKey(this.Handle, hotkeyId);
        }
        catch (Exception ex)
        {
            LogService.Fatal(ex, "Form1 constructor failed");
            MessageBox.Show($"Launch failed: {ex}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Application.Exit();
        }
    }

    private void InitCenterPanels()
    {
        int cx = 525;
        pnlSpread = new Panel { Location = new Point(cx, 38), Size = new Size(300, 300), BorderStyle = BorderStyle.FixedSingle, BackColor = Color.Black };
        EnableDoubleBuffering(pnlSpread);
        pnlSpread.Paint += PnlSpread_Paint;
        this.Controls.Add(pnlSpread);

        pnlRecoil = new Panel { Location = new Point(cx, 313), Size = new Size(300, 300), BorderStyle = BorderStyle.FixedSingle, BackColor = Color.Black };
        EnableDoubleBuffering(pnlRecoil);
        pnlRecoil.Paint += PnlRecoil_Paint;
        this.Controls.Add(pnlRecoil);

        spreadRenderer = new PanelRenderer(pnlSpread);
        recoilRenderer = new PanelRenderer(pnlRecoil);
    }

    private void InitTopButtons()
    {
        int cx = 525;
        btnSave = new Button { Text = "Save", Location = new Point(cx, 6), Size = new Size(59, 26) };
        btnSave.Click += BtnSave_Click;
        this.Controls.Add(btnSave);

        btnCsvToScripts = new Button { Text = "CSV>Script", Location = new Point(cx + 61, 6), Size = new Size(88, 26) };
        btnCsvToScripts.Click += BtnCsvToScripts_Click;
        this.Controls.Add(btnCsvToScripts);

        btnScriptsToCsv = new Button { Text = "Script>CSV", Location = new Point(cx + 151, 6), Size = new Size(88, 26) };
        btnScriptsToCsv.Click += BtnScriptsToCsv_Click;
        this.Controls.Add(btnScriptsToCsv);

        var btnRefresh = new Button { Text = "Rfsh", Location = new Point(cx + 241, 6), Size = new Size(59, 26) };
        btnRefresh.Click += BtnRefresh_Click;
        this.Controls.Add(btnRefresh);

        var btnCopy = new Button { Text = "L>R", Location = new Point(cx + 22, 618), Size = new Size(48, 26) };
        btnCopy.Click += CopyLeftToRight;
        this.Controls.Add(btnCopy);

        //glory to our coders all i dont need to write a hook myself but just call a cvar
        var btnCopyCvar = new Button { Text = "wpn_reload_script all", Location = new Point(cx + 73, 618), Size = new Size(152, 26) };
        btnCopyCvar.Tag = false;
        btnCopyCvar.Click += BtnQuickExport_Click;
        btnCopyCvar.MouseLeave += (s, e) => CancelConfirm(btnCopyCvar);
        btnCopyCvar.MouseUp += (s, e) =>
        {
            if (e.Button == MouseButtons.Right) CancelConfirm(btnCopyCvar);
        };
        this.Controls.Add(btnCopyCvar);
        
        var btnConvertToTemplate = new Button { Text = "Tmpl", Location = new Point(cx + 22, 646), Size = new Size(48, 26) };
        btnConvertToTemplate.Click += BtnConvertToTemplate_Click;
        this.Controls.Add(btnConvertToTemplate);

        var btnToggleDov = new Button { Text = "DoV", Location = new Point(cx + 73, 646), Size = new Size(75, 26), BackColor = SystemColors.Control };
        btnToggleDov.Click += (s, e) => ToggleAltStats(WeaponScriptService.AltStatMode.Dov);
        this.Controls.Add(btnToggleDov);

        var btnToggleZombie = new Button { Text = "Zmb", Location = new Point(cx + 150, 646), Size = new Size(75, 26), BackColor = SystemColors.Control };
        btnToggleZombie.Click += (s, e) => ToggleAltStats(WeaponScriptService.AltStatMode.Zombie);
        this.Controls.Add(btnToggleZombie);

        var btnCopyR = new Button { Text = "L<R", Location = new Point(cx + 228, 618), Size = new Size(48, 26) };
        btnCopyR.Click += CopyRightToLeft;
        this.Controls.Add(btnCopyR);

        var btnWiki = new Button { Text = "Wiki", Location = new Point(cx + 228, 646), Size = new Size(48, 26) };
        btnWiki.Click += BtnWiki_Click;
        this.Controls.Add(btnWiki);

        var tooltip = new ToolTip();
        tooltip.SetToolTip(btnSave, "Save current weapon data to CSV (Ctrl+S)\nCtrl+Z/Y to undo/redo");
        tooltip.SetToolTip(btnCsvToScripts, "Export CSV data to weapon script files");
        tooltip.SetToolTip(btnScriptsToCsv, "Import weapon script files to CSV");
        tooltip.SetToolTip(btnRefresh, "Reload weapon list from CSV (Ctrl+R)");
        tooltip.SetToolTip(btnCopy, "Copy left panel values to right");
        tooltip.SetToolTip(btnCopyCvar, "Quick export: save CSV and export to scripts (Ctrl+Shift+S)\nRight-click to cancel");
        tooltip.SetToolTip(btnConvertToTemplate, "Convert old scripts to preset_file template format");
        tooltip.SetToolTip(btnToggleDov, "Toggle Day of Victory alternate stats");
        tooltip.SetToolTip(btnToggleZombie, "Toggle Zombie Mode alternate stats");
        tooltip.SetToolTip(btnCopyR, "Copy right panel values to left");
        tooltip.SetToolTip(btnWiki, "Open Wiki Stats Updater");
    }

    private static void CancelConfirm(Button btn)
    {
        if (btn.Tag is true)
        {
            LogService.Debug("BtnQuickExport: right-click cancelled");
            btn.Text = "wpn_reload_script all";
            btn.Tag = false;
        }
    }

    private void Form1_FormClosing(object sender, FormClosingEventArgs e)
    {
        //结束未完成的rapid 保存当前状态
        if (_rapidStartLeft != null || _rapidStartRight != null) PushUndo();
        bool leftDirty = currentWeaponLeft != null && HasUnsavedChanges(true, checkBothSides: true);
        bool rightDirty = currentWeaponRight != null && HasUnsavedChanges(false, checkBothSides: true);
        if (leftDirty || rightDirty)
        {
            LogService.Debug($"FormClosing: unsaved changes (L={leftDirty}, R={rightDirty}), prompting user");
            var result = MessageBox.Show("Unsaved changes will be lost. Save now?",
                "Unsaved Changes", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
            if (result == DialogResult.Yes)
            {
                LogService.Info("Form1 closing: saved changes");
                BtnSave_Click(this, EventArgs.Empty);
            }
            else if (result == DialogResult.Cancel)
            {
                LogService.Info("Form1 closing: cancelled");
                e.Cancel = true;
            }
            else
            {
                LogService.Info("Form1 closing: discarded changes");
            }
        }
        else
        {
            LogService.Info("Form1 closing: no unsaved changes");
        }
    }

    #endregion
    #region 窗口交互

    //给所有可交互控件绑Enter事件 追踪最后焦点所在面板侧
    private void MarkPanelControls()
    {
        int count = 0;
        foreach (Control c in GetAllDescendants(this))
        {
            if (c is TextBox || c is NumericUpDown || c is TrackBar || c is CheckBox || c is ComboBox)
            {
                c.Enter += MarkFocusSide;
                count++;
            }
        }
        LogService.Debug($"MarkPanelControls: {count} controls bound");
    }

    private static IEnumerable<Control> GetAllDescendants(Control parent)
    {
        foreach (Control c in parent.Controls)
        {
            yield return c;
            foreach (Control child in GetAllDescendants(c))
                yield return child;
        }
    }

    //控件获得焦点时记录其在左半还是右半
    private void MarkFocusSide(object sender, EventArgs e)
    {
        if (sender is Control c)
        {
            var formX = this.PointToClient(c.PointToScreen(Point.Empty)).X;
            lastFocusLeft = formX < 525;
            LogService.DebugDebounce("focus_side", $"Focus side: {(lastFocusLeft ? "L" : "R")} ({c.GetType().Name})", 300);
        }
    }
    
    //无焦点控件时回退到lastFocusLeft 有控件时先更新再返回
    private bool IsControlOnLeft(Control ctrl)
    {
        if (ctrl != null)
        {
            var formX = this.PointToClient(ctrl.PointToScreen(Point.Empty)).X;
            lastFocusLeft = formX < 525;
        }
        return lastFocusLeft;
    }

    protected override void WndProc(ref Message m)
    {
        const int WM_HOTKEY = 0x0312;
        if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == hotkeyId)
        {
            try
            {
                bool mcvOrSelf = IsMcvForeground() || GetForegroundWindow() == this.Handle;
                if (this.WindowState == FormWindowState.Minimized || !this.Visible)
                {
                    if (!mcvOrSelf) return;
                    LogService.Debug("Hotkey Ctrl+T: restore from minimized to topmost");
                    this.Visible = true;
                    this.WindowState = FormWindowState.Normal;
                    ShowWindow(this.Handle, SW_RESTORE);
                    SetWindowPos(this.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
                    isTopmost = true;
                    this.Text = "Keyvalues Mangler™ 5000 [Topmost]";
                    this.Activate();
                }
                else if (isTopmost)
                {
                    LogService.Debug("Hotkey Ctrl+T: exit topmost");
                    SetWindowPos(this.Handle, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
                    if (mcvOrSelf)
                        this.WindowState = FormWindowState.Minimized;
                    isTopmost = false;
                    this.Text = "Keyvalues Mangler™ 5000";
                }
                else
                {
                    if (mcvOrSelf)
                    {
                        LogService.Debug("Hotkey Ctrl+T: minimize");
                        this.WindowState = FormWindowState.Minimized;
                        isTopmost = false;
                        this.Text = "Keyvalues Mangler™ 5000";
                    }
                    else
                    {
                        LogService.Debug("Hotkey Ctrl+T: set topmost");
                        SetWindowPos(this.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
                        isTopmost = true;
                        this.Text = "Keyvalues Mangler™ 5000 [Topmost]";
                        this.Activate();
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "Form1.WndProc hotkey handler");
            }
        }
        base.WndProc(ref m);
    }

    private static bool IsMcvForeground()
    {
        IntPtr fgw = GetForegroundWindow();
        if (fgw == IntPtr.Zero) return false;
        GetWindowThreadProcessId(fgw, out uint pid);
        try
        {
            using var p = Process.GetProcessById((int)pid);
            bool isMcv = p.ProcessName.Equals("mcv_x64", StringComparison.OrdinalIgnoreCase);
            LogService.Debug($"IsMcvForeground: {p.ProcessName} -> {isMcv}");
            return isMcv;
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "Form1.IsMcvForeground");
            return false;
        }
    }

    public void ScheduleUndo()
    {
        if (_undoInProgress || updatingControls || initializing) return;
        _undoPending = true;
        _undoTimer?.Stop();
        _undoTimer?.Start();
        LogService.DebugDebounce("schedule_undo", "ScheduleUndo", 200);
    }

    public void PushUndoNow()
    {
        _undoTimer?.Stop();
        _undoPending = false;
        PushUndo();
    }

    public void PushUndo(bool clearRedo = true)
    {
        if (_undoInProgress || currentWeaponLeft == null) return;
        _rapidStartLeft = null;
        _rapidStartRight = null;
        if (clearRedo) _redoStack.Clear();

        var entry = new UndoEntry
        {
            LeftScriptName = currentWeaponLeft?.ScriptName,
            RightScriptName = currentWeaponRight?.ScriptName,
            LeftData = new WeaponData(),
            RightData = new WeaponData(),
            ShowingAltStats = showingAltStats,
            AltMode = currentAltStatMode
        };
        SaveControlsToWeapon(entry.LeftData, true);
        SaveControlsToWeapon(entry.RightData, false);

        _undoStack.AddLast(entry);
        if (_undoStack.Count > MaxUndo) _undoStack.RemoveFirst();
        LogService.Debug($"PushUndo: stack={_undoStack.Count}, redo={_redoStack.Count}, altStats={showingAltStats}");
    }

    private void PopUndo()
    {
        if (_undoPending) { _undoTimer?.Stop(); PushUndo(); _undoPending = false; }
        if (_undoStack.Count == 0) return;
        _undoInProgress = true;
        try
        {
            var redoEntry = new UndoEntry
            {
                LeftScriptName = currentWeaponLeft?.ScriptName,
                RightScriptName = currentWeaponRight?.ScriptName,
                LeftData = new WeaponData(),
                RightData = new WeaponData(),
                ShowingAltStats = showingAltStats,
                AltMode = currentAltStatMode
            };
            SaveControlsToWeapon(redoEntry.LeftData, true);
            SaveControlsToWeapon(redoEntry.RightData, false);
            _redoStack.AddLast(redoEntry);

            var entry = _undoStack.Last!.Value;
            _undoStack.RemoveLast();
            RestoreUndoEntry(entry);
            LogService.Debug($"PopUndo: stack={_undoStack.Count}, redo={_redoStack.Count}");
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "Form1.PopUndo");
        }
        finally { _undoInProgress = false; }
    }

    private void PopRedo()
    {
        if (_undoPending) { _undoTimer?.Stop(); PushUndo(); _undoPending = false; }
        if (_redoStack.Count == 0) return;
        _undoInProgress = true;
        try
        {
            var entry = _redoStack.Last!.Value;
            _redoStack.RemoveLast();

            var undoEntry = new UndoEntry
            {
                LeftScriptName = currentWeaponLeft?.ScriptName,
                RightScriptName = currentWeaponRight?.ScriptName,
                LeftData = new WeaponData(),
                RightData = new WeaponData(),
                ShowingAltStats = showingAltStats,
                AltMode = currentAltStatMode
            };
            SaveControlsToWeapon(undoEntry.LeftData, true);
            SaveControlsToWeapon(undoEntry.RightData, false);
            _undoStack.AddLast(undoEntry);
            if (_undoStack.Count > MaxUndo) _undoStack.RemoveFirst();

            RestoreUndoEntry(entry);
            LogService.Debug($"PopRedo: stack={_undoStack.Count}, redo={_redoStack.Count}");
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "Form1.PopRedo");
        }
        finally { _undoInProgress = false; }
    }

    public void ClearRedo()
    {
        _redoStack.Clear();
    }

    public void ClearUndoHistory()
    {
        LogService.Debug("ClearUndoHistory");
        _undoStack.Clear();
        _redoStack.Clear();
        _rapidStartLeft = null;
        _rapidStartRight = null;
    }

    public void StoreSnapshot(bool? leftOnly = null)
    {
        if (initializing)
        {
            LogService.Debug("StoreSnapshot: skipped (initializing)");
            return;
        }
        bool updateLeft = leftOnly != false;
        bool updateRight = leftOnly != true;
        if (updateLeft)
        {
            _snapshotLeft = new WeaponData();
            SaveControlsToWeapon(_snapshotLeft, true);
        }
        if (updateRight)
        {
            _snapshotRight = new WeaponData();
            SaveControlsToWeapon(_snapshotRight, false);
        }
        LogService.Debug($"StoreSnapshot: updated (altStats={showingAltStats}, L={updateLeft}, R={updateRight})");
    }

    private void RestoreUndoEntry(UndoEntry entry)
    {
        LogService.Debug($"RestoreUndoEntry: L={entry.LeftScriptName}, R={entry.RightScriptName}, altStats={entry.ShowingAltStats}");
        if (!string.IsNullOrEmpty(entry.LeftScriptName))
        {
            var w = weapons.FirstOrDefault(x => x.ScriptName == entry.LeftScriptName);
            if (w != null && currentWeaponLeft?.ScriptName != w.ScriptName)
            {
                cmbWeaponsL.SelectedIndexChanged -= WeaponSelectedL;
                currentWeaponLeft = w;
                cmbWeaponsL.SelectedItem = w;
                cmbWeaponsL.SelectedIndexChanged += WeaponSelectedL;
            }
        }
        LoadWeaponToControls(entry.LeftData, true);

        if (!string.IsNullOrEmpty(entry.RightScriptName))
        {
            var w = weapons.FirstOrDefault(x => x.ScriptName == entry.RightScriptName);
            if (w != null && currentWeaponRight?.ScriptName != w.ScriptName)
            {
                cmbWeaponsR.SelectedIndexChanged -= WeaponSelectedR;
                currentWeaponRight = w;
                cmbWeaponsR.SelectedItem = w;
                cmbWeaponsR.SelectedIndexChanged += WeaponSelectedR;
            }
        }
        LoadWeaponToControls(entry.RightData, false);

        if (entry.ShowingAltStats != showingAltStats || entry.AltMode != currentAltStatMode)
        {
            showingAltStats = entry.ShowingAltStats;
            currentAltStatMode = entry.AltMode;
            if (showingAltStats) HighlightAltStatButton(currentAltStatMode);
            else ResetAltStatButtons();
        }

        StoreSnapshot();
        UpdateAllDamage();
        pnlSpread.Invalidate();
        pnlRecoil.Invalidate();
    }

    #endregion
    #region 复制

    private void CopyLeftToRight(object sender, EventArgs e)
    {
        if (currentWeaponLeft != null && currentWeaponRight != null)
        {
            LogService.Debug($"Copy L>R: {currentWeaponLeft.ScriptName} -> {currentWeaponRight.ScriptName}");
            PushUndo();
            var temp = new WeaponData();
            SaveControlsToWeapon(temp, true);
            LoadWeaponToControls(temp, false);
            UpdateAllDamage();
            pnlSpread.Invalidate();
            pnlRecoil.Invalidate();
        }
    }

    private void CopyRightToLeft(object sender, EventArgs e)
    {
        if (currentWeaponRight != null && currentWeaponLeft != null)
        {
            LogService.Debug($"Copy R>L: {currentWeaponRight.ScriptName} -> {currentWeaponLeft.ScriptName}");
            PushUndo();
            var temp = new WeaponData();
            SaveControlsToWeapon(temp, false);
            LoadWeaponToControls(temp, true);
            UpdateAllDamage();
            pnlSpread.Invalidate();
            pnlRecoil.Invalidate();
        }
    }

    //不拷贝ScriptName和PrintName防止覆盖武器身份
    private static void CopyWeaponDataFields(WeaponData src, WeaponData dst)
    {
        dst.DamageHeadMultiplier = src.DamageHeadMultiplier;
        dst.DamageChestMultiplier = src.DamageChestMultiplier;
        dst.DamageStomachMultiplier = src.DamageStomachMultiplier;
        dst.DamageLegMultiplier = src.DamageLegMultiplier;
        dst.DamageArmMultiplier = src.DamageArmMultiplier;
        dst.BulletSpread = src.BulletSpread;
        dst.BulletSpreadDegreesIronsighted = src.BulletSpreadDegreesIronsighted;
        dst.BulletSpreadDegreesBipod = src.BulletSpreadDegreesBipod;
        dst.BulletSpreadDegreesBipodIronsighted = src.BulletSpreadDegreesBipodIronsighted;
        dst.ViewSlideRecoilUp = src.ViewSlideRecoilUp;
        dst.ViewSlideRecoilRight = src.ViewSlideRecoilRight;
        dst.ViewSlideRecoilIronsightUp = src.ViewSlideRecoilIronsightUp;
        dst.ViewSlideRecoilIronsightRight = src.ViewSlideRecoilIronsightRight;
        dst.FireModes = src.FireModes;
        dst.FireRate = src.FireRate;
        dst.RangeModifier = src.RangeModifier;
        dst.ClipSize = src.ClipSize;
        dst.DefaultClip = src.DefaultClip;
        dst.ExtraBulletChamber = src.ExtraBulletChamber;
        dst.BulletsPerShot = src.BulletsPerShot;
        dst.SecondaryFireRate = src.SecondaryFireRate;
        dst.IronSight = src.IronSight;
        dst.IronsightSpeedScale = src.IronsightSpeedScale;
        dst.Weight = src.Weight;
        dst.ZMBuyPrice = src.ZMBuyPrice;
        dst.ZMWeight = src.ZMWeight;
        dst.MetalPenetrationDepth = src.MetalPenetrationDepth;
        dst.GlassPenetrationDepth = src.GlassPenetrationDepth;
        dst.ConcretePenetrationDepth = src.ConcretePenetrationDepth;
        dst.WoodPenetrationDepth = src.WoodPenetrationDepth;
        dst.OtherPenetrationDepth = src.OtherPenetrationDepth;
        dst.MetalDamageModifier = src.MetalDamageModifier;
        dst.GlassDamageModifier = src.GlassDamageModifier;
        dst.ConcreteDamageModifier = src.ConcreteDamageModifier;
        dst.WoodDamageModifier = src.WoodDamageModifier;
        dst.OtherDamageModifier = src.OtherDamageModifier;
        dst.CrouchSpreadMultiplier = src.CrouchSpreadMultiplier;
        dst.ProneSpreadMultiplier = src.ProneSpreadMultiplier;
        dst.StandMoveSpreadMultiplier = src.StandMoveSpreadMultiplier;
        dst.SneakMoveSpreadMultiplier = src.SneakMoveSpreadMultiplier;
        dst.CrouchMoveSpreadMultiplier = src.CrouchMoveSpreadMultiplier;
        dst.JumpSpreadMultiplier = src.JumpSpreadMultiplier;
        dst.DamageGeneric = src.DamageGeneric;
        dst.DovBulletSpreadDegreesBipod = src.DovBulletSpreadDegreesBipod;
        dst.DovBulletSpreadDegreesBipodIronsighted = src.DovBulletSpreadDegreesBipodIronsighted;
        dst.DovFireModes = src.DovFireModes;
    }

    #endregion
    #region 备选值联动同步

    private static WeaponData CloneTopLevelFields(WeaponData src)
    {
        var dst = new WeaponData();
        CopyWeaponDataFields(src, dst);
        return dst;
    }

    //保存顶层值后将备选值中与旧顶层值一致的字段同步到新顶层值
    private static void SyncAltStatsToMatchTopLevel(WeaponData oldW, WeaponData newW)
    {
        LogService.Debug($"SyncAltStatsToMatchTopLevel called for {newW.ScriptName}");
        //double
        SyncDoubleIfMatch(oldW.DamageGeneric, newW.DamageGeneric, newW.DovDamageGeneric, newW.ZombieDamageGeneric,
            (w, v) => w.DovDamageGeneric = v, (w, v) => w.ZombieDamageGeneric = v, newW);
        SyncDoubleIfMatch(oldW.DamageHeadMultiplier, newW.DamageHeadMultiplier, newW.DovDamageHeadMultiplier, newW.ZombieDamageHeadMultiplier,
            (w, v) => w.DovDamageHeadMultiplier = v, (w, v) => w.ZombieDamageHeadMultiplier = v, newW);
        SyncDoubleIfMatch(oldW.DamageChestMultiplier, newW.DamageChestMultiplier, newW.DovDamageChestMultiplier, newW.ZombieDamageChestMultiplier,
            (w, v) => w.DovDamageChestMultiplier = v, (w, v) => w.ZombieDamageChestMultiplier = v, newW);
        SyncDoubleIfMatch(oldW.DamageStomachMultiplier, newW.DamageStomachMultiplier, newW.DovDamageStomachMultiplier, newW.ZombieDamageStomachMultiplier,
            (w, v) => w.DovDamageStomachMultiplier = v, (w, v) => w.ZombieDamageStomachMultiplier = v, newW);
        SyncDoubleIfMatch(oldW.DamageLegMultiplier, newW.DamageLegMultiplier, newW.DovDamageLegMultiplier, newW.ZombieDamageLegMultiplier,
            (w, v) => w.DovDamageLegMultiplier = v, (w, v) => w.ZombieDamageLegMultiplier = v, newW);
        SyncDoubleIfMatch(oldW.DamageArmMultiplier, newW.DamageArmMultiplier, newW.DovDamageArmMultiplier, newW.ZombieDamageArmMultiplier,
            (w, v) => w.DovDamageArmMultiplier = v, (w, v) => w.ZombieDamageArmMultiplier = v, newW);
        SyncDoubleIfMatch(oldW.BulletSpread, newW.BulletSpread, newW.DovBulletSpread, newW.ZombieBulletSpread,
            (w, v) => w.DovBulletSpread = v, (w, v) => w.ZombieBulletSpread = v, newW);
        SyncDoubleIfMatch(oldW.BulletSpreadDegreesIronsighted, newW.BulletSpreadDegreesIronsighted, newW.DovBulletSpreadDegreesIronsighted, newW.ZombieBulletSpreadDegreesIronsighted,
            (w, v) => w.DovBulletSpreadDegreesIronsighted = v, (w, v) => w.ZombieBulletSpreadDegreesIronsighted = v, newW);
        SyncDoubleIfMatch(oldW.BulletSpreadDegreesBipod, newW.BulletSpreadDegreesBipod, newW.DovBulletSpreadDegreesBipod, newW.ZombieBulletSpreadDegreesBipod,
            (w, v) => w.DovBulletSpreadDegreesBipod = v, (w, v) => w.ZombieBulletSpreadDegreesBipod = v, newW);
        SyncDoubleIfMatch(oldW.BulletSpreadDegreesBipodIronsighted, newW.BulletSpreadDegreesBipodIronsighted, newW.DovBulletSpreadDegreesBipodIronsighted, newW.ZombieBulletSpreadDegreesBipodIronsighted,
            (w, v) => w.DovBulletSpreadDegreesBipodIronsighted = v, (w, v) => w.ZombieBulletSpreadDegreesBipodIronsighted = v, newW);
        SyncDoubleIfMatch(oldW.RangeModifier, newW.RangeModifier, newW.DovRangeModifier, newW.ZombieRangeModifier,
            (w, v) => w.DovRangeModifier = v, (w, v) => w.ZombieRangeModifier = v, newW);
        SyncDoubleIfMatch(oldW.IronsightSpeedScale, newW.IronsightSpeedScale, newW.DovIronsightSpeedScale, newW.ZombieIronsightSpeedScale,
            (w, v) => w.DovIronsightSpeedScale = v, (w, v) => w.ZombieIronsightSpeedScale = v, newW);
        SyncDoubleIfMatch(oldW.CrouchSpreadMultiplier, newW.CrouchSpreadMultiplier, newW.DovCrouchSpreadMultiplier, newW.ZombieCrouchSpreadMultiplier,
            (w, v) => w.DovCrouchSpreadMultiplier = v, (w, v) => w.ZombieCrouchSpreadMultiplier = v, newW);
        SyncDoubleIfMatch(oldW.ProneSpreadMultiplier, newW.ProneSpreadMultiplier, newW.DovProneSpreadMultiplier, newW.ZombieProneSpreadMultiplier,
            (w, v) => w.DovProneSpreadMultiplier = v, (w, v) => w.ZombieProneSpreadMultiplier = v, newW);
        SyncDoubleIfMatch(oldW.StandMoveSpreadMultiplier, newW.StandMoveSpreadMultiplier, newW.DovStandMoveSpreadMultiplier, newW.ZombieStandMoveSpreadMultiplier,
            (w, v) => w.DovStandMoveSpreadMultiplier = v, (w, v) => w.ZombieStandMoveSpreadMultiplier = v, newW);
        SyncDoubleIfMatch(oldW.SneakMoveSpreadMultiplier, newW.SneakMoveSpreadMultiplier, newW.DovSneakMoveSpreadMultiplier, newW.ZombieSneakMoveSpreadMultiplier,
            (w, v) => w.DovSneakMoveSpreadMultiplier = v, (w, v) => w.ZombieSneakMoveSpreadMultiplier = v, newW);
        SyncDoubleIfMatch(oldW.CrouchMoveSpreadMultiplier, newW.CrouchMoveSpreadMultiplier, newW.DovCrouchMoveSpreadMultiplier, newW.ZombieCrouchMoveSpreadMultiplier,
            (w, v) => w.DovCrouchMoveSpreadMultiplier = v, (w, v) => w.ZombieCrouchMoveSpreadMultiplier = v, newW);
        SyncDoubleIfMatch(oldW.JumpSpreadMultiplier, newW.JumpSpreadMultiplier, newW.DovJumpSpreadMultiplier, newW.ZombieJumpSpreadMultiplier,
            (w, v) => w.DovJumpSpreadMultiplier = v, (w, v) => w.ZombieJumpSpreadMultiplier = v, newW);
        SyncDoubleIfMatch(oldW.ViewSlideRecoilUp, newW.ViewSlideRecoilUp, newW.DovViewSlideRecoilUp, newW.ZombieViewSlideRecoilUp,
            (w, v) => w.DovViewSlideRecoilUp = v, (w, v) => w.ZombieViewSlideRecoilUp = v, newW);
        SyncDoubleIfMatch(oldW.ViewSlideRecoilRight, newW.ViewSlideRecoilRight, newW.DovViewSlideRecoilRight, newW.ZombieViewSlideRecoilRight,
            (w, v) => w.DovViewSlideRecoilRight = v, (w, v) => w.ZombieViewSlideRecoilRight = v, newW);
        SyncDoubleIfMatch(oldW.ViewSlideRecoilIronsightUp, newW.ViewSlideRecoilIronsightUp, newW.DovViewSlideRecoilIronsightUp, newW.ZombieViewSlideRecoilIronsightUp,
            (w, v) => w.DovViewSlideRecoilIronsightUp = v, (w, v) => w.ZombieViewSlideRecoilIronsightUp = v, newW);
        SyncDoubleIfMatch(oldW.ViewSlideRecoilIronsightRight, newW.ViewSlideRecoilIronsightRight, newW.DovViewSlideRecoilIronsightRight, newW.ZombieViewSlideRecoilIronsightRight,
            (w, v) => w.DovViewSlideRecoilIronsightRight = v, (w, v) => w.ZombieViewSlideRecoilIronsightRight = v, newW);
        SyncDoubleIfMatch(oldW.Weight, newW.Weight, newW.DovWeight, newW.ZombieWeight,
            (w, v) => w.DovWeight = v, (w, v) => w.ZombieWeight = v, newW);
        //int
        SyncIntIfMatch(oldW.FireRate, newW.FireRate, newW.DovFireRate, newW.ZombieFireRate,
            (w, v) => w.DovFireRate = v, (w, v) => w.ZombieFireRate = v, newW);
        SyncIntIfMatch(oldW.ExtraBulletChamber, newW.ExtraBulletChamber, newW.DovExtraBulletChamber, newW.ZombieExtraBulletChamber,
            (w, v) => w.DovExtraBulletChamber = v, (w, v) => w.ZombieExtraBulletChamber = v, newW);
        SyncIntIfMatch(oldW.SecondaryFireRate, newW.SecondaryFireRate, newW.DovSecondaryFireRate, newW.ZombieSecondaryFireRate,
            (w, v) => w.DovSecondaryFireRate = v, (w, v) => w.ZombieSecondaryFireRate = v, newW);
        SyncIntIfMatch(oldW.IronSight, newW.IronSight, newW.DovIronSight, newW.ZombieIronSight,
            (w, v) => w.DovIronSight = v, (w, v) => w.ZombieIronSight = v, newW);
        SyncIntIfMatch(oldW.DefaultClip, newW.DefaultClip, newW.DovDefaultClip, newW.ZombieDefaultClip,
            (w, v) => w.DovDefaultClip = v, (w, v) => w.ZombieDefaultClip = v, newW);
        SyncIntIfMatch(oldW.BulletsPerShot, newW.BulletsPerShot, newW.DovBulletsPerShot, newW.ZombieBulletsPerShot,
            (w, v) => w.DovBulletsPerShot = v, (w, v) => w.ZombieBulletsPerShot = v, newW);
        //string
        SyncStrIfMatch(oldW.ClipSize, newW.ClipSize, newW.DovClipSize, newW.ZombieClipSize,
            (w, v) => w.DovClipSize = v, (w, v) => w.ZombieClipSize = v, newW);
        SyncStrIfMatch(oldW.FireModes, newW.FireModes, newW.DovFireModes, newW.ZombieFireModes,
            (w, v) => w.DovFireModes = v, (w, v) => w.ZombieFireModes = v, newW);
    }

    private static void SyncDoubleIfMatch(double? oldVal, double? newVal,
        double? dov, double? zombie,
        Action<WeaponData, double?> setDov, Action<WeaponData, double?> setZombie,
        WeaponData w)
    {
        if (dov.HasValue && oldVal.HasValue && Math.Abs(dov.Value - oldVal.Value) < 0.001)
        {
            LogService.Debug($"SyncDoubleIfMatch: clearing Dov (old={oldVal}, dov={dov})");
            setDov(w, null);
        }
        if (zombie.HasValue && oldVal.HasValue && Math.Abs(zombie.Value - oldVal.Value) < 0.001)
        {
            LogService.Debug($"SyncDoubleIfMatch: clearing Zombie (old={oldVal}, zombie={zombie})");
            setZombie(w, null);
        }
    }

    private static void SyncIntIfMatch(int? oldVal, int? newVal,
        int? dov, int? zombie,
        Action<WeaponData, int?> setDov, Action<WeaponData, int?> setZombie,
        WeaponData w)
    {
        if (dov.HasValue && oldVal.HasValue && dov.Value == oldVal.Value)
        {
            LogService.Debug($"SyncIntIfMatch: clearing Dov (old={oldVal}, dov={dov})");
            setDov(w, null);
        }
        if (zombie.HasValue && oldVal.HasValue && zombie.Value == oldVal.Value)
        {
            LogService.Debug($"SyncIntIfMatch: clearing Zombie (old={oldVal}, zombie={zombie})");
            setZombie(w, null);
        }
    }

    private static void SyncStrIfMatch(string oldVal, string newVal,
        string dov, string zombie,
        Action<WeaponData, string> setDov, Action<WeaponData, string> setZombie,
        WeaponData w)
    {
        if (!string.IsNullOrEmpty(dov) && string.Equals(dov, oldVal, StringComparison.OrdinalIgnoreCase))
        {
            LogService.Debug($"SyncStrIfMatch: clearing Dov (old={oldVal}, dov={dov})");
            setDov(w, null);
        }
        if (!string.IsNullOrEmpty(zombie) && string.Equals(zombie, oldVal, StringComparison.OrdinalIgnoreCase))
        {
            LogService.Debug($"SyncStrIfMatch: clearing Zombie (old={oldVal}, zombie={zombie})");
            setZombie(w, null);
        }
    }

    #endregion
    #region 杂项

    private static void EnableDoubleBuffering(Control control)
    {
        typeof(Control).InvokeMember("DoubleBuffered",
            BindingFlags.SetProperty | BindingFlags.Instance | BindingFlags.NonPublic,
            null, control, new object[] { true });
    }

    private static bool SystemUsesDarkMode()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int intVal && intVal == 0)
            {
                LogService.Info("DarkMode: detected via Windows registry");
                return true;
            }
        }
        catch (Exception ex) { LogService.Info($"DarkMode: Windows registry check failed: {ex.Message}"); }

        //下面这些真的会有人用到吗
        string gtkTheme = Environment.GetEnvironmentVariable("GTK_THEME") ?? "";
        if (!string.IsNullOrEmpty(gtkTheme))
        {
            LogService.Info($"DarkMode: GTK_THEME={gtkTheme}");
            if (gtkTheme.Contains("dark", StringComparison.OrdinalIgnoreCase))
            {
                LogService.Info("DarkMode: detected via GTK_THEME");
                return true;
            }
        }
        else
        {
            LogService.Info("DarkMode: GTK_THEME not set, trying config files");
        }

        var homes = new[] {
            Environment.GetEnvironmentVariable("HOME") ?? "",
            "/home/" + (Environment.GetEnvironmentVariable("USER") ?? ""),
            "/home/" + (Environment.GetEnvironmentVariable("LOGNAME") ?? "")
        }.Where(h => !string.IsNullOrEmpty(h)).Distinct().ToList();

        LogService.Info($"DarkMode: trying {homes.Count} home paths: [{string.Join(", ", homes)}]");

        if (TryDetectLinuxDark(homes,
            new[] { ".config/gtk-4.0/settings.ini", ".config/gtk-3.0/settings.ini" },
            "[Settings]", "gtk-theme-name", out string gtkSource))
        {
            LogService.Info($"DarkMode: detected via {gtkSource}");
            return true;
        }

        if (TryDetectLinuxDark(homes,
            new[] { ".config/xfce4/xfconf/xfce-perchannel-xml/xsettings.xml" },
            "", "ThemeName", out string xfceSource, isXml: true))
        {
            LogService.Info($"DarkMode: detected via {xfceSource}");
            return true;
        }

        if (TryDetectKdeDark(homes))
        {
            LogService.Info("DarkMode: detected via KDE activeBackground");
            return true;
        }

        LogService.Info("DarkMode: not detected");
        return false;
    }

    private static bool TryDetectLinuxDark(List<string> homes, string[] relativePaths,
        string section, string keyName, out string source, bool isXml = false)
    {
        source = "";
        try
        {
            foreach (string home in homes)
            {
                foreach (string relPath in relativePaths)
                {
                    string path = System.IO.Path.Combine(home, relPath);
                    if (!File.Exists(path))
                    {
                        LogService.Info($"DarkMode: config not found: {path}");
                        continue;
                    }

                    LogService.Info($"DarkMode: reading {path}");
                    string value = isXml
                        ? ExtractXmlValue(path, keyName)
                        : ExtractIniValue(path, section, keyName);

                    if (!string.IsNullOrEmpty(value))
                    {
                        LogService.Info($"DarkMode: {System.IO.Path.GetFileName(path)} {keyName}={value}");
                        if (value.Contains("dark", StringComparison.OrdinalIgnoreCase))
                        {
                            source = path;
                            return true;
                        }
                    }
                }
            }
        }
        catch (Exception ex) { LogService.Info($"DarkMode: config check failed: {ex.Message}"); }
        return false;
    }

    private static string ExtractIniValue(string path, string section, string key)
    {
        bool inSection = false;
        foreach (string line in File.ReadLines(path))
        {
            string trimmed = line.Trim();
            if (trimmed.Equals(section, StringComparison.OrdinalIgnoreCase))
            { inSection = true; continue; }
            if (inSection && trimmed.StartsWith("[")) break;
            if (inSection && trimmed.StartsWith(key, StringComparison.OrdinalIgnoreCase))
            {
                string[] parts = trimmed.Split('=');
                return parts.Length >= 2 ? parts[1].Trim() : "";
            }
        }
        return "";
    }

    private static string ExtractXmlValue(string path, string key)
    {
        string content = File.ReadAllText(path);
        var match = System.Text.RegularExpressions.Regex.Match(content,
            $@"<property\s+name=""{key}""[^>]*>\s*<value[^>]*>\s*([^<]+)");
        return match.Success ? match.Groups[1].Value.Trim() : "";
    }

    private static bool TryDetectKdeDark(List<string> homes)
    {
        try
        {
            foreach (string home in homes)
            {
                string path = System.IO.Path.Combine(home, ".config", "kdeglobals");
                if (!File.Exists(path))
                {
                    LogService.Info($"DarkMode: KDE config not found: {path}");
                    continue;
                }

                LogService.Info($"DarkMode: reading {path}");
                string color = ExtractIniValue(path, "[WM]", "activeBackground");
                if (string.IsNullOrEmpty(color))
                {
                    LogService.Info("DarkMode: KDE activeBackground not found");
                    continue;
                }

                LogService.Info($"DarkMode: KDE activeBackground={color}");
                string[] rgb = color.Split(',');
                if (rgb.Length == 3 &&
                    int.TryParse(rgb[0], out int r) &&
                    int.TryParse(rgb[1], out int g) &&
                    int.TryParse(rgb[2], out int b) &&
                    (r + g + b) / 3.0 < 120)
                    return true;
            }
        }
        catch (Exception ex) { LogService.Info($"DarkMode: KDE check failed: {ex.Message}"); }
        return false;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    private void SetTitleBarDark()
    {
        try
        {
            int useDark = 1;
            DwmSetWindowAttribute(this.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int));
        }
        catch { }
    }

    private void ApplyDarkMode()
    {
        this.BackColor = Color.FromArgb(32, 32, 32);
        this.ForeColor = Color.FromArgb(240, 240, 240);

        foreach (Control c in GetAllDescendants(this))
        {
            if (c is Label lbl)
            {
                if (lbl == lblC64_1 || lbl == lblC64_2 || lbl == lblC64_3) continue;
                if (lbl.ForeColor == Color.DarkRed)
                    lbl.ForeColor = Color.FromArgb(255, 100, 100);
                else
                    lbl.ForeColor = Color.FromArgb(240, 240, 240);
            }
            else if (c is Button btn)
            {
                btn.BackColor = Color.FromArgb(60, 60, 60);
                btn.ForeColor = Color.FromArgb(240, 240, 240);
                btn.FlatStyle = FlatStyle.Flat;
                btn.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80);
            }
            else if (c is TextBox txt)
            {
                txt.BackColor = Color.FromArgb(50, 50, 50);
                txt.ForeColor = Color.FromArgb(240, 240, 240);
            }
            else if (c is NumericUpDown nud)
            {
                nud.BackColor = Color.FromArgb(50, 50, 50);
                nud.ForeColor = Color.FromArgb(240, 240, 240);
            }
            else if (c is ComboBox cmb)
            {
                cmb.BackColor = Color.FromArgb(50, 50, 50);
                cmb.ForeColor = Color.FromArgb(240, 240, 240);
            }
            else if (c is TrackBar tb)
            {
                tb.BackColor = Color.FromArgb(32, 32, 32);
            }
            else if (c is GroupBox gb)
            {
                gb.ForeColor = Color.FromArgb(200, 200, 200);
            }
            else if (c is CheckBox chk)
            {
                chk.ForeColor = Color.FromArgb(240, 240, 240);
            }
            else if (c is Panel pnl)
            {
                if (pnl == pnlSpread || pnl == pnlRecoil) continue;
                pnl.BackColor = Color.FromArgb(40, 40, 40);
            }
        }
        _darkMode = true;
        SetTitleBarDark();
    }

    private void PnlSpread_Paint(object sender, PaintEventArgs e)
    {
        bool leftAds = nudIronSightL.Value != 0;
        bool rightAds = nudIronSightR.Value != 0;
        spreadRenderer.DrawSpread(e.Graphics, currentWeaponLeft, currentWeaponRight,
            (double)nudHipSpreadL.Value, leftAds ? (double)nudAdsSpreadL.Value : 0,
            (double)nudBipodHipSpreadL.Value, leftAds ? (double)nudBipodAdsSpreadL.Value : 0,
            currentWeaponRight != null ? (double)nudHipSpreadR.Value : 1.0,
            currentWeaponRight != null && rightAds ? (double)nudAdsSpreadR.Value : 1.0,
            currentWeaponRight != null ? (double)nudBipodHipSpreadR.Value : 0,
            currentWeaponRight != null && rightAds ? (double)nudBipodAdsSpreadR.Value : 0);
    }

    private void PnlRecoil_Paint(object sender, PaintEventArgs e)
    {
        bool leftAds = nudIronSightL.Value != 0;
        bool rightAds = nudIronSightR.Value != 0;
        float maxScale = (showingAltStats && currentAltStatMode == WeaponScriptService.AltStatMode.Dov) ? 1.25f : 2.5f;
        recoilRenderer.DrawRecoil(e.Graphics, currentWeaponLeft, currentWeaponRight,
            (double)nudHipRecoilUpL.Value, (double)nudHipRecoilRightL.Value,
            leftAds ? (double)nudAdsRecoilUpL.Value : 0, leftAds ? (double)nudAdsRecoilRightL.Value : 0,
            currentWeaponRight != null ? (double)nudHipRecoilUpR.Value : 0,
            currentWeaponRight != null ? (double)nudHipRecoilRightR.Value : 0,
            currentWeaponRight != null && rightAds ? (double)nudAdsRecoilUpR.Value : 0,
            currentWeaponRight != null && rightAds ? (double)nudAdsRecoilRightR.Value : 0,
            maxScale);
    }

    public class LogForm : Form
    {
        public LogForm(string title, string logText, bool darkMode = false)
        {
            this.Text = title;
            this.Size = new Size(320, 240);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MinimizeBox = false;
            this.MaximizeBox = false;
            this.TopMost = true;
            var txt = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = new Font("Consolas", 9), Text = logText.Replace("\n", "\r\n") };
            if (darkMode)
            {
                this.BackColor = Color.FromArgb(32, 32, 32);
                this.ForeColor = Color.FromArgb(240, 240, 240);
                txt.BackColor = Color.FromArgb(50, 50, 50);
                txt.ForeColor = Color.FromArgb(240, 240, 240);
                this.Shown += (_, _) =>
                {
                    try
                    {
                        int useDark = 1;
                        DwmSetWindowAttribute(this.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int));
                    }
                    catch { }
                };
            }
            this.Controls.Add(txt);
        }
    }
    #endregion
}