using System.Threading.Tasks;
using System.Windows.Forms;
using WeaponDamageCalc.Models;
using WeaponDamageCalc.Services;

namespace WeaponDamageCalc;

public partial class Form1
{
    #region 选择保存检测
    private void WeaponSelectedL(object? sender, EventArgs e)
    {
        if (initializing)
        {
            if (cmbWeaponsL.SelectedItem is WeaponData initW)
            {
                currentWeaponLeft = initW;
                LoadWeaponToControls(initW, true);
            }
            return;
            //初始化阶段直接加载不检查
        }
        if (currentWeaponLeft != null && HasUnsavedChanges(true))
        {
            var result = MessageBox.Show("Unsaved changes to left weapon. Discard?",
                "Unsaved Changes", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (result != DialogResult.Yes)
            {
                cmbWeaponsL.SelectedIndexChanged -= WeaponSelectedL;
                cmbWeaponsL.SelectedItem = currentWeaponLeft;
                cmbWeaponsL.SelectedIndexChanged += WeaponSelectedL;
                return;
            }
        }
        if (cmbWeaponsL.SelectedItem is WeaponData w)
        {
            currentWeaponLeft = w;
            LoadWeaponToControls(w, true);
            UpdateAllDamage();
            pnlSpread.Invalidate();
            pnlRecoil.Invalidate();
            StoreSnapshot(true);
            if (showingDovStats && WeaponHasDovStats(w))
            {
                updatingControls = true;
                LoadDovStatsToControls(true);
                SetDovReadonly(true);
                StoreSnapshot(true);
                updatingControls = false;
            }
            if (showingDovStats && !WeaponHasDovStats(w))
            {
                RestoreAllNudEnabled(true);
                if (!WeaponHasDovStats(currentWeaponRight))
                    ExitDovMode();
            }
        }
    }

    private void WeaponSelectedR(object? sender, EventArgs e)
    {
        if (initializing)
        {
            if (cmbWeaponsR.SelectedItem is WeaponData initW)
            {
                currentWeaponRight = initW;
                LoadWeaponToControls(initW, false);
            }
            return;
        }
        if (currentWeaponRight != null && HasUnsavedChanges(false))
        {
            var result = MessageBox.Show("Unsaved changes to right weapon. Discard?",
                "Unsaved Changes", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (result != DialogResult.Yes)
            {
                cmbWeaponsR.SelectedIndexChanged -= WeaponSelectedR;
                cmbWeaponsR.SelectedItem = currentWeaponRight;
                cmbWeaponsR.SelectedIndexChanged += WeaponSelectedR;
                return;
            }
        }
        if (cmbWeaponsR.SelectedItem is WeaponData w)
        {
            currentWeaponRight = w;
            LoadWeaponToControls(w, false);
            UpdateAllDamage();
            pnlSpread.Invalidate();
            pnlRecoil.Invalidate();
            StoreSnapshot(false);
            if (showingDovStats && WeaponHasDovStats(w))
            {
                updatingControls = true;
                LoadDovStatsToControls(false);
                SetDovReadonly(false);
                StoreSnapshot(false);
                updatingControls = false;
            }
            if (showingDovStats && !WeaponHasDovStats(w))
            {
                RestoreAllNudEnabled(false);
                if (!WeaponHasDovStats(currentWeaponLeft))
                    ExitDovMode();
            }
        }
    }

    private bool HasUnsavedChanges(bool isLeft)
    {
        var original = isLeft ? snapshotLeft : snapshotRight;
        if (original == null) return false;
        //同一武器时只检查焦点侧 因为保存时也只保存焦点侧
        if (currentWeaponLeft != null && currentWeaponRight != null
            && ReferenceEquals(currentWeaponLeft, currentWeaponRight))
        {
            bool focusLeft = IsControlOnLeft(this.ActiveControl);
            if (isLeft != focusLeft) return false;
        }
        var temp = new WeaponData();
        SaveControlsToWeapon(temp, isLeft);
        return !WeaponDataEquals(temp, original);
        //控件值写入临时对象与原始武器逐字段比对
    }

    private static bool WeaponDataEquals(WeaponData a, WeaponData b)//双浮点比对容忍0.0001误差 防止SaveControlsToWeapon的SliderStep浮点往返造成假阳性
    {
        return NullableEquals(a.DamageHeadMultiplier, b.DamageHeadMultiplier)
            && NullableEquals(a.DamageChestMultiplier, b.DamageChestMultiplier)
            && NullableEquals(a.DamageStomachMultiplier, b.DamageStomachMultiplier)
            && NullableEquals(a.DamageLegMultiplier, b.DamageLegMultiplier)
            && NullableEquals(a.DamageArmMultiplier, b.DamageArmMultiplier)
            && NullableEquals(a.BulletSpread, b.BulletSpread)
            && NullableEquals(a.BulletSpreadDegreesIronsighted, b.BulletSpreadDegreesIronsighted)
            && NullableEquals(a.BulletSpreadDegreesBipod, b.BulletSpreadDegreesBipod)
            && NullableEquals(a.BulletSpreadDegreesBipodIronsighted, b.BulletSpreadDegreesBipodIronsighted)
            && NullableEquals(a.ViewSlideRecoilUp, b.ViewSlideRecoilUp)
            && NullableEquals(a.ViewSlideRecoilRight, b.ViewSlideRecoilRight)
            && NullableEquals(a.ViewSlideRecoilIronsightUp, b.ViewSlideRecoilIronsightUp)
            && NullableEquals(a.ViewSlideRecoilIronsightRight, b.ViewSlideRecoilIronsightRight)
            && string.Equals(a.FireModes, b.FireModes)
            && IntNullableEquals(a.FireRate, b.FireRate)
            && IntNullableEquals(a.SecondaryFireRate, b.SecondaryFireRate)
            && NullableEquals(a.RangeModifier, b.RangeModifier)
            && string.Equals(a.ClipSize, b.ClipSize)
            && IntNullableEquals(a.ExtraBulletChamber, b.ExtraBulletChamber)
            && IntNullableEquals(a.BulletsPerShot, b.BulletsPerShot)
            && NullableEquals(a.IronsightSpeedScale, b.IronsightSpeedScale)
            && IntNullableEquals(a.IronSight, b.IronSight)
            && NullableEquals(a.Weight, b.Weight)
            && IntNullableEquals(a.ZMBuyPrice, b.ZMBuyPrice)
            && IntNullableEquals(a.ZMWeight, b.ZMWeight)
            && NullableEquals(a.MetalPenetrationDepth, b.MetalPenetrationDepth)
            && NullableEquals(a.GlassPenetrationDepth, b.GlassPenetrationDepth)
            && NullableEquals(a.ConcretePenetrationDepth, b.ConcretePenetrationDepth)
            && NullableEquals(a.WoodPenetrationDepth, b.WoodPenetrationDepth)
            && NullableEquals(a.OtherPenetrationDepth, b.OtherPenetrationDepth)
            && NullableEquals(a.MetalDamageModifier, b.MetalDamageModifier)
            && NullableEquals(a.GlassDamageModifier, b.GlassDamageModifier)
            && NullableEquals(a.ConcreteDamageModifier, b.ConcreteDamageModifier)
            && NullableEquals(a.WoodDamageModifier, b.WoodDamageModifier)
            && NullableEquals(a.OtherDamageModifier, b.OtherDamageModifier)
            && NullableEquals(a.CrouchSpreadMultiplier, b.CrouchSpreadMultiplier)
            && NullableEquals(a.ProneSpreadMultiplier, b.ProneSpreadMultiplier)
            && NullableEquals(a.StandMoveSpreadMultiplier, b.StandMoveSpreadMultiplier)
            && NullableEquals(a.SneakMoveSpreadMultiplier, b.SneakMoveSpreadMultiplier)
            && NullableEquals(a.CrouchMoveSpreadMultiplier, b.CrouchMoveSpreadMultiplier)
            && NullableEquals(a.JumpSpreadMultiplier, b.JumpSpreadMultiplier)
            && NullableEquals(a.DamageGeneric, b.DamageGeneric);
    }

    private static bool NullableEquals(double? a, double? b)
    {
        double va = a ?? 0.0;
        double vb = b ?? 0.0;
        return Math.Abs(va - vb) < 0.001;
        //容差比较 防止掉精度导致误判为未保存
    }

    private static bool IntNullableEquals(int? a, int? b)
    {
        if (!a.HasValue && !b.HasValue) return true;
        if (!a.HasValue || !b.HasValue) return false;
        return a.Value == b.Value;
    }

    #endregion
    #region 联动刷新

    private void SliderChangedL(object? sender, EventArgs e)
    {
        if (updatingControls) return;
        updatingControls = true;//都是防止滑块和数字框互相触发死循环
        if (sender is TrackBar tb && tb.Tag is NumericUpDown nud)
            nud.Value = Math.Round((decimal)(tb.Value * SliderStep), 2);
        updatingControls = false;
        UpdateAllDamage();
    }

    private void SliderChangedR(object? sender, EventArgs e)
    {
        if (updatingControls) return;
        updatingControls = true;
        if (sender is TrackBar tb && tb.Tag is NumericUpDown nud)
            nud.Value = Math.Round((decimal)(tb.Value * SliderStep), 2);
        updatingControls = false;
        UpdateAllDamage();
    }

    private void NumericChangedL(object? sender, EventArgs e)
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

    private void NumericChangedR(object? sender, EventArgs e)
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

    private void SpreadRecoilChangedL(object? sender, EventArgs e)
    {
        pnlSpread.Invalidate();
        pnlRecoil.Invalidate();
    }

    private void SpreadRecoilChangedR(object? sender, EventArgs e)
    {
        pnlSpread.Invalidate();
        pnlRecoil.Invalidate();
    }

    private void RangeModifierChangedL(object? sender, EventArgs e)
    {
        UpdateAllDamage();
    }

    private void RangeModifierChangedR(object? sender, EventArgs e)
    {
        UpdateAllDamage();
    }

    #endregion
    #region 保存导入导出

    private async void BtnSave_Click(object? sender, EventArgs e)
    {
        if (System.Threading.Interlocked.Exchange(ref saveLock, 1) != 0) return;
        try
        {
            //强制提交活跃控件的待定输入 防止NUD焦点未移走导致值未更新
            var active = this.ActiveControl;
            if (active != null)
            {
                this.ActiveControl = null;
                active.Focus();
            }
            bool sameWeapon = currentWeaponLeft != null && currentWeaponRight != null
                && ReferenceEquals(currentWeaponLeft, currentWeaponRight);
            if (sameWeapon)
            {
                //同一武器时只保存焦点所在侧 防止后保存的一侧覆盖前一侧
                bool focusLeft = IsControlOnLeft(active);
                if (focusLeft)
                {
                    SaveControlsToWeapon(currentWeaponLeft!, true);
                    StoreSnapshot(true);
                    if (showingDovStats && WeaponHasDovStats(currentWeaponLeft))
                        LoadDovStatsToControls(false);
                    else
                        LoadWeaponToControls(currentWeaponLeft!, false);
                    UpdateAllDamage();
                    pnlSpread.Invalidate();
                    pnlRecoil.Invalidate();
                }
                else
                {
                    SaveControlsToWeapon(currentWeaponRight!, false);
                    StoreSnapshot(false);
                    if (showingDovStats && WeaponHasDovStats(currentWeaponRight))
                        LoadDovStatsToControls(true);
                    else
                        LoadWeaponToControls(currentWeaponRight!, true);
                    UpdateAllDamage();
                    pnlSpread.Invalidate();
                    pnlRecoil.Invalidate();
                }
            }
            else
            {
                if (currentWeaponLeft != null) SaveControlsToWeapon(currentWeaponLeft, true);
                if (currentWeaponRight != null) SaveControlsToWeapon(currentWeaponRight, false);
            }
            if (showingDovStats)
            {
                if (currentWeaponLeft != null) SyncDovFields(currentWeaponLeft);
                if (currentWeaponRight != null && !ReferenceEquals(currentWeaponLeft, currentWeaponRight))
                    SyncDovFields(currentWeaponRight);
            }
            var savedOriginalTitle = this.Text;
            this.Text = "Saved!";
            try
            {
                CsvService.SaveWeapons(Path.Combine(AppContext.BaseDirectory, "weapons.csv"), weapons);
                StoreSnapshot(true);
                StoreSnapshot(false);
                await Task.Delay(1145);
            }
            catch (Exception ex) { MessageBox.Show($"Save failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { this.Text = savedOriginalTitle; }
        }
        finally { System.Threading.Interlocked.Exchange(ref saveLock, 0); }
    }

    private void BtnCsvToScripts_Click(object? sender, EventArgs e)
    {
        string initialDir = string.IsNullOrEmpty(lastScriptsDir) ? AppContext.BaseDirectory : lastScriptsDir;
        using var dlg = new FolderBrowserDialog { Description = "Select the folder containing weapon scripts (will be overwritten)", UseDescriptionForTitle = true, InitialDirectory = initialDir };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        lastScriptsDir = dlg.SelectedPath;
        string dir = dlg.SelectedPath;
        if (MessageBox.Show($"Overwrite all scripts in the folder below with CSV data?\n\n{dir}", "Confirm Overwrite", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        string csv = Path.Combine(AppContext.BaseDirectory, "weapons.csv");
        Task.Run(() =>
        {
            try
            {
                string log = WeaponScriptService.ExportCsvToScripts(csv, dir);
                this.Invoke(() => { using var lf = new LogForm("Export Complete", log); lf.ShowDialog(this); });
            }
            catch (Exception ex) { this.Invoke(() => MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)); }
        });
    }

    private async void BtnQuickExport_Click(object? sender, EventArgs e)
    {
        if (System.Threading.Interlocked.Exchange(ref saveLock, 1) != 0) return;
        try
        {
            if (sender is not Button btn) return;
            bool confirmed = btn.Tag is true;
            if (!confirmed)
            {
                if (string.IsNullOrEmpty(lastScriptsDir))
                {
                    string initialDir = AppContext.BaseDirectory;
                    using var dlg = new FolderBrowserDialog { Description = "Select the folder containing weapon scripts (will be overwritten)", UseDescriptionForTitle = true, InitialDirectory = initialDir };
                    if (dlg.ShowDialog() != DialogResult.OK) return;
                    lastScriptsDir = dlg.SelectedPath;
                }
                btn.Text = "confirm";
                btn.Tag = true;
                return;
            }
            //强制提交活跃控件输入
            var active = this.ActiveControl;
            if (active != null) { this.ActiveControl = null; active.Focus(); }
            bool sameWeapon = currentWeaponLeft != null && currentWeaponRight != null
                && ReferenceEquals(currentWeaponLeft, currentWeaponRight);
            if (sameWeapon)
            {
                bool focusLeft = IsControlOnLeft(active);
                if (focusLeft)
                {
                    SaveControlsToWeapon(currentWeaponLeft!, true);
                    StoreSnapshot(true);
                    if (showingDovStats && WeaponHasDovStats(currentWeaponLeft))
                        LoadDovStatsToControls(false);
                    else
                        LoadWeaponToControls(currentWeaponLeft!, false);
                }
                else
                {
                    SaveControlsToWeapon(currentWeaponRight!, false);
                    StoreSnapshot(false);
                    if (showingDovStats && WeaponHasDovStats(currentWeaponRight))
                        LoadDovStatsToControls(true);
                    else
                        LoadWeaponToControls(currentWeaponRight!, true);
                }
            }
            else
            {
                if (currentWeaponLeft != null) SaveControlsToWeapon(currentWeaponLeft, true);
                if (currentWeaponRight != null) SaveControlsToWeapon(currentWeaponRight, false);
            }
            if (showingDovStats)
            {
                if (currentWeaponLeft != null) SyncDovFields(currentWeaponLeft);
                if (currentWeaponRight != null && !ReferenceEquals(currentWeaponLeft, currentWeaponRight))
                    SyncDovFields(currentWeaponRight);
            }
            var originalTitle = this.Text;
            try
            {
                CsvService.SaveWeapons(Path.Combine(AppContext.BaseDirectory, "weapons.csv"), weapons);
                StoreSnapshot(true);
                StoreSnapshot(false);
                string csv = Path.Combine(AppContext.BaseDirectory, "weapons.csv");
                bool dovMode = showingDovStats;
                await Task.Run(() =>
                {
                    if (dovMode)
                        WeaponScriptService.ExportDovToScripts(csv, lastScriptsDir);
                    else
                        WeaponScriptService.ExportCsvToScripts(csv, lastScriptsDir);
                });
                btn.Text = "wpn_reload_script all";
                btn.Tag = false;
                this.Text = "Exported!";
                await Task.Delay(1145);
            }
            catch (Exception ex)
            {
                btn.Text = "wpn_reload_script all";
                btn.Tag = false;
                MessageBox.Show($"Quick export failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { this.Text = originalTitle; }
        }
        finally { System.Threading.Interlocked.Exchange(ref saveLock, 0); }
    }
    
    private void BtnScriptsToCsv_Click(object? sender, EventArgs e)
    {
        string initialDir = string.IsNullOrEmpty(lastScriptsDir) ? AppContext.BaseDirectory : lastScriptsDir;
        using var dlg = new FolderBrowserDialog { Description = "Select folder containing weapon scripts", UseDescriptionForTitle = true, InitialDirectory = initialDir };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        lastScriptsDir = dlg.SelectedPath;
        string dir = dlg.SelectedPath;
        string csv = Path.Combine(AppContext.BaseDirectory, "weapons.csv");
        Task.Run(() =>
        {
            try
            {
                string log = WeaponScriptService.ImportScriptsToCsv(dir, csv);
                this.Invoke(() => { RefreshWeaponList(); using var lf = new LogForm("Import Complete", log); lf.ShowDialog(this); });
            }
            catch (Exception ex) { this.Invoke(() => MessageBox.Show($"Import failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)); }
        });
    }

    private void BtnConvertToTemplate_Click(object? sender, EventArgs e)
    {
        string initialDir = string.IsNullOrEmpty(lastScriptsDir) ? AppContext.BaseDirectory : lastScriptsDir;
        using var dlg = new FolderBrowserDialog
        {
            Description = "Select folder containing weapon scripts to convert",
            UseDescriptionForTitle = true,
            InitialDirectory = initialDir
        };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        string dir = dlg.SelectedPath;

        var result = MessageBox.Show("Select conversion mode:\n\nYes = Full (keep empty keys)\nNo = Simple (remove empty keys)\nCancel = Abort",
            "Template Convert Mode", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (result == DialogResult.Cancel) return;
        bool simpleMode = (result == DialogResult.No);

        Task.Run(() =>
        {
            try
            {
                string log = Tools.ScriptToTemplateConverter.ConvertAll(dir, simpleMode);
                this.Invoke(() =>
                {
                    RefreshWeaponList();
                    using var lf = new LogForm("Template Convert", log);
                    lf.ShowDialog(this);
                });
            }
            catch (Exception ex)
            {
                this.Invoke(() => MessageBox.Show($"Template convert failed: {ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error));
            }
        });
    }

    private void ToggleDovStats(object? sender, EventArgs e)
    {
        bool leftHasDov = WeaponHasDovStats(currentWeaponLeft);
        bool rightHasDov = WeaponHasDovStats(currentWeaponRight);
        if (!leftHasDov && !rightHasDov) return;
        bool anyDirty = (currentWeaponLeft != null && HasUnsavedChanges(true))
                     || (currentWeaponRight != null && HasUnsavedChanges(false));
        if (anyDirty)
        {
            var result = MessageBox.Show("Unsaved changes will be lost. Switch stats mode?",
                "Unsaved Changes", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (result != DialogResult.Yes) return;
        }
        showingDovStats = !showingDovStats;
        if (sender is Button btn)
            btn.BackColor = showingDovStats ? Color.LightGreen : SystemColors.Control;
        
        updatingControls = true;
        if (showingDovStats)
        {
            if (leftHasDov) { LoadDovStatsToControls(true); StoreSnapshot(true); SetDovReadonly(true); }
            if (rightHasDov) { LoadDovStatsToControls(false); StoreSnapshot(false); SetDovReadonly(false); }
        }
        else
        {
            if (leftHasDov && currentWeaponLeft != null)
            {
                LoadWeaponToControls(currentWeaponLeft, true);
                StoreSnapshot(true);
            }
            if (rightHasDov && currentWeaponRight != null)
            {
                LoadWeaponToControls(currentWeaponRight, false);
                StoreSnapshot(false);
            }
            RestoreAllNudEnabled(true);
            RestoreAllNudEnabled(false);
        }
        updatingControls = false;
        UpdateAllDamage();
        pnlSpread.Invalidate();
        pnlRecoil.Invalidate();
    }

    private void SetDovReadonly(bool isLeft)
    {
        var w = isLeft ? currentWeaponLeft : currentWeaponRight;
        //穿透及其减伤永远不在dov块中
        SetNudEnabled(isLeft ? nudMetalPenL : nudMetalPenR, false);
        SetNudEnabled(isLeft ? nudGlassPenL : nudGlassPenR, false);
        SetNudEnabled(isLeft ? nudConcretePenL : nudConcretePenR, false);
        SetNudEnabled(isLeft ? nudWoodPenL : nudWoodPenR, false);
        SetNudEnabled(isLeft ? nudOtherPenL : nudOtherPenR, false);
        SetNudEnabled(isLeft ? nudMetalDmgModL : nudMetalDmgModR, false);
        SetNudEnabled(isLeft ? nudGlassDmgModL : nudGlassDmgModR, false);
        SetNudEnabled(isLeft ? nudConcreteDmgModL : nudConcreteDmgModR, false);
        SetNudEnabled(isLeft ? nudWoodDmgModL : nudWoodDmgModR, false);
        SetNudEnabled(isLeft ? nudOtherDmgModL : nudOtherDmgModR, false);
        if (w != null)//开镜散布和后座如果没有dov值则只读
        {
            bool noAds = w.IronSight == 0;
            SetNudEnabled(isLeft ? nudAdsSpreadL : nudAdsSpreadR, !noAds && w.DovBulletSpreadDegreesIronsighted != null);
            SetNudEnabled(isLeft ? nudAdsRecoilUpL : nudAdsRecoilUpR, !noAds && w.DovViewSlideRecoilIronsightUp != null);
            SetNudEnabled(isLeft ? nudAdsRecoilRightL : nudAdsRecoilRightR, !noAds && w.DovViewSlideRecoilIronsightRight != null);
            SetNudEnabled(isLeft ? nudIronsightSpeedScaleL : nudIronsightSpeedScaleR, !noAds);
        }
    }

    private static void SetNudEnabled(NumericUpDown nud, bool enabled) => nud.Enabled = enabled;

    // 只要有一个dov专属字段不为空就代表有dov数据
    private static bool WeaponHasDovStats(WeaponData? weapon) => weapon?.DovDamageGeneric != null;

    private void ExitDovMode()
    {
        if (!WeaponHasDovStats(currentWeaponLeft) && !WeaponHasDovStats(currentWeaponRight))
        {
            showingDovStats = false;
            RestoreAllNudEnabled(true);
            RestoreAllNudEnabled(false);
            foreach (Control c in this.Controls)
            {
                if (c is Button btn && btn.Text == "DoV")
                {
                    btn.BackColor = SystemColors.Control;
                    break;
                }
            }
        }
    }

    private void LoadDovStatsToControls(bool isLeft)
    {
        var weapon = isLeft ? currentWeaponLeft : currentWeaponRight;
        if (weapon == null) return;
        var temp = new WeaponData();
        CopyWeaponDataFields(weapon, temp);
        
        // 把dov专属属性映射回标准属性让LoadWeaponToControls能正确加载
        temp.ExtraBulletChamber = weapon.DovExtraBulletChamber ?? weapon.ExtraBulletChamber;
        temp.FireRate = weapon.DovFireRate ?? weapon.FireRate;
        temp.BulletSpread = weapon.DovBulletSpread ?? weapon.BulletSpread;
        temp.BulletSpreadDegreesIronsighted = weapon.DovBulletSpreadDegreesIronsighted ?? weapon.BulletSpreadDegreesIronsighted;
        temp.RangeModifier = weapon.DovRangeModifier ?? weapon.RangeModifier;
        temp.IronsightSpeedScale = weapon.DovIronsightSpeedScale ?? weapon.IronsightSpeedScale;
        temp.CrouchSpreadMultiplier = weapon.DovCrouchSpreadMultiplier ?? weapon.CrouchSpreadMultiplier;
        temp.ProneSpreadMultiplier = weapon.DovProneSpreadMultiplier ?? weapon.ProneSpreadMultiplier;
        temp.StandMoveSpreadMultiplier = weapon.DovStandMoveSpreadMultiplier ?? weapon.StandMoveSpreadMultiplier;
        temp.SneakMoveSpreadMultiplier = weapon.DovSneakMoveSpreadMultiplier ?? weapon.SneakMoveSpreadMultiplier;
        temp.CrouchMoveSpreadMultiplier = weapon.DovCrouchMoveSpreadMultiplier ?? weapon.CrouchMoveSpreadMultiplier;
        temp.JumpSpreadMultiplier = weapon.DovJumpSpreadMultiplier ?? weapon.JumpSpreadMultiplier;
        temp.ViewSlideRecoilUp = weapon.DovViewSlideRecoilUp ?? weapon.ViewSlideRecoilUp;
        temp.ViewSlideRecoilRight = weapon.DovViewSlideRecoilRight ?? weapon.ViewSlideRecoilRight;
        temp.ViewSlideRecoilIronsightUp = weapon.DovViewSlideRecoilIronsightUp ?? weapon.ViewSlideRecoilIronsightUp;
        temp.ViewSlideRecoilIronsightRight = weapon.DovViewSlideRecoilIronsightRight ?? weapon.ViewSlideRecoilIronsightRight;
        temp.DamageHeadMultiplier = weapon.DovDamageHeadMultiplier ?? weapon.DamageHeadMultiplier;
        temp.DamageChestMultiplier = weapon.DovDamageChestMultiplier ?? weapon.DamageChestMultiplier;
        temp.DamageStomachMultiplier = weapon.DovDamageStomachMultiplier ?? weapon.DamageStomachMultiplier;
        temp.DamageLegMultiplier = weapon.DovDamageLegMultiplier ?? weapon.DamageLegMultiplier;
        temp.DamageArmMultiplier = weapon.DovDamageArmMultiplier ?? weapon.DamageArmMultiplier;
        temp.DamageGeneric = weapon.DovDamageGeneric ?? weapon.DamageGeneric;
        temp.CrosshairMinDistance = weapon.DovCrosshairMinDistance ?? weapon.CrosshairMinDistance;
        temp.CrosshairDeltaDistance = weapon.DovCrosshairDeltaDistance ?? weapon.CrosshairDeltaDistance;
        temp.Weight = weapon.DovWeight ?? weapon.Weight;
        temp.ZMBuyPrice = weapon.DovZMBuyPrice ?? weapon.ZMBuyPrice;
        temp.ZMWeight = weapon.DovZMWeight ?? weapon.ZMWeight;
        temp.ClipSize = weapon.DovClipSize ?? weapon.ClipSize;
        temp.BulletSpreadDegreesBipod = weapon.DovBulletSpreadDegreesBipod ?? weapon.BulletSpreadDegreesBipod;
        temp.BulletSpreadDegreesBipodIronsighted = weapon.DovBulletSpreadDegreesBipodIronsighted ?? weapon.BulletSpreadDegreesBipodIronsighted;
        temp.SecondaryFireRate = weapon.DovSecondaryFireRate ?? weapon.SecondaryFireRate;
        temp.IronSight = weapon.DovIronSight ?? weapon.IronSight;
        

        LoadWeaponToControls(temp, isLeft);

        if (!string.IsNullOrEmpty(weapon.DovFireModes))
        {
            if (isLeft) txtFireModesL.Text = weapon.DovFireModes;
            else txtFireModesR.Text = weapon.DovFireModes;
        }
    }

    private void RestoreAllNudEnabled(bool isLeft)
    {
        var nuds = isLeft
            ? new[] { nudExtraBulletChamberL, nudBulletsPerShotL, nudIronsightSpeedScaleL, nudWeightL, nudZMBuyPriceL, nudZMWeightL,
                      nudMetalPenL, nudGlassPenL, nudConcretePenL, nudWoodPenL, nudOtherPenL,
                      nudMetalDmgModL, nudGlassDmgModL, nudConcreteDmgModL, nudWoodDmgModL, nudOtherDmgModL,
                      nudCrouchSpreadL, nudProneSpreadL, nudStandMoveSpreadL, nudSneakMoveSpreadL, nudCrouchMoveSpreadL, nudJumpSpreadL,
                      nudSecondaryFireRateL, nudIronSightL, nudAdsSpreadL, nudAdsRecoilUpL, nudAdsRecoilRightL, nudIronsightSpeedScaleL }
            : new[] { nudExtraBulletChamberR, nudBulletsPerShotR, nudIronsightSpeedScaleR, nudWeightR, nudZMBuyPriceR, nudZMWeightR,
                      nudMetalPenR, nudGlassPenR, nudConcretePenR, nudWoodPenR, nudOtherPenR,
                      nudMetalDmgModR, nudGlassDmgModR, nudConcreteDmgModR, nudWoodDmgModR, nudOtherDmgModR,
                      nudCrouchSpreadR, nudProneSpreadR, nudStandMoveSpreadR, nudSneakMoveSpreadR, nudCrouchMoveSpreadR, nudJumpSpreadR,
                      nudSecondaryFireRateR, nudIronSightR, nudAdsSpreadR, nudAdsRecoilUpR, nudAdsRecoilRightR, nudIronsightSpeedScaleR };
        foreach (var nud in nuds) nud.Enabled = true;
    }

    private void SyncDovFields(WeaponData w)
    {
        w.DovDamageHeadMultiplier = w.DamageHeadMultiplier;
        w.DovDamageChestMultiplier = w.DamageChestMultiplier;
        w.DovDamageStomachMultiplier = w.DamageStomachMultiplier;
        w.DovDamageLegMultiplier = w.DamageLegMultiplier;
        w.DovDamageArmMultiplier = w.DamageArmMultiplier;
        w.DovDamageGeneric = w.DamageGeneric;
        w.DovBulletSpread = w.BulletSpread;
        w.DovBulletSpreadDegreesIronsighted = w.BulletSpreadDegreesIronsighted;
        w.DovRangeModifier = w.RangeModifier;
        w.DovIronsightSpeedScale = w.IronsightSpeedScale;
        w.DovCrouchSpreadMultiplier = w.CrouchSpreadMultiplier;
        w.DovProneSpreadMultiplier = w.ProneSpreadMultiplier;
        w.DovStandMoveSpreadMultiplier = w.StandMoveSpreadMultiplier;
        w.DovSneakMoveSpreadMultiplier = w.SneakMoveSpreadMultiplier;
        w.DovCrouchMoveSpreadMultiplier = w.CrouchMoveSpreadMultiplier;
        w.DovJumpSpreadMultiplier = w.JumpSpreadMultiplier;
        w.DovViewSlideRecoilUp = w.ViewSlideRecoilUp;
        w.DovViewSlideRecoilRight = w.ViewSlideRecoilRight;
        w.DovViewSlideRecoilIronsightUp = w.ViewSlideRecoilIronsightUp;
        w.DovViewSlideRecoilIronsightRight = w.ViewSlideRecoilIronsightRight;
        w.DovFireRate = w.FireRate;
        w.DovExtraBulletChamber = w.ExtraBulletChamber;
        w.DovCrosshairMinDistance = w.CrosshairMinDistance;
        w.DovCrosshairDeltaDistance = w.CrosshairDeltaDistance;
        w.DovWeight = w.Weight;
        w.DovZMBuyPrice = w.ZMBuyPrice;
        w.DovZMWeight = w.ZMWeight;
        w.DovClipSize = w.ClipSize;
        w.DovBulletSpreadDegreesBipod = w.BulletSpreadDegreesBipod;
        w.DovBulletSpreadDegreesBipodIronsighted = w.BulletSpreadDegreesBipodIronsighted;
        w.DovFireModes = w.FireModes;
        w.DovSecondaryFireRate = w.SecondaryFireRate;
        w.DovIronSight = w.IronSight;
    }

    private void BtnRefresh_Click(object? sender, EventArgs e) => RefreshWeaponList();

    private async void RefreshWeaponList()
    {
        if (refreshing) return;
        refreshing = true;
        string leftName = currentWeaponLeft?.ScriptName ?? "";
        string rightName = currentWeaponRight?.ScriptName ?? "";
        try
        {
            await Task.Run(() =>
            {
                string csv = Path.Combine(AppContext.BaseDirectory, "weapons.csv");
                weapons = CsvService.LoadWeapons(csv);
                this.Invoke(() =>
                {
                    //DataSource设为null再赋值新列表 触发重新绑定
                    cmbWeaponsL.DataSource = null;
                    cmbWeaponsL.DataSource = new List<WeaponData>(weapons);
                    cmbWeaponsL.DisplayMember = "PrintName";
                    cmbWeaponsR.DataSource = null;
                    cmbWeaponsR.DataSource = new List<WeaponData>(weapons);
                    cmbWeaponsR.DisplayMember = "PrintName";
                    if (weapons.Count > 0)
                    {
                        //临时解绑事件防止刷新时弹出未保存确认
                        cmbWeaponsL.SelectedIndexChanged -= WeaponSelectedL;
                        cmbWeaponsR.SelectedIndexChanged -= WeaponSelectedR;
                        RestoreComboSelection(cmbWeaponsL, leftName);
                        RestoreComboSelection(cmbWeaponsR, rightName);
                        cmbWeaponsL.SelectedIndexChanged += WeaponSelectedL;
                        cmbWeaponsR.SelectedIndexChanged += WeaponSelectedR;
                    }
                    UpdateC64Labels(weapons.Count > 0);
                });
            });
        }
        catch (Exception ex) { this.Invoke(() => MessageBox.Show($"Refresh failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)); }
        finally { refreshing = false; }
    }

    private static void RestoreComboSelection(ComboBox cmb, string scriptName)
    {
        if (string.IsNullOrEmpty(scriptName)) { cmb.SelectedIndex = 0; return; }
        foreach (WeaponData w in cmb.Items)
        {
            if (string.Equals(w.ScriptName, scriptName, StringComparison.OrdinalIgnoreCase))
            {
                cmb.SelectedItem = w;
                return;
            }
        }
        cmb.SelectedIndex = 0;
    }

    private void Form1_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.S)
        {
            e.SuppressKeyPress = true;
            BtnSave_Click(sender, e);
        }
        else if (e.Control && e.KeyCode == Keys.D1)
        {
            e.SuppressKeyPress = true;
            cmbWeaponsL.Focus();
            cmbWeaponsL.DroppedDown = true;
        }
        else if (e.Control && e.KeyCode == Keys.D2)
        {
            e.SuppressKeyPress = true;
            cmbWeaponsR.Focus();
            cmbWeaponsR.DroppedDown = true;
        }
        else if (e.Control && e.KeyCode == Keys.Z)
        {
            e.SuppressKeyPress = true;
            RestoreSnapshot(IsControlOnLeft(this.ActiveControl));
        }
        else if (e.Control && e.KeyCode == Keys.R)
        {
            e.SuppressKeyPress = true;
            RefreshWeaponList();
        }
    }
    #endregion
}