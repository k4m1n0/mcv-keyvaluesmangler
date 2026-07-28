using System;
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
        try
        {
        if (initializing)
        {
            if (cmbWeaponsL.SelectedItem is WeaponData initW)
            {
                currentWeaponLeft = initW;
                LoadWeaponToControls(initW, true);
                StoreSnapshot();
            }
            return;
            //初始化阶段直接加载不检查
        }

        bool isRapid = _rapidStartLeft != null && (DateTime.Now - _rapidDeadlineL).TotalMilliseconds < RapidSettleMs;
        if (!isRapid)
        {
            bool wasRapid = _rapidStartLeft != null;
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
            PushUndo();
            _rapidStartLeft = wasRapid ? null : currentWeaponLeft?.ScriptName;
        }
        //rapid中跳过弹窗和入栈 只更新截止时间
        _rapidDeadlineL = DateTime.Now.AddMilliseconds(RapidSettleMs);

        if (cmbWeaponsL.SelectedItem is WeaponData w)
        {
            currentWeaponLeft = w;
            LoadWeaponToControls(w, true);
            StoreSnapshot();
            UpdateAllDamage();
            pnlSpread.Invalidate();
            pnlRecoil.Invalidate();
            if (!isRapid)
                LogService.Debug($"Weapon selected L: {w.ScriptName}");
            else
                LogService.DebugDebounce("rapid_weapon_L", $"Rapid weapon L: {w.ScriptName}", 300);
            if (showingAltStats && WeaponHasAltStats(w, currentAltStatMode))
            {
                updatingControls = true;
                LoadAltStatsToControls(true, currentAltStatMode);
                SetAltStatReadonly(true, currentAltStatMode);
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
        catch (Exception ex)
        {
            LogService.Error(ex, "WeaponSelectedL");
        }
    }

    private void WeaponSelectedR(object? sender, EventArgs e)
    {
        try
        {
        if (initializing)
        {
            if (cmbWeaponsR.SelectedItem is WeaponData initW)
            {
                currentWeaponRight = initW;
                LoadWeaponToControls(initW, false);
                StoreSnapshot();
            }
            return;
        }

        bool isRapid = _rapidStartRight != null && (DateTime.Now - _rapidDeadlineR).TotalMilliseconds < RapidSettleMs;
        if (!isRapid)
        {
            bool wasRapid = _rapidStartRight != null;
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
            PushUndo();
            _rapidStartRight = wasRapid ? null : currentWeaponRight?.ScriptName;
        }
        _rapidDeadlineR = DateTime.Now.AddMilliseconds(RapidSettleMs);

        if (cmbWeaponsR.SelectedItem is WeaponData w)
        {
            currentWeaponRight = w;
            LoadWeaponToControls(w, false);
            StoreSnapshot();
            UpdateAllDamage();
            pnlSpread.Invalidate();
            pnlRecoil.Invalidate();
            if (!isRapid)
                LogService.Debug($"Weapon selected R: {w.ScriptName}");
            else
                LogService.DebugDebounce("rapid_weapon_R", $"Rapid weapon R: {w.ScriptName}", 300);
            if (showingAltStats && WeaponHasAltStats(w, currentAltStatMode))
            {
                updatingControls = true;
                LoadAltStatsToControls(false, currentAltStatMode);
                SetAltStatReadonly(false, currentAltStatMode);
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
        catch (Exception ex)
        {
            LogService.Error(ex, "WeaponSelectedR");
        }
    }

    private bool HasUnsavedChanges(bool isLeft)
    {
        var snap = isLeft ? _snapshotLeft : _snapshotRight;
        if (snap == null)
        {
            LogService.Debug($"HasUnsavedChanges({(isLeft ? "L" : "R")}): snap is null -> false");
            return false;
        }
        //同一武器时任意一侧有修改都算 不区分焦点
        if (currentWeaponLeft != null && currentWeaponRight != null
            && ReferenceEquals(currentWeaponLeft, currentWeaponRight))
        {
            var tempL = new WeaponData(); SaveControlsToWeapon(tempL, true);
            var tempR = new WeaponData(); SaveControlsToWeapon(tempR, false);
            bool leftDiff = !WeaponDataEquals(tempL, _snapshotLeft);
            bool rightDiff = !WeaponDataEquals(tempR, _snapshotRight);
            bool result = leftDiff || rightDiff;
            LogService.Debug($"HasUnsavedChanges(sameWeapon): L={leftDiff}, R={rightDiff} -> {result}");
            return result;
        }
        var temp = new WeaponData();
        SaveControlsToWeapon(temp, isLeft);
        bool diff = !WeaponDataEquals(temp, snap);
        LogService.Debug($"HasUnsavedChanges({(isLeft ? "L" : "R")}): diff={diff}");
        return diff;
        //控件值写入临时对象与保存点快照比对
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
        LogService.DebugDebounce("spread_recoil_L", "Spread/Recoil L: panel invalidate", 500);
        pnlSpread.Invalidate(); pnlRecoil.Invalidate();
    }

    private void SpreadRecoilChangedR(object? sender, EventArgs e)
    {
        LogService.DebugDebounce("spread_recoil_R", "Spread/Recoil R: panel invalidate", 500);
        pnlSpread.Invalidate(); pnlRecoil.Invalidate();
    }

    private void RangeModifierChangedL(object? sender, EventArgs e) { UpdateAllDamage(); }
    private void RangeModifierChangedR(object? sender, EventArgs e) { UpdateAllDamage(); }

    #endregion
    #region 保存导入导出

    private async void BtnSave_Click(object? sender, EventArgs e)
    {
        if (System.Threading.Interlocked.Exchange(ref saveLock, 1) != 0) return;
        try
        {
            LogService.Info("BtnSave: saving weapons...");
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
                    if (showingAltStats && WeaponHasAltStats(currentWeaponLeft, currentAltStatMode))
                        LoadAltStatsToControls(false, currentAltStatMode);
                    else LoadWeaponToControls(currentWeaponLeft!, false);
                }
                else
                {
                    SaveControlsToWeapon(currentWeaponRight!, false);
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
            //栈顶已是当前状态则不入栈 避免连续保存产生重复条目
            bool sameAsTop = _undoStack.Count > 0;
            if (sameAsTop)
            {
                var last = _undoStack.Last!.Value;
                var tempL = new WeaponData(); SaveControlsToWeapon(tempL, true);
                var tempR = new WeaponData(); SaveControlsToWeapon(tempR, false);
                sameAsTop = WeaponDataEquals(tempL, last.LeftData) && WeaponDataEquals(tempR, last.RightData);
            }
            if (!sameAsTop) PushUndo();
            ClearRedo();
            StoreSnapshot();
            var savedTitle = this.Text;
            this.Text = "Saved!";
            try
            {
                CsvService.SaveWeapons(Path.Combine(AppContext.BaseDirectory, "weapons.csv"), weapons);
                await Task.Delay(1145);
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "BtnSave");
                MessageBox.Show($"Save failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { this.Text = savedTitle; }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "BtnSave outer");
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
        LogService.Info($"BtnCsvToScripts: exporting to {dir}");
        Task.Run(() =>
        {
            try
            {
                string log = WeaponScriptService.ExportCsvToScripts(csv, dir);
                this.Invoke(() => { using var lf = new LogForm("Export Complete", log); lf.ShowDialog(this); });
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "BtnCsvToScripts");
                this.Invoke(() => MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error));
            }
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
            LogService.Info($"BtnQuickExport: exporting to {lastScriptsDir}, altStats={showingAltStats}");
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
                    if (showingAltStats && WeaponHasAltStats(currentWeaponLeft, currentAltStatMode))
                        LoadAltStatsToControls(false, currentAltStatMode);
                    else LoadWeaponToControls(currentWeaponLeft!, false);
                }
                else
                {
                    SaveControlsToWeapon(currentWeaponRight!, false);
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
            ClearRedo();
            StoreSnapshot();
            var originalTitle = this.Text;
            try
            {
                CsvService.SaveWeapons(Path.Combine(AppContext.BaseDirectory, "weapons.csv"), weapons);
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
            catch (Exception ex)
            {
                LogService.Error(ex, "BtnQuickExport");
                btn.Text = "wpn_reload_script all"; btn.Tag = false;
                MessageBox.Show($"Quick export failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { this.Text = originalTitle; }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "BtnQuickExport outer");
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
        LogService.Info($"BtnScriptsToCsv: importing from {dir}");
        Task.Run(() =>
        {
            try
            {
                string log = WeaponScriptService.ImportScriptsToCsv(dir, csv);
                this.Invoke(() => { RefreshWeaponList(); ClearUndoHistory(); using var lf = new LogForm("Import Complete", log); lf.ShowDialog(this); });
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "BtnScriptsToCsv");
                this.Invoke(() => MessageBox.Show($"Import failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error));
            }
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
        LogService.Info($"BtnConvertToTemplate: dir={dir}, simpleMode={simpleMode}");
        Task.Run(() =>
        {
            try
            {
                string log = Tools.ScriptToTemplateConverter.ConvertAll(dir, simpleMode);
                this.Invoke(() => { RefreshWeaponList(); using var lf = new LogForm("Template Convert", log); lf.ShowDialog(this); });
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "BtnConvertToTemplate");
                this.Invoke(() => MessageBox.Show($"Template convert failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error));
            }
        });
    }

    private void BtnRefresh_Click(object? sender, EventArgs e)
    {
        LogService.Debug("BtnRefresh clicked");
        RefreshWeaponList();
    }

    private async void RefreshWeaponList()
    {
        if (refreshing) return;
        refreshing = true;
        string leftName = currentWeaponLeft?.ScriptName ?? "";
        string rightName = currentWeaponRight?.ScriptName ?? "";
        LogService.Info($"RefreshWeaponList: from CSV, restoring {leftName} / {rightName}");
        try
        {
            await Task.Run(() =>
            {
                string csv = Path.Combine(AppContext.BaseDirectory, "weapons.csv");
                weapons = CsvService.LoadWeapons(csv);
                this.Invoke(() =>
                {
                    try
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
                        if (currentWeaponLeft != null) { LoadWeaponToControls(currentWeaponLeft, true); UpdateAllDamage(); }
                        if (currentWeaponRight != null) { LoadWeaponToControls(currentWeaponRight, false); UpdateAllDamage(); }
                        pnlSpread.Invalidate();
                        pnlRecoil.Invalidate();
                    }
                    else { cmbWeaponsL.SelectedIndexChanged += WeaponSelectedL; cmbWeaponsR.SelectedIndexChanged += WeaponSelectedR; }
                    UpdateC64Labels(weapons.Count > 0);
                    }
                    catch (Exception ex)
                    {
                        LogService.Error(ex, "RefreshWeaponList.Invoke");
                    }
                });
            });
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "RefreshWeaponList");
            this.Invoke(() => MessageBox.Show($"Refresh failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error));
        }
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
        if (e.Control && e.Shift && e.KeyCode == Keys.S) { LogService.Debug("Hotkey: Ctrl+Shift+S"); e.SuppressKeyPress = true; BtnSave_Click(sender, e); BtnQuickExport_Click(sender, e); }
        else if (e.Control && e.KeyCode == Keys.S) { LogService.Debug("Hotkey: Ctrl+S"); e.SuppressKeyPress = true; BtnSave_Click(sender, e); }
        else if (e.Control && e.KeyCode == Keys.Y) { LogService.Debug("Hotkey: Ctrl+Y (redo)"); e.SuppressKeyPress = true; PopRedo(); }
        else if (e.Control && e.KeyCode == Keys.Z) { LogService.Debug("Hotkey: Ctrl+Z (undo)"); e.SuppressKeyPress = true; PopUndo(); }
        else if (e.Control && e.KeyCode == Keys.D1) { LogService.Debug("Hotkey: Ctrl+1 (focus L)"); e.SuppressKeyPress = true; cmbWeaponsL.Focus(); cmbWeaponsL.DroppedDown = true; }
        else if (e.Control && e.KeyCode == Keys.D2) { LogService.Debug("Hotkey: Ctrl+2 (focus R)"); e.SuppressKeyPress = true; cmbWeaponsR.Focus(); cmbWeaponsR.DroppedDown = true; }
        else if (e.Control && e.KeyCode == Keys.R) { LogService.Debug("Hotkey: Ctrl+R (refresh)"); e.SuppressKeyPress = true; RefreshWeaponList(); }
    }
    #endregion
}