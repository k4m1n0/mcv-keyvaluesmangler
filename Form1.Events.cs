using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Text;
using System.Text.RegularExpressions;
using WeaponDamageCalc.Models;
using WeaponDamageCalc.Services;

namespace WeaponDamageCalc;

public partial class Form1
{
    #region 武器选择
    private void WeaponSelected(bool bIsLeft, object? sender, EventArgs e)
    {
        try
        {
            if (bSuppressUndo) return;
            var cmb = bIsLeft ? cmbWeaponsL : cmbWeaponsR;
            var wCurrent = bIsLeft ? wCurrentLeft : wCurrentRight;
            string sSide = bIsLeft ? "L" : "R";
            var dtRapidDeadline = bIsLeft ? dtRapidDeadlineL : dtRapidDeadlineR;
            var sRapidStart = bIsLeft ? sRapidStartLeft : sRapidStartRight;

            if (bRestoringSelection)
            {
                LogService.Debug("WeaponSelected: restoring selection, skip");
                return;
            }
            if (bInitializing)
            {
                if (cmb.SelectedItem is WeaponData wInit)
                {
                    if (bIsLeft) wCurrentLeft = wInit;
                    else wCurrentRight = wInit;
                    LoadWeaponToControls(wInit, bIsLeft);
                    StoreSnapshot();
                }
                return;
                //初始化阶段直接加载不检查
            }

            bool bIsRapid = sRapidStart != null && (DateTime.Now - dtRapidDeadline).TotalMilliseconds < iRapidSettleMs;
            if (!bIsRapid)
            {
                bool bWasRapid = sRapidStart != null;
                if (bShowingAltStats)
                {
                    bool bFocusLeft = ResolveSameWeaponFocus();
                    if (bFocusLeft && wCurrentLeft != null && WeaponHasAltStats(wCurrentLeft, amCurrentAltStat))
                        SyncAltStatFields(wCurrentLeft, amCurrentAltStat, true);
                    else if (!bFocusLeft && wCurrentRight != null && WeaponHasAltStats(wCurrentRight, amCurrentAltStat))
                        SyncAltStatFields(wCurrentRight, amCurrentAltStat, false);
                    if (wCurrent != null && HasUnsavedChanges(bIsLeft, bCheckBothSides: true))
                    {
                        var drResult = MessageBox.Show($"Unsaved alt stat changes to {sSide.ToLower()} weapon. Discard?",
                            "Unsaved Changes", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                        if (drResult != DialogResult.Yes)
                        {
                            bRestoringSelection = true;
                            cmb.SelectedItem = wCurrent;
                            BeginInvoke(() => bRestoringSelection = false);
                            return;
                        }
                    }
                }
                else if (wCurrent != null && HasUnsavedChanges(bIsLeft))
                {
                    var drResult = MessageBox.Show($"Unsaved changes to {sSide.ToLower()} weapon. Discard?",
                        "Unsaved Changes", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                    if (drResult != DialogResult.Yes)
                    {
                        bRestoringSelection = true;
                        cmb.SelectedItem = wCurrent;
                        BeginInvoke(() => bRestoringSelection = false);
                        return;
                    }
                }
                PushUndo();
                if (bIsLeft) sRapidStartLeft = bWasRapid ? null : wCurrentLeft?.ScriptName;
                else sRapidStartRight = bWasRapid ? null : wCurrentRight?.ScriptName;
            }
            //rapid中跳过弹窗和入栈 只更新截止时间
            if (bIsLeft) dtRapidDeadlineL = DateTime.Now.AddMilliseconds(iRapidSettleMs);
            else dtRapidDeadlineR = DateTime.Now.AddMilliseconds(iRapidSettleMs);

            if (cmb.SelectedItem is WeaponData w)
            {
                if (bIsLeft) wCurrentLeft = w;
                else wCurrentRight = w;
                bUpdatingControls = true;
                try { LoadWeaponToControls(w, bIsLeft); }
                finally { bUpdatingControls = false; }
                UpdateAllDamage();
                pnlSpread.Invalidate();
                pnlRecoil.Invalidate();
                if (!bIsRapid)
                    LogService.Debug($"Weapon selected {sSide}: {w.ScriptName}");
                else
                    LogService.DebugDebounce($"rapid_weapon_{sSide}", $"Rapid weapon {sSide}: {w.ScriptName}", 300);
                if (bShowingAltStats)
                {
                    bool bHasAltStats = WeaponHasAltStats(w, amCurrentAltStat);
                    if (bHasAltStats)
                    {
                        bUpdatingControls = true;
                        LoadAltStatsToControls(bIsLeft, amCurrentAltStat);
                        SetAltStatReadonly(bIsLeft, amCurrentAltStat);
                        bUpdatingControls = false;
                    }
                    else
                    {
                        RestoreAllNudEnabled(bIsLeft);
                        bUpdatingControls = true;
                        LoadWeaponToControls(w, bIsLeft);
                        bUpdatingControls = false;
                    }
                    HighlightAltStatButton(amCurrentAltStat);
                    UpdateAllDamage();
                    pnlSpread.Invalidate();
                    pnlRecoil.Invalidate();
                }
                StoreSnapshot(bLeftOnly: bIsLeft);
                SetC64Status("READY.");
            }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, $"WeaponSelected{(bIsLeft ? "L" : "R")}");
        }
    }

    #endregion
    #region 未保存检测

    private bool HasUnsavedChanges(bool bIsLeft, bool bCheckBothSides = false)
    {
        var wSnap = bIsLeft ? wSnapshotLeft : wSnapshotRight;
        if (wSnap == null)
        {
            LogService.Debug($"HasUnsavedChanges({(bIsLeft ? "L" : "R")}): snap is null -> false");
            return false;
        }
        //同一武器时任意一侧有修改都算 不区分焦点
        if (wCurrentLeft != null && wCurrentRight != null
            && ReferenceEquals(wCurrentLeft, wCurrentRight))
        {
            if (!bCheckBothSides)
            {
                var wTemp = new WeaponData();
                SaveControlsToWeapon(wTemp, bIsLeft);
                bool bDiff = !VisibleValuesEqual(wTemp, wSnap);
                LogService.Debug($"HasUnsavedChanges(sameWeapon, {(bIsLeft ? "L" : "R")} only): diff={bDiff}");
                return bDiff;
            }
            var wTempL = new WeaponData(); SaveControlsToWeapon(wTempL, true);
            var wTempR = new WeaponData(); SaveControlsToWeapon(wTempR, false);
            bool bLeftDiff = !VisibleValuesEqual(wTempL, wSnapshotLeft);
            bool bRightDiff = !VisibleValuesEqual(wTempR, wSnapshotRight);
            bool bResult = bLeftDiff || bRightDiff;
            LogService.Debug($"HasUnsavedChanges(sameWeapon, both): L={bLeftDiff}, R={bRightDiff} -> {bResult}");
            return bResult;
        }
        var wTemp2 = new WeaponData();
        SaveControlsToWeapon(wTemp2, bIsLeft);
        bool bDiff2 = !VisibleValuesEqual(wTemp2, wSnap);
        LogService.Debug($"HasUnsavedChanges({(bIsLeft ? "L" : "R")}): diff={bDiff2}");
        return bDiff2;
        //控件值写入临时对象与保存点快照比对
    }

    private static bool VisibleValuesEqual(WeaponData? wA, WeaponData? wB)//双浮点比对容忍0.0001误差 防止SaveControlsToWeapon的SliderStep浮点往返造成假阳性
    {
        if (wA == null || wB == null) return wA == wB;
        return NullableEquals(wA.DamageHeadMultiplier, wB.DamageHeadMultiplier)
            && NullableEquals(wA.DamageChestMultiplier, wB.DamageChestMultiplier)
            && NullableEquals(wA.DamageStomachMultiplier, wB.DamageStomachMultiplier)
            && NullableEquals(wA.DamageLegMultiplier, wB.DamageLegMultiplier)
            && NullableEquals(wA.DamageArmMultiplier, wB.DamageArmMultiplier)
            && NullableEquals(wA.BulletSpread, wB.BulletSpread)
            && NullableEquals(wA.BulletSpreadDegreesIronsighted, wB.BulletSpreadDegreesIronsighted)
            && NullableEquals(wA.BulletSpreadDegreesBipod, wB.BulletSpreadDegreesBipod)
            && NullableEquals(wA.BulletSpreadDegreesBipodIronsighted, wB.BulletSpreadDegreesBipodIronsighted)
            && NullableEquals(wA.ViewSlideRecoilUp, wB.ViewSlideRecoilUp)
            && NullableEquals(wA.ViewSlideRecoilRight, wB.ViewSlideRecoilRight)
            && NullableEquals(wA.ViewSlideRecoilIronsightUp, wB.ViewSlideRecoilIronsightUp)
            && NullableEquals(wA.ViewSlideRecoilIronsightRight, wB.ViewSlideRecoilIronsightRight)
            && string.Equals(wA.FireModes, wB.FireModes)
            && IntNullableEquals(wA.FireRate, wB.FireRate)
            && IntNullableEquals(wA.SecondaryFireRate, wB.SecondaryFireRate)
            && NullableEquals(wA.RangeModifier, wB.RangeModifier)
            && string.Equals(wA.ClipSize, wB.ClipSize)
            && IntNullableEquals(wA.ExtraBulletChamber, wB.ExtraBulletChamber)
            && IntNullableEquals(wA.BulletsPerShot, wB.BulletsPerShot)
            && NullableEquals(wA.IronsightSpeedScale, wB.IronsightSpeedScale)
            && IntNullableEquals(wA.IronSight, wB.IronSight)
            && NullableEquals(wA.Weight, wB.Weight)
            && IntNullableEquals(wA.ZMBuyPrice, wB.ZMBuyPrice)
            && IntNullableEquals(wA.ZMWeight, wB.ZMWeight)
            && NullableEquals(wA.MetalPenetrationDepth, wB.MetalPenetrationDepth)
            && NullableEquals(wA.GlassPenetrationDepth, wB.GlassPenetrationDepth)
            && NullableEquals(wA.ConcretePenetrationDepth, wB.ConcretePenetrationDepth)
            && NullableEquals(wA.WoodPenetrationDepth, wB.WoodPenetrationDepth)
            && NullableEquals(wA.OtherPenetrationDepth, wB.OtherPenetrationDepth)
            && NullableEquals(wA.MetalDamageModifier, wB.MetalDamageModifier)
            && NullableEquals(wA.GlassDamageModifier, wB.GlassDamageModifier)
            && NullableEquals(wA.ConcreteDamageModifier, wB.ConcreteDamageModifier)
            && NullableEquals(wA.WoodDamageModifier, wB.WoodDamageModifier)
            && NullableEquals(wA.OtherDamageModifier, wB.OtherDamageModifier)
            && NullableEquals(wA.CrouchSpreadMultiplier, wB.CrouchSpreadMultiplier)
            && NullableEquals(wA.ProneSpreadMultiplier, wB.ProneSpreadMultiplier)
            && NullableEquals(wA.StandMoveSpreadMultiplier, wB.StandMoveSpreadMultiplier)
            && NullableEquals(wA.SneakMoveSpreadMultiplier, wB.SneakMoveSpreadMultiplier)
            && NullableEquals(wA.CrouchMoveSpreadMultiplier, wB.CrouchMoveSpreadMultiplier)
            && NullableEquals(wA.JumpSpreadMultiplier, wB.JumpSpreadMultiplier)
            && NullableEquals(wA.DamageGeneric, wB.DamageGeneric);
    }

    private static bool NullableEquals(double? fA, double? fB)
    {
        double dVa = fA ?? 0.0;
        double dVb = fB ?? 0.0;
        return Math.Abs(dVa - dVb) < 0.001;
        //容差比较 防止掉精度导致误判为未保存
    }

    private static bool IntNullableEquals(int? nA, int? nB)
    {
        if (!nA.HasValue && !nB.HasValue) return true;
        if (!nA.HasValue || !nB.HasValue) return false;
        return nA.Value == nB.Value;
    }

    //同一武器时判定应该读哪一侧的控件值
    private bool ResolveSameWeaponFocus()
    {
        var wCmpL = new WeaponData(); SaveControlsToWeapon(wCmpL, true);
        var wCmpR = new WeaponData(); SaveControlsToWeapon(wCmpR, false);
        if (!VisibleValuesEqual(wCmpL, wCmpR))
        {
            bool bLeftDiff = !VisibleValuesEqual(wCmpL, wSnapshotLeft);
            bool bRightDiff = !VisibleValuesEqual(wCmpR, wSnapshotRight);
            if (bLeftDiff && !bRightDiff) return true;
            if (!bLeftDiff && bRightDiff) return false;
        }
        return bLastFocusLeft;
    }

    #endregion
    #region 滑块联动

    private void SliderChangedL(object? sender, EventArgs e)
    {
        if (bUpdatingControls) return;
        bUpdatingControls = true;//都是防止滑块和数字框互相触发死循环
        if (sender is TrackBar tb && tb.Tag is NumericUpDown nud)
            nud.Value = Math.Round((decimal)(tb.Value * dSliderStep), 2);
        bUpdatingControls = false;
        UpdateAllDamage();
    }

    private void SliderChangedR(object? sender, EventArgs e)
    {
        if (bUpdatingControls) return;
        bUpdatingControls = true;
        if (sender is TrackBar tb && tb.Tag is NumericUpDown nud)
            nud.Value = Math.Round((decimal)(tb.Value * dSliderStep), 2);
        bUpdatingControls = false;
        UpdateAllDamage();
    }

    private void NumericChangedL(object? sender, EventArgs e)
    {
        if (bUpdatingControls) return;
        bUpdatingControls = true;
        if (sender is NumericUpDown nud && nud.Tag is TrackBar tb)
        {
            int iIv = (int)Math.Round((double)nud.Value / dSliderStep);
            iIv = Math.Max(tb.Minimum, Math.Min(tb.Maximum, iIv));
            tb.Value = iIv;
            nud.Value = Math.Round(nud.Value / nud.Increment) * nud.Increment;
        }
        bUpdatingControls = false;
        UpdateAllDamage();
    }

    private void NumericChangedR(object? sender, EventArgs e)
    {
        if (bUpdatingControls) return;
        bUpdatingControls = true;
        if (sender is NumericUpDown nud && nud.Tag is TrackBar tb)
        {
            int iIv = (int)Math.Round((double)nud.Value / dSliderStep);
            iIv = Math.Max(tb.Minimum, Math.Min(tb.Maximum, iIv));
            tb.Value = iIv;
            nud.Value = Math.Round(nud.Value / nud.Increment) * nud.Increment;
        }
        bUpdatingControls = false;
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
    #region 保存导出

    private void CommitCurrentControlsToWeapons()
    {
        //保存是操作边界 结束rapid序列防止保存后的切换被误判为rapid
        sRapidStartLeft = null;
        sRapidStartRight = null;

        //强制提交活跃控件的待定输入 防止NUD焦点未移走导致值未更新
        var ctrlActive = this.ActiveControl;
        if (ctrlActive != null) { this.ActiveControl = null; ctrlActive.Focus(); }
        bool bSameWeapon = wCurrentLeft != null && wCurrentRight != null
            && ReferenceEquals(wCurrentLeft, wCurrentRight);
        if (bSameWeapon)
        {
            bool bFocusLeft = ResolveSameWeaponFocus();
            if (bFocusLeft)
            {
                if (bShowingAltStats)
                {
                    SyncAltStatFields(wCurrentLeft!, amCurrentAltStat, true);
                    var wOldClone = CloneWeaponData(wCurrentLeft!);
                    SyncAltStatsToMatchTopLevel(wOldClone, wCurrentLeft!);
                    LoadAltStatsToControls(true, amCurrentAltStat);
                    LoadAltStatsToControls(false, amCurrentAltStat);
                }
                else
                {
                    var wOld = CloneWeaponData(wCurrentLeft!);
                    SaveControlsToWeapon(wCurrentLeft!, true);
                    SyncAltStatsToMatchTopLevel(wOld, wCurrentLeft!);
                    LoadWeaponToControls(wCurrentLeft!, false);
                }
            }
            else
            {
                if (bShowingAltStats)
                {
                    SyncAltStatFields(wCurrentRight!, amCurrentAltStat, false);
                    var wOldClone = CloneWeaponData(wCurrentRight!);
                    SyncAltStatsToMatchTopLevel(wOldClone, wCurrentRight!);
                    LoadAltStatsToControls(false, amCurrentAltStat);
                    LoadAltStatsToControls(true, amCurrentAltStat);
                }
                else
                {
                    var wOld = CloneWeaponData(wCurrentRight!);
                    SaveControlsToWeapon(wCurrentRight!, false);
                    SyncAltStatsToMatchTopLevel(wOld, wCurrentRight!);
                    LoadWeaponToControls(wCurrentRight!, true);
                }
            }
            UpdateAllDamage(); pnlSpread.Invalidate(); pnlRecoil.Invalidate();
        }
        else if (!bShowingAltStats)
        {
            if (wCurrentLeft != null)
            {
                var wOldL = CloneWeaponData(wCurrentLeft);
                SaveControlsToWeapon(wCurrentLeft, true);
                SyncAltStatsToMatchTopLevel(wOldL, wCurrentLeft);
            }
            if (wCurrentRight != null)
            {
                var wOldR = CloneWeaponData(wCurrentRight);
                SaveControlsToWeapon(wCurrentRight, false);
                SyncAltStatsToMatchTopLevel(wOldR, wCurrentRight);
            }
        }
        if (bShowingAltStats && !bSameWeapon)
        {
            if (wCurrentLeft != null)
            {
                SyncAltStatFields(wCurrentLeft, amCurrentAltStat);
                var wOldCloneL = CloneWeaponData(wCurrentLeft);
                SyncAltStatsToMatchTopLevel(wOldCloneL, wCurrentLeft);
            }
            if (wCurrentRight != null && !ReferenceEquals(wCurrentLeft, wCurrentRight))
            {
                SyncAltStatFields(wCurrentRight, amCurrentAltStat);
                var wOldCloneR = CloneWeaponData(wCurrentRight);
                SyncAltStatsToMatchTopLevel(wOldCloneR, wCurrentRight);
            }
        }
        //栈顶已是当前状态则不入栈 避免连续保存产生重复条目
        bool bSameAsTop = llUndoStack.Count > 0;
        if (bSameAsTop)
        {
            var ueLast = llUndoStack.Last!.Value;
            var wTempL = new WeaponData(); SaveControlsToWeapon(wTempL, true);
            var wTempR = new WeaponData(); SaveControlsToWeapon(wTempR, false);
            bSameAsTop = VisibleValuesEqual(wTempL, ueLast.LeftData) && VisibleValuesEqual(wTempR, ueLast.RightData);
        }
        if (!bSameAsTop) PushUndo();
        ClearRedo();
        StoreSnapshot();
        if (bShowingAltStats)
            HighlightAltStatButton(amCurrentAltStat);
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        if (System.Threading.Interlocked.Exchange(ref iSaveLock, 1) != 0) return;
        try
        {
            LogService.Info("BtnSave: saving weapons...");
            CommitCurrentControlsToWeapons();
            try
            {
                SetC64Status("SAVING...");
                CsvService.SaveWeapons(Path.Combine(AppContext.BaseDirectory, "weapons.csv"), rgWeapons);
                tmrSnapshotCheck?.Stop(); tmrSnapshotCheck?.Dispose(); tmrSnapshotCheck = null;
                SetC64Status("SAVED.");
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "BtnSave");
                SetC64Status("SAVE FAILED.");
                MessageBox.Show($"Save failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "BtnSave outer");
        }
        finally { System.Threading.Interlocked.Exchange(ref iSaveLock, 0); }
    }

    #endregion
    #region 导入与转换

    private async void BtnCsvToScripts_Click(object? sender, EventArgs e)
    {
        string sInitialDir = string.IsNullOrEmpty(sLastScriptsDir) ? AppContext.BaseDirectory : sLastScriptsDir;
        using var dlg = new FolderBrowserDialog { Description = "Select the folder containing weapon scripts (will be overwritten)", UseDescriptionForTitle = true, InitialDirectory = sInitialDir };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        sLastScriptsDir = dlg.SelectedPath;
        string sDir = dlg.SelectedPath;
        if (MessageBox.Show($"Overwrite all scripts in the folder below with CSV data?\n\n{sDir}", "Confirm Overwrite", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        string sCsv = Path.Combine(AppContext.BaseDirectory, "weapons.csv");
        LogService.Info($"BtnCsvToScripts: exporting to {sDir}");
        try
        {
            string sLog = await Task.Run(() => WeaponScriptService.ExportCsvToScripts(sCsv, sDir));
            var lf = new LogForm("Export Complete", sLog, bDarkMode);
            lf.Show(this);
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "BtnCsvToScripts");
            MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void BtnQuickExport_Click(object? sender, EventArgs e)
    {
        if (System.Threading.Interlocked.Exchange(ref iSaveLock, 1) != 0) return;
        try
        {
            if (string.IsNullOrEmpty(sLastScriptsDir))
            {
                using var dlg = new FolderBrowserDialog { Description = "Select the folder containing weapon scripts (will be overwritten)", UseDescriptionForTitle = true, InitialDirectory = AppContext.BaseDirectory };
                if (dlg.ShowDialog() != DialogResult.OK) return;
                sLastScriptsDir = dlg.SelectedPath;
            }

            var btn = sender as Button;
            if (btn != null && btn.Tag is not true)
            {
                btn.Text = "confirm"; btn.Tag = true; return;
            }

            LogService.Info($"BtnQuickExport: exporting to {sLastScriptsDir}, altStats={bShowingAltStats}");
            CommitCurrentControlsToWeapons();
            try
            {
                SetC64Status("SAVING...");
                CsvService.SaveWeapons(Path.Combine(AppContext.BaseDirectory, "weapons.csv"), rgWeapons);
                string sCsv = Path.Combine(AppContext.BaseDirectory, "weapons.csv");
                await Task.Run(() =>
                {
                    WeaponScriptService.ExportCsvToScripts(sCsv, sLastScriptsDir);
                    WeaponScriptService.ExportAltStatsToScripts(sCsv, sLastScriptsDir, WeaponScriptService.AltStatMode.Dov);
                    WeaponScriptService.ExportAltStatsToScripts(sCsv, sLastScriptsDir, WeaponScriptService.AltStatMode.Zombie);
                });
                if (btn != null) { btn.Text = "wpn_reload_script all"; btn.Tag = false; }
                tmrSnapshotCheck?.Stop(); tmrSnapshotCheck?.Dispose(); tmrSnapshotCheck = null;
                SetC64Status("EXPORTED.");
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "BtnQuickExport");
                if (btn != null) { btn.Text = "wpn_reload_script all"; btn.Tag = false; }
                SetC64Status("EXPORT FAILED.");
                MessageBox.Show($"Quick export failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "BtnQuickExport outer");
        }
        finally { System.Threading.Interlocked.Exchange(ref iSaveLock, 0); }
    }

    private async void BtnScriptsToCsv_Click(object? sender, EventArgs e)
    {
        string sInitialDir = string.IsNullOrEmpty(sLastScriptsDir) ? AppContext.BaseDirectory : sLastScriptsDir;
        using var dlg = new FolderBrowserDialog { Description = "Select folder containing weapon scripts", UseDescriptionForTitle = true, InitialDirectory = sInitialDir };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        sLastScriptsDir = dlg.SelectedPath;
        string sDir = dlg.SelectedPath;
        string sCsv = Path.Combine(AppContext.BaseDirectory, "weapons.csv");
        LogService.Info($"BtnScriptsToCsv: importing from {sDir}");
        try
        {
            string sLog = await Task.Run(() => WeaponScriptService.ImportScriptsToCsv(sDir, sCsv));
            await RefreshWeaponList();
            ClearUndoHistory();
            var lf = new LogForm("Import Complete", sLog, bDarkMode);
            lf.Show(this);
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "BtnScriptsToCsv");
            MessageBox.Show($"Import failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void BtnConvertToTemplate_Click(object? sender, EventArgs e)
    {
        string sInitialDir = string.IsNullOrEmpty(sLastScriptsDir) ? AppContext.BaseDirectory : sLastScriptsDir;
        using var dlg = new FolderBrowserDialog { Description = "Select folder containing weapon scripts to convert", UseDescriptionForTitle = true, InitialDirectory = sInitialDir };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        string sDir = dlg.SelectedPath;
        var drResult = MessageBox.Show("Select conversion mode:\n\nYes = Full (keep empty keys)\nNo = Simple (remove empty keys)\nCancel = Abort",
            "Template Convert Mode", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (drResult == DialogResult.Cancel) return;
        bool bSimpleMode = (drResult == DialogResult.No);
        LogService.Info($"BtnConvertToTemplate: dir={sDir}, simpleMode={bSimpleMode}");
        try
        {
            string sLog = await Task.Run(() => Tools.ScriptToTemplateConverter.ConvertAll(sDir, bSimpleMode));
            await RefreshWeaponList();
            var lf = new LogForm("Template Convert", sLog, bDarkMode);
            lf.Show(this);
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "BtnConvertToTemplate");
            MessageBox.Show($"Template convert failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private bool PickScriptsDir(string sDescription)
    {
        string sInitial = string.IsNullOrEmpty(sLastScriptsDir) ? AppContext.BaseDirectory : sLastScriptsDir;
        using var dlg = new FolderBrowserDialog { Description = sDescription, UseDescriptionForTitle = true, InitialDirectory = sInitial };
        if (dlg.ShowDialog() != DialogResult.OK || string.IsNullOrEmpty(dlg.SelectedPath)) return false;
        sLastScriptsDir = dlg.SelectedPath;
        return true;
    }

    private void OpenScriptForCurrent(bool bIsLeft)
    {
        var wWeapon = bIsLeft ? wCurrentLeft : wCurrentRight;

        if (string.IsNullOrEmpty(sLastScriptsDir) && !PickScriptsDir("Select folder containing weapon scripts"))
            return;

        if (wWeapon == null || string.IsNullOrWhiteSpace(wWeapon.ScriptName))
            return;

        string sScriptName = wWeapon.ScriptName;

        string ResolvePath()
        {
            string sPath = Path.Combine(sLastScriptsDir, Path.GetFileName(sScriptName));
            if (!File.Exists(sPath) && !sPath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                sPath += ".txt";
            return sPath;
        }

        string sScriptPath = ResolvePath();

        if (!File.Exists(sScriptPath))
        {
            if (!PickScriptsDir("Script not found in current folder. Select the correct scripts folder")) return;
            sScriptPath = ResolvePath();
        }

        if (!File.Exists(sScriptPath))
        {
            MessageBox.Show($"Script not found:\n{sScriptPath}", "Open Script", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            using var p = Process.Start(new ProcessStartInfo(sScriptPath) { UseShellExecute = true });
            LogService.Info($"OpenScriptForCurrent: opened {sScriptPath}");
        }
        catch (Exception ex)
        {
            LogService.Error(ex, $"OpenScriptForCurrent: {sScriptPath}");
            MessageBox.Show($"Failed to open script: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    #endregion
    #region 刷新和快捷键

    private async void BtnRefresh_Click(object? sender, EventArgs e)
    {
        LogService.Debug("BtnRefresh clicked");
        await RefreshWeaponList();
    }

    public async Task RefreshWeaponList()
    {
        if (bRefreshing) return;
        bRefreshing = true;
        bSuppressUndo  = true;
        SetC64Status("LOADING...");
        string sLeftName = wCurrentLeft?.ScriptName ?? "";
        string sRightName = wCurrentRight?.ScriptName ?? "";
        LogService.Info($"RefreshWeaponList: from CSV, restoring {sLeftName} / {sRightName}");
        try
        {
            string sCsv = Path.Combine(AppContext.BaseDirectory, "weapons.csv");
            rgWeapons = await Task.Run(() => CsvService.LoadWeapons(sCsv));
            if (rgWeapons.Count == 0)
            {
                wCurrentLeft = null;
                wCurrentRight = null;
                wSnapshotLeft = null;
                wSnapshotRight = null;
                tmrUndo?.Stop();
                bUndoPending = false;
            }
            try
            {
                //先解绑事件防止刷新时弹出未保存确认
                cmbWeaponsL.SelectedIndexChanged -= (s, ev) => WeaponSelected(true, s, ev);
                cmbWeaponsR.SelectedIndexChanged -= (s, ev) => WeaponSelected(false, s, ev);
                //DataSource设为null再赋值新列表 触发重新绑定
                if (rgWeapons.Count > 0)
                {
                    cmbWeaponsL.DataSource = null; cmbWeaponsL.DataSource = new List<WeaponData>(rgWeapons); cmbWeaponsL.DisplayMember = "PrintName";
                    cmbWeaponsR.DataSource = null; cmbWeaponsR.DataSource = new List<WeaponData>(rgWeapons); cmbWeaponsR.DisplayMember = "PrintName";
                    RestoreComboSelection(cmbWeaponsR, sRightName);
                    RestoreComboSelection(cmbWeaponsL, sLeftName);
                    cmbWeaponsL.SelectedIndexChanged += (s, ev) => WeaponSelected(true, s, ev); cmbWeaponsR.SelectedIndexChanged += (s, ev) => WeaponSelected(false, s, ev);
                    wCurrentLeft = cmbWeaponsL.SelectedItem as WeaponData;
                    wCurrentRight = cmbWeaponsR.SelectedItem as WeaponData;
                    if (wCurrentLeft != null) { LoadWeaponToControls(wCurrentLeft, true); }
                    if (wCurrentRight != null) { LoadWeaponToControls(wCurrentRight, false); }
                    if (bShowingAltStats)
                    {
                        if (wCurrentLeft != null && WeaponHasAltStats(wCurrentLeft, amCurrentAltStat))
                            LoadAltStatsToControls(true, amCurrentAltStat);
                        if (wCurrentRight != null && WeaponHasAltStats(wCurrentRight, amCurrentAltStat))
                            LoadAltStatsToControls(false, amCurrentAltStat);
                    }
                    StoreSnapshot();
                    if (bShowingAltStats)
                        HighlightAltStatButton(amCurrentAltStat);
                    UpdateAllDamage();
                    pnlSpread.Invalidate();
                    pnlRecoil.Invalidate();
                }
                else
                {
                    cmbWeaponsL.DataSource = null;
                    cmbWeaponsR.DataSource = null;
                    cmbWeaponsL.SelectedIndexChanged += (s, ev) => WeaponSelected(true, s, ev);
                    cmbWeaponsR.SelectedIndexChanged += (s, ev) => WeaponSelected(false, s, ev);
                    if (bShowingAltStats)
                    {
                        bShowingAltStats = false;
                        RestoreAllNudEnabled(true);
                        RestoreAllNudEnabled(false);
                        ResetAltStatButtons();
                    }
                    wCurrentLeft = null;
                    wCurrentRight = null;
                    var wEmpty = new WeaponData();
                    LoadWeaponToControls(wEmpty, true);
                    LoadWeaponToControls(wEmpty, false);
                    wSnapshotLeft = null;
                    wSnapshotRight = null;
                    UpdateAllDamage();
                    pnlSpread.Invalidate();
                    pnlRecoil.Invalidate();
                }
                UpdateC64Labels();
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "RefreshWeaponList.Invoke");
            }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "RefreshWeaponList");
            MessageBox.Show($"Refresh failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            bSuppressUndo = false;
            tmrUndo?.Stop();
            bUndoPending = false;
            bRefreshing = false;
            SetC64Status(rgWeapons.Count > 0 ? "READY." : "");
        }
    }

    private static void RestoreComboSelection(ComboBox cmb, string sScriptName)
    {
        if (string.IsNullOrEmpty(sScriptName)) { cmb.SelectedIndex = 0; return; }
        foreach (WeaponData w in cmb.Items)
            if (string.Equals(w.ScriptName, sScriptName, StringComparison.OrdinalIgnoreCase)) { cmb.SelectedItem = w; return; }
        cmb.SelectedIndex = 0;
    }

    private async void Form1_KeyDown(object? sender, KeyEventArgs e)
    {
        try
        {
            if (e.Control && e.Shift && e.KeyCode == Keys.S) { LogService.Debug("Hotkey: Ctrl+Shift+S"); e.SuppressKeyPress = true; FlashButton(btnQuickExport); BtnQuickExport_Click(sender, e); }
            else if (e.Control && e.KeyCode == Keys.S) { LogService.Debug("Hotkey: Ctrl+S"); e.SuppressKeyPress = true; FlashButton(btnSave); BtnSave_Click(sender, e); }
            else if (e.Control && e.KeyCode == Keys.Y) { LogService.Debug("Hotkey: Ctrl+Y (redo)"); e.SuppressKeyPress = true; PopRedo(); }
            else if (e.Control && e.KeyCode == Keys.Z) { LogService.Debug("Hotkey: Ctrl+Z (undo)"); e.SuppressKeyPress = true; PopUndo(); }
            else if (e.Control && e.KeyCode == Keys.D1) { LogService.Debug("Hotkey: Ctrl+1 (focus L)"); e.SuppressKeyPress = true; cmbWeaponsL.Focus(); cmbWeaponsL.DroppedDown = true; }
            else if (e.Control && e.KeyCode == Keys.D2) { LogService.Debug("Hotkey: Ctrl+2 (focus R)"); e.SuppressKeyPress = true; cmbWeaponsR.Focus(); cmbWeaponsR.DroppedDown = true; }
            else if ((e.Control && e.KeyCode == Keys.R) || e.KeyCode == Keys.F5) { LogService.Debug($"Hotkey: {(e.Control ? "Ctrl+R" : "F5")} (refresh)"); e.SuppressKeyPress = true; FlashButton(btnRefresh); await RefreshWeaponList(); }
            else if (e.Control && e.KeyCode is Keys.Up or Keys.Down or Keys.Left or Keys.Right) { LogService.Debug($"Hotkey: Ctrl+{e.KeyCode} (navigate focus)"); e.SuppressKeyPress = true; e.Handled = true; NavigateFocus(e.KeyCode); }
            #if DEBUG
                    else if (e.Control && e.Shift && e.KeyCode == Keys.F12)
                    {
                        e.SuppressKeyPress = true;
                        WeaponDamageCalc.Tools.CsvMapperTests.RunAll();
                        await WeaponDamageCalc.Tools.UndoTests.RunAll(this);
                    }
            #endif
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "Form1_KeyDown");
        }
    }
    #endregion
}