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
    private const int MaxUndo = 50;

    private PanelRenderer spreadRenderer = null!;
    private PanelRenderer recoilRenderer = null!;

    private bool lastFocusLeft = true;

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

            this.KeyPreview = true;
            this.KeyDown += Form1_KeyDown;
            this.FormClosing += Form1_FormClosing;

            this.Shown += (s, e) =>
            {
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

        btnCsvToScripts = new Button { Text = "CSV>Scripts", Location = new Point(cx + 61, 6), Size = new Size(88, 26) };
        btnCsvToScripts.Click += BtnCsvToScripts_Click;
        this.Controls.Add(btnCsvToScripts);

        btnScriptsToCsv = new Button { Text = "Scripts>CSV", Location = new Point(cx + 151, 6), Size = new Size(88, 26) };
        btnScriptsToCsv.Click += BtnScriptsToCsv_Click;
        this.Controls.Add(btnScriptsToCsv);

        var btnRefresh = new Button { Text = "Rfsh", Location = new Point(cx + 241, 6), Size = new Size(59, 26) };
        btnRefresh.Click += BtnRefresh_Click;
        this.Controls.Add(btnRefresh);

        var btnCopy = new Button { Text = "L>R", Location = new Point(cx + 22, 620), Size = new Size(48, 24) };
        btnCopy.Click += CopyLeftToRight;
        this.Controls.Add(btnCopy);

        //glory to our coders all i dont need to write a hook myself but just call a cvar
        var btnCopyCvar = new Button { Text = "wpn_reload_script all", Location = new Point(cx + 72, 620), Size = new Size(154, 24) };
        btnCopyCvar.Tag = false;
        btnCopyCvar.Click += BtnQuickExport_Click;
        btnCopyCvar.MouseLeave += (s, e) => CancelConfirm(btnCopyCvar);
        btnCopyCvar.MouseUp += (s, e) =>
        {
            if (e.Button == MouseButtons.Right) CancelConfirm(btnCopyCvar);
        };
        this.Controls.Add(btnCopyCvar);
        
        var btnConvertToTemplate = new Button { Text = "Tmpl", Location = new Point(cx + 22, 644), Size = new Size(48, 24) };
        btnConvertToTemplate.Click += BtnConvertToTemplate_Click;
        this.Controls.Add(btnConvertToTemplate);

        var btnToggleDov = new Button { Text = "DoV", Location = new Point(cx + 72, 644), Size = new Size(77, 24), BackColor = SystemColors.Control };
        btnToggleDov.Click += (s, e) => ToggleAltStats(WeaponScriptService.AltStatMode.Dov);
        this.Controls.Add(btnToggleDov);

        var btnToggleZombie = new Button { Text = "Zmb", Location = new Point(cx + 149, 644), Size = new Size(77, 24), BackColor = SystemColors.Control };
        btnToggleZombie.Click += (s, e) => ToggleAltStats(WeaponScriptService.AltStatMode.Zombie);
        this.Controls.Add(btnToggleZombie);

        var btnCopyR = new Button { Text = "L<R", Location = new Point(cx + 228, 620), Size = new Size(48, 24) };
        btnCopyR.Click += CopyRightToLeft;
        this.Controls.Add(btnCopyR);

        var btnWiki = new Button { Text = "Wiki", Location = new Point(cx + 228, 644), Size = new Size(48, 24) };
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
        bool leftDirty = currentWeaponLeft != null && HasUnsavedChanges(true);
        bool rightDirty = currentWeaponRight != null && HasUnsavedChanges(false);
        if (leftDirty || rightDirty)
        {
            LogService.Debug($"FormClosing: unsaved changes (L={leftDirty}, R={rightDirty}), prompting user");
            var result = MessageBox.Show("Unsaved changes will be lost. Save now?",
                "Unsaved Changes", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
            if (result == DialogResult.Yes)
            {
                LogService.Debug("FormClosing: user chose Save");
                BtnSave_Click(this, EventArgs.Empty);
            }
            else if (result == DialogResult.Cancel)
            {
                LogService.Debug("FormClosing: user cancelled");
                e.Cancel = true;
            }
            else
            {
                LogService.Debug("FormClosing: user chose Discard");
            }
        }
    }

    #endregion
    #region 窗口交互

    //给所有可交互控件绑Enter事件 追踪最后焦点所在面板侧
    private void MarkPanelControls()
    {
        foreach (Control c in GetAllDescendants(this))
        {
            if (c is TextBox || c is NumericUpDown || c is TrackBar || c is CheckBox || c is ComboBox)
            {
                c.Enter += MarkFocusSide;
            }
        }
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
            return p.ProcessName.Equals("mcv_x64", StringComparison.OrdinalIgnoreCase);
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

    public void StoreSnapshot()
    {
        if (initializing)
        {
            LogService.Debug("StoreSnapshot: skipped (initializing)");
            return;
        }
        _snapshotLeft = new WeaponData();
        _snapshotRight = new WeaponData();
        SaveControlsToWeapon(_snapshotLeft, true);
        SaveControlsToWeapon(_snapshotRight, false);
        LogService.Debug($"StoreSnapshot: updated (altStats={showingAltStats})");
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
    #region 杂项

    private static void EnableDoubleBuffering(Control control)
    {
        typeof(Control).InvokeMember("DoubleBuffered",
            BindingFlags.SetProperty | BindingFlags.Instance | BindingFlags.NonPublic,
            null, control, new object[] { true });
    }

    #nullable enable
    //放下面也会爆warn 还有虽然拆分了 下面这些还是没必要单独建个文件

    private void PnlSpread_Paint(object? sender, PaintEventArgs e)
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

    private void PnlRecoil_Paint(object? sender, PaintEventArgs e)
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
        public LogForm(string title, string logText)
        {
            this.Text = title;
            this.Size = new Size(320, 240);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MinimizeBox = false;
            this.MaximizeBox = false;
            this.TopMost = true;
            var txt = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = new Font("Consolas", 9), Text = logText.Replace("\n", "\r\n") };
            this.Controls.Add(txt);
        }
    }
    #endregion
}