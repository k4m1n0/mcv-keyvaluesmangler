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
    private List<WeaponData> rgWeapons = null!;
    private WeaponData? wCurrentLeft = null;
    private WeaponData? wCurrentRight = null;
    private static string? sLastWikiUser = null;

    private bool bUpdatingControls = false;
    private bool bInitializing = true;

    private const double dSliderMin = 0.0;
    private const double dSliderMax = 7.5;
    private const double dSliderStep = 0.01;
    private const double dDistanceDivisor = 12.7;//本来500HU=31.25英尺 但MCV的好像不一样 sb英制单位

    private string sLastScriptsDir = "";
    private bool bRefreshing = false;
    private int iSaveLock = 0;
    private bool bIsTopmost = false;
    private bool bShowingAltStats = false;
    private WeaponScriptService.AltStatMode amCurrentAltStat = WeaponScriptService.AltStatMode.Dov;

    private string? sRapidStartLeft = null;
    private string? sRapidStartRight = null;
    private DateTime dtRapidDeadlineL;
    private DateTime dtRapidDeadlineR;
    private const int iRapidSettleMs = 300;

    private PanelRenderer prSpreadRenderer = null!;
    private PanelRenderer prRecoilRenderer = null!;

    private const int iCenterX = 525;
    private bool bLastFocusLeft = true;
    public static bool bForceDarkMode = false;
    public static bool bForceLightMode = false;
    private bool bDarkMode = false;

    private const int iHotkeyId = 9008;//唯一热键id

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

            string sCsvPath = Path.Combine(AppContext.BaseDirectory, "weapons.csv");
            rgWeapons = File.Exists(sCsvPath) ? CsvService.LoadWeapons(sCsvPath) : new List<WeaponData>();
            LogService.Info($"Weapons loaded: {rgWeapons.Count}");

            tmrUndo = new System.Windows.Forms.Timer { Interval = 300 };
            tmrUndo.Tick += (_, _) => { tmrUndo.Stop(); if (bUndoPending) { bUndoPending = false; PushUndo(); } };

            InitCenterPanels();
            InitLeftPanel();
            InitRightPanel();
            InitC64Labels();
            StartC64Anim();
            InitTopButtons();
            MarkPanelControls();
            if (SystemUsesDarkMode())
                ApplyDarkMode();
                
            this.KeyPreview = true;
            this.KeyDown += Form1_KeyDown;
            this.FormClosing += Form1_FormClosing;

            this.Shown += (s, e) =>
            {
                var scr = Screen.FromControl(this);
                float fScale = Math.Min((float)scr.WorkingArea.Width / 1366f, (float)scr.WorkingArea.Height / 768f);
                fScale = fScale < 1.0f ? Math.Max(fScale, 1280f / 1366f) : 1.0f;
                this.Scale(new SizeF(fScale, fScale));
                int iW = (int)(1366 * fScale), iH = (int)(768 * fScale);
                this.Size = new Size(Math.Min(iW, scr.WorkingArea.Width), Math.Min(iH, scr.WorkingArea.Height));

                bool bNeedDrag = scr.Bounds.Width < 1280 || scr.Bounds.Height < 720;
                LogService.Info($"Scale: {fScale:F3}, screen: {scr.WorkingArea.Width}x{scr.WorkingArea.Height}, needDrag={bNeedDrag}");
                if (bNeedDrag)
                {
                    var pnlScroll = new Panel { Location = Point.Empty, Size = new Size(iW, iH), AutoScroll = true };
                    EnableDoubleBuffering(pnlScroll);
                    foreach (var ctrl in this.Controls.Cast<Control>().ToList()) { this.Controls.Remove(ctrl); pnlScroll.Controls.Add(ctrl); }
                    this.Controls.Add(pnlScroll);
                    //收缩panel至实际控件底部 消除多余空白
                    int iBottom = 0;
                    foreach (Control ctrl in pnlScroll.Controls) iBottom = Math.Max(iBottom, ctrl.Bottom);
                    if (iBottom < pnlScroll.Height) pnlScroll.Height = iBottom;

                    bool bDragging = false;
                    Point ptLast = Point.Empty, ptOff = Point.Empty;
                    int iMx = Math.Min(this.ClientSize.Width - pnlScroll.Width, 0), iMy = Math.Min(this.ClientSize.Height - pnlScroll.Height, 0);
                    void Bind(Control pCtrl)
                    {
                        pCtrl.MouseDown += (_, me) => { bDragging = true; ptLast = pCtrl.PointToScreen(me.Location); };
                        pCtrl.MouseUp += (_, _) => bDragging = false;
                        pCtrl.MouseMove += (_, me) =>
                        {
                            if (!bDragging) return;
                            var ptCur = pCtrl.PointToScreen(me.Location);
                            ptOff.X = Math.Clamp(ptOff.X + ptCur.X - ptLast.X, iMx, 0);
                            ptOff.Y = Math.Clamp(ptOff.Y + ptCur.Y - ptLast.Y, iMy, 0);
                            pnlScroll.Location = ptOff;
                            ptLast = ptCur;
                        };
                        foreach (Control ctrlChild in pCtrl.Controls) Bind(ctrlChild);
                    }
                    Bind(pnlScroll);
                    //跨屏幕拖动时重算可拖动范围
                    this.Resize += (_, _) =>
                    {
                        iMx = Math.Min(this.ClientSize.Width - pnlScroll.Width, 0);
                        iMy = Math.Min(this.ClientSize.Height - pnlScroll.Height, 0);
                        ptOff.X = Math.Clamp(ptOff.X, iMx, 0);
                        ptOff.Y = Math.Clamp(ptOff.Y, iMy, 0);
                        pnlScroll.Location = ptOff;
                    };
                }

                var sOriginalTitle = this.Text;
                this.Text = "Keyvalues Mangler™ 5000 - Ctrl+T to toggle topmost/minimize";
                var tmrTitle = new System.Windows.Forms.Timer { Interval = 1919 };
                tmrTitle.Tick += (_, _) => { this.Text = sOriginalTitle; tmrTitle.Stop(); tmrTitle.Dispose(); };
                tmrTitle.Start();

                if (rgWeapons.Count > 0)
                {
                    cmbWeaponsL.SelectedIndexChanged -= (s, ev) => WeaponSelected(true, s, ev);
                    cmbWeaponsR.SelectedIndexChanged -= (s, ev) => WeaponSelected(false, s, ev);

                    cmbWeaponsL.DataSource = null;
                    cmbWeaponsL.DataSource = new List<WeaponData>(rgWeapons);
                    cmbWeaponsL.DisplayMember = "PrintName";
                    cmbWeaponsL.SelectedIndex = 0;

                    cmbWeaponsR.DataSource = null;
                    cmbWeaponsR.DataSource = new List<WeaponData>(rgWeapons);
                    cmbWeaponsR.DisplayMember = "PrintName";
                    cmbWeaponsR.SelectedIndex = 0;

                    cmbWeaponsL.SelectedIndexChanged += (s, ev) => WeaponSelected(true, s, ev);
                    cmbWeaponsR.SelectedIndexChanged += (s, ev) => WeaponSelected(false, s, ev);

                    bInitializing = false;
                    if (cmbWeaponsL.SelectedItem is WeaponData wL)
                    {
                        wCurrentLeft = wL;
                        LoadWeaponToControls(wL, true);
                    }
                    if (cmbWeaponsR.SelectedItem is WeaponData wR)
                    {
                        wCurrentRight = wR;
                        LoadWeaponToControls(wR, false);
                    }
                    StoreSnapshot();
                    UpdateAllDamage();
                    pnlSpread.Invalidate();
                    pnlRecoil.Invalidate();

                    UpdateC64Labels(true);
                }
                bInitializing = false;
                RegisterHotKey(this.Handle, iHotkeyId, MOD_CONTROL, VK_T);
                LogService.Info("Form1 ready");
            };

            this.FormClosed += (s, e) => UnregisterHotKey(this.Handle, iHotkeyId);
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
        pnlSpread = new Panel { Location = new Point(iCenterX, 38), Size = new Size(300, 300), BorderStyle = BorderStyle.FixedSingle, BackColor = Color.Black };
        EnableDoubleBuffering(pnlSpread);
        pnlSpread.Paint += PnlSpread_Paint;
        this.Controls.Add(pnlSpread);

        pnlRecoil = new Panel { Location = new Point(iCenterX, 313), Size = new Size(300, 300), BorderStyle = BorderStyle.FixedSingle, BackColor = Color.Black };
        EnableDoubleBuffering(pnlRecoil);
        pnlRecoil.Paint += PnlRecoil_Paint;
        this.Controls.Add(pnlRecoil);

        prSpreadRenderer = new PanelRenderer(pnlSpread);
        prRecoilRenderer = new PanelRenderer(pnlRecoil);
    }

    private void InitTopButtons()
    {
        btnSave = new Button { Text = "Save", Location = new Point(iCenterX, 6), Size = new Size(58, 26) };
        btnSave.Click += BtnSave_Click;
        this.Controls.Add(btnSave);

        btnCsvToScripts = new Button { Text = "CSV>Script", Location = new Point(iCenterX + 60, 6), Size = new Size(89, 26) };
        btnCsvToScripts.Click += BtnCsvToScripts_Click;
        this.Controls.Add(btnCsvToScripts);

        btnScriptsToCsv = new Button { Text = "Script>CSV", Location = new Point(iCenterX + 151, 6), Size = new Size(89, 26) };
        btnScriptsToCsv.Click += BtnScriptsToCsv_Click;
        this.Controls.Add(btnScriptsToCsv);

        btnRefresh = new Button { Text = "Rfsh", Location = new Point(iCenterX + 242, 6), Size = new Size(58, 26) };
        btnRefresh.Click += BtnRefresh_Click;
        this.Controls.Add(btnRefresh);

        var btnCopy = new Button { Text = "L>R", Location = new Point(iCenterX + 22, 618), Size = new Size(48, 26) };
        btnCopy.Click += CopyLeftToRight;
        this.Controls.Add(btnCopy);

        //glory to our coders all i dont need to write a hook myself but just call a cvar
        btnQuickExport = new Button { Text = "wpn_reload_script all", Location = new Point(iCenterX + 73, 618), Size = new Size(152, 26) };
        btnQuickExport.Tag = false;
        btnQuickExport.Click += BtnQuickExport_Click;
        btnQuickExport.MouseLeave += (s, e) => CancelConfirm(btnQuickExport);
        btnQuickExport.MouseUp += (s, e) =>
        {
            if (e.Button == MouseButtons.Right) CancelConfirm(btnQuickExport);
        };
        this.Controls.Add(btnQuickExport);
        
        var btnConvertToTemplate = new Button { Text = "Tmpl", Location = new Point(iCenterX + 22, 646), Size = new Size(48, 26) };
        btnConvertToTemplate.Click += BtnConvertToTemplate_Click;
        this.Controls.Add(btnConvertToTemplate);

        var btnToggleDov = new Button { Text = "DoV", Location = new Point(iCenterX + 73, 646), Size = new Size(75, 26), BackColor = SystemColors.Control };
        btnToggleDov.Click += (s, e) => ToggleAltStats(WeaponScriptService.AltStatMode.Dov);
        this.Controls.Add(btnToggleDov);

        var btnToggleZombie = new Button { Text = "Zmb", Location = new Point(iCenterX + 150, 646), Size = new Size(75, 26), BackColor = SystemColors.Control };
        btnToggleZombie.Click += (s, e) => ToggleAltStats(WeaponScriptService.AltStatMode.Zombie);
        this.Controls.Add(btnToggleZombie);

        var btnCopyR = new Button { Text = "L<R", Location = new Point(iCenterX + 228, 618), Size = new Size(48, 26) };
        btnCopyR.Click += CopyRightToLeft;
        this.Controls.Add(btnCopyR);

        var btnWiki = new Button { Text = "Wiki", Location = new Point(iCenterX + 228, 646), Size = new Size(48, 26) };
        btnWiki.Click += BtnWiki_Click;
        this.Controls.Add(btnWiki);

        var ttTooltip = new ToolTip();
        ttTooltip.SetToolTip(btnSave, "Save current weapon data to CSV (Ctrl+S)\nCtrl+Z/Y to undo/redo");
        ttTooltip.SetToolTip(btnCsvToScripts, "Export CSV data to weapon script files");
        ttTooltip.SetToolTip(btnScriptsToCsv, "Import weapon script files to CSV");
        ttTooltip.SetToolTip(btnRefresh, "Reload weapon list from CSV (Ctrl+R)");
        ttTooltip.SetToolTip(btnCopy, "Copy left panel values to right");
        ttTooltip.SetToolTip(btnQuickExport, "Quick export: save CSV and export to scripts (Ctrl+Shift+S)\nRight-click to cancel");
        ttTooltip.SetToolTip(btnConvertToTemplate, "Convert old scripts to preset_file template format");
        ttTooltip.SetToolTip(btnToggleDov, "Toggle Day of Victory alternate stats");
        ttTooltip.SetToolTip(btnToggleZombie, "Toggle Zombie Mode alternate stats");
        ttTooltip.SetToolTip(btnCopyR, "Copy right panel values to left");
        ttTooltip.SetToolTip(btnWiki, "Open Wiki Stats Updater");
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

    private void Form1_FormClosing(object? sender, FormClosingEventArgs e)
    {
        //结束未完成的rapid/pending 保存当前状态
        if (bUndoPending || sRapidStartLeft != null || sRapidStartRight != null)
        {
            tmrUndo?.Stop();
            bUndoPending = false;
            PushUndo();
        }
        tmrSnapshotCheck?.Stop(); tmrSnapshotCheck?.Dispose(); tmrSnapshotCheck = null;
        bool bLeftDirty = wCurrentLeft != null && HasUnsavedChanges(true, bCheckBothSides: true);
        bool bRightDirty = wCurrentRight != null && HasUnsavedChanges(false, bCheckBothSides: true);
        if (bLeftDirty || bRightDirty)
        {
            LogService.Debug($"FormClosing: unsaved changes (L={bLeftDirty}, R={bRightDirty}), prompting user");
            var drResult = MessageBox.Show("Unsaved changes will be lost. Save now?",
                "Unsaved Changes", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
            if (drResult == DialogResult.Yes)
            {
                LogService.Info("Form1 closing: saved changes");
                BtnSave_Click(this, EventArgs.Empty);
            }
            else if (drResult == DialogResult.Cancel)
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
        int iCount = 0;
        foreach (Control ctrl in GetAllDescendants(this))
        {
            if (ctrl is TextBox || ctrl is NumericUpDown || ctrl is TrackBar || ctrl is CheckBox || ctrl is ComboBox)
            {
                ctrl.Enter += MarkFocusSide;
                iCount++;
            }
        }
        LogService.Debug($"MarkPanelControls: {iCount} controls bound");
    }

    private static IEnumerable<Control> GetAllDescendants(Control ctrlParent)
    {
        foreach (Control ctrl in ctrlParent.Controls)
        {
            yield return ctrl;
            foreach (Control ctrlChild in GetAllDescendants(ctrl))
                yield return ctrlChild;
        }
    }

    //控件获得焦点时记录其在左半还是右半
    private void MarkFocusSide(object? sender, EventArgs e)
    {
        if (sender is Control ctrl)
        {
            int iFormX = this.PointToClient(ctrl.PointToScreen(Point.Empty)).X;
            bLastFocusLeft = iFormX < iCenterX;
            LogService.Debug($"Focus side: {(bLastFocusLeft ? "L" : "R")} ({ctrl.GetType().Name})");
        }
    }
    
    protected override void WndProc(ref Message m)
    {
        const int WM_HOTKEY = 0x0312;
        if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == iHotkeyId)
        {
            try
            {
                bool bMcvOrSelf = IsMcvForeground() || GetForegroundWindow() == this.Handle;
                if (this.WindowState == FormWindowState.Minimized || !this.Visible)
                {
                    if (!bMcvOrSelf) return;
                    LogService.Debug("Hotkey Ctrl+T: restore from minimized to topmost");
                    this.Visible = true;
                    this.WindowState = FormWindowState.Normal;
                    ShowWindow(this.Handle, SW_RESTORE);
                    SetWindowPos(this.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
                    bIsTopmost = true;
                    this.Text = "Keyvalues Mangler™ 5000 [Topmost]";
                    this.Activate();
                }
                else if (bIsTopmost)
                {
                    LogService.Debug("Hotkey Ctrl+T: exit topmost");
                    SetWindowPos(this.Handle, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
                    if (bMcvOrSelf)
                        this.WindowState = FormWindowState.Minimized;
                    bIsTopmost = false;
                    this.Text = "Keyvalues Mangler™ 5000";
                }
                else
                {
                    if (bMcvOrSelf)
                    {
                        LogService.Debug("Hotkey Ctrl+T: minimize");
                        this.WindowState = FormWindowState.Minimized;
                        bIsTopmost = false;
                        this.Text = "Keyvalues Mangler™ 5000";
                    }
                    else
                    {
                        LogService.Debug("Hotkey Ctrl+T: set topmost");
                        SetWindowPos(this.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
                        bIsTopmost = true;
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
        IntPtr hFgw = GetForegroundWindow();
        if (hFgw == IntPtr.Zero) return false;
        GetWindowThreadProcessId(hFgw, out uint dwPid);
        try
        {
            using var proc = Process.GetProcessById((int)dwPid);
            bool bIsMcv = proc.ProcessName.Equals("mcv_x64", StringComparison.OrdinalIgnoreCase);
            LogService.Debug($"IsMcvForeground: {proc.ProcessName} -> {bIsMcv}");
            return bIsMcv;
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "Form1.IsMcvForeground");
            return false;
        }
    }

    #endregion
    #region 复制

    private void CopyLeftToRight(object? sender, EventArgs e)
    {
        if (wCurrentLeft != null && wCurrentRight != null)
        {
            LogService.Debug($"Copy L>R: {wCurrentLeft.ScriptName} -> {wCurrentRight.ScriptName}");
            PushUndo();
            var wTemp = new WeaponData();
            SaveControlsToWeapon(wTemp, true);
            LoadWeaponToControls(wTemp, false);
            UpdateAllDamage();
            pnlSpread.Invalidate();
            pnlRecoil.Invalidate();
        }
    }

    private void CopyRightToLeft(object? sender, EventArgs e)
    {
        if (wCurrentRight != null && wCurrentLeft != null)
        {
            LogService.Debug($"Copy R>L: {wCurrentRight.ScriptName} -> {wCurrentLeft.ScriptName}");
            PushUndo();
            var wTemp = new WeaponData();
            SaveControlsToWeapon(wTemp, false);
            LoadWeaponToControls(wTemp, true);
            UpdateAllDamage();
            pnlSpread.Invalidate();
            pnlRecoil.Invalidate();
        }
    }

    #endregion
    #region 联动同步

    private static WeaponData CloneWeaponData(WeaponData wSrc)
    {
        var wDst = wSrc.ShallowClone();
        wDst.ScriptName = string.Empty;
        wDst.PrintName = string.Empty;
        wDst.PrimaryAmmo = string.Empty;
        return wDst;
    }

    //保存顶层值后将备选值中与旧顶层值一致的字段同步到新顶层值
    private static void SyncAltStatsToMatchTopLevel(WeaponData wOld, WeaponData wNew)
    {
        LogService.Debug($"SyncAltStatsToMatchTopLevel called for {wNew.ScriptName}");
        //double
        SyncDoubleIfMatch(wOld.DamageGeneric, wNew.DamageGeneric, wNew.DovDamageGeneric, wNew.ZombieDamageGeneric,
            (w, fV) => w.DovDamageGeneric = fV, (w, fV) => w.ZombieDamageGeneric = fV, wNew);
        SyncDoubleIfMatch(wOld.DamageHeadMultiplier, wNew.DamageHeadMultiplier, wNew.DovDamageHeadMultiplier, wNew.ZombieDamageHeadMultiplier,
            (w, fV) => w.DovDamageHeadMultiplier = fV, (w, fV) => w.ZombieDamageHeadMultiplier = fV, wNew);
        SyncDoubleIfMatch(wOld.DamageChestMultiplier, wNew.DamageChestMultiplier, wNew.DovDamageChestMultiplier, wNew.ZombieDamageChestMultiplier,
            (w, fV) => w.DovDamageChestMultiplier = fV, (w, fV) => w.ZombieDamageChestMultiplier = fV, wNew);
        SyncDoubleIfMatch(wOld.DamageStomachMultiplier, wNew.DamageStomachMultiplier, wNew.DovDamageStomachMultiplier, wNew.ZombieDamageStomachMultiplier,
            (w, fV) => w.DovDamageStomachMultiplier = fV, (w, fV) => w.ZombieDamageStomachMultiplier = fV, wNew);
        SyncDoubleIfMatch(wOld.DamageLegMultiplier, wNew.DamageLegMultiplier, wNew.DovDamageLegMultiplier, wNew.ZombieDamageLegMultiplier,
            (w, fV) => w.DovDamageLegMultiplier = fV, (w, fV) => w.ZombieDamageLegMultiplier = fV, wNew);
        SyncDoubleIfMatch(wOld.DamageArmMultiplier, wNew.DamageArmMultiplier, wNew.DovDamageArmMultiplier, wNew.ZombieDamageArmMultiplier,
            (w, fV) => w.DovDamageArmMultiplier = fV, (w, fV) => w.ZombieDamageArmMultiplier = fV, wNew);
        SyncDoubleIfMatch(wOld.BulletSpread, wNew.BulletSpread, wNew.DovBulletSpread, wNew.ZombieBulletSpread,
            (w, fV) => w.DovBulletSpread = fV, (w, fV) => w.ZombieBulletSpread = fV, wNew);
        SyncDoubleIfMatch(wOld.BulletSpreadDegreesIronsighted, wNew.BulletSpreadDegreesIronsighted, wNew.DovBulletSpreadDegreesIronsighted, wNew.ZombieBulletSpreadDegreesIronsighted,
            (w, fV) => w.DovBulletSpreadDegreesIronsighted = fV, (w, fV) => w.ZombieBulletSpreadDegreesIronsighted = fV, wNew);
        SyncDoubleIfMatch(wOld.BulletSpreadDegreesBipod, wNew.BulletSpreadDegreesBipod, wNew.DovBulletSpreadDegreesBipod, wNew.ZombieBulletSpreadDegreesBipod,
            (w, fV) => w.DovBulletSpreadDegreesBipod = fV, (w, fV) => w.ZombieBulletSpreadDegreesBipod = fV, wNew);
        SyncDoubleIfMatch(wOld.BulletSpreadDegreesBipodIronsighted, wNew.BulletSpreadDegreesBipodIronsighted, wNew.DovBulletSpreadDegreesBipodIronsighted, wNew.ZombieBulletSpreadDegreesBipodIronsighted,
            (w, fV) => w.DovBulletSpreadDegreesBipodIronsighted = fV, (w, fV) => w.ZombieBulletSpreadDegreesBipodIronsighted = fV, wNew);
        SyncDoubleIfMatch(wOld.RangeModifier, wNew.RangeModifier, wNew.DovRangeModifier, wNew.ZombieRangeModifier,
            (w, fV) => w.DovRangeModifier = fV, (w, fV) => w.ZombieRangeModifier = fV, wNew);
        SyncDoubleIfMatch(wOld.IronsightSpeedScale, wNew.IronsightSpeedScale, wNew.DovIronsightSpeedScale, wNew.ZombieIronsightSpeedScale,
            (w, fV) => w.DovIronsightSpeedScale = fV, (w, fV) => w.ZombieIronsightSpeedScale = fV, wNew);
        SyncDoubleIfMatch(wOld.CrouchSpreadMultiplier, wNew.CrouchSpreadMultiplier, wNew.DovCrouchSpreadMultiplier, wNew.ZombieCrouchSpreadMultiplier,
            (w, fV) => w.DovCrouchSpreadMultiplier = fV, (w, fV) => w.ZombieCrouchSpreadMultiplier = fV, wNew);
        SyncDoubleIfMatch(wOld.ProneSpreadMultiplier, wNew.ProneSpreadMultiplier, wNew.DovProneSpreadMultiplier, wNew.ZombieProneSpreadMultiplier,
            (w, fV) => w.DovProneSpreadMultiplier = fV, (w, fV) => w.ZombieProneSpreadMultiplier = fV, wNew);
        SyncDoubleIfMatch(wOld.StandMoveSpreadMultiplier, wNew.StandMoveSpreadMultiplier, wNew.DovStandMoveSpreadMultiplier, wNew.ZombieStandMoveSpreadMultiplier,
            (w, fV) => w.DovStandMoveSpreadMultiplier = fV, (w, fV) => w.ZombieStandMoveSpreadMultiplier = fV, wNew);
        SyncDoubleIfMatch(wOld.SneakMoveSpreadMultiplier, wNew.SneakMoveSpreadMultiplier, wNew.DovSneakMoveSpreadMultiplier, wNew.ZombieSneakMoveSpreadMultiplier,
            (w, fV) => w.DovSneakMoveSpreadMultiplier = fV, (w, fV) => w.ZombieSneakMoveSpreadMultiplier = fV, wNew);
        SyncDoubleIfMatch(wOld.CrouchMoveSpreadMultiplier, wNew.CrouchMoveSpreadMultiplier, wNew.DovCrouchMoveSpreadMultiplier, wNew.ZombieCrouchMoveSpreadMultiplier,
            (w, fV) => w.DovCrouchMoveSpreadMultiplier = fV, (w, fV) => w.ZombieCrouchMoveSpreadMultiplier = fV, wNew);
        SyncDoubleIfMatch(wOld.JumpSpreadMultiplier, wNew.JumpSpreadMultiplier, wNew.DovJumpSpreadMultiplier, wNew.ZombieJumpSpreadMultiplier,
            (w, fV) => w.DovJumpSpreadMultiplier = fV, (w, fV) => w.ZombieJumpSpreadMultiplier = fV, wNew);
        SyncDoubleIfMatch(wOld.ViewSlideRecoilUp, wNew.ViewSlideRecoilUp, wNew.DovViewSlideRecoilUp, wNew.ZombieViewSlideRecoilUp,
            (w, fV) => w.DovViewSlideRecoilUp = fV, (w, fV) => w.ZombieViewSlideRecoilUp = fV, wNew);
        SyncDoubleIfMatch(wOld.ViewSlideRecoilRight, wNew.ViewSlideRecoilRight, wNew.DovViewSlideRecoilRight, wNew.ZombieViewSlideRecoilRight,
            (w, fV) => w.DovViewSlideRecoilRight = fV, (w, fV) => w.ZombieViewSlideRecoilRight = fV, wNew);
        SyncDoubleIfMatch(wOld.ViewSlideRecoilIronsightUp, wNew.ViewSlideRecoilIronsightUp, wNew.DovViewSlideRecoilIronsightUp, wNew.ZombieViewSlideRecoilIronsightUp,
            (w, fV) => w.DovViewSlideRecoilIronsightUp = fV, (w, fV) => w.ZombieViewSlideRecoilIronsightUp = fV, wNew);
        SyncDoubleIfMatch(wOld.ViewSlideRecoilIronsightRight, wNew.ViewSlideRecoilIronsightRight, wNew.DovViewSlideRecoilIronsightRight, wNew.ZombieViewSlideRecoilIronsightRight,
            (w, fV) => w.DovViewSlideRecoilIronsightRight = fV, (w, fV) => w.ZombieViewSlideRecoilIronsightRight = fV, wNew);
        SyncDoubleIfMatch(wOld.Weight, wNew.Weight, wNew.DovWeight, wNew.ZombieWeight,
            (w, fV) => w.DovWeight = fV, (w, fV) => w.ZombieWeight = fV, wNew);
        //int
        SyncIntIfMatch(wOld.FireRate, wNew.FireRate, wNew.DovFireRate, wNew.ZombieFireRate,
            (w, nV) => w.DovFireRate = nV, (w, nV) => w.ZombieFireRate = nV, wNew);
        SyncIntIfMatch(wOld.ExtraBulletChamber, wNew.ExtraBulletChamber, wNew.DovExtraBulletChamber, wNew.ZombieExtraBulletChamber,
            (w, nV) => w.DovExtraBulletChamber = nV, (w, nV) => w.ZombieExtraBulletChamber = nV, wNew);
        SyncIntIfMatch(wOld.SecondaryFireRate, wNew.SecondaryFireRate, wNew.DovSecondaryFireRate, wNew.ZombieSecondaryFireRate,
            (w, nV) => w.DovSecondaryFireRate = nV, (w, nV) => w.ZombieSecondaryFireRate = nV, wNew);
        SyncIntIfMatch(wOld.IronSight, wNew.IronSight, wNew.DovIronSight, wNew.ZombieIronSight,
            (w, nV) => w.DovIronSight = nV, (w, nV) => w.ZombieIronSight = nV, wNew);
        SyncIntIfMatch(wOld.DefaultClip, wNew.DefaultClip, wNew.DovDefaultClip, wNew.ZombieDefaultClip,
            (w, nV) => w.DovDefaultClip = nV, (w, nV) => w.ZombieDefaultClip = nV, wNew);
        SyncIntIfMatch(wOld.BulletsPerShot, wNew.BulletsPerShot, wNew.DovBulletsPerShot, wNew.ZombieBulletsPerShot,
            (w, nV) => w.DovBulletsPerShot = nV, (w, nV) => w.ZombieBulletsPerShot = nV, wNew);
        SyncIntIfMatch(wOld.ZMBuyPrice, wNew.ZMBuyPrice, wNew.DovZMBuyPrice, null,
            (w, nV) => w.DovZMBuyPrice = nV, null, wNew);
        SyncIntIfMatch(wOld.ZMWeight, wNew.ZMWeight, wNew.DovZMWeight, null,
            (w, nV) => w.DovZMWeight = nV, null, wNew);
        //string
        SyncStrIfMatch(wOld.ClipSize, wNew.ClipSize, wNew.DovClipSize, wNew.ZombieClipSize,
            (w, sV) => w.DovClipSize = sV ?? string.Empty, (w, sV) => w.ZombieClipSize = sV ?? string.Empty, wNew);
        SyncStrIfMatch(wOld.FireModes, wNew.FireModes, wNew.DovFireModes, wNew.ZombieFireModes,
            (w, sV) => w.DovFireModes = sV ?? string.Empty, (w, sV) => w.ZombieFireModes = sV ?? string.Empty, wNew);
    }

    private static void SyncDoubleIfMatch(double? fOldVal, double? fNewVal,
        double? fDov, double? fZombie,
        Action<WeaponData, double?> actSetDov, Action<WeaponData, double?> actSetZombie,
        WeaponData w)
    {
        if (fDov.HasValue && fOldVal.HasValue && Math.Abs(fDov.Value - fOldVal.Value) < 0.001)
        {
            LogService.Debug($"SyncDoubleIfMatch: clearing Dov (old={fOldVal}, dov={fDov})");
            actSetDov(w, null);
        }
        if (fZombie.HasValue && fOldVal.HasValue && Math.Abs(fZombie.Value - fOldVal.Value) < 0.001)
        {
            LogService.Debug($"SyncDoubleIfMatch: clearing Zombie (old={fOldVal}, zombie={fZombie})");
            actSetZombie(w, null);
        }
    }

    private static void SyncIntIfMatch(int? nOldVal, int? nNewVal,
        int? nDov, int? nZombie,
        Action<WeaponData, int?> actSetDov, Action<WeaponData, int?>? actSetZombie,
        WeaponData w)
    {
        if (nDov.HasValue && nOldVal.HasValue && nDov.Value == nOldVal.Value)
        {
            LogService.Debug($"SyncIntIfMatch: clearing Dov (old={nOldVal}, dov={nDov})");
            actSetDov(w, null);
        }
        if (nZombie.HasValue && nOldVal.HasValue && nZombie.Value == nOldVal.Value)
        {
            LogService.Debug($"SyncIntIfMatch: clearing Zombie (old={nOldVal}, zombie={nZombie})");
            actSetZombie?.Invoke(w, null);
        }
    }

    private static void SyncStrIfMatch(string sOldVal, string sNewVal,
        string sDov, string sZombie,
        Action<WeaponData, string?> actSetDov, Action<WeaponData, string?> actSetZombie,
        WeaponData w)
    {
        if (!string.IsNullOrEmpty(sDov) && string.Equals(sDov, sOldVal, StringComparison.OrdinalIgnoreCase))
        {
            LogService.Debug($"SyncStrIfMatch: clearing Dov (old={sOldVal}, dov={sDov})");
            actSetDov(w, null);
        }
        if (!string.IsNullOrEmpty(sZombie) && string.Equals(sZombie, sOldVal, StringComparison.OrdinalIgnoreCase))
        {
            LogService.Debug($"SyncStrIfMatch: clearing Zombie (old={sOldVal}, zombie={sZombie})");
            actSetZombie(w, null);
        }
    }

    #endregion
    #region 杂项

    private static void EnableDoubleBuffering(Control ctrl)
    {
        typeof(Control).InvokeMember("DoubleBuffered",
            BindingFlags.SetProperty | BindingFlags.Instance | BindingFlags.NonPublic,
            null, ctrl, new object[] { true });
    }

    private bool SystemUsesDarkMode()
    {
        if (bForceLightMode) return false;
        if (bForceDarkMode) return true;
        try
        {
            using var rkKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (rkKey?.GetValue("AppsUseLightTheme") is int iIntVal && iIntVal == 0)
            {
                LogService.Info("DarkMode: detected via Windows registry");
                return true;
            }
        }
        catch (Exception ex) { LogService.Info($"DarkMode: Windows registry check failed: {ex.Message}"); }

        //下面这些真的会有人用到吗
        string sGtkTheme = Environment.GetEnvironmentVariable("GTK_THEME") ?? "";
        if (!string.IsNullOrEmpty(sGtkTheme))
        {
            LogService.Info($"DarkMode: GTK_THEME={sGtkTheme}");
            if (sGtkTheme.Contains("dark", StringComparison.OrdinalIgnoreCase))
            {
                LogService.Info("DarkMode: detected via GTK_THEME");
                return true;
            }
        }
        else
        {
            LogService.Info("DarkMode: GTK_THEME not set, trying config files");
        }

        var rgHomes = new[] {
            Environment.GetEnvironmentVariable("HOME") ?? "",
            "/home/" + (Environment.GetEnvironmentVariable("USER") ?? ""),
            "/home/" + (Environment.GetEnvironmentVariable("LOGNAME") ?? "")
        }.Where(sH => !string.IsNullOrEmpty(sH)).Distinct().ToList();

        LogService.Info($"DarkMode: trying {rgHomes.Count} home paths: [{string.Join(", ", rgHomes)}]");

        if (TryDetectLinuxDark(rgHomes,
            new[] { ".config/gtk-4.0/settings.ini", ".config/gtk-3.0/settings.ini" },
            "[Settings]", "gtk-theme-name", out string sGtkSource))
        {
            LogService.Info($"DarkMode: detected via {sGtkSource}");
            return true;
        }

        if (TryDetectLinuxDark(rgHomes,
            new[] { ".config/xfce4/xfconf/xfce-perchannel-xml/xsettings.xml" },
            "", "ThemeName", out string sXfceSource, bIsXml: true))
        {
            LogService.Info($"DarkMode: detected via {sXfceSource}");
            return true;
        }

        if (TryDetectKdeDark(rgHomes))
        {
            LogService.Info("DarkMode: detected via KDE activeBackground");
            return true;
        }

        LogService.Info("DarkMode: not detected");
        return false;
    }

    private static bool TryDetectLinuxDark(List<string> rgHomes, string[] rgRelativePaths,
        string sSection, string sKeyName, out string sSource, bool bIsXml = false)
    {
        sSource = "";
        try
        {
            foreach (string sHome in rgHomes)
            {
                foreach (string sRelPath in rgRelativePaths)
                {
                    string sPath = System.IO.Path.Combine(sHome, sRelPath);
                    if (!File.Exists(sPath))
                    {
                        LogService.Info($"DarkMode: config not found: {sPath}");
                        continue;
                    }

                    LogService.Info($"DarkMode: reading {sPath}");
                    string sValue = bIsXml
                        ? ExtractXmlValue(sPath, sKeyName)
                        : ExtractIniValue(sPath, sSection, sKeyName);

                    if (!string.IsNullOrEmpty(sValue))
                    {
                        LogService.Info($"DarkMode: {System.IO.Path.GetFileName(sPath)} {sKeyName}={sValue}");
                        if (sValue.Contains("dark", StringComparison.OrdinalIgnoreCase))
                        {
                            sSource = sPath;
                            return true;
                        }
                    }
                }
            }
        }
        catch (Exception ex) { LogService.Info($"DarkMode: config check failed: {ex.Message}"); }
        return false;
    }

    private static string ExtractIniValue(string sPath, string sSection, string sKey)
    {
        bool bInSection = false;
        foreach (string sLine in File.ReadLines(sPath))
        {
            string sTrimmed = sLine.Trim();
            if (sTrimmed.Equals(sSection, StringComparison.OrdinalIgnoreCase))
            { bInSection = true; continue; }
            if (bInSection && sTrimmed.StartsWith("[")) break;
            if (bInSection && sTrimmed.StartsWith(sKey, StringComparison.OrdinalIgnoreCase))
            {
                string[] rgParts = sTrimmed.Split('=');
                return rgParts.Length >= 2 ? rgParts[1].Trim() : "";
            }
        }
        return "";
    }

    private static string ExtractXmlValue(string sPath, string sKey)
    {
        string sContent = File.ReadAllText(sPath);
        //匹配XML属性块<property name="{sKey}" ...><value ...>...</value>捕获<value>内文本
        var m = System.Text.RegularExpressions.Regex.Match(sContent,
            $@"<property\s+name=""{sKey}""[^>]*>\s*<value[^>]*>\s*([^<]+)");
        return m.Success ? m.Groups[1].Value.Trim() : "";
    }

    private static bool TryDetectKdeDark(List<string> rgHomes)
    {
        try
        {
            foreach (string sHome in rgHomes)
            {
                string sPath = System.IO.Path.Combine(sHome, ".config", "kdeglobals");
                if (!File.Exists(sPath))
                {
                    LogService.Info($"DarkMode: KDE config not found: {sPath}");
                    continue;
                }

                LogService.Info($"DarkMode: reading {sPath}");
                string sColor = ExtractIniValue(sPath, "[WM]", "activeBackground");
                if (string.IsNullOrEmpty(sColor))
                {
                    LogService.Info("DarkMode: KDE activeBackground not found");
                    continue;
                }

                LogService.Info($"DarkMode: KDE activeBackground={sColor}");
                string[] rgRgb = sColor.Split(',');
                if (rgRgb.Length == 3 &&
                    int.TryParse(rgRgb[0], out int iR) &&
                    int.TryParse(rgRgb[1], out int iG) &&
                    int.TryParse(rgRgb[2], out int iB) &&
                    (iR + iG + iB) / 3.0 < 120)
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
            int iUseDark = 1;
            DwmSetWindowAttribute(this.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref iUseDark, sizeof(int));
        }
        catch { }
    }

    private void ApplyDarkMode()
    {
        this.BackColor = Color.FromArgb(32, 32, 32);
        this.ForeColor = Color.FromArgb(240, 240, 240);

        foreach (Control ctrl in GetAllDescendants(this))
        {
            if (ctrl is Label lbl)
            {
                if (lbl == lblC64_1 || lbl == lblC64_2 || lbl == lblC64_3) continue;
                if (lbl.ForeColor == Color.DarkRed)
                    lbl.ForeColor = Color.FromArgb(255, 100, 100);
                else
                    lbl.ForeColor = Color.FromArgb(240, 240, 240);
            }
            else if (ctrl is Button btn)
            {
                btn.BackColor = Color.FromArgb(60, 60, 60);
                btn.ForeColor = Color.FromArgb(240, 240, 240);
                btn.FlatStyle = FlatStyle.Flat;
                btn.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80);
            }
            else if (ctrl is TextBox txt)
            {
                txt.BackColor = Color.FromArgb(50, 50, 50);
                txt.ForeColor = Color.FromArgb(240, 240, 240);
            }
            else if (ctrl is NumericUpDown nud)
            {
                nud.BackColor = Color.FromArgb(50, 50, 50);
                nud.ForeColor = Color.FromArgb(240, 240, 240);
            }
            else if (ctrl is ComboBox cmb)
            {
                cmb.BackColor = Color.FromArgb(50, 50, 50);
                cmb.ForeColor = Color.FromArgb(240, 240, 240);
            }
            else if (ctrl is TrackBar tb)
            {
                tb.BackColor = Color.FromArgb(32, 32, 32);
            }
            else if (ctrl is GroupBox gb)
            {
                gb.ForeColor = Color.FromArgb(200, 200, 200);
            }
            else if (ctrl is CheckBox chk)
            {
                chk.ForeColor = Color.FromArgb(240, 240, 240);
            }
            else if (ctrl is Panel pnl)
            {
                if (pnl == pnlSpread || pnl == pnlRecoil) continue;
                pnl.BackColor = Color.FromArgb(40, 40, 40);
            }
        }
        bDarkMode = true;
        SetTitleBarDark();
    }

    private async void FlashButton(Button btn)
    {
        Color cOld = btn.BackColor;
        btn.BackColor = Color.FromArgb(80, 180, 80);
        await Task.Delay(810);
        btn.BackColor = cOld;
    }

    private void PnlSpread_Paint(object? sender, PaintEventArgs e)
    {
        bool bLeftAds = nudIronSightL.Value != 0;
        bool bRightAds = nudIronSightR.Value != 0;
        prSpreadRenderer.DrawSpread(e.Graphics, wCurrentLeft, wCurrentRight,
            (double)nudHipSpreadL.Value, bLeftAds ? (double)nudAdsSpreadL.Value : 0,
            (double)nudBipodHipSpreadL.Value, bLeftAds ? (double)nudBipodAdsSpreadL.Value : 0,
            wCurrentRight != null ? (double)nudHipSpreadR.Value : 1.0,
            wCurrentRight != null && bRightAds ? (double)nudAdsSpreadR.Value : 1.0,
            wCurrentRight != null ? (double)nudBipodHipSpreadR.Value : 0,
            wCurrentRight != null && bRightAds ? (double)nudBipodAdsSpreadR.Value : 0);
    }

    private void PnlRecoil_Paint(object? sender, PaintEventArgs e)
    {
        bool bLeftAds = nudIronSightL.Value != 0;
        bool bRightAds = nudIronSightR.Value != 0;
        float fMaxScale = (bShowingAltStats && amCurrentAltStat == WeaponScriptService.AltStatMode.Dov) ? 1.25f : 2.5f;
        prRecoilRenderer.DrawRecoil(e.Graphics, wCurrentLeft, wCurrentRight,
            (double)nudHipRecoilUpL.Value, (double)nudHipRecoilRightL.Value,
            bLeftAds ? (double)nudAdsRecoilUpL.Value : 0, bLeftAds ? (double)nudAdsRecoilRightL.Value : 0,
            wCurrentRight != null ? (double)nudHipRecoilUpR.Value : 0,
            wCurrentRight != null ? (double)nudHipRecoilRightR.Value : 0,
            wCurrentRight != null && bRightAds ? (double)nudAdsRecoilUpR.Value : 0,
            wCurrentRight != null && bRightAds ? (double)nudAdsRecoilRightR.Value : 0,
            fMaxScale);
    }

    public class LogForm : Form
    {
        public LogForm(string sTitle, string sLogText, bool bDarkMode = false)
        {
            this.Text = sTitle;
            this.Size = new Size(320, 240);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MinimizeBox = false;
            this.MaximizeBox = false;
            this.TopMost = true;
            this.ShowInTaskbar = true;
            var txt = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = new Font("Consolas", 9), Text = sLogText.Replace("\n", "\r\n") };
            if (bDarkMode)
            {
                this.BackColor = Color.FromArgb(32, 32, 32);
                this.ForeColor = Color.FromArgb(240, 240, 240);
                txt.BackColor = Color.FromArgb(50, 50, 50);
                txt.ForeColor = Color.FromArgb(240, 240, 240);
                this.Shown += (_, _) =>
                {
                    try
                    {
                        int iUseDark = 1;
                        DwmSetWindowAttribute(this.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref iUseDark, sizeof(int));
                    }
                    catch { }
                };
            }
            this.Controls.Add(txt);
        }
    }
    #endregion
}