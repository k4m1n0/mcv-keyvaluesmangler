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
    private WeaponData? snapshotLeft = null;
    private WeaponData? snapshotRight = null;
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
        try
        {
            this.Text = "Keyvalues Mangler™ 5000";
            this.Size = new Size(1366, 768);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;

            string csvPath = Path.Combine(AppContext.BaseDirectory, "weapons.csv");
            weapons = File.Exists(csvPath) ? CsvService.LoadWeapons(csvPath) : new List<WeaponData>();

            InitLeftPanel(weapons);
            InitRightPanel(weapons);
            InitCenterPanels();
            InitC64Labels();
            InitTopButtons();
            MarkPanelControls();

            this.KeyPreview = true;
            this.KeyDown += Form1_KeyDown;
            this.FormClosing += Form1_FormClosing;

            this.Shown += (s, e) =>
            {
                if (weapons.Count > 0)
                {
                    cmbWeaponsL.DataSource = null;
                    cmbWeaponsL.DataSource = new List<WeaponData>(weapons);
                    cmbWeaponsL.DisplayMember = "PrintName";
                    cmbWeaponsL.SelectedIndex = 0;

                    cmbWeaponsR.DataSource = null;
                    cmbWeaponsR.DataSource = new List<WeaponData>(weapons);
                    cmbWeaponsR.DisplayMember = "PrintName";
                    cmbWeaponsR.SelectedIndex = 0;

                    UpdateC64Labels(true);
                }
                initializing = false;
                RegisterHotKey(this.Handle, hotkeyId, MOD_CONTROL, VK_T);
            };

            this.FormClosed += (s, e) => UnregisterHotKey(this.Handle, hotkeyId);
        }
        catch (Exception ex)
        {
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
        tooltip.SetToolTip(btnSave, "Save current weapon data to CSV (Ctrl+S)");
        tooltip.SetToolTip(btnCsvToScripts, "Export CSV data to weapon script files");
        tooltip.SetToolTip(btnScriptsToCsv, "Import weapon script files to CSV");
        tooltip.SetToolTip(btnRefresh, "Reload weapon list from CSV (Ctrl+R)");
        tooltip.SetToolTip(btnCopy, "Copy left panel values to right");
        tooltip.SetToolTip(btnCopyCvar, "Quick export: save CSV and export to scripts\nRight-click to cancel");
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
            btn.Text = "wpn_reload_script all";
            btn.Tag = false;
        }
    }

    private void Form1_FormClosing(object sender, FormClosingEventArgs e)
    {
        bool leftDirty = currentWeaponLeft != null && HasUnsavedChanges(true);
        bool rightDirty = currentWeaponRight != null && HasUnsavedChanges(false);
        if (leftDirty || rightDirty)
        {
            var result = MessageBox.Show("Unsaved changes will be lost. Save now?",
                "Unsaved Changes", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
            if (result == DialogResult.Yes)
            {
                BtnSave_Click(this, EventArgs.Empty);
            }
            else if (result == DialogResult.Cancel)
            {
                e.Cancel = true;
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
            bool mcvOrSelf = IsMcvForeground() || GetForegroundWindow() == this.Handle;
            if (this.WindowState == FormWindowState.Minimized || !this.Visible)
            {
                if (!mcvOrSelf) return;
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
                    this.WindowState = FormWindowState.Minimized;
                    isTopmost = false;
                    this.Text = "Keyvalues Mangler™ 5000";
                }
                else
                {
                    SetWindowPos(this.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
                    isTopmost = true;
                    this.Text = "Keyvalues Mangler™ 5000 [Topmost]";
                    this.Activate();
                }
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
        catch { return false; }
    }

    private void StoreSnapshot(bool isLeft)
    {
        var snap = new WeaponData();
        SaveControlsToWeapon(snap, isLeft);
        if (isLeft) snapshotLeft = snap; else snapshotRight = snap;
    }

    private void RestoreSnapshot(bool isLeft)
    {
        var snap = isLeft ? snapshotLeft : snapshotRight;
        if (snap == null) return;
        var temp = new WeaponData();
        CopyWeaponDataFields(snap, temp);
        LoadWeaponToControls(temp, isLeft);
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
