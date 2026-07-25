using System.Threading.Tasks;
using System.Windows.Forms;
using System.Text;
using System.Text.RegularExpressions;
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

    private void SpreadRecoilChangedL(object? sender, EventArgs e) { pnlSpread.Invalidate(); pnlRecoil.Invalidate(); }
    private void SpreadRecoilChangedR(object? sender, EventArgs e) { pnlSpread.Invalidate(); pnlRecoil.Invalidate(); }
    private void RangeModifierChangedL(object? sender, EventArgs e) { UpdateAllDamage(); }
    private void RangeModifierChangedR(object? sender, EventArgs e) { UpdateAllDamage(); }

    #endregion
    #region 保存导入导出

    private async void BtnSave_Click(object? sender, EventArgs e)
    {
        if (System.Threading.Interlocked.Exchange(ref saveLock, 1) != 0) return;
        try
        {
            //强制提交活跃控件的待定输入 防止NUD焦点未移走导致值未更新
            var active = this.ActiveControl;
            if (active != null) { this.ActiveControl = null; active.Focus(); }
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
                    else LoadWeaponToControls(currentWeaponLeft!, false);
                }
                else
                {
                    SaveControlsToWeapon(currentWeaponRight!, false);
                    StoreSnapshot(false);
                    if (showingAltStats && WeaponHasAltStats(currentWeaponRight, currentAltStatMode))
                        LoadAltStatsToControls(true, currentAltStatMode);
                    else LoadWeaponToControls(currentWeaponRight!, true);
                }
                UpdateAllDamage(); pnlSpread.Invalidate(); pnlRecoil.Invalidate();
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
            var savedTitle = this.Text;
            this.Text = "Saved!";
            try
            {
                CsvService.SaveWeapons(Path.Combine(AppContext.BaseDirectory, "weapons.csv"), weapons);
                StoreSnapshot(true); StoreSnapshot(false);
                await Task.Delay(1145);
            }
            catch (Exception ex) { MessageBox.Show($"Save failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { this.Text = savedTitle; }
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
            if (btn.Tag is not true)
            {
                if (string.IsNullOrEmpty(lastScriptsDir))
                {
                    using var dlg = new FolderBrowserDialog { Description = "Select the folder containing weapon scripts (will be overwritten)", UseDescriptionForTitle = true, InitialDirectory = AppContext.BaseDirectory };
                    if (dlg.ShowDialog() != DialogResult.OK) return;
                    lastScriptsDir = dlg.SelectedPath;
                }
                btn.Text = "confirm"; btn.Tag = true; return;
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
                    SaveControlsToWeapon(currentWeaponLeft!, true); StoreSnapshot(true);
                    if (showingAltStats && WeaponHasAltStats(currentWeaponLeft, currentAltStatMode))
                        LoadAltStatsToControls(false, currentAltStatMode);
                    else LoadWeaponToControls(currentWeaponLeft!, false);
                }
                else
                {
                    SaveControlsToWeapon(currentWeaponRight!, false); StoreSnapshot(false);
                    if (showingAltStats && WeaponHasAltStats(currentWeaponRight, currentAltStatMode))
                        LoadAltStatsToControls(true, currentAltStatMode);
                    else LoadWeaponToControls(currentWeaponRight!, true);
                }
                UpdateAllDamage(); pnlSpread.Invalidate(); pnlRecoil.Invalidate();
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
                StoreSnapshot(true); StoreSnapshot(false);
                string csv = Path.Combine(AppContext.BaseDirectory, "weapons.csv");
                var exportMode = showingAltStats ? currentAltStatMode : (WeaponScriptService.AltStatMode?)null;
                await Task.Run(() =>
                {
                    if (exportMode.HasValue)
                        WeaponScriptService.ExportAltStatsToScripts(csv, lastScriptsDir, exportMode.Value);
                    else WeaponScriptService.ExportCsvToScripts(csv, lastScriptsDir);
                });
                btn.Text = "wpn_reload_script all"; btn.Tag = false;
                this.Text = "Exported!"; await Task.Delay(1145);
            }
            catch (Exception ex) { btn.Text = "wpn_reload_script all"; btn.Tag = false; MessageBox.Show($"Quick export failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
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
            try { string log = WeaponScriptService.ImportScriptsToCsv(dir, csv); this.Invoke(() => { RefreshWeaponList(); using var lf = new LogForm("Import Complete", log); lf.ShowDialog(this); }); }
            catch (Exception ex) { this.Invoke(() => MessageBox.Show($"Import failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)); }
        });
    }

    private void BtnConvertToTemplate_Click(object? sender, EventArgs e)
    {
        string initialDir = string.IsNullOrEmpty(lastScriptsDir) ? AppContext.BaseDirectory : lastScriptsDir;
        using var dlg = new FolderBrowserDialog { Description = "Select folder containing weapon scripts to convert", UseDescriptionForTitle = true, InitialDirectory = initialDir };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        string dir = dlg.SelectedPath;
        var result = MessageBox.Show("Select conversion mode:\n\nYes = Full (keep empty keys)\nNo = Simple (remove empty keys)\nCancel = Abort",
            "Template Convert Mode", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (result == DialogResult.Cancel) return;
        bool simpleMode = (result == DialogResult.No);
        Task.Run(() =>
        {
            try { string log = Tools.ScriptToTemplateConverter.ConvertAll(dir, simpleMode); this.Invoke(() => { RefreshWeaponList(); using var lf = new LogForm("Template Convert", log); lf.ShowDialog(this); }); }
            catch (Exception ex) { this.Invoke(() => MessageBox.Show($"Template convert failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)); }
        });
    }

    private void BtnWiki_Click(object? sender, EventArgs e)
    {
        var dlg = new Form
        {
            Text = "Wiki Stats Updater", Size = new Size(660, 570),
            StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedSingle,
            MinimizeBox = false, MaximizeBox = false
        };

        var lblPage = new Label { Text = "Page:", Location = new Point(12, 14), Size = new Size(38, 20), TextAlign = ContentAlignment.MiddleRight };
        var txtPage = new TextBox { Location = new Point(56, 12), Size = new Size(200, 22), Text = "Weapons of Vietnam" };
        txtPage.TextChanged += (_, _) =>
        {
            string t = txtPage.Text;
            var m = Regex.Match(t, @"(?:wiki/|title=)([^?#&]+)");
            if (m.Success)
            {
                string extracted = Uri.UnescapeDataString(m.Groups[1].Value).Replace('_', ' ');
                if (t != extracted)
                {
                    txtPage.Text = extracted;
                    txtPage.SelectionStart = extracted.Length;
                }
            }
        };
        var btnFetch = new Button { Text = "Fetch", Location = new Point(262, 11), Size = new Size(55, 24) };
        var lblStatus = new Label { Location = new Point(324, 14), AutoSize = true, ForeColor = Color.DarkGreen };

        var lblUser = new Label { Text = "User:", Location = new Point(12, 42), Size = new Size(38, 20), TextAlign = ContentAlignment.MiddleRight };
        var txtUser = new TextBox { Location = new Point(56, 40), Size = new Size(80, 22) };
        var lblPw = new Label { Text = "Pw:", Location = new Point(142, 42), Size = new Size(24, 20), TextAlign = ContentAlignment.MiddleRight };
        var txtPw = new TextBox { Location = new Point(170, 40), Size = new Size(80, 22), PasswordChar = '*' };
        var btnDryRun = new Button { Text = "DryRun", Location = new Point(256, 39), Size = new Size(65, 24) };
        var btnBatchDR = new Button { Text = "BatchDR", Location = new Point(326, 39), Size = new Size(65, 24) };

        var lblInput = new Label { Text = "Source:", Location = new Point(12, 74), AutoSize = true };
        var txtInput = new TextBox { Location = new Point(12, 92), Size = new Size(620, 100), Multiline = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9), MaxLength = 0 };
        var lblOutput = new Label { Text = "Result:", Location = new Point(12, 198), AutoSize = true };
        var txtOutput = new TextBox { Location = new Point(12, 216), Size = new Size(620, 240), Multiline = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9), ReadOnly = true, MaxLength = 0 };

        var btnSelectDir = new Button { Text = "Scripts...", Location = new Point(12, 464), Size = new Size(80, 26) };
        var lblDir = new Label { Location = new Point(98, 469), AutoSize = true, ForeColor = Color.Gray };
        var btnConvert = new Button { Text = "Convert", Location = new Point(12, 492), Size = new Size(80, 26) };
        var btnCopy = new Button { Text = "Copy", Location = new Point(98, 492), Size = new Size(60, 26) };
        var btnReset = new Button { Text = "Reset", Location = new Point(164, 492), Size = new Size(60, 26) };

        string? selectedDir = string.IsNullOrEmpty(lastScriptsDir) ? null : lastScriptsDir;
        if (selectedDir != null) lblDir.Text = selectedDir;
        Dictionary<string, string>? _titleToScript = null;
        bool dryRunDone = false, batchDryDone = false;
        CancellationTokenSource? dryRunCts = null, batchCts = null;

        void ResetBatchState()
        {
            batchDryDone = false; btnBatchDR.Text = "BatchDR"; btnBatchDR.BackColor = SystemColors.Control;
            dryRunDone = false; btnDryRun.Text = "DryRun"; btnDryRun.BackColor = SystemColors.Control;
            SetEditControlsEnabled(true);
        }

        void SetEditControlsEnabled(bool enabled)
        {
            btnConvert.Enabled = enabled;
            btnSelectDir.Enabled = enabled;
            btnFetch.Enabled = enabled;
        }

        void PickDir()
        {
            if (selectedDir != null && Directory.Exists(selectedDir)) return;
            using var fbd = new FolderBrowserDialog();
            if (fbd.ShowDialog() == DialogResult.OK) { selectedDir = fbd.SelectedPath; lblDir.Text = selectedDir; }
        }

        async Task<bool> EnsureSource()
        {
            if (!string.IsNullOrWhiteSpace(txtInput.Text)) return true;
            var src = await WikiApiService.GetPageSourceAsync(txtPage.Text);
            if (src == null)
            {
                //反查脚本名
                if (_titleToScript == null)
                {
                    try { _titleToScript = await BuildTitleToScriptMap(); } catch { }
                }
                string? foundTitle = null;
                string input = txtPage.Text.Trim();
                string inputNoExt = Path.GetFileNameWithoutExtension(input);
                if (_titleToScript != null)
                {
                    foreach (var kv in _titleToScript)
                    {
                        string sn = kv.Value;
                        string snNoExt = Path.GetFileNameWithoutExtension(sn);
                        string snStem = snNoExt.StartsWith("weapon_", StringComparison.OrdinalIgnoreCase) ? snNoExt.Substring(7) : snNoExt;
                        if (sn.Equals(input, StringComparison.OrdinalIgnoreCase)
                            || snNoExt.Equals(input, StringComparison.OrdinalIgnoreCase)
                            || snNoExt.Equals(inputNoExt, StringComparison.OrdinalIgnoreCase)
                            || snStem.Equals(inputNoExt, StringComparison.OrdinalIgnoreCase))
                        {
                            foundTitle = kv.Key;
                            break;
                        }
                    }
                    //精确匹配失败 尝试模糊匹配页面标题
                    if (foundTitle == null)
                    {
                        foundTitle = _titleToScript.Keys.FirstOrDefault(k =>
                            k.Replace("_", " ").StartsWith(input, StringComparison.OrdinalIgnoreCase)
                            || k.IndexOf(input, StringComparison.OrdinalIgnoreCase) >= 0);
                    }
                }
                if (foundTitle != null)
                {
                    txtPage.Text = foundTitle;
                    src = await WikiApiService.GetPageSourceAsync(foundTitle);
                }
                if (src == null) { lblStatus.Text = "Page not found"; return false; }
            }
            //索引未构建则构建
            if (_titleToScript == null)
            {
                try { _titleToScript = await BuildTitleToScriptMap(); } catch { }
            }
            txtInput.Text = src;
            lblStatus.Text = $"OK: {txtPage.Text}" + (_titleToScript?.Count > 0 ? $" (+{_titleToScript.Count} idx)" : "");
            return true;
        }

        void EnterCancel(Button btn, CancellationTokenSource cts, ref EventHandler? h)
        {
            btn.Text = "Cancel"; btn.BackColor = Color.LightCoral;
            h = (_, _) => { if (cts is { IsCancellationRequested: false }) { btn.Text = "Cancel"; btn.BackColor = Color.LightCoral; } };
            btn.MouseLeave += h;
        }

        void ExitCancel(Button btn, string text, Color color, EventHandler? h)
        {
            if (h != null) btn.MouseLeave -= h;
            btn.Text = text; btn.BackColor = color;
        }

        void ToggleDryRun()
        {
            dryRunDone = !dryRunDone;
            btnDryRun.Text = dryRunDone ? "Upload" : "DryRun";
            btnDryRun.BackColor = dryRunDone ? Color.LightSalmon : SystemColors.Control;
            SetEditControlsEnabled(!dryRunDone);
        }

        void ToggleBatch()
        {
            batchDryDone = !batchDryDone;
            btnBatchDR.Text = batchDryDone ? "BatchUp" : "BatchDR";
            btnBatchDR.BackColor = batchDryDone ? Color.LightSalmon : SystemColors.Control;
            SetEditControlsEnabled(!batchDryDone);
        }

        btnSelectDir.Click += (_, _) =>
        {
            using var fbd = new FolderBrowserDialog();
            if (selectedDir != null && Directory.Exists(selectedDir))
                fbd.InitialDirectory = selectedDir;
            if (fbd.ShowDialog() == DialogResult.OK) { selectedDir = fbd.SelectedPath; lblDir.Text = selectedDir; }
        };

        btnConvert.Click += async (_, _) =>
        {
            if (dryRunDone || batchDryDone)
            {
                lblStatus.Text = "Cannot convert while upload is pending. Complete or cancel first.";
                return;
            }
            if (selectedDir == null || !Directory.Exists(selectedDir)) PickDir();
            if (selectedDir == null) return;
            //如果没有索引映射 尝试从wiki拉取
            if (_titleToScript == null && !string.IsNullOrWhiteSpace(txtInput.Text))
            {
                try
                {
                    _titleToScript = await BuildTitleToScriptMap();
                    if (_titleToScript != null)
                        lblStatus.Text = $"索引已加载 ({_titleToScript.Count} 个武器)";
                }
                catch { }
            }
            try { txtOutput.Text = DoConvert(txtInput.Text, selectedDir, _titleToScript).Replace("\n", "\r\n"); }
            catch (Exception ex) { txtOutput.Text = $"Error: {ex.Message}"; }
        };

        btnCopy.Click += (_, _) => { if (!string.IsNullOrEmpty(txtOutput.Text)) Clipboard.SetText(txtOutput.Text); };

        btnReset.Click += (_, _) =>
        {
            if (dryRunCts != null) { dryRunCts.Cancel(); dryRunCts.Dispose(); dryRunCts = null; }
            if (batchCts != null) { batchCts.Cancel(); batchCts.Dispose(); batchCts = null; }
            txtPage.Text = "Weapons of Vietnam";
            txtInput.Clear();
            txtOutput.Clear();
            _titleToScript = null;
            dryRunDone = false; btnDryRun.Text = "DryRun"; btnDryRun.BackColor = SystemColors.Control;
            batchDryDone = false; btnBatchDR.Text = "BatchDR"; btnBatchDR.BackColor = SystemColors.Control;
            SetEditControlsEnabled(true);
            lblStatus.Text = "";
        };

        btnFetch.Click += async (_, _) =>
        {
            if (dryRunDone || batchDryDone)
            {
                lblStatus.Text = "Cannot fetch while upload is pending. Complete or cancel first.";
                return;
            }
            var source = await WikiApiService.GetPageSourceAsync(txtPage.Text);
            if (source == null) { lblStatus.Text = "Page not found"; return; }
            _titleToScript = await BuildTitleToScriptMap();
            txtInput.Text = source; txtOutput.Clear(); ResetBatchState();
            lblStatus.Text = $"OK: {txtPage.Text}" + (_titleToScript?.Count > 0 ? $" (+{_titleToScript.Count} idx)" : "");
        };

        btnDryRun.Click += async (_, _) =>
        {
            if (dryRunCts != null)
            {
                dryRunCts.Cancel();
                dryRunCts.Dispose();
                dryRunCts = null;
                btnDryRun.Text = dryRunDone ? "Upload" : "DryRun";
                btnDryRun.BackColor = dryRunDone ? Color.LightSalmon : SystemColors.Control;
                lblStatus.Text = "Cancelled";
                return;
            }
            if (batchCts != null) { lblStatus.Text = "Batch is running"; return; }

            if (batchDryDone)
            {
                batchDryDone = false; btnBatchDR.Text = "BatchDR"; btnBatchDR.BackColor = SystemColors.Control;
            }

            if (dryRunDone && string.IsNullOrWhiteSpace(txtOutput.Text))
            {
                lblStatus.Text = "Result is empty. Run DryRun first.";
                return;
            }

            //如果是upload模式但还没有dryrun 结果先自动转换
            if (!dryRunDone && string.IsNullOrWhiteSpace(txtOutput.Text))
            {
                if (selectedDir == null || !Directory.Exists(selectedDir)) PickDir();
                if (selectedDir == null) return;
                if (!await EnsureSource()) return;
                try { txtOutput.Text = DoConvert(txtInput.Text, selectedDir, _titleToScript).Replace("\n", "\r\n"); }
                catch (Exception ex) { txtOutput.Text = $"Error: {ex.Message}"; return; }
            }

            dryRunCts = new CancellationTokenSource(); var token = dryRunCts.Token;
            EventHandler? h = null; EnterCancel(btnDryRun, dryRunCts, ref h);

            try
            {
                if (!dryRunDone)
                {
                    await Task.Run(() => token.ThrowIfCancellationRequested(), token);
                    lblStatus.Text = $"Ready: {txtPage.Text} (click Upload to save)";
                }
                else
                {
                    string content = txtOutput.Text;
                    if (!await EnsureLogin(txtUser.Text, txtPw.Text, lblStatus)) return;
                    token.ThrowIfCancellationRequested();
                    if (await WikiApiService.IsSameContentAsync(txtPage.Text, content))
                    {
                        lblStatus.Text = "Unchanged, skip";
                    }
                    else
                    {
                        bool ok = await WikiApiService.SavePageAsync(txtPage.Text, content, "Update weapon data from scripts");
                        lblStatus.Text = ok ? "Saved!" : "Save failed";
                    }
                }
                ToggleDryRun();
                ExitCancel(btnDryRun, btnDryRun.Text, btnDryRun.BackColor, h);
            }
            catch (OperationCanceledException)
            {
                lblStatus.Text = "Cancelled";
                ExitCancel(btnDryRun, dryRunDone ? "Upload" : "DryRun",
                        dryRunDone ? Color.LightSalmon : SystemColors.Control, h);
            }
            finally { dryRunCts?.Dispose(); dryRunCts = null; }
        };

        btnBatchDR.Click += async (_, _) =>
        {
            if (batchCts != null)
            {
                batchCts.Cancel();
                batchCts.Dispose();
                batchCts = null;
                btnBatchDR.Text = batchDryDone ? "BatchUp" : "BatchDR";
                btnBatchDR.BackColor = batchDryDone ? Color.LightSalmon : SystemColors.Control;
                lblStatus.Text = "Batch cancelled";
                return;
            }
            if (dryRunCts != null) { lblStatus.Text = "DryRun is running"; return; }

            if (dryRunDone)
            {
                dryRunDone = false; btnDryRun.Text = "DryRun"; btnDryRun.BackColor = SystemColors.Control;
            }

            batchCts = new CancellationTokenSource(); var token = batchCts.Token;
            EventHandler? h = null; EnterCancel(btnBatchDR, batchCts, ref h);

            try
            {
                if (selectedDir == null || !Directory.Exists(selectedDir)) PickDir();
                if (selectedDir == null) return;
                if (!await EnsureSource()) return;
                if (!Regex.IsMatch(txtInput.Text, @"^=\[\[.+\]\]=\s*$", RegexOptions.Multiline))
                {
                    lblStatus.Text = "Not a summary page. Batch requires a weapon list page.";
                    return;
                }
                var links = ExtractWeaponLinks(txtInput.Text, _titleToScript);
                if (links.Count == 0) { lblStatus.Text = "No weapon links found"; return; }

                string wikiDir = Path.Combine(AppContext.BaseDirectory, "wiki"); Directory.CreateDirectory(wikiDir);
                var log = new StringBuilder(); int done = 0, fail = 0, skip = 0;

                if (!batchDryDone)
                {
                    if (!await EnsureLogin(txtUser.Text, txtPw.Text, lblStatus)) return;
                    log.AppendLine($"=== Batch DryRun: {links.Count} pages ===");
                    foreach (var link in links)
                    {
                        token.ThrowIfCancellationRequested();
                        try
                        {
                            string? src = await WikiApiService.GetPageSourceAsync(link);
                            if (src == null)
                            {
                                fail++; log.AppendLine($"FAIL fetch: {link}");
                            }
                            else
                            {
                                string converted = Tools.WikiTableConverter.Convert(src, selectedDir);
                                SaveToWikiDir(link.Replace(" ", "_").Replace("/", "_") + ".txt", converted);
                                done++; log.AppendLine($"OK: {link}");
                            }
                        }
                        catch { fail++; log.AppendLine($"FAIL: {link}"); }
                        lblStatus.Text = $"DR [{done + fail}/{links.Count}]";
                    }
                    log.AppendLine($"Done: {done} ok, {fail} fail");
                }
                else
                {
                    if (!await EnsureLogin(txtUser.Text, txtPw.Text, lblStatus)) return;
                    log.AppendLine($"=== Batch Upload: {links.Count} pages ===");
                    foreach (var link in links)
                    {
                        token.ThrowIfCancellationRequested();
                        string fp = Path.Combine(wikiDir, link.Replace(" ", "_").Replace("/", "_") + ".txt");
                        if (!File.Exists(fp)) { skip++; log.AppendLine($"SKIP (no file): {link}"); continue; }
                        string content = File.ReadAllText(fp);
                        if (await WikiApiService.IsSameContentAsync(link, content)) { skip++; log.AppendLine($"SKIP (unchanged): {link}"); continue; }
                        var ok = await WikiApiService.SavePageAsync(link, content, "Update weapon data from scripts");
                        if (ok) { done++; log.AppendLine($"OK: {link}"); } else { fail++; log.AppendLine($"FAIL upload: {link}"); }
                        lblStatus.Text = $"Up [{done + fail}/{links.Count - skip}]";
                    }
                    log.AppendLine($"Done: {done} ok, {fail} fail, {skip} skip");
                }
                txtOutput.Text = log.ToString();
                ToggleBatch();
                ExitCancel(btnBatchDR, btnBatchDR.Text, btnBatchDR.BackColor, h);
            }
            catch (OperationCanceledException)
            {
                lblStatus.Text = "Batch cancelled";
                ExitCancel(btnBatchDR, batchDryDone ? "BatchUp" : "BatchDR",
                        batchDryDone ? Color.LightSalmon : SystemColors.Control, h);
            }
            finally { batchCts?.Dispose(); batchCts = null; }
        };

        dlg.Controls.AddRange(new Control[] {
            lblPage, txtPage, btnFetch, lblStatus,
            lblUser, txtUser, lblPw, txtPw, btnDryRun, btnBatchDR,
            lblInput, txtInput, lblOutput, txtOutput,
            btnSelectDir, lblDir, btnConvert, btnCopy, btnReset
        });
        dlg.ShowDialog(this);
    }

    private static string DoConvert(string input, string scriptsDir, Dictionary<string, string>? titleToScript)
    {
        input = input.Replace("\r\n", "\n").Replace('\r', '\n');
        if (Regex.IsMatch(input, @"^=\[\[.+\]\]=\s*$", RegexOptions.Multiline))
        {
            var map = titleToScript != null ? new Dictionary<string, string>(titleToScript, StringComparer.OrdinalIgnoreCase) : new();
            foreach (var path in Directory.GetFiles(scriptsDir, "weapon_*.txt"))
            {
                string sn = Path.GetFileNameWithoutExtension(path);
                string c = File.ReadAllText(path).Replace("\r\n", "\n");
                var pm = Regex.Match(c, @"""printname""\s+""([^""]*)""");
                string d = pm.Success ? pm.Groups[1].Value.TrimStart('#') : sn;
                if (!map.ContainsKey(d.Replace("_", " "))) map[d.Replace("_", " ")] = sn;
            }
            return Tools.WikiTableConverter.ConvertSummaryPage(input, scriptsDir, map);
        }
        return Tools.WikiTableConverter.Convert(input, scriptsDir);
    }

    private static async Task<bool> EnsureLogin(string user, string pw, Label status)
    {
        if (WikiApiService.IsLoggedIn) return true;
        if (!await WikiApiService.LoginAsync(user, pw)) { status.Text = "Login failed"; return false; }
        status.Text = "Logged in"; return true;
    }

    private static async Task<Dictionary<string, string>?> BuildTitleToScriptMap()
    {
        try
        {
            string? idx = await WikiApiService.GetPageSourceAsync("Weapon Script Name");
            if (idx == null) return null;
            idx = idx.Replace("\r\n", "\n").Replace('\r', '\n');
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in Regex.Matches(idx, @"\|\s*(weapon_[^\s|]+)\s*\n\|\s*\[\[([^\]|]+)"))
                map[m.Groups[2].Value.Trim()] = m.Groups[1].Value;
            return map;
        }
        catch { return null; }
    }

    private static List<string> ExtractWeaponLinks(string pageSource, Dictionary<string, string>? titleToScript)
    {
        if (titleToScript == null || titleToScript.Count == 0) return new();
        var links = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(pageSource, @"\[\[([^\]|:#<>]+)\]\]"))
            if (titleToScript.ContainsKey(m.Groups[1].Value.Trim()))
                links.Add(m.Groups[1].Value.Trim());
        return links.OrderBy(x => x).ToList();
    }

    private static void SaveToWikiDir(string fileName, string content)
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "wiki");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, fileName), content);
    }

    private void ToggleAltStats(WeaponScriptService.AltStatMode mode)
    {
        bool leftHas = WeaponHasAltStats(currentWeaponLeft, mode);
        bool rightHas = WeaponHasAltStats(currentWeaponRight, mode);
        if (!leftHas && !rightHas) return;
        if ((currentWeaponLeft != null && HasUnsavedChanges(true)) || (currentWeaponRight != null && HasUnsavedChanges(false)))
        {
            if (MessageBox.Show("Unsaved changes will be lost. Switch stats mode?", "Unsaved Changes", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        }

        //如果正在显示同一种模式则关闭 否则切换到新模式
        if (showingAltStats && currentAltStatMode == mode)
        {
            showingAltStats = false;
            if (currentWeaponLeft != null) { LoadWeaponToControls(currentWeaponLeft, true); StoreSnapshot(true); }
            if (currentWeaponRight != null) { LoadWeaponToControls(currentWeaponRight, false); StoreSnapshot(false); }
            RestoreAllNudEnabled(true); RestoreAllNudEnabled(false);
            ResetAltStatButtons();
        }
        else
        {
            showingAltStats = true; currentAltStatMode = mode;
            HighlightAltStatButton(mode);
            updatingControls = true;
            if (leftHas) { LoadAltStatsToControls(true, mode); StoreSnapshot(true); SetAltStatReadonly(true, mode); }
            if (rightHas) { LoadAltStatsToControls(false, mode); StoreSnapshot(false); SetAltStatReadonly(false, mode); }
            updatingControls = false;
        }
        UpdateAllDamage(); pnlSpread.Invalidate(); pnlRecoil.Invalidate();
    }

    //高亮当前模式的按钮 另一个恢复默认
    private void HighlightAltStatButton(WeaponScriptService.AltStatMode mode)
    {
        foreach (Control c in this.Controls)
        {
            if (c is Button btn)
            {
                if (btn.Text == "DoV") btn.BackColor = mode == WeaponScriptService.AltStatMode.Dov ? Color.LightGreen : SystemColors.Control;
                else if (btn.Text == "Zmb") btn.BackColor = mode == WeaponScriptService.AltStatMode.Zombie ? Color.LightGreen : SystemColors.Control;
            }
        }
    }

    private void ResetAltStatButtons()
    {
        foreach (Control c in this.Controls)
            if (c is Button btn && (btn.Text == "DoV" || btn.Text == "Zmb")) btn.BackColor = SystemColors.Control;
    }

    private void SetAltStatReadonly(bool isLeft, WeaponScriptService.AltStatMode mode)
    {
        var w = isLeft ? currentWeaponLeft : currentWeaponRight;
        if (w != null)
        {
            bool noAds = GetAltStatIronSight(w, mode) == 0;
            SetNudEnabled(isLeft ? nudAdsSpreadL : nudAdsSpreadR, !noAds);
            SetNudEnabled(isLeft ? nudAdsRecoilUpL : nudAdsRecoilUpR, !noAds);
            SetNudEnabled(isLeft ? nudAdsRecoilRightL : nudAdsRecoilRightR, !noAds);
            SetNudEnabled(isLeft ? nudIronsightSpeedScaleL : nudIronsightSpeedScaleR, !noAds);
        }
    }

    private static int? GetAltStatIronSight(WeaponData w, WeaponScriptService.AltStatMode mode) => mode switch
    {
        WeaponScriptService.AltStatMode.Dov => w.DovIronSight ?? w.IronSight,
        WeaponScriptService.AltStatMode.Zombie => w.ZombieIronSight ?? w.IronSight,
        _ => w.IronSight
    };

    private static void SetNudEnabled(NumericUpDown nud, bool enabled) => nud.Enabled = enabled;

    private static bool WeaponHasAltStats(WeaponData? weapon, WeaponScriptService.AltStatMode mode) => mode switch
    {
        WeaponScriptService.AltStatMode.Dov => weapon?.DovDamageGeneric != null || weapon?.DovFireRate != null,
        WeaponScriptService.AltStatMode.Zombie => weapon?.ZombieClipSize != null || weapon?.ZombieDamageGeneric != null || weapon?.ZombieFireRate != null || weapon?.ZombieWeight != null,
        _ => false
    };

    private void ExitAltStatMode()
    {
        if (!WeaponHasAltStats(currentWeaponLeft, currentAltStatMode) && !WeaponHasAltStats(currentWeaponRight, currentAltStatMode))
        { showingAltStats = false; RestoreAllNudEnabled(true); RestoreAllNudEnabled(false); ResetAltStatButtons(); }
    }

    private void LoadAltStatsToControls(bool isLeft, WeaponScriptService.AltStatMode mode)
    {
        var weapon = isLeft ? currentWeaponLeft : currentWeaponRight;
        if (weapon == null) return;
        var temp = new WeaponData();
        CopyWeaponDataFields(weapon, temp);

        bool isDov = mode == WeaponScriptService.AltStatMode.Dov;
        temp.ExtraBulletChamber = (isDov ? weapon.DovExtraBulletChamber : weapon.ZombieExtraBulletChamber) ?? weapon.ExtraBulletChamber;
        temp.FireRate = (isDov ? weapon.DovFireRate : weapon.ZombieFireRate) ?? weapon.FireRate;
        temp.BulletSpread = (isDov ? weapon.DovBulletSpread : weapon.ZombieBulletSpread) ?? weapon.BulletSpread;
        temp.BulletSpreadDegreesIronsighted = (isDov ? weapon.DovBulletSpreadDegreesIronsighted : weapon.ZombieBulletSpreadDegreesIronsighted) ?? weapon.BulletSpreadDegreesIronsighted;
        temp.BulletSpreadDegreesBipod = (isDov ? weapon.DovBulletSpreadDegreesBipod : weapon.ZombieBulletSpreadDegreesBipod) ?? weapon.BulletSpreadDegreesBipod;
        temp.BulletSpreadDegreesBipodIronsighted = (isDov ? weapon.DovBulletSpreadDegreesBipodIronsighted : weapon.ZombieBulletSpreadDegreesBipodIronsighted) ?? weapon.BulletSpreadDegreesBipodIronsighted;
        temp.RangeModifier = (isDov ? weapon.DovRangeModifier : weapon.ZombieRangeModifier) ?? weapon.RangeModifier;
        temp.IronsightSpeedScale = (isDov ? weapon.DovIronsightSpeedScale : weapon.ZombieIronsightSpeedScale) ?? weapon.IronsightSpeedScale;
        temp.CrouchSpreadMultiplier = (isDov ? weapon.DovCrouchSpreadMultiplier : weapon.ZombieCrouchSpreadMultiplier) ?? weapon.CrouchSpreadMultiplier;
        temp.ProneSpreadMultiplier = (isDov ? weapon.DovProneSpreadMultiplier : weapon.ZombieProneSpreadMultiplier) ?? weapon.ProneSpreadMultiplier;
        temp.StandMoveSpreadMultiplier = (isDov ? weapon.DovStandMoveSpreadMultiplier : weapon.ZombieStandMoveSpreadMultiplier) ?? weapon.StandMoveSpreadMultiplier;
        temp.SneakMoveSpreadMultiplier = (isDov ? weapon.DovSneakMoveSpreadMultiplier : weapon.ZombieSneakMoveSpreadMultiplier) ?? weapon.SneakMoveSpreadMultiplier;
        temp.CrouchMoveSpreadMultiplier = (isDov ? weapon.DovCrouchMoveSpreadMultiplier : weapon.ZombieCrouchMoveSpreadMultiplier) ?? weapon.CrouchMoveSpreadMultiplier;
        temp.JumpSpreadMultiplier = (isDov ? weapon.DovJumpSpreadMultiplier : weapon.ZombieJumpSpreadMultiplier) ?? weapon.JumpSpreadMultiplier;
        temp.ViewSlideRecoilUp = (isDov ? weapon.DovViewSlideRecoilUp : weapon.ZombieViewSlideRecoilUp) ?? weapon.ViewSlideRecoilUp;
        temp.ViewSlideRecoilRight = (isDov ? weapon.DovViewSlideRecoilRight : weapon.ZombieViewSlideRecoilRight) ?? weapon.ViewSlideRecoilRight;
        temp.ViewSlideRecoilIronsightUp = (isDov ? weapon.DovViewSlideRecoilIronsightUp : weapon.ZombieViewSlideRecoilIronsightUp) ?? weapon.ViewSlideRecoilIronsightUp;
        temp.ViewSlideRecoilIronsightRight = (isDov ? weapon.DovViewSlideRecoilIronsightRight : weapon.ZombieViewSlideRecoilIronsightRight) ?? weapon.ViewSlideRecoilIronsightRight;
        temp.DamageHeadMultiplier = (isDov ? weapon.DovDamageHeadMultiplier : weapon.ZombieDamageHeadMultiplier) ?? weapon.DamageHeadMultiplier;
        temp.DamageChestMultiplier = (isDov ? weapon.DovDamageChestMultiplier : weapon.ZombieDamageChestMultiplier) ?? weapon.DamageChestMultiplier;
        temp.DamageStomachMultiplier = (isDov ? weapon.DovDamageStomachMultiplier : weapon.ZombieDamageStomachMultiplier) ?? weapon.DamageStomachMultiplier;
        temp.DamageLegMultiplier = (isDov ? weapon.DovDamageLegMultiplier : weapon.ZombieDamageLegMultiplier) ?? weapon.DamageLegMultiplier;
        temp.DamageArmMultiplier = (isDov ? weapon.DovDamageArmMultiplier : weapon.ZombieDamageArmMultiplier) ?? weapon.DamageArmMultiplier;
        temp.DamageGeneric = (isDov ? weapon.DovDamageGeneric : weapon.ZombieDamageGeneric) ?? weapon.DamageGeneric;
        temp.ShakeScale = (isDov ? weapon.DovShakeScale : weapon.ZombieShakeScale) ?? weapon.ShakeScale;
        temp.ShakeFreq = (isDov ? weapon.DovShakeFreq : weapon.ZombieShakeFreq) ?? weapon.ShakeFreq;
        temp.ShakeDuration = (isDov ? weapon.DovShakeDuration : weapon.ZombieShakeDuration) ?? weapon.ShakeDuration;
        temp.CrosshairMinDistance = (isDov ? weapon.DovCrosshairMinDistance : weapon.ZombieCrosshairMinDistance) ?? weapon.CrosshairMinDistance;
        temp.CrosshairDeltaDistance = (isDov ? weapon.DovCrosshairDeltaDistance : weapon.ZombieCrosshairDeltaDistance) ?? weapon.CrosshairDeltaDistance;
        temp.Weight = (isDov ? weapon.DovWeight : weapon.ZombieWeight) ?? weapon.Weight;
        temp.ZMBuyPrice = (isDov ? weapon.DovZMBuyPrice : weapon.ZombieZMBuyPrice) ?? weapon.ZMBuyPrice;
        temp.ZMWeight = (isDov ? weapon.DovZMWeight : weapon.ZombieZMWeight) ?? weapon.ZMWeight;
        temp.RecoilPushbackValue = (isDov ? weapon.DovRecoilPushbackValue : weapon.ZombieRecoilPushbackValue) ?? weapon.RecoilPushbackValue;
        temp.IronsightWalkBobbingStrength = (isDov ? weapon.DovIronsightWalkBobbingStrength : weapon.ZombieIronsightWalkBobbingStrength) ?? weapon.IronsightWalkBobbingStrength;
        temp.MetalPenetrationDepth = (isDov ? weapon.DovMetalPenetrationDepth : weapon.ZombieMetalPenetrationDepth) ?? weapon.MetalPenetrationDepth;
        temp.GlassPenetrationDepth = (isDov ? weapon.DovGlassPenetrationDepth : weapon.ZombieGlassPenetrationDepth) ?? weapon.GlassPenetrationDepth;
        temp.ConcretePenetrationDepth = (isDov ? weapon.DovConcretePenetrationDepth : weapon.ZombieConcretePenetrationDepth) ?? weapon.ConcretePenetrationDepth;
        temp.WoodPenetrationDepth = (isDov ? weapon.DovWoodPenetrationDepth : weapon.ZombieWoodPenetrationDepth) ?? weapon.WoodPenetrationDepth;
        temp.OtherPenetrationDepth = (isDov ? weapon.DovOtherPenetrationDepth : weapon.ZombieOtherPenetrationDepth) ?? weapon.OtherPenetrationDepth;
        temp.MetalDamageModifier = (isDov ? weapon.DovMetalDamageModifier : weapon.ZombieMetalDamageModifier) ?? weapon.MetalDamageModifier;
        temp.GlassDamageModifier = (isDov ? weapon.DovGlassDamageModifier : weapon.ZombieGlassDamageModifier) ?? weapon.GlassDamageModifier;
        temp.ConcreteDamageModifier = (isDov ? weapon.DovConcreteDamageModifier : weapon.ZombieConcreteDamageModifier) ?? weapon.ConcreteDamageModifier;
        temp.WoodDamageModifier = (isDov ? weapon.DovWoodDamageModifier : weapon.ZombieWoodDamageModifier) ?? weapon.WoodDamageModifier;
        temp.OtherDamageModifier = (isDov ? weapon.DovOtherDamageModifier : weapon.ZombieOtherDamageModifier) ?? weapon.OtherDamageModifier;
        temp.NearwallDistance = (isDov ? weapon.DovNearwallDistance : weapon.ZombieNearwallDistance) ?? weapon.NearwallDistance;
        temp.ClipSize = (isDov ? weapon.DovClipSize : weapon.ZombieClipSize) ?? weapon.ClipSize;
        temp.SecondaryFireRate = (isDov ? weapon.DovSecondaryFireRate : weapon.ZombieSecondaryFireRate) ?? weapon.SecondaryFireRate;
        temp.IronSight = (isDov ? weapon.DovIronSight : weapon.ZombieIronSight) ?? weapon.IronSight;

        LoadWeaponToControls(temp, isLeft);

        string? altFireModes = isDov ? weapon.DovFireModes : weapon.ZombieFireModes;
        if (!string.IsNullOrEmpty(altFireModes))
        { if (isLeft) txtFireModesL.Text = altFireModes; else txtFireModesR.Text = altFireModes; }
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
        bool isDov = mode == WeaponScriptService.AltStatMode.Dov;
        if (isDov)
        {
            w.DovDamageHeadMultiplier = w.DamageHeadMultiplier; w.DovDamageChestMultiplier = w.DamageChestMultiplier;
            w.DovDamageStomachMultiplier = w.DamageStomachMultiplier; w.DovDamageLegMultiplier = w.DamageLegMultiplier;
            w.DovDamageArmMultiplier = w.DamageArmMultiplier; w.DovDamageGeneric = w.DamageGeneric;
            w.DovBulletSpread = w.BulletSpread; w.DovBulletSpreadDegreesIronsighted = w.BulletSpreadDegreesIronsighted;
            w.DovBulletSpreadDegreesBipod = w.BulletSpreadDegreesBipod; w.DovBulletSpreadDegreesBipodIronsighted = w.BulletSpreadDegreesBipodIronsighted;
            w.DovRangeModifier = w.RangeModifier; w.DovIronsightSpeedScale = w.IronsightSpeedScale;
            w.DovCrouchSpreadMultiplier = w.CrouchSpreadMultiplier; w.DovProneSpreadMultiplier = w.ProneSpreadMultiplier;
            w.DovStandMoveSpreadMultiplier = w.StandMoveSpreadMultiplier; w.DovSneakMoveSpreadMultiplier = w.SneakMoveSpreadMultiplier;
            w.DovCrouchMoveSpreadMultiplier = w.CrouchMoveSpreadMultiplier; w.DovJumpSpreadMultiplier = w.JumpSpreadMultiplier;
            w.DovViewSlideRecoilUp = w.ViewSlideRecoilUp; w.DovViewSlideRecoilRight = w.ViewSlideRecoilRight;
            w.DovViewSlideRecoilIronsightUp = w.ViewSlideRecoilIronsightUp; w.DovViewSlideRecoilIronsightRight = w.ViewSlideRecoilIronsightRight;
            w.DovFireRate = w.FireRate; w.DovExtraBulletChamber = w.ExtraBulletChamber;
            w.DovShakeScale = w.ShakeScale; w.DovShakeFreq = w.ShakeFreq; w.DovShakeDuration = w.ShakeDuration;
            w.DovCrosshairMinDistance = w.CrosshairMinDistance; w.DovCrosshairDeltaDistance = w.CrosshairDeltaDistance;
            w.DovWeight = w.Weight; w.DovZMBuyPrice = w.ZMBuyPrice; w.DovZMWeight = w.ZMWeight;
            w.DovRecoilPushbackValue = w.RecoilPushbackValue; w.DovIronsightWalkBobbingStrength = w.IronsightWalkBobbingStrength;
            w.DovMetalPenetrationDepth = w.MetalPenetrationDepth; w.DovGlassPenetrationDepth = w.GlassPenetrationDepth;
            w.DovConcretePenetrationDepth = w.ConcretePenetrationDepth; w.DovWoodPenetrationDepth = w.WoodPenetrationDepth;
            w.DovOtherPenetrationDepth = w.OtherPenetrationDepth;
            w.DovMetalDamageModifier = w.MetalDamageModifier; w.DovGlassDamageModifier = w.GlassDamageModifier;
            w.DovConcreteDamageModifier = w.ConcreteDamageModifier; w.DovWoodDamageModifier = w.WoodDamageModifier;
            w.DovOtherDamageModifier = w.OtherDamageModifier; w.DovNearwallDistance = w.NearwallDistance;
            w.DovClipSize = w.ClipSize; w.DovFireModes = w.FireModes;
            w.DovSecondaryFireRate = w.SecondaryFireRate; w.DovIronSight = w.IronSight;
        }
        else
        {
            w.ZombieDamageHeadMultiplier = w.DamageHeadMultiplier; w.ZombieDamageChestMultiplier = w.DamageChestMultiplier;
            w.ZombieDamageStomachMultiplier = w.DamageStomachMultiplier; w.ZombieDamageLegMultiplier = w.DamageLegMultiplier;
            w.ZombieDamageArmMultiplier = w.DamageArmMultiplier; w.ZombieDamageGeneric = w.DamageGeneric;
            w.ZombieBulletSpread = w.BulletSpread; w.ZombieBulletSpreadDegreesIronsighted = w.BulletSpreadDegreesIronsighted;
            w.ZombieBulletSpreadDegreesBipod = w.BulletSpreadDegreesBipod; w.ZombieBulletSpreadDegreesBipodIronsighted = w.BulletSpreadDegreesBipodIronsighted;
            w.ZombieRangeModifier = w.RangeModifier; w.ZombieIronsightSpeedScale = w.IronsightSpeedScale;
            w.ZombieCrouchSpreadMultiplier = w.CrouchSpreadMultiplier; w.ZombieProneSpreadMultiplier = w.ProneSpreadMultiplier;
            w.ZombieStandMoveSpreadMultiplier = w.StandMoveSpreadMultiplier; w.ZombieSneakMoveSpreadMultiplier = w.SneakMoveSpreadMultiplier;
            w.ZombieCrouchMoveSpreadMultiplier = w.CrouchMoveSpreadMultiplier; w.ZombieJumpSpreadMultiplier = w.JumpSpreadMultiplier;
            w.ZombieViewSlideRecoilUp = w.ViewSlideRecoilUp; w.ZombieViewSlideRecoilRight = w.ViewSlideRecoilRight;
            w.ZombieViewSlideRecoilIronsightUp = w.ViewSlideRecoilIronsightUp; w.ZombieViewSlideRecoilIronsightRight = w.ViewSlideRecoilIronsightRight;
            w.ZombieFireRate = w.FireRate; w.ZombieExtraBulletChamber = w.ExtraBulletChamber;
            w.ZombieShakeScale = w.ShakeScale; w.ZombieShakeFreq = w.ShakeFreq; w.ZombieShakeDuration = w.ShakeDuration;
            w.ZombieCrosshairMinDistance = w.CrosshairMinDistance; w.ZombieCrosshairDeltaDistance = w.CrosshairDeltaDistance;
            w.ZombieWeight = w.Weight; w.ZombieZMBuyPrice = w.ZMBuyPrice; w.ZombieZMWeight = w.ZMWeight;
            w.ZombieRecoilPushbackValue = w.RecoilPushbackValue; w.ZombieIronsightWalkBobbingStrength = w.IronsightWalkBobbingStrength;
            w.ZombieMetalPenetrationDepth = w.MetalPenetrationDepth; w.ZombieGlassPenetrationDepth = w.GlassPenetrationDepth;
            w.ZombieConcretePenetrationDepth = w.ConcretePenetrationDepth; w.ZombieWoodPenetrationDepth = w.WoodPenetrationDepth;
            w.ZombieOtherPenetrationDepth = w.OtherPenetrationDepth;
            w.ZombieMetalDamageModifier = w.MetalDamageModifier; w.ZombieGlassDamageModifier = w.GlassDamageModifier;
            w.ZombieConcreteDamageModifier = w.ConcreteDamageModifier; w.ZombieWoodDamageModifier = w.WoodDamageModifier;
            w.ZombieOtherDamageModifier = w.OtherDamageModifier; w.ZombieNearwallDistance = w.NearwallDistance;
            w.ZombieClipSize = w.ClipSize; w.ZombieFireModes = w.FireModes;
            w.ZombieSecondaryFireRate = w.SecondaryFireRate; w.ZombieIronSight = w.IronSight;
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
                    cmbWeaponsL.DataSource = null; cmbWeaponsL.DataSource = new List<WeaponData>(weapons); cmbWeaponsL.DisplayMember = "PrintName";
                    cmbWeaponsR.DataSource = null; cmbWeaponsR.DataSource = new List<WeaponData>(weapons); cmbWeaponsR.DisplayMember = "PrintName";
                    if (weapons.Count > 0)
                    {
                        RestoreComboSelection(cmbWeaponsR, rightName);
                        cmbWeaponsL.SelectedIndexChanged += WeaponSelectedL; cmbWeaponsR.SelectedIndexChanged += WeaponSelectedR;
                        RestoreComboSelection(cmbWeaponsL, leftName);
                    }
                    else { cmbWeaponsL.SelectedIndexChanged += WeaponSelectedL; cmbWeaponsR.SelectedIndexChanged += WeaponSelectedR; }
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
            if (string.Equals(w.ScriptName, scriptName, StringComparison.OrdinalIgnoreCase)) { cmb.SelectedItem = w; return; }
        cmb.SelectedIndex = 0;
    }

    private void Form1_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.S) { e.SuppressKeyPress = true; BtnSave_Click(sender, e); }
        else if (e.Control && e.KeyCode == Keys.D1) { e.SuppressKeyPress = true; cmbWeaponsL.Focus(); cmbWeaponsL.DroppedDown = true; }
        else if (e.Control && e.KeyCode == Keys.D2) { e.SuppressKeyPress = true; cmbWeaponsR.Focus(); cmbWeaponsR.DroppedDown = true; }
        else if (e.Control && e.KeyCode == Keys.Z) { e.SuppressKeyPress = true; RestoreSnapshot(IsControlOnLeft(this.ActiveControl)); }
        else if (e.Control && e.KeyCode == Keys.R) { e.SuppressKeyPress = true; RefreshWeaponList(); }
    }
    #endregion
}