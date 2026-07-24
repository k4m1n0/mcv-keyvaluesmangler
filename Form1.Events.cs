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
            if (showingAltStats && WeaponHasAltStats(w, currentAltStatMode))
            {
                updatingControls = true;
                LoadAltStatsToControls(true, currentAltStatMode);
                SetAltStatReadonly(true, currentAltStatMode);
                StoreSnapshot(true);
                updatingControls = false;
            }
            if (showingAltStats && !WeaponHasAltStats(w, currentAltStatMode))
            {
                RestoreAllNudEnabled(true);
                if (!WeaponHasAltStats(currentWeaponRight, currentAltStatMode))
                    ExitAltStatMode();
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
            if (showingAltStats && WeaponHasAltStats(w, currentAltStatMode))
            {
                updatingControls = true;
                LoadAltStatsToControls(false, currentAltStatMode);
                SetAltStatReadonly(false, currentAltStatMode);
                StoreSnapshot(false);
                updatingControls = false;
            }
            if (showingAltStats && !WeaponHasAltStats(w, currentAltStatMode))
            {
                RestoreAllNudEnabled(false);
                if (!WeaponHasAltStats(currentWeaponLeft, currentAltStatMode))
                    ExitAltStatMode();
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
                    if (showingAltStats && WeaponHasAltStats(currentWeaponLeft, currentAltStatMode))
                        LoadAltStatsToControls(false, currentAltStatMode);
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
                    if (showingAltStats && WeaponHasAltStats(currentWeaponRight, currentAltStatMode))
                        LoadAltStatsToControls(true, currentAltStatMode);
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
            if (showingAltStats)
            {
                if (currentWeaponLeft != null) SyncAltStatFields(currentWeaponLeft, currentAltStatMode);
                if (currentWeaponRight != null && !ReferenceEquals(currentWeaponLeft, currentWeaponRight))
                    SyncAltStatFields(currentWeaponRight, currentAltStatMode);
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
                    if (showingAltStats && WeaponHasAltStats(currentWeaponLeft, currentAltStatMode))
                        LoadAltStatsToControls(false, currentAltStatMode);
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
                    if (showingAltStats && WeaponHasAltStats(currentWeaponRight, currentAltStatMode))
                        LoadAltStatsToControls(true, currentAltStatMode);
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
            if (showingAltStats)
            {
                if (currentWeaponLeft != null) SyncAltStatFields(currentWeaponLeft, currentAltStatMode);
                if (currentWeaponRight != null && !ReferenceEquals(currentWeaponLeft, currentWeaponRight))
                    SyncAltStatFields(currentWeaponRight, currentAltStatMode);
            }
            var originalTitle = this.Text;
            try
            {
                CsvService.SaveWeapons(Path.Combine(AppContext.BaseDirectory, "weapons.csv"), weapons);
                StoreSnapshot(true);
                StoreSnapshot(false);
                string csv = Path.Combine(AppContext.BaseDirectory, "weapons.csv");
                var exportMode = showingAltStats ? currentAltStatMode : (WeaponScriptService.AltStatMode?)null;
                await Task.Run(() =>
                {
                    if (exportMode.HasValue)
                        WeaponScriptService.ExportAltStatsToScripts(csv, lastScriptsDir, exportMode.Value);
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

    private void BtnWiki_Click(object? sender, EventArgs e)
    {
        using var dlg = new Form
        {
            Text = "Wiki Table Converter",
            Size = new Size(640, 480),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.Sizable,
            MinimizeBox = false,
            MaximizeBox = false
        };

        var lblInput = new Label { Text = "Paste existing wiki tables below:", Location = new Point(12, 12), AutoSize = true };
        var txtInput = new TextBox
        {
            Location = new Point(12, 30), Size = new Size(600, 150),
            Multiline = true, ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9)
        };

        var btnSelectDir = new Button { Text = "Select scripts folder...", Location = new Point(12, 190), Size = new Size(140, 26) };
        var lblDir = new Label { Text = "(same as CSV>Scripts)", Location = new Point(158, 194), AutoSize = true, ForeColor = Color.Gray };

        var lblOutput = new Label { Text = "Result:", Location = new Point(12, 225), AutoSize = true };
        var txtOutput = new TextBox
        {
            Location = new Point(12, 243), Size = new Size(600, 150),
            Multiline = true, ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9), ReadOnly = true
        };

        var btnConvert = new Button { Text = "Convert", Location = new Point(12, 403), Size = new Size(80, 26) };
        var btnCopy = new Button { Text = "Copy", Location = new Point(100, 403), Size = new Size(80, 26) };

        string? selectedDir = string.IsNullOrEmpty(lastScriptsDir) ? null : lastScriptsDir;
        if (selectedDir != null) lblDir.Text = selectedDir;

        btnSelectDir.Click += (s2, e2) =>
        {
            using var fbd = new FolderBrowserDialog { Description = "Select folder containing weapon scripts" };
            if (fbd.ShowDialog() == DialogResult.OK)
            {
                selectedDir = fbd.SelectedPath;
                lblDir.Text = selectedDir;
            }
        };

        btnConvert.Click += (s2, e2) =>
        {
            if (string.IsNullOrEmpty(selectedDir) || !Directory.Exists(selectedDir))
            {
                MessageBox.Show("Please select a valid scripts folder first.", "No folder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(txtInput.Text))
            {
                MessageBox.Show("Paste wiki tables first.", "No input", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try
            {
                txtOutput.Text = Tools.WikiTableConverter.Convert(txtInput.Text, selectedDir).Replace("\n", "\r\n");
            }
            catch (Exception ex)
            {
                txtOutput.Text = $"Error: {ex.Message}";
            }
        };

        btnCopy.Click += (s2, e2) =>
        {
            if (!string.IsNullOrEmpty(txtOutput.Text))
                Clipboard.SetText(txtOutput.Text);
        };

        dlg.Controls.Add(lblInput);
        dlg.Controls.Add(txtInput);
        dlg.Controls.Add(btnSelectDir);
        dlg.Controls.Add(lblDir);
        dlg.Controls.Add(lblOutput);
        dlg.Controls.Add(txtOutput);
        dlg.Controls.Add(btnConvert);
        dlg.Controls.Add(btnCopy);
        dlg.ShowDialog(this);
    }

    //切换备选数值模式 Dov或Zombie
    private void ToggleAltStats(WeaponScriptService.AltStatMode mode)
    {
        bool leftHas = WeaponHasAltStats(currentWeaponLeft, mode);
        bool rightHas = WeaponHasAltStats(currentWeaponRight, mode);
        if (!leftHas && !rightHas) return;
        bool anyDirty = (currentWeaponLeft != null && HasUnsavedChanges(true))
                     || (currentWeaponRight != null && HasUnsavedChanges(false));
        if (anyDirty)
        {
            var result = MessageBox.Show("Unsaved changes will be lost. Switch stats mode?",
                "Unsaved Changes", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (result != DialogResult.Yes) return;
        }

        //如果正在显示同一种模式 则关闭；否则切换到新模式
        if (showingAltStats && currentAltStatMode == mode)
        {
            showingAltStats = false;
            if (currentWeaponLeft != null) { LoadWeaponToControls(currentWeaponLeft, true); StoreSnapshot(true); }
            if (currentWeaponRight != null) { LoadWeaponToControls(currentWeaponRight, false); StoreSnapshot(false); }
            RestoreAllNudEnabled(true);
            RestoreAllNudEnabled(false);
            ResetAltStatButtons();
        }
        else
        {
            showingAltStats = true;
            currentAltStatMode = mode;
            HighlightAltStatButton(mode);
            updatingControls = true;
            if (leftHas) { LoadAltStatsToControls(true, mode); StoreSnapshot(true); SetAltStatReadonly(true, mode); }
            if (rightHas) { LoadAltStatsToControls(false, mode); StoreSnapshot(false); SetAltStatReadonly(false, mode); }
            updatingControls = false;
        }
        UpdateAllDamage();
        pnlSpread.Invalidate();
        pnlRecoil.Invalidate();
    }

    //高亮当前模式的按钮 另一个恢复默认
    private void HighlightAltStatButton(WeaponScriptService.AltStatMode mode)
    {
        foreach (Control c in this.Controls)
        {
            if (c is Button btn)
            {
                if (btn.Text == "DoV")
                    btn.BackColor = mode == WeaponScriptService.AltStatMode.Dov ? Color.LightGreen : SystemColors.Control;
                else if (btn.Text == "Zmb")
                    btn.BackColor = mode == WeaponScriptService.AltStatMode.Zombie ? Color.LightGreen : SystemColors.Control;
            }
        }
    }

    //恢复两个按钮的颜色
    private void ResetAltStatButtons()
    {
        foreach (Control c in this.Controls)
        {
            if (c is Button btn && (btn.Text == "DoV" || btn.Text == "Zmb"))
                btn.BackColor = SystemColors.Control;
        }
    }

    //根据mode设置只读 现在dov_stats和zombie_stats都可覆盖所有字段 不再有穿透等硬编码只读
    private void SetAltStatReadonly(bool isLeft, WeaponScriptService.AltStatMode mode)
    {
        var w = isLeft ? currentWeaponLeft : currentWeaponRight;
        //备选数值模式下 所有字段都可被覆盖 无需设置任何字段为只读
        //唯一例外：IronSight为0时禁用ADS相关控件
        if (w != null)
        {
            bool noAds = GetAltStatIronSight(w, mode) == 0;
            SetNudEnabled(isLeft ? nudAdsSpreadL : nudAdsSpreadR, !noAds);
            SetNudEnabled(isLeft ? nudAdsRecoilUpL : nudAdsRecoilUpR, !noAds);
            SetNudEnabled(isLeft ? nudAdsRecoilRightL : nudAdsRecoilRightR, !noAds);
            SetNudEnabled(isLeft ? nudIronsightSpeedScaleL : nudIronsightSpeedScaleR, !noAds);
        }
    }

    //获取当前备选模式下的IronSight值
    private static int? GetAltStatIronSight(WeaponData w, WeaponScriptService.AltStatMode mode) => mode switch
    {
        WeaponScriptService.AltStatMode.Dov => w.DovIronSight ?? w.IronSight,
        WeaponScriptService.AltStatMode.Zombie => w.ZombieIronSight ?? w.IronSight,
        _ => w.IronSight
    };

    private static void SetNudEnabled(NumericUpDown nud, bool enabled) => nud.Enabled = enabled;

    //判断武器是否有指定备选模式的数据
    private static bool WeaponHasAltStats(WeaponData? weapon, WeaponScriptService.AltStatMode mode) => mode switch
    {
        WeaponScriptService.AltStatMode.Dov => weapon?.DovDamageGeneric != null || weapon?.DovFireRate != null,
        WeaponScriptService.AltStatMode.Zombie => weapon?.ZombieClipSize != null || weapon?.ZombieDamageGeneric != null || weapon?.ZombieFireRate != null || weapon?.ZombieWeight != null,
        _ => false
    };

    private void ExitAltStatMode()
    {
        if (!WeaponHasAltStats(currentWeaponLeft, currentAltStatMode) && !WeaponHasAltStats(currentWeaponRight, currentAltStatMode))
        {
            showingAltStats = false;
            RestoreAllNudEnabled(true);
            RestoreAllNudEnabled(false);
            ResetAltStatButtons();
        }
    }

    //加载备选数值到控件 mode指定Dov或Zombie
    private void LoadAltStatsToControls(bool isLeft, WeaponScriptService.AltStatMode mode)
    {
        var weapon = isLeft ? currentWeaponLeft : currentWeaponRight;
        if (weapon == null) return;
        var temp = new WeaponData();
        CopyWeaponDataFields(weapon, temp);

        if (mode == WeaponScriptService.AltStatMode.Dov)
        {
            // 把dov专属属性映射回标准属性让LoadWeaponToControls能正确加载
            temp.ExtraBulletChamber = weapon.DovExtraBulletChamber ?? weapon.ExtraBulletChamber;
            temp.FireRate = weapon.DovFireRate ?? weapon.FireRate;
            temp.BulletSpread = weapon.DovBulletSpread ?? weapon.BulletSpread;
            temp.BulletSpreadDegreesIronsighted = weapon.DovBulletSpreadDegreesIronsighted ?? weapon.BulletSpreadDegreesIronsighted;
            temp.BulletSpreadDegreesBipod = weapon.DovBulletSpreadDegreesBipod ?? weapon.BulletSpreadDegreesBipod;
            temp.BulletSpreadDegreesBipodIronsighted = weapon.DovBulletSpreadDegreesBipodIronsighted ?? weapon.BulletSpreadDegreesBipodIronsighted;
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
            temp.ShakeScale = weapon.DovShakeScale ?? weapon.ShakeScale;
            temp.ShakeFreq = weapon.DovShakeFreq ?? weapon.ShakeFreq;
            temp.ShakeDuration = weapon.DovShakeDuration ?? weapon.ShakeDuration;
            temp.CrosshairMinDistance = weapon.DovCrosshairMinDistance ?? weapon.CrosshairMinDistance;
            temp.CrosshairDeltaDistance = weapon.DovCrosshairDeltaDistance ?? weapon.CrosshairDeltaDistance;
            temp.Weight = weapon.DovWeight ?? weapon.Weight;
            temp.ZMBuyPrice = weapon.DovZMBuyPrice ?? weapon.ZMBuyPrice;
            temp.ZMWeight = weapon.DovZMWeight ?? weapon.ZMWeight;
            temp.RecoilPushbackValue = weapon.DovRecoilPushbackValue ?? weapon.RecoilPushbackValue;
            temp.IronsightWalkBobbingStrength = weapon.DovIronsightWalkBobbingStrength ?? weapon.IronsightWalkBobbingStrength;
            temp.MetalPenetrationDepth = weapon.DovMetalPenetrationDepth ?? weapon.MetalPenetrationDepth;
            temp.GlassPenetrationDepth = weapon.DovGlassPenetrationDepth ?? weapon.GlassPenetrationDepth;
            temp.ConcretePenetrationDepth = weapon.DovConcretePenetrationDepth ?? weapon.ConcretePenetrationDepth;
            temp.WoodPenetrationDepth = weapon.DovWoodPenetrationDepth ?? weapon.WoodPenetrationDepth;
            temp.OtherPenetrationDepth = weapon.DovOtherPenetrationDepth ?? weapon.OtherPenetrationDepth;
            temp.MetalDamageModifier = weapon.DovMetalDamageModifier ?? weapon.MetalDamageModifier;
            temp.GlassDamageModifier = weapon.DovGlassDamageModifier ?? weapon.GlassDamageModifier;
            temp.ConcreteDamageModifier = weapon.DovConcreteDamageModifier ?? weapon.ConcreteDamageModifier;
            temp.WoodDamageModifier = weapon.DovWoodDamageModifier ?? weapon.WoodDamageModifier;
            temp.OtherDamageModifier = weapon.DovOtherDamageModifier ?? weapon.OtherDamageModifier;
            temp.NearwallDistance = weapon.DovNearwallDistance ?? weapon.NearwallDistance;
            temp.ClipSize = weapon.DovClipSize ?? weapon.ClipSize;
            temp.SecondaryFireRate = weapon.DovSecondaryFireRate ?? weapon.SecondaryFireRate;
            temp.IronSight = weapon.DovIronSight ?? weapon.IronSight;
        }
        else //Zombie
        {
            temp.ExtraBulletChamber = weapon.ZombieExtraBulletChamber ?? weapon.ExtraBulletChamber;
            temp.FireRate = weapon.ZombieFireRate ?? weapon.FireRate;
            temp.BulletSpread = weapon.ZombieBulletSpread ?? weapon.BulletSpread;
            temp.BulletSpreadDegreesIronsighted = weapon.ZombieBulletSpreadDegreesIronsighted ?? weapon.BulletSpreadDegreesIronsighted;
            temp.BulletSpreadDegreesBipod = weapon.ZombieBulletSpreadDegreesBipod ?? weapon.BulletSpreadDegreesBipod;
            temp.BulletSpreadDegreesBipodIronsighted = weapon.ZombieBulletSpreadDegreesBipodIronsighted ?? weapon.BulletSpreadDegreesBipodIronsighted;
            temp.RangeModifier = weapon.ZombieRangeModifier ?? weapon.RangeModifier;
            temp.IronsightSpeedScale = weapon.ZombieIronsightSpeedScale ?? weapon.IronsightSpeedScale;
            temp.CrouchSpreadMultiplier = weapon.ZombieCrouchSpreadMultiplier ?? weapon.CrouchSpreadMultiplier;
            temp.ProneSpreadMultiplier = weapon.ZombieProneSpreadMultiplier ?? weapon.ProneSpreadMultiplier;
            temp.StandMoveSpreadMultiplier = weapon.ZombieStandMoveSpreadMultiplier ?? weapon.StandMoveSpreadMultiplier;
            temp.SneakMoveSpreadMultiplier = weapon.ZombieSneakMoveSpreadMultiplier ?? weapon.SneakMoveSpreadMultiplier;
            temp.CrouchMoveSpreadMultiplier = weapon.ZombieCrouchMoveSpreadMultiplier ?? weapon.CrouchMoveSpreadMultiplier;
            temp.JumpSpreadMultiplier = weapon.ZombieJumpSpreadMultiplier ?? weapon.JumpSpreadMultiplier;
            temp.ViewSlideRecoilUp = weapon.ZombieViewSlideRecoilUp ?? weapon.ViewSlideRecoilUp;
            temp.ViewSlideRecoilRight = weapon.ZombieViewSlideRecoilRight ?? weapon.ViewSlideRecoilRight;
            temp.ViewSlideRecoilIronsightUp = weapon.ZombieViewSlideRecoilIronsightUp ?? weapon.ViewSlideRecoilIronsightUp;
            temp.ViewSlideRecoilIronsightRight = weapon.ZombieViewSlideRecoilIronsightRight ?? weapon.ViewSlideRecoilIronsightRight;
            temp.DamageHeadMultiplier = weapon.ZombieDamageHeadMultiplier ?? weapon.DamageHeadMultiplier;
            temp.DamageChestMultiplier = weapon.ZombieDamageChestMultiplier ?? weapon.DamageChestMultiplier;
            temp.DamageStomachMultiplier = weapon.ZombieDamageStomachMultiplier ?? weapon.DamageStomachMultiplier;
            temp.DamageLegMultiplier = weapon.ZombieDamageLegMultiplier ?? weapon.DamageLegMultiplier;
            temp.DamageArmMultiplier = weapon.ZombieDamageArmMultiplier ?? weapon.DamageArmMultiplier;
            temp.DamageGeneric = weapon.ZombieDamageGeneric ?? weapon.DamageGeneric;
            temp.ShakeScale = weapon.ZombieShakeScale ?? weapon.ShakeScale;
            temp.ShakeFreq = weapon.ZombieShakeFreq ?? weapon.ShakeFreq;
            temp.ShakeDuration = weapon.ZombieShakeDuration ?? weapon.ShakeDuration;
            temp.CrosshairMinDistance = weapon.ZombieCrosshairMinDistance ?? weapon.ZombieCrosshairMinDistance;
            temp.CrosshairDeltaDistance = weapon.ZombieCrosshairDeltaDistance ?? weapon.ZombieCrosshairDeltaDistance;
            temp.Weight = weapon.ZombieWeight ?? weapon.Weight;
            temp.ZMBuyPrice = weapon.ZombieZMBuyPrice ?? weapon.ZMBuyPrice;
            temp.ZMWeight = weapon.ZombieZMWeight ?? weapon.ZMWeight;
            temp.RecoilPushbackValue = weapon.ZombieRecoilPushbackValue ?? weapon.RecoilPushbackValue;
            temp.IronsightWalkBobbingStrength = weapon.ZombieIronsightWalkBobbingStrength ?? weapon.IronsightWalkBobbingStrength;
            temp.MetalPenetrationDepth = weapon.ZombieMetalPenetrationDepth ?? weapon.MetalPenetrationDepth;
            temp.GlassPenetrationDepth = weapon.ZombieGlassPenetrationDepth ?? weapon.GlassPenetrationDepth;
            temp.ConcretePenetrationDepth = weapon.ZombieConcretePenetrationDepth ?? weapon.ConcretePenetrationDepth;
            temp.WoodPenetrationDepth = weapon.ZombieWoodPenetrationDepth ?? weapon.WoodPenetrationDepth;
            temp.OtherPenetrationDepth = weapon.ZombieOtherPenetrationDepth ?? weapon.OtherPenetrationDepth;
            temp.MetalDamageModifier = weapon.ZombieMetalDamageModifier ?? weapon.MetalDamageModifier;
            temp.GlassDamageModifier = weapon.ZombieGlassDamageModifier ?? weapon.GlassDamageModifier;
            temp.ConcreteDamageModifier = weapon.ZombieConcreteDamageModifier ?? weapon.ConcreteDamageModifier;
            temp.WoodDamageModifier = weapon.ZombieWoodDamageModifier ?? weapon.WoodDamageModifier;
            temp.OtherDamageModifier = weapon.ZombieOtherDamageModifier ?? weapon.OtherDamageModifier;
            temp.NearwallDistance = weapon.ZombieNearwallDistance ?? weapon.NearwallDistance;
            temp.ClipSize = weapon.ZombieClipSize ?? weapon.ClipSize;
            temp.SecondaryFireRate = weapon.ZombieSecondaryFireRate ?? weapon.SecondaryFireRate;
            temp.IronSight = weapon.ZombieIronSight ?? weapon.IronSight;
        }

        LoadWeaponToControls(temp, isLeft);

        string? altFireModes = mode == WeaponScriptService.AltStatMode.Dov ? weapon.DovFireModes : weapon.ZombieFireModes;
        if (!string.IsNullOrEmpty(altFireModes))
        {
            if (isLeft) txtFireModesL.Text = altFireModes;
            else txtFireModesR.Text = altFireModes;
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

    //将顶层值同步回备选数值字段
    private static void SyncAltStatFields(WeaponData w, WeaponScriptService.AltStatMode mode)
    {
        if (mode == WeaponScriptService.AltStatMode.Dov)
        {
            w.DovDamageHeadMultiplier = w.DamageHeadMultiplier;
            w.DovDamageChestMultiplier = w.DamageChestMultiplier;
            w.DovDamageStomachMultiplier = w.DamageStomachMultiplier;
            w.DovDamageLegMultiplier = w.DamageLegMultiplier;
            w.DovDamageArmMultiplier = w.DamageArmMultiplier;
            w.DovDamageGeneric = w.DamageGeneric;
            w.DovBulletSpread = w.BulletSpread;
            w.DovBulletSpreadDegreesIronsighted = w.BulletSpreadDegreesIronsighted;
            w.DovBulletSpreadDegreesBipod = w.BulletSpreadDegreesBipod;
            w.DovBulletSpreadDegreesBipodIronsighted = w.BulletSpreadDegreesBipodIronsighted;
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
            w.DovShakeScale = w.ShakeScale;
            w.DovShakeFreq = w.ShakeFreq;
            w.DovShakeDuration = w.ShakeDuration;
            w.DovCrosshairMinDistance = w.CrosshairMinDistance;
            w.DovCrosshairDeltaDistance = w.CrosshairDeltaDistance;
            w.DovWeight = w.Weight;
            w.DovZMBuyPrice = w.ZMBuyPrice;
            w.DovZMWeight = w.ZMWeight;
            w.DovRecoilPushbackValue = w.RecoilPushbackValue;
            w.DovIronsightWalkBobbingStrength = w.IronsightWalkBobbingStrength;
            w.DovMetalPenetrationDepth = w.MetalPenetrationDepth;
            w.DovGlassPenetrationDepth = w.GlassPenetrationDepth;
            w.DovConcretePenetrationDepth = w.ConcretePenetrationDepth;
            w.DovWoodPenetrationDepth = w.WoodPenetrationDepth;
            w.DovOtherPenetrationDepth = w.OtherPenetrationDepth;
            w.DovMetalDamageModifier = w.MetalDamageModifier;
            w.DovGlassDamageModifier = w.GlassDamageModifier;
            w.DovConcreteDamageModifier = w.ConcreteDamageModifier;
            w.DovWoodDamageModifier = w.WoodDamageModifier;
            w.DovOtherDamageModifier = w.OtherDamageModifier;
            w.DovNearwallDistance = w.NearwallDistance;
            w.DovClipSize = w.ClipSize;
            w.DovFireModes = w.FireModes;
            w.DovSecondaryFireRate = w.SecondaryFireRate;
            w.DovIronSight = w.IronSight;
        }
        else //Zombie
        {
            w.ZombieDamageHeadMultiplier = w.DamageHeadMultiplier;
            w.ZombieDamageChestMultiplier = w.DamageChestMultiplier;
            w.ZombieDamageStomachMultiplier = w.DamageStomachMultiplier;
            w.ZombieDamageLegMultiplier = w.DamageLegMultiplier;
            w.ZombieDamageArmMultiplier = w.DamageArmMultiplier;
            w.ZombieDamageGeneric = w.DamageGeneric;
            w.ZombieBulletSpread = w.BulletSpread;
            w.ZombieBulletSpreadDegreesIronsighted = w.BulletSpreadDegreesIronsighted;
            w.ZombieBulletSpreadDegreesBipod = w.BulletSpreadDegreesBipod;
            w.ZombieBulletSpreadDegreesBipodIronsighted = w.BulletSpreadDegreesBipodIronsighted;
            w.ZombieRangeModifier = w.RangeModifier;
            w.ZombieIronsightSpeedScale = w.IronsightSpeedScale;
            w.ZombieCrouchSpreadMultiplier = w.CrouchSpreadMultiplier;
            w.ZombieProneSpreadMultiplier = w.ProneSpreadMultiplier;
            w.ZombieStandMoveSpreadMultiplier = w.StandMoveSpreadMultiplier;
            w.ZombieSneakMoveSpreadMultiplier = w.SneakMoveSpreadMultiplier;
            w.ZombieCrouchMoveSpreadMultiplier = w.CrouchMoveSpreadMultiplier;
            w.ZombieJumpSpreadMultiplier = w.JumpSpreadMultiplier;
            w.ZombieViewSlideRecoilUp = w.ViewSlideRecoilUp;
            w.ZombieViewSlideRecoilRight = w.ViewSlideRecoilRight;
            w.ZombieViewSlideRecoilIronsightUp = w.ViewSlideRecoilIronsightUp;
            w.ZombieViewSlideRecoilIronsightRight = w.ViewSlideRecoilIronsightRight;
            w.ZombieFireRate = w.FireRate;
            w.ZombieExtraBulletChamber = w.ExtraBulletChamber;
            w.ZombieShakeScale = w.ShakeScale;
            w.ZombieShakeFreq = w.ShakeFreq;
            w.ZombieShakeDuration = w.ShakeDuration;
            w.ZombieCrosshairMinDistance = w.CrosshairMinDistance;
            w.ZombieCrosshairDeltaDistance = w.CrosshairDeltaDistance;
            w.ZombieWeight = w.Weight;
            w.ZombieZMBuyPrice = w.ZMBuyPrice;
            w.ZombieZMWeight = w.ZMWeight;
            w.ZombieRecoilPushbackValue = w.RecoilPushbackValue;
            w.ZombieIronsightWalkBobbingStrength = w.IronsightWalkBobbingStrength;
            w.ZombieMetalPenetrationDepth = w.MetalPenetrationDepth;
            w.ZombieGlassPenetrationDepth = w.GlassPenetrationDepth;
            w.ZombieConcretePenetrationDepth = w.ConcretePenetrationDepth;
            w.ZombieWoodPenetrationDepth = w.WoodPenetrationDepth;
            w.ZombieOtherPenetrationDepth = w.OtherPenetrationDepth;
            w.ZombieMetalDamageModifier = w.MetalDamageModifier;
            w.ZombieGlassDamageModifier = w.GlassDamageModifier;
            w.ZombieConcreteDamageModifier = w.ConcreteDamageModifier;
            w.ZombieWoodDamageModifier = w.WoodDamageModifier;
            w.ZombieOtherDamageModifier = w.OtherDamageModifier;
            w.ZombieNearwallDistance = w.NearwallDistance;
            w.ZombieClipSize = w.ClipSize;
            w.ZombieFireModes = w.FireModes;
            w.ZombieSecondaryFireRate = w.SecondaryFireRate;
            w.ZombieIronSight = w.IronSight;
        }
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
                    //先解绑事件防止刷新时弹出未保存确认
                    cmbWeaponsL.SelectedIndexChanged -= WeaponSelectedL;
                    cmbWeaponsR.SelectedIndexChanged -= WeaponSelectedR;
                    //DataSource设为null再赋值新列表 触发重新绑定
                    cmbWeaponsL.DataSource = null;
                    cmbWeaponsL.DataSource = new List<WeaponData>(weapons);
                    cmbWeaponsL.DisplayMember = "PrintName";
                    cmbWeaponsR.DataSource = null;
                    cmbWeaponsR.DataSource = new List<WeaponData>(weapons);
                    cmbWeaponsR.DisplayMember = "PrintName";
                    if (weapons.Count > 0)
                    {
                        RestoreComboSelection(cmbWeaponsR, rightName);
                        cmbWeaponsL.SelectedIndexChanged += WeaponSelectedL;
                        cmbWeaponsR.SelectedIndexChanged += WeaponSelectedR;
                        RestoreComboSelection(cmbWeaponsL, leftName);
                    }
                    else
                    {
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