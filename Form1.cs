using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using WeaponDamageCalc.Models;
using WeaponDamageCalc.Services;

namespace WeaponDamageCalc;

#region 声明

public partial class Form1 : Form
{
    private ComboBox cmbWeapons = null!;
    private TrackBar trkHead = null!;
    private TrackBar trkChest = null!;
    private TrackBar trkStomach = null!;
    private TrackBar trkLeg = null!;
    private TrackBar trkArm = null!;
    private TrackBar trkDistance = null!;
    private NumericUpDown nudHead = null!;
    private NumericUpDown nudChest = null!;
    private NumericUpDown nudStomach = null!;
    private NumericUpDown nudLeg = null!;
    private NumericUpDown nudArm = null!;

    private NumericUpDown nudHipSpread = null!;
    private NumericUpDown nudAdsSpread = null!;
    private NumericUpDown nudBipodHipSpread = null!;
    private NumericUpDown nudBipodAdsSpread = null!;
    private NumericUpDown nudHipRecoilUp = null!;
    private NumericUpDown nudHipRecoilRight = null!;
    private NumericUpDown nudAdsRecoilUp = null!;
    private NumericUpDown nudAdsRecoilRight = null!;

    private TextBox txtFireModes = null!;
    private NumericUpDown nudExtraBulletChamber = null!;
    private NumericUpDown nudBulletsPerShot = null!;
    private NumericUpDown nudFireRate = null!;
    private NumericUpDown nudRangeModifier = null!;
    private NumericUpDown nudIronsightSpeedScale = null!;
    private NumericUpDown nudWeight = null!;
    private NumericUpDown nudZMBuyPrice = null!;
    private NumericUpDown nudZMWeight = null!;
    private NumericUpDown nudMetalPen = null!;
    private NumericUpDown nudGlassPen = null!;
    private NumericUpDown nudConcretePen = null!;
    private NumericUpDown nudWoodPen = null!;
    private NumericUpDown nudOtherPen = null!;
    private NumericUpDown nudMetalDmgMod = null!;
    private NumericUpDown nudGlassDmgMod = null!;
    private NumericUpDown nudConcreteDmgMod = null!;
    private NumericUpDown nudWoodDmgMod = null!;
    private NumericUpDown nudOtherDmgMod = null!;
    private NumericUpDown nudDamageGeneric = null!;
    private NumericUpDown nudCrouchSpread = null!;
    private NumericUpDown nudProneSpread = null!;
    private NumericUpDown nudStandMoveSpread = null!;
    private NumericUpDown nudSneakMoveSpread = null!;
    private NumericUpDown nudCrouchMoveSpread = null!;
    private NumericUpDown nudJumpSpread = null!;

    private Label lblHeadDmg = null!;
    private Label lblChestDmg = null!;
    private Label lblStomachDmg = null!;
    private Label lblLegDmg = null!;
    private Label lblArmDmg = null!;
    private Label lblCmpDmg = null!;
    private Label lblCmpTtk = null!;
    private Button btnSave = null!;
    private Button btnCsvToScripts = null!;
    private Button btnScriptsToCsv = null!;
    private Panel pnlSpread = null!;
    private Panel pnlRecoil = null!;
    private CheckBox chkVest = null!;
    private Label lblC64_1 = null!;
    private Label lblC64_2 = null!;
    private Label lblC64_3 = null!;

    private List<WeaponData> weapons = null!;
    private WeaponData? currentWeapon;
    private bool updatingControls = false;

    private const double SliderMin = 0.0;
    private const double SliderMax = 5.0;
    private const double SliderStep = 0.01;
    private const double DistanceDivisor = 9.525;
    private NumericUpDown nudDistance = null!;
    private static WeaponData? copiedWeaponData = null;
    private ComboBox cmbCompare = null!;
    private WeaponData? compareWeapon = null;
    private TextBox txtCapacity = null!;
    private string lastScriptsDir = "";
    private bool refreshing = false;

#endregion
#region 初始化

    public Form1()
    {
        try
        {
            this.Text = "Keyvalues Mangler™ 5000";
            this.Size = new Size(1024, 768);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;

            string csvPath = Path.Combine(AppContext.BaseDirectory, "weapons.csv");
            if (File.Exists(csvPath))
            {
                weapons = CsvService.LoadWeapons(csvPath);
            }
            else
            {
                weapons = new List<WeaponData>();
            }

            InitializeControls();
            this.KeyPreview = true;
            this.KeyDown += Form1_KeyDown;

            cmbWeapons.DataSource = weapons;
            cmbWeapons.DisplayMember = "PrintName";
            cmbWeapons.SelectedIndexChanged += WeaponSelected;
            if (weapons.Any())
                cmbWeapons.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Launch failed: {ex.Message}\n\n{ex.StackTrace}", "Error",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
            Application.Exit();
        }
    }

    private void InitializeControls()
    {
        int xLeft = 10;
        int rightPanelX = 700;

        CreateWeaponSelectorAndButtons(xLeft);
        CreateDamageMultiplierGroup(xLeft);
        CreateRangeGroup(xLeft);
        CreateSpreadRecoilAndPropertiesGroups(xLeft);
        CreateSpreadMultiplierGroup(xLeft);
        CreateOtherStatsGroup(xLeft);
        CreateVisualPanels(rightPanelX);
        CreateC64Labels(rightPanelX, weapons.Any());
    }

#endregion
#region 创建控件

    private void CreateC64Labels(int rightPanelX, bool hasData)
    {
        lblC64_1 = new Label
        {
            Location = new Point(rightPanelX, 683),
            Size = new Size(300, 13),
            Font = new Font("Consolas", 8, FontStyle.Bold),
            ForeColor = Color.FromArgb(200, 200, 255),
            BackColor = Color.FromArgb(60, 60, 160),
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        lblC64_2 = new Label
        {
            Location = new Point(rightPanelX, 694),
            Size = new Size(300, 13),
            Font = new Font("Consolas", 8, FontStyle.Bold),
            ForeColor = Color.FromArgb(200, 200, 255),
            BackColor = Color.FromArgb(60, 60, 160),
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        lblC64_3 = new Label
        {
            Location = new Point(rightPanelX, 705),
            Size = new Size(300, 13),
            Font = new Font("Consolas", 8, FontStyle.Bold),
            ForeColor = Color.FromArgb(200, 200, 255),
            BackColor = Color.FromArgb(60, 60, 160),
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        this.Controls.Add(lblC64_1);
        this.Controls.Add(lblC64_2);
        this.Controls.Add(lblC64_3);
        UpdateC64Labels(hasData);
    }

    private void UpdateC64Labels(bool hasData)
    {
        lblC64_1.Text = hasData ? "        **** COMMODORE 64 BASIC V2 ****" : "";
        lblC64_2.Text = hasData ? "     64K RAM SYSTEM  38911 BASIC BYTES FREE" : "";
        lblC64_3.Text = hasData ? "READY." : "";
    }

    private void CreateWeaponSelectorAndButtons(int xLeft)
    {
        this.Controls.Add(new Label { Text = "Weapon:", Location = new Point(xLeft, 10), Size = new Size(60, 20) });
        cmbWeapons = new ComboBox
        {
            Location = new Point(xLeft + 60, 8),
            Size = new Size(180, 23),
            AutoCompleteMode = AutoCompleteMode.SuggestAppend,
            AutoCompleteSource = AutoCompleteSource.ListItems
        };
        this.Controls.Add(cmbWeapons);

        btnSave = new Button { Text = "Save CSV", Location = new Point(xLeft + 245, 6), Size = new Size(80, 26) };
        btnSave.Click += BtnSave_Click;
        this.Controls.Add(btnSave);

        btnCsvToScripts = new Button { Text = "CSV>Scripts", Location = new Point(xLeft + 330, 6), Size = new Size(90, 26) };
        btnCsvToScripts.Click += BtnCsvToScripts_Click;
        this.Controls.Add(btnCsvToScripts);

        btnScriptsToCsv = new Button { Text = "Scripts>CSV", Location = new Point(xLeft + 425, 6), Size = new Size(90, 26) };
        btnScriptsToCsv.Click += BtnScriptsToCsv_Click;
        this.Controls.Add(btnScriptsToCsv);

        var btnRefresh = new Button { Text = "Refresh", Location = new Point(xLeft + 520, 6), Size = new Size(60, 26) };
        btnRefresh.Click += BtnRefresh_Click;
        this.Controls.Add(btnRefresh);

        this.Controls.Add(new Label { Text = "Dmg", Location = new Point(xLeft + 565, 10), Size = new Size(55, 18), TextAlign = ContentAlignment.MiddleRight });

        nudDamageGeneric = new NumericUpDown
        {
            Location = new Point(xLeft + 620, 8),
            Size = new Size(59, 22),
            DecimalPlaces = 1,
            Increment = 1m,
            Minimum = 0m,
            Maximum = 999m
        };
        nudDamageGeneric.ValueChanged += (s, e) =>
        {
            if (currentWeapon != null)
                currentWeapon.DamageGeneric = (double)nudDamageGeneric.Value;
            UpdateAllDamage();
        };
        this.Controls.Add(nudDamageGeneric);
    }

    private void CreateDamageMultiplierGroup(int xLeft)
    {
        var gb = new GroupBox { Text = "Damage Multiplier", Location = new Point(xLeft, 38), Size = new Size(680, 220) };
        int y = 22;
        (trkHead, nudHead, lblHeadDmg) = CreateSliderRow(gb, "Head", ref y);
        (trkChest, nudChest, lblChestDmg) = CreateSliderRow(gb, "Chest", ref y);
        (trkStomach, nudStomach, lblStomachDmg) = CreateSliderRow(gb, "Stomach", ref y);
        (trkLeg, nudLeg, lblLegDmg) = CreateSliderRow(gb, "Leg", ref y);
        (trkArm, nudArm, lblArmDmg) = CreateSliderRow(gb, "Arm", ref y);
        this.Controls.Add(gb);
    }

    private void CreateRangeGroup(int xLeft)
    {
        var gb = new GroupBox { Text = "Range", Location = new Point(xLeft, 262), Size = new Size(680, 58) };
        gb.Controls.Add(new Label { Text = "0", Location = new Point(8, 20), Size = new Size(20, 18) });
        trkDistance = new TrackBar { Location = new Point(30, 16), Size = new Size(535, 35), Minimum = 0, Maximum = 100, SmallChange = 1, LargeChange = 10 };
        trkDistance.ValueChanged += (s, e) =>
        {
            nudDistance.Value = trkDistance.Value;
            UpdateAllDamage();
        };
        gb.Controls.Add(trkDistance);

        nudDistance = new NumericUpDown
        {
            Location = new Point(570, 16),
            Size = new Size(45, 22),
            DecimalPlaces = 0,
            Increment = 1,
            Minimum = 0,
            Maximum = 100,
            Value = 0
        };
        nudDistance.ValueChanged += (s, e) =>
        {
            trkDistance.Value = (int)nudDistance.Value;
            UpdateAllDamage();
        };
        gb.Controls.Add(nudDistance);

        chkVest = new CheckBox { Text = "Vest", Location = new Point(620, 18), Size = new Size(55, 22) };
        chkVest.CheckedChanged += (s, e) => UpdateAllDamage();
        gb.Controls.Add(chkVest);
        this.Controls.Add(gb);
    }

    private void CreateSpreadRecoilAndPropertiesGroups(int xLeft)
    {
        var gbSpread = new GroupBox { Text = "Spread (°)", Location = new Point(xLeft, 325), Size = new Size(220, 130) };
        int y = 20;
        nudHipSpread = CreateNullableNumericRow(gbSpread, "Hip", 8, y, 100m); nudHipSpread.ValueChanged += SpreadRecoilChanged; y += 24;
        nudAdsSpread = CreateNullableNumericRow(gbSpread, "ADS", 8, y, 100m); nudAdsSpread.ValueChanged += SpreadRecoilChanged; y += 24;
        nudBipodHipSpread = CreateNullableNumericRow(gbSpread, "Bipod Hip", 8, y, 100m); nudBipodHipSpread.ValueChanged += SpreadRecoilChanged; y += 24;
        nudBipodAdsSpread = CreateNullableNumericRow(gbSpread, "Bipod ADS", 8, y, 100m); nudBipodAdsSpread.ValueChanged += SpreadRecoilChanged;
        this.Controls.Add(gbSpread);

        var gbRecoil = new GroupBox { Text = "Recoil (°)", Location = new Point(240, 325), Size = new Size(220, 130) };
        y = 20;
        nudHipRecoilUp = CreateNullableNumericRow(gbRecoil, "Hip Up", 8, y, 100m); nudHipRecoilUp.ValueChanged += SpreadRecoilChanged; y += 24;
        nudHipRecoilRight = CreateNullableNumericRow(gbRecoil, "Hip Rt", 8, y, 100m); nudHipRecoilRight.ValueChanged += SpreadRecoilChanged; y += 24;
        nudAdsRecoilUp = CreateNullableNumericRow(gbRecoil, "ADS Up", 8, y, 100m); nudAdsRecoilUp.ValueChanged += SpreadRecoilChanged; y += 24;
        nudAdsRecoilRight = CreateNullableNumericRow(gbRecoil, "ADS Rt", 8, y, 100m); nudAdsRecoilRight.ValueChanged += SpreadRecoilChanged;
        this.Controls.Add(gbRecoil);

        var gbProp = new GroupBox { Text = "Weapon Stats", Location = new Point(470, 325), Size = new Size(220, 130) };
        y = 20;
        txtFireModes = CreateTextBoxRow(gbProp, "Fire Mode", 8, y); y += 24;
        nudFireRate = CreateNullableIntNumericRow(gbProp, "ROF", 8, y, 10000m); y += 24;
        nudRangeModifier = CreateNullableNumericRow(gbProp, "Range Mod", 8, y, 10m);
        nudRangeModifier.DecimalPlaces = 3; nudRangeModifier.Increment = 0.001m; nudRangeModifier.ValueChanged += RangeModifierChanged; y += 24;
        txtCapacity = CreateTextBoxRow(gbProp, "Capacity", 8, y);
        this.Controls.Add(gbProp);
    }

    private void CreateSpreadMultiplierGroup(int xLeft)
    {
        var gb = new GroupBox { Text = "Spread Multiplier", Location = new Point(xLeft, 460), Size = new Size(680, 75) };
        int y = 20;
        nudCrouchSpread = CreateNullableNumericRow(gb, "Crouch Spr", 8, y, 100m);
        nudProneSpread = CreateNullableNumericRow(gb, "Prone Spr", 230, y, 100m);
        nudStandMoveSpread = CreateNullableNumericRow(gb, "Move Spr", 455, y, 100m);
        y += 26;
        nudSneakMoveSpread = CreateNullableNumericRow(gb, "SnkMov Spr", 8, y, 100m);
        nudCrouchMoveSpread = CreateNullableNumericRow(gb, "CrhMov Spr", 230, y, 100m);
        nudJumpSpread = CreateNullableNumericRow(gb, "Jump Spr", 455, y, 100m);
        this.Controls.Add(gb);
    }

    private void CreateOtherStatsGroup(int xLeft)
    {
        var gb = new GroupBox { Text = "Other Stats", Location = new Point(xLeft, 540), Size = new Size(680, 180) };
        int y = 20;
        nudExtraBulletChamber = CreateNullableIntNumericRow(gb, "Chamber", 8, y, 1000m);
        nudBulletsPerShot = CreateNullableIntNumericRow(gb, "Pellets", 230, y, 100m);
        nudIronsightSpeedScale = CreateNullableNumericRow(gb, "ADS Spd", 455, y, 10m);
        y += 26;
        nudWeight = CreateNullableNumericRow(gb, "Weight", 8, y, 100m);
        nudZMBuyPrice = CreateNullableIntNumericRow(gb, "ZM Price", 230, y, 1000000m);
        nudZMWeight = CreateNullableIntNumericRow(gb, "ZM Block", 455, y, 100m);
        y += 26;
        nudMetalPen = CreateNullableNumericRow(gb, "Metal Dept", 8, y, 10000m);
        nudGlassPen = CreateNullableNumericRow(gb, "Glass Dept", 230, y, 10000m);
        nudConcretePen = CreateNullableNumericRow(gb, "Concr Dept", 455, y, 10000m);
        y += 26;
        nudWoodPen = CreateNullableNumericRow(gb, "Wood Dept", 8, y, 10000m);
        nudOtherPen = CreateNullableNumericRow(gb, "Other Dept", 230, y, 10000m);
        nudConcreteDmgMod = CreateNullableNumericRow(gb, "Concr Mod", 455, y, 100m);
        y += 26;
        nudMetalDmgMod = CreateNullableNumericRow(gb, "Metal Mod", 8, y, 100m);
        nudGlassDmgMod = CreateNullableNumericRow(gb, "Glass Mod", 230, y, 100m);
        y += 26;
        nudWoodDmgMod = CreateNullableNumericRow(gb, "Wood Mod", 8, y, 100m);
        nudOtherDmgMod = CreateNullableNumericRow(gb, "Other Mod", 230, y, 100m);
        this.Controls.Add(gb);
    }

    private void CreateVisualPanels(int rightPanelX)
    {
        var btnCopy = new Button { Text = "Copy", Location = new Point(rightPanelX, 6), Size = new Size(55, 26) };
        btnCopy.Click += (s, e) =>
        {
            if (currentWeapon != null)
            {
                copiedWeaponData = new WeaponData();
                SaveControlsToWeapon(copiedWeaponData);
                copiedWeaponData.ScriptName = currentWeapon.ScriptName;
                copiedWeaponData.PrintName = currentWeapon.PrintName;
            }
        };
        this.Controls.Add(btnCopy);

        var btnPaste = new Button { Text = "Paste", Location = new Point(rightPanelX + 58, 6), Size = new Size(55, 26) };
        btnPaste.Click += (s, e) =>
        {
            if (currentWeapon != null && copiedWeaponData != null)
            {
                LoadWeaponToControls(copiedWeaponData);
                UpdateAllDamage();
                pnlSpread.Invalidate();
                pnlRecoil.Invalidate();
            }
        };
        this.Controls.Add(btnPaste);

        cmbCompare = new ComboBox
        {
            Location = new Point(rightPanelX + 116, 6),
            Size = new Size(184, 23),
            DropDownStyle = ComboBoxStyle.DropDown,
            AutoCompleteMode = AutoCompleteMode.SuggestAppend,
            AutoCompleteSource = AutoCompleteSource.ListItems,
            DisplayMember = "PrintName"
        };
        cmbCompare.SelectedIndexChanged += (s, e) =>
        {
            if (cmbCompare.SelectedItem is WeaponData cw)
            {
                compareWeapon = cw;
                UpdateCompareDamage();
                pnlSpread.Invalidate();
                pnlRecoil.Invalidate();
            }
        };
        if (weapons.Count > 0)
        {
            cmbCompare.DataSource = new List<WeaponData>(weapons);
        }
        this.Controls.Add(cmbCompare);

        pnlSpread = new Panel { Location = new Point(rightPanelX, 46), Size = new Size(300, 300), BorderStyle = BorderStyle.FixedSingle, BackColor = Color.Black };
        EnableDoubleBuffering(pnlSpread);
        pnlSpread.Paint += PnlSpread_Paint;
        this.Controls.Add(pnlSpread);

        pnlRecoil = new Panel { Location = new Point(rightPanelX, 351), Size = new Size(300, 300), BorderStyle = BorderStyle.FixedSingle, BackColor = Color.Black };
        EnableDoubleBuffering(pnlRecoil);
        pnlRecoil.Paint += PnlRecoil_Paint;
        this.Controls.Add(pnlRecoil);

        lblCmpDmg = new Label
        {
            Location = new Point(rightPanelX, 656),
            Size = new Size(300, 20),
            Font = new Font("Consolas", 8, FontStyle.Bold),
            ForeColor = Color.DarkRed,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleCenter,
            Text = "Compare Damage"
        };
        this.Controls.Add(lblCmpDmg);

        lblCmpTtk = new Label
        {
            Location = new Point(rightPanelX, 670),
            Size = new Size(300, 12),
            Font = new Font("Consolas", 7, FontStyle.Bold),
            ForeColor = Color.DarkRed,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleCenter,
            Text = ""
        };
        this.Controls.Add(lblCmpTtk);
    }

#endregion
#region 事件处理

    private void WeaponSelected(object? sender, EventArgs e)
    {
        if (cmbWeapons.SelectedItem is WeaponData w)
        {
            currentWeapon = w;
            LoadWeaponToControls(w);
            UpdateAllDamage();
            pnlSpread.Invalidate();
            pnlRecoil.Invalidate();
        }
    }

    private void SliderChanged(object? sender, EventArgs e)
    {
        if (updatingControls) return;
        updatingControls = true;
        if (sender is TrackBar tb && tb.Tag is NumericUpDown nud)
            nud.Value = Math.Round((decimal)(tb.Value * SliderStep), 2);
        updatingControls = false;
        UpdateAllDamage();
    }

    private void NumericChanged(object? sender, EventArgs e)
    {
        if (updatingControls) return;
        updatingControls = true;
        if (sender is NumericUpDown nud && nud.Tag is TrackBar tb)
        {
            int iv = (int)Math.Round((double)nud.Value / SliderStep);
            iv = Math.Max(tb.Minimum, Math.Min(tb.Maximum, iv));
            tb.Value = iv;
            nud.Value = Math.Round(nud.Value, 2);
        }
        updatingControls = false;
        UpdateAllDamage();
    }

    private void SpreadRecoilChanged(object? sender, EventArgs e) { pnlSpread.Invalidate(); pnlRecoil.Invalidate(); }
    private void RangeModifierChanged(object? sender, EventArgs e) => UpdateAllDamage();

#endregion
#region 保存加载

    private async void BtnSave_Click(object? sender, EventArgs e)
    {
        if (currentWeapon == null) return;
        SaveControlsToWeapon(currentWeapon);

        try
        {
            CsvService.SaveWeapons(Path.Combine(AppContext.BaseDirectory, "weapons.csv"), weapons);

            var originalTitle = this.Text;
            this.Text = "Saved!";
            await Task.Delay(1145);
            this.Text = originalTitle;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存失败：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void BtnCsvToScripts_Click(object? sender, EventArgs e)
    {
        string initialDir = string.IsNullOrEmpty(lastScriptsDir) ? AppContext.BaseDirectory : lastScriptsDir;
        using var dlg = new FolderBrowserDialog { Description = "选择武器脚本所在的文件夹（将直接覆盖）", UseDescriptionForTitle = true, InitialDirectory = initialDir };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        lastScriptsDir = dlg.SelectedPath;
        string dir = dlg.SelectedPath;
        if (MessageBox.Show($"确定要用 CSV 数据覆盖以下文件夹中的所有脚本吗？\n\n{dir}", "确认覆盖", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        string csv = Path.Combine(AppContext.BaseDirectory, "weapons.csv");
        Task.Run(() =>
        {
            try
            {
                string log = WeaponScriptService.ExportCsvToScripts(csv, dir);
                this.Invoke(() => { using var lf = new LogForm("导出完成", log); lf.ShowDialog(this); });
            }
            catch (Exception ex) { this.Invoke(() => MessageBox.Show($"导出脚本时出错：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error)); }
        });
    }

    private void BtnScriptsToCsv_Click(object? sender, EventArgs e)
    {
        string initialDir = string.IsNullOrEmpty(lastScriptsDir) ? AppContext.BaseDirectory : lastScriptsDir;
        using var dlg = new FolderBrowserDialog { Description = "选择包含武器脚本的文件夹", UseDescriptionForTitle = true, InitialDirectory = initialDir };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        lastScriptsDir = dlg.SelectedPath;
        string dir = dlg.SelectedPath;
        string csv = Path.Combine(AppContext.BaseDirectory, "weapons.csv");
        Task.Run(() =>
        {
            try
            {
                string log = WeaponScriptService.ImportScriptsToCsv(dir, csv);
                this.Invoke(() => { RefreshWeaponList(); using var lf = new LogForm("导入完成", log); lf.ShowDialog(this); });
            }
            catch (Exception ex) { this.Invoke(() => MessageBox.Show($"导入脚本时出错：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error)); }
        });
    }

    private void BtnRefresh_Click(object? sender, EventArgs e) => RefreshWeaponList();

    private async void RefreshWeaponList()
    {
        if (refreshing) return;
        refreshing = true;
        try
        {
            await Task.Run(() =>
            {
                string csv = Path.Combine(AppContext.BaseDirectory, "weapons.csv");
                var newWeapons = CsvService.LoadWeapons(csv);
                
                this.Invoke(() =>
                {
                    weapons = newWeapons;

                    cmbWeapons.DataSource = null;
                    cmbWeapons.DataSource = weapons;
                    cmbWeapons.DisplayMember = "PrintName";
                    cmbWeapons.ValueMember = "ScriptName";
                    if (weapons.Any())
                        cmbWeapons.SelectedIndex = 0;

                    cmbCompare.DataSource = null;
                    cmbCompare.DataSource = new List<WeaponData>(weapons);
                    cmbCompare.DisplayMember = "PrintName";
                    if (compareWeapon != null)
                    {
                        var match = weapons.FirstOrDefault(w => w.ScriptName == compareWeapon.ScriptName);
                        if (match != null) cmbCompare.SelectedItem = match;
                    }
                    else
                        cmbCompare.SelectedIndex = -1;

                    UpdateC64Labels(weapons.Any());
                });
            });
        }
        catch (Exception ex)
        {
            this.Invoke(() => MessageBox.Show($"刷新武器列表失败：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error));
        }
        finally
        {
            refreshing = false;
        }
    }

    private void Form1_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.S)
        {
            e.SuppressKeyPress = true;
            BtnSave_Click(sender, e);
        }
    }

#endregion
#region 伤害计算

    private void UpdateAllDamage()
    {
        if (currentWeapon == null) return;
        double hm = trkHead.Value * SliderStep, cm = trkChest.Value * SliderStep, sm = trkStomach.Value * SliderStep;
        double lm = trkLeg.Value * SliderStep, am = trkArm.Value * SliderStep;
        double dist = trkDistance.Value, dg = currentWeapon.DamageGeneric ?? 0, rm = (double)nudRangeModifier.Value;
        double bd = dg * Math.Pow(rm, dist / DistanceDivisor);
        double vest = chkVest.Checked ? ((currentWeapon.BulletsPerShot ?? 1) > 1 ? 0.8 : 0.9) : 1.0;
        int rpm = currentWeapon.FireRate ?? 600;
        int pellets = currentWeapon.BulletsPerShot ?? 1;

        UpdateDamageLabel(lblHeadDmg, bd * hm * pellets, 100, 1.0, rpm);
        UpdateDamageLabel(lblChestDmg, bd * cm * vest * pellets, 100, 1.0, rpm);
        UpdateDamageLabel(lblStomachDmg, bd * sm * vest * pellets, 100, 1.0, rpm);
        UpdateDamageLabel(lblLegDmg, bd * lm * pellets, 100, 1.0, rpm);
        UpdateDamageLabel(lblArmDmg, bd * am * pellets, 100, 1.0, rpm);

        UpdateCompareDamage();
    }

    private void UpdateDamageLabel(Label lbl, double damage, double hp, double pelletMult, int rpm)
    {
        double dmgPerShot = damage * pelletMult;
        if (dmgPerShot <= 0 || rpm <= 0)
        {
            lbl.Text = "= 0.0 | ∞shots | ∞ms";
            return;
        }
        int shots = (int)Math.Ceiling(hp / dmgPerShot);
        double ttkMs = (shots - 1) * 60000.0 / rpm;
        lbl.Text = $"= {damage:F1} | {shots}shots | {ttkMs:F0}ms";
    }

    private void UpdateCompareDamage()
    {
        if (compareWeapon == null)
        {
            lblCmpDmg.Text = "";
            lblCmpTtk.Text = "";
            return;
        }
        double dist = trkDistance.Value;
        double dg = compareWeapon.DamageGeneric ?? 0;
        double rm = compareWeapon.RangeModifier ?? 1.0;
        double bd = dg * Math.Pow(rm, dist / DistanceDivisor);
        double vest = chkVest.Checked ? ((compareWeapon.BulletsPerShot ?? 1) > 1 ? 0.8 : 0.9) : 1.0;
        int pellets = compareWeapon.BulletsPerShot ?? 1;
        double head = bd * (compareWeapon.DamageHeadMultiplier ?? 1.0) * pellets;
        double chest = bd * (compareWeapon.DamageChestMultiplier ?? 1.0) * vest * pellets;
        double stomach = bd * (compareWeapon.DamageStomachMultiplier ?? 1.0) * vest * pellets;
        double leg = bd * (compareWeapon.DamageLegMultiplier ?? 1.0) * pellets;
        double arm = bd * (compareWeapon.DamageArmMultiplier ?? 1.0) * pellets;
        lblCmpDmg.Text = $"Cmp: {dg:F0} | H:{head:F1} C:{chest:F1} S:{stomach:F1} L:{leg:F1} A:{arm:F1}";
    }

#endregion
#region 可视化

    private void PnlSpread_Paint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Black);
        int cx = pnlSpread.Width / 2, cy = pnlSpread.Height / 2;
        float r = Math.Min(cx, cy) - 20, s = r / 15f;

        DrawSpreadCircle(g, cx, cy, (float)((double)nudHipSpread.Value * s), Color.Red, DashStyle.Solid);
        DrawSpreadCircle(g, cx, cy, (float)((double)nudAdsSpread.Value * s), Color.Red, DashStyle.Dash);
        if ((double)nudBipodHipSpread.Value > 0) DrawSpreadCircle(g, cx, cy, (float)((double)nudBipodHipSpread.Value * s), Color.Lime, DashStyle.Solid);
        if ((double)nudBipodAdsSpread.Value > 0) DrawSpreadCircle(g, cx, cy, (float)((double)nudBipodAdsSpread.Value * s), Color.Lime, DashStyle.Dash);
        if (compareWeapon != null)
        {
            DrawSpreadCircle(g, cx, cy, (float)((compareWeapon.BulletSpread ?? 1.0) * s), Color.DodgerBlue, DashStyle.Solid);
            DrawSpreadCircle(g, cx, cy, (float)((compareWeapon.BulletSpreadDegreesIronsighted ?? 1.0) * s), Color.DodgerBlue, DashStyle.Dash);
            if ((compareWeapon.BulletSpreadDegreesBipod ?? 0) > 0) DrawSpreadCircle(g, cx, cy, (float)((compareWeapon.BulletSpreadDegreesBipod ?? 0) * s), Color.Yellow, DashStyle.Solid);
            if ((compareWeapon.BulletSpreadDegreesBipodIronsighted ?? 0) > 0) DrawSpreadCircle(g, cx, cy, (float)((compareWeapon.BulletSpreadDegreesBipodIronsighted ?? 0) * s), Color.Yellow, DashStyle.Dash);
        }
        DrawCurrentSpreadLegend(g, 5, 5);
        DrawCompareSpreadLegend(g, 5, pnlSpread.Height - 5);
        if (compareWeapon != null)
        {
            using var df = new Font("Consolas", 7, FontStyle.Bold);
            using var db = new SolidBrush(Color.FromArgb(200, 200, 200));
            float dx = pnlSpread.Width - 5;
            float dy = pnlSpread.Height - 5;
            string s1 = $"H:{compareWeapon.BulletSpread:F2}";
            string s2 = $"A:{compareWeapon.BulletSpreadDegreesIronsighted:F2}";
            string s3 = $"BH:{compareWeapon.BulletSpreadDegreesBipod:F2}";
            string s4 = $"BA:{compareWeapon.BulletSpreadDegreesBipodIronsighted:F2}";
            var s1s = g.MeasureString(s1, df); g.DrawString(s1, df, db, dx - s1s.Width, dy - 60);
            var s2s = g.MeasureString(s2, df); g.DrawString(s2, df, db, dx - s2s.Width, dy - 44);
            var s3s = g.MeasureString(s3, df); g.DrawString(s3, df, db, dx - s3s.Width, dy - 28);
            var s4s = g.MeasureString(s4, df); g.DrawString(s4, df, db, dx - s4s.Width, dy - 12);
        }
        string curCal = currentWeapon?.PrimaryAmmo ?? "";
        string cmpCal = compareWeapon?.PrimaryAmmo ?? "";
        string calText = "";
        if (!string.IsNullOrEmpty(curCal) && !string.IsNullOrEmpty(cmpCal))
            calText = $"{curCal} | {cmpCal}";
        else if (!string.IsNullOrEmpty(curCal))
            calText = curCal;
        else if (!string.IsNullOrEmpty(cmpCal))
            calText = cmpCal;

        if (!string.IsNullOrEmpty(calText))
        {
            using var calFont = new Font("Arial", 7, FontStyle.Bold);
            using var calBrush = new SolidBrush(Color.FromArgb(180, 180, 180));
            var calSize = g.MeasureString(calText, calFont);
            g.DrawString(calText, calFont, calBrush, pnlSpread.Width - calSize.Width - 5, 5);
        }
    }

    private void DrawSpreadCircle(Graphics g, int cx, int cy, float r, Color c, DashStyle ds)
    {
        using var p = new Pen(c, 1.2f) { DashStyle = ds };
        g.DrawEllipse(p, cx - r, cy - r, r * 2, r * 2);
    }

    private void DrawCurrentSpreadLegend(Graphics g, int x, int y)
    {
        using var f = new Font("Arial", 7);
        using var red = new SolidBrush(Color.Red);
        using var green = new SolidBrush(Color.Lime);
        g.DrawString("━ Hip", f, red, x, y);
        g.DrawString("┅ ADS", f, red, x, y + 14);
        g.DrawString("━ Bipod Hip", f, green, x, y + 28);
        g.DrawString("┅ Bipod ADS", f, green, x, y + 42);
    }

    private void DrawCompareSpreadLegend(Graphics g, int x, int bottomY)
    {
        using var f = new Font("Arial", 7);
        using var white = new SolidBrush(Color.White);
        using var blue = new SolidBrush(Color.DodgerBlue);
        using var yellow = new SolidBrush(Color.Yellow);
        int y = bottomY - 70;
        g.DrawString("Compare", f, white, x, y);
        g.DrawString("━ Hip", f, blue, x, y +14 );
        g.DrawString("┅ ADS", f, blue, x, y + 28);
        g.DrawString("━ Bipod Hip", f, yellow, x, y + 42);
        g.DrawString("┅ Bipod ADS", f, yellow, x, y + 56);
    }

    private void PnlRecoil_Paint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Black);
        int cx = pnlRecoil.Width / 2, cy = pnlRecoil.Height - 30, shots = 30;
        float s = Math.Min(2.5f, (pnlRecoil.Height - 40) / ((float)nudHipRecoilUp.Value * shots));
        DrawRecoilSector(g, cx, cy, (float)nudHipRecoilUp.Value, (float)nudHipRecoilRight.Value, shots, s, Color.FromArgb(80, 255, 0, 0), Color.Red, "Hip", "right");
        DrawRecoilSector(g, cx, cy, (float)nudAdsRecoilUp.Value, (float)nudAdsRecoilRight.Value, shots, s, Color.FromArgb(80, 0, 255, 0), Color.Lime, "ADS", "right");
        if (compareWeapon != null)
        {
            DrawRecoilSector(g, cx, cy, (float)(compareWeapon.ViewSlideRecoilUp ?? 0), (float)(compareWeapon.ViewSlideRecoilRight ?? 0), shots, s, Color.FromArgb(40, 0, 191, 255), Color.DeepSkyBlue, "Cmp Hip", "left");
            DrawRecoilSector(g, cx, cy, (float)(compareWeapon.ViewSlideRecoilIronsightUp ?? 0), (float)(compareWeapon.ViewSlideRecoilIronsightRight ?? 0), shots, s, Color.FromArgb(40, 255, 165, 0), Color.Yellow, "Cmp ADS", "left");
        }
        DrawRecoilLegend(g, 8, pnlRecoil.Height - 50);
        if (compareWeapon != null)
        {
            using var df = new Font("Consolas", 7, FontStyle.Bold);
            using var db = new SolidBrush(Color.FromArgb(200, 200, 200));
            float dx = pnlRecoil.Width - 5;
            float dy = pnlRecoil.Height - 5;
            string r1 = $"HU:{compareWeapon.ViewSlideRecoilUp:F2}";
            string r2 = $"HR:{compareWeapon.ViewSlideRecoilRight:F2}";
            string r3 = $"AU:{compareWeapon.ViewSlideRecoilIronsightUp:F2}";
            string r4 = $"AR:{compareWeapon.ViewSlideRecoilIronsightRight:F2}";
            var r1s = g.MeasureString(r1, df); g.DrawString(r1, df, db, dx - r1s.Width, dy - 60);
            var r2s = g.MeasureString(r2, df); g.DrawString(r2, df, db, dx - r2s.Width, dy - 44);
            var r3s = g.MeasureString(r3, df); g.DrawString(r3, df, db, dx - r3s.Width, dy - 28);
            var r4s = g.MeasureString(r4, df); g.DrawString(r4, df, db, dx - r4s.Width, dy - 12);
        }
    }

    private void DrawRecoilSector(Graphics g, int cx, int cy, float up, float right, int shots, float scale, Color fill, Color line, string label, string side)
    {
        float totalUp = up * shots * scale;
        float totalRight = right * shots * scale;

        float radius = totalUp;
        if (radius <= 0) return;

        float halfAngle = (float)Math.Atan2(totalRight, totalUp);
        float startAngle = 270 - halfAngle * 180f / (float)Math.PI;
        float sweepAngle = 2 * halfAngle * 180f / (float)Math.PI;

        using var b = new SolidBrush(fill);
        g.FillPie(b, cx - radius, cy - radius, radius * 2, radius * 2, startAngle, sweepAngle);

        using var p = new Pen(line, 1.2f);
        g.DrawPie(p, cx - radius, cy - radius, radius * 2, radius * 2, startAngle, sweepAngle);

        using var f = new Font("Arial", 6);
        using var br = new SolidBrush(line);

        float labelX, labelY;
        if (side == "left")
        {
            labelX = cx - totalRight - 42;
            labelY = cy - totalUp - 5;
        }
        else
        {
            labelX = cx + totalRight + 4;
            labelY = cy - totalUp - 5;
        }

        g.DrawString(label, f, br, labelX, labelY);
    }

    private void DrawRecoilLegend(Graphics g, int x, int y)
    {
        using var f = new Font("Arial", 7);
        using var w = new SolidBrush(Color.White);
        g.DrawString("Red/Green = Hip/ADS", f, w, x, y);
        g.DrawString("Blue/Yellow = Compare", f, w, x, y + 14);
        g.DrawString("30 rounds", f, w, x, y + 28);
    }

#endregion
#region 工具方法

    private void LoadWeaponToControls(WeaponData w)
    {
        SetControlsValue(trkHead, nudHead, w.DamageHeadMultiplier ?? 1.0);
        SetControlsValue(trkChest, nudChest, w.DamageChestMultiplier ?? 1.0);
        SetControlsValue(trkStomach, nudStomach, w.DamageStomachMultiplier ?? 1.0);
        SetControlsValue(trkLeg, nudLeg, w.DamageLegMultiplier ?? 1.0);
        SetControlsValue(trkArm, nudArm, w.DamageArmMultiplier ?? 1.0);
        nudHipSpread.Value = (decimal)(w.BulletSpread ?? 1.0);
        nudAdsSpread.Value = (decimal)(w.BulletSpreadDegreesIronsighted ?? 1.0);
        nudBipodHipSpread.Value = (decimal)(w.BulletSpreadDegreesBipod ?? 0);
        nudBipodAdsSpread.Value = (decimal)(w.BulletSpreadDegreesBipodIronsighted ?? 0);
        nudHipRecoilUp.Value = (decimal)(w.ViewSlideRecoilUp ?? 0);
        nudHipRecoilRight.Value = (decimal)(w.ViewSlideRecoilRight ?? 0);
        nudAdsRecoilUp.Value = (decimal)(w.ViewSlideRecoilIronsightUp ?? 0);
        nudAdsRecoilRight.Value = (decimal)(w.ViewSlideRecoilIronsightRight ?? 0);
        txtFireModes.Text = w.FireModes ?? "";
        nudFireRate.Value = w.FireRate ?? 0;
        nudRangeModifier.Value = (decimal)(w.RangeModifier ?? 1.0);
        txtCapacity.Text = w.ClipSize ?? w.DefaultClip?.ToString() ?? "";
        nudExtraBulletChamber.Value = w.ExtraBulletChamber ?? 0;
        nudBulletsPerShot.Value = w.BulletsPerShot ?? 1;
        nudIronsightSpeedScale.Value = (decimal)(w.IronsightSpeedScale ?? 1.0);
        nudWeight.Value = (decimal)(w.Weight ?? 0);
        nudZMBuyPrice.Value = w.ZMBuyPrice ?? 0;
        nudZMWeight.Value = w.ZMWeight ?? 0;
        nudMetalPen.Value = (decimal)(w.MetalPenetrationDepth ?? 0);
        nudGlassPen.Value = (decimal)(w.GlassPenetrationDepth ?? 0);
        nudConcretePen.Value = (decimal)(w.ConcretePenetrationDepth ?? 0);
        nudWoodPen.Value = (decimal)(w.WoodPenetrationDepth ?? 0);
        nudOtherPen.Value = (decimal)(w.OtherPenetrationDepth ?? 0);
        nudMetalDmgMod.Value = (decimal)(w.MetalDamageModifier ?? 1.0);
        nudGlassDmgMod.Value = (decimal)(w.GlassDamageModifier ?? 1.0);
        nudConcreteDmgMod.Value = (decimal)(w.ConcreteDamageModifier ?? 1.0);
        nudWoodDmgMod.Value = (decimal)(w.WoodDamageModifier ?? 1.0);
        nudOtherDmgMod.Value = (decimal)(w.OtherDamageModifier ?? 1.0);
        nudCrouchSpread.Value = (decimal)(w.CrouchSpreadMultiplier ?? 0);
        nudProneSpread.Value = (decimal)(w.ProneSpreadMultiplier ?? 0);
        nudStandMoveSpread.Value = (decimal)(w.StandMoveSpreadMultiplier ?? 0);
        nudSneakMoveSpread.Value = (decimal)(w.SneakMoveSpreadMultiplier ?? 0);
        nudCrouchMoveSpread.Value = (decimal)(w.CrouchMoveSpreadMultiplier ?? 0);
        nudJumpSpread.Value = (decimal)(w.JumpSpreadMultiplier ?? 0);
        nudDamageGeneric.Value = (decimal)(w.DamageGeneric ?? 0);
    }

    private void SetControlsValue(TrackBar tb, NumericUpDown nud, double v)
    {
        int iv = (int)Math.Round(v / SliderStep);
        iv = Math.Max(tb.Minimum, Math.Min(tb.Maximum, iv));
        tb.Value = iv;
        nud.Value = Math.Round((decimal)v, 2);
    }

    private void SaveControlsToWeapon(WeaponData w)
    {
        w.DamageHeadMultiplier = trkHead.Value * SliderStep;
        w.DamageChestMultiplier = trkChest.Value * SliderStep;
        w.DamageStomachMultiplier = trkStomach.Value * SliderStep;
        w.DamageLegMultiplier = trkLeg.Value * SliderStep;
        w.DamageArmMultiplier = trkArm.Value * SliderStep;
        w.BulletSpread = (double)nudHipSpread.Value;
        w.BulletSpreadDegreesIronsighted = (double)nudAdsSpread.Value;
        w.BulletSpreadDegreesBipod = (double)nudBipodHipSpread.Value;
        w.BulletSpreadDegreesBipodIronsighted = (double)nudBipodAdsSpread.Value;
        w.ViewSlideRecoilUp = (double)nudHipRecoilUp.Value;
        w.ViewSlideRecoilRight = (double)nudHipRecoilRight.Value;
        w.ViewSlideRecoilIronsightUp = (double)nudAdsRecoilUp.Value;
        w.ViewSlideRecoilIronsightRight = (double)nudAdsRecoilRight.Value;
        w.FireModes = txtFireModes.Text;
        w.FireRate = (int)nudFireRate.Value;
        w.RangeModifier = (double)nudRangeModifier.Value;
        w.ClipSize = txtCapacity.Text;
        var clipParts = txtCapacity.Text.Split('/');
        if (clipParts.Length > 0 && int.TryParse(clipParts[0], out int firstNum))
            w.DefaultClip = firstNum;
        w.ExtraBulletChamber = (int)nudExtraBulletChamber.Value;
        w.BulletsPerShot = (int)nudBulletsPerShot.Value;
        w.IronsightSpeedScale = (double)nudIronsightSpeedScale.Value;
        w.Weight = (double)nudWeight.Value;
        w.ZMBuyPrice = (int)nudZMBuyPrice.Value;
        w.ZMWeight = (int)nudZMWeight.Value;
        w.MetalPenetrationDepth = (double)nudMetalPen.Value;
        w.GlassPenetrationDepth = (double)nudGlassPen.Value;
        w.ConcretePenetrationDepth = (double)nudConcretePen.Value;
        w.WoodPenetrationDepth = (double)nudWoodPen.Value;
        w.OtherPenetrationDepth = (double)nudOtherPen.Value;
        w.MetalDamageModifier = (double)nudMetalDmgMod.Value;
        w.GlassDamageModifier = (double)nudGlassDmgMod.Value;
        w.ConcreteDamageModifier = (double)nudConcreteDmgMod.Value;
        w.WoodDamageModifier = (double)nudWoodDmgMod.Value;
        w.OtherDamageModifier = (double)nudOtherDmgMod.Value;
        w.CrouchSpreadMultiplier = (double)nudCrouchSpread.Value;
        w.ProneSpreadMultiplier = (double)nudProneSpread.Value;
        w.StandMoveSpreadMultiplier = (double)nudStandMoveSpread.Value;
        w.SneakMoveSpreadMultiplier = (double)nudSneakMoveSpread.Value;
        w.CrouchMoveSpreadMultiplier = (double)nudCrouchMoveSpread.Value;
        w.JumpSpreadMultiplier = (double)nudJumpSpread.Value;
        w.DamageGeneric = (double)nudDamageGeneric.Value;
    }

    private static void EnableDoubleBuffering(Control control)
    {
        typeof(Control).InvokeMember("DoubleBuffered",
            BindingFlags.SetProperty | BindingFlags.Instance | BindingFlags.NonPublic,
            null, control, new object[] { true });
    }

    private (TrackBar, NumericUpDown, Label) CreateSliderRow(Control parent, string text, ref int y)
    {
        parent.Controls.Add(new Label { Text = text, Location = new Point(8, y + 10), Size = new Size(35, 18), TextAlign = ContentAlignment.MiddleLeft });
        var tb = new TrackBar { Location = new Point(45, y), Size = new Size(410, 38), Minimum = (int)(SliderMin / SliderStep), Maximum = (int)(SliderMax / SliderStep), TickFrequency = (int)(0.5 / SliderStep), Value = (int)(1.0 / SliderStep) };
        var nud = new NumericUpDown { Location = new Point(460, y + 6), Size = new Size(55, 22), DecimalPlaces = 2, Increment = 0.01m, Minimum = (decimal)SliderMin, Maximum = 1000m, Value = 1.00m };
        var lbl = new Label { Text = "= 0.0 | ∞shots | ∞ms", Location = new Point(525, y + 8), Size = new Size(145, 20), TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.DarkRed, Font = new Font("Arial", 8, FontStyle.Bold) };
        parent.Controls.Add(tb);
        parent.Controls.Add(nud);
        parent.Controls.Add(lbl);

        tb.Tag = nud;
        nud.Tag = tb;
        tb.ValueChanged += SliderChanged;
        nud.ValueChanged += NumericChanged;

        y += 36;
        return (tb, nud, lbl);
    }

    private NumericUpDown CreateNullableNumericRow(Control parent, string text, int x, int y, decimal max)
    {
        parent.Controls.Add(new Label { Text = text, Location = new Point(x, y + 3), Size = new Size(80, 18), TextAlign = ContentAlignment.MiddleLeft });
        var nud = new NumericUpDown { Location = new Point(x + 85, y + 1), Size = new Size(70, 22), DecimalPlaces = 2, Increment = 0.01m, Minimum = 0m, Maximum = max };
        parent.Controls.Add(nud);
        return nud;
    }

    private NumericUpDown CreateNullableIntNumericRow(Control parent, string text, int x, int y, decimal max)
    {
        parent.Controls.Add(new Label { Text = text, Location = new Point(x, y + 3), Size = new Size(80, 18), TextAlign = ContentAlignment.MiddleLeft });
        var nud = new NumericUpDown { Location = new Point(x + 85, y + 1), Size = new Size(70, 22), DecimalPlaces = 0, Increment = 1, Minimum = 0m, Maximum = max };
        parent.Controls.Add(nud);
        return nud;
    }

    private TextBox CreateTextBoxRow(Control parent, string text, int x, int y)
    {
        parent.Controls.Add(new Label { Text = text, Location = new Point(x, y + 3), Size = new Size(80, 18), TextAlign = ContentAlignment.MiddleLeft });
        var tb = new TextBox { Location = new Point(x + 85, y + 1), Size = new Size(70, 22) };
        parent.Controls.Add(tb);
        return tb;
    }
}

#endregion
//下面这玩意感觉单独建个文件没啥意义

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
        var txt = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = new Font("Consolas", 9), Text = logText };
        this.Controls.Add(txt);
    }
}