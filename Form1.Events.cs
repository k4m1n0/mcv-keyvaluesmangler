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
        }
    }

    private bool HasUnsavedChanges(bool isLeft)
    {
        var original = isLeft ? currentWeaponLeft : currentWeaponRight;
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
            && NullableEquals(a.RangeModifier, b.RangeModifier)
            && string.Equals(a.ClipSize, b.ClipSize)
            && IntNullableEquals(a.ExtraBulletChamber, b.ExtraBulletChamber)
            && IntNullableEquals(a.BulletsPerShot, b.BulletsPerShot)
            && NullableEquals(a.IronsightSpeedScale, b.IronsightSpeedScale)
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
        return Math.Abs(va - vb) < 0.0001;
        //容差比较 防止掉精度导致误判为未保存
    }

    private static bool IntNullableEquals(int? a, int? b)
    {
        if (!a.HasValue || !b.HasValue) return true;
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
                SaveControlsToWeapon(currentWeaponLeft!, true);
            else
                SaveControlsToWeapon(currentWeaponRight!, false);
        }
        else
        {
            if (currentWeaponLeft != null) SaveControlsToWeapon(currentWeaponLeft, true);
            if (currentWeaponRight != null) SaveControlsToWeapon(currentWeaponRight, false);
        }
        try
        {
            CsvService.SaveWeapons(Path.Combine(AppContext.BaseDirectory, "weapons.csv"), weapons);
            var originalTitle = this.Text;
            this.Text = "Saved!";
            await Task.Delay(1145);
            this.Text = originalTitle;
        }
        catch (Exception ex) { MessageBox.Show($"Save failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
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
                SaveControlsToWeapon(currentWeaponLeft!, true);
            else
                SaveControlsToWeapon(currentWeaponRight!, false);
        }
        else
        {
            if (currentWeaponLeft != null) SaveControlsToWeapon(currentWeaponLeft, true);
            if (currentWeaponRight != null) SaveControlsToWeapon(currentWeaponRight, false);
        }
        var originalTitle = this.Text;
        try
        {
            CsvService.SaveWeapons(Path.Combine(AppContext.BaseDirectory, "weapons.csv"), weapons);
            string csv = Path.Combine(AppContext.BaseDirectory, "weapons.csv");
            await Task.Run(() =>
            {
                WeaponScriptService.ExportCsvToScripts(csv, lastScriptsDir);
            });
            Clipboard.SetText("wpn_reload_script all");
            btn.Text = "wpn_reload_script all";
            btn.Tag = false;
            this.Text = "Exported!";
            await Task.Delay(1145);
            this.Text = originalTitle;
        }
        catch (Exception ex)
        {
            btn.Text = "wpn_reload_script all";
            btn.Tag = false;
            this.Text = originalTitle;
            MessageBox.Show($"Quick export failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
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
                    if (weapons.Count > 0) { cmbWeaponsL.SelectedIndex = 0; cmbWeaponsR.SelectedIndex = 0; }
                    UpdateC64Labels(weapons.Count > 0);
                });
            });
        }
        catch (Exception ex) { this.Invoke(() => MessageBox.Show($"Refresh failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)); }
        finally { refreshing = false; }
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
    }
    #endregion
}