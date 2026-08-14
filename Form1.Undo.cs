using System;
using System.Collections.Generic;
using System.Windows.Forms;
using WeaponDamageCalc.Models;
using WeaponDamageCalc.Services;

namespace WeaponDamageCalc;

//:)
public partial class Form1
{
    #region 数据结构

    private bool bUndoInProgress;
    private System.Windows.Forms.Timer tmrUndo = null!;
    private System.Windows.Forms.Timer? tmrSnapshotCheck;
    private bool bUndoPending;

    private WeaponData? wSnapshotLeft;
    private WeaponData? wSnapshotRight;

    private class UndoEntry
    {
        public string? LeftScriptName;
        public string? RightScriptName;
        public WeaponData LeftData = null!;
        public WeaponData RightData = null!;
        public bool ShowingAltStats;
        public WeaponScriptService.AltStatMode AltMode;
    }

    private LinkedList<UndoEntry> llUndoStack = new();
    private LinkedList<UndoEntry> llRedoStack = new();
    private const int iMaxUndo = 100;

    #endregion
    #region 快照

    public void StoreSnapshot(bool? bLeftOnly = null)
    {
        if (bInitializing)
        {
            LogService.Debug("StoreSnapshot: skipped (initializing)");
            return;
        }
        bool bUpdateLeft = bLeftOnly != false;
        bool bUpdateRight = bLeftOnly != true;
        if (bUpdateLeft)
        {
            wSnapshotLeft = new WeaponData();
            SaveControlsToWeapon(wSnapshotLeft, true);
        }
        if (bUpdateRight)
        {
            wSnapshotRight = new WeaponData();
            SaveControlsToWeapon(wSnapshotRight, false);
        }
        LogService.Debug($"StoreSnapshot: updated (altStats={bShowingAltStats}, L={bUpdateLeft}, R={bUpdateRight})");
    }

    private void ScheduleSnapshotCheck(WeaponData wSnapL, WeaponData wSnapR, string sSource)
    {
        if (wSnapL == null) return;

        tmrSnapshotCheck?.Stop(); tmrSnapshotCheck?.Dispose();
        tmrSnapshotCheck = new System.Windows.Forms.Timer { Interval = 1145 };
        tmrSnapshotCheck.Tick += (_, _) =>
        {
            tmrSnapshotCheck.Stop(); tmrSnapshotCheck.Dispose(); tmrSnapshotCheck = null;
            var wTempL = new WeaponData(); SaveControlsToWeapon(wTempL, true);
            var wTempR = new WeaponData(); SaveControlsToWeapon(wTempR, false);
            bool bChanged = !WeaponDataEquals(wTempL, wSnapL) || !WeaponDataEquals(wTempR, wSnapR);

            if (bChanged) SetC64Status("UNSAVED CHANGES.");
            else { StoreSnapshot(); SetC64Status("READY.", false); }
        };
        tmrSnapshotCheck.Start();
    }

    #endregion
    #region 入栈

    public void ScheduleUndo()
    {
        if (bUndoInProgress || bUpdatingControls || bInitializing) return;
        bUndoPending = true;
        tmrUndo?.Stop();
        tmrUndo?.Start();
    }

    public void PushUndoNow()
    {
        tmrUndo?.Stop();
        bUndoPending = false;
        PushUndo();
    }

    public void PushUndo(bool bClearRedo = true)
    {
        if (bUndoInProgress || wCurrentLeft == null) return;

        //新操作开始 结束上一次的rapid序列
        sRapidStartLeft = null;
        sRapidStartRight = null;

        //新操作使重做历史失效
        if (bClearRedo) llRedoStack.Clear();

        var ueEntry = new UndoEntry
        {
            LeftScriptName = wCurrentLeft?.ScriptName,
            RightScriptName = wCurrentRight?.ScriptName,
            LeftData = new WeaponData(),
            RightData = new WeaponData(),
            ShowingAltStats = bShowingAltStats,
            AltMode = amCurrentAltStat
        };
        SaveControlsToWeapon(ueEntry.LeftData, true);
        SaveControlsToWeapon(ueEntry.RightData, false);

        llUndoStack.AddLast(ueEntry);
        if (llUndoStack.Count > iMaxUndo) llUndoStack.RemoveFirst();

        if (!bUndoPending)
        {
            bool bLeftChanged = wSnapshotLeft != null && !WeaponDataEquals(ueEntry.LeftData, wSnapshotLeft);
            bool bRightChanged = wSnapshotRight != null && !WeaponDataEquals(ueEntry.RightData, wSnapshotRight);
            if (!bLeftChanged && !bRightChanged)
                SetC64Status("READY.");
            else
                SetC64Status("UNSAVED CHANGES.");
        }
        LogService.Debug($"PushUndo: stack={llUndoStack.Count}, redo={llRedoStack.Count}, altStats={bShowingAltStats}");
    }

    #endregion
    #region 出栈

    private void PopUndo()
    {
        LogService.Debug($"PopUndo entry: pending={bUndoPending}, inProgress={bUndoInProgress}, stack={llUndoStack.Count}");

        tmrC64Reset?.Stop(); tmrC64Reset?.Dispose(); tmrC64Reset = null;

        if (bUndoInProgress) return;
        if (bUndoPending) { tmrUndo?.Stop(); bUndoPending = false; PushUndo(false); }
        if (llUndoStack.Count < 2)
        {
            LogService.Debug($"PopUndo: stack<2, aborted. stack={llUndoStack.Count}");
            return;
        }
        bUndoInProgress = true;
        try
        {
            var ueCurrent = llUndoStack.Last!.Value;
            llUndoStack.RemoveLast();
            llRedoStack.AddLast(ueCurrent);
            if (llRedoStack.Count > iMaxUndo) llRedoStack.RemoveFirst();

            var ueEntry = llUndoStack.Last!.Value;
            RestoreUndoEntry(ueEntry);
            LogService.Debug($"PopUndo after Restore: C64 text='{lblC64_3.Text}'");

            //用撤销条目的数据作为新快照避免撤销后误报未保存
            wSnapshotLeft = ueEntry.LeftData;
            wSnapshotRight = ueEntry.RightData;
            SetC64Status("UNDONE.");
            ScheduleSnapshotCheck(ueEntry.LeftData, ueEntry.RightData, "PopUndo");
            LogService.Debug($"PopUndo: stack={llUndoStack.Count}, redo={llRedoStack.Count}");
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "Form1.PopUndo");
        }
        finally { bUndoInProgress = false; }
    }

    private void PopRedo()
    {
        LogService.Debug($"PopRedo entry: pending={bUndoPending}, inProgress={bUndoInProgress}, redo={llRedoStack.Count}");

        tmrC64Reset?.Stop(); tmrC64Reset?.Dispose(); tmrC64Reset = null;

        if (bUndoInProgress) return;
        if (bUndoPending) { tmrUndo?.Stop(); bUndoPending = false; PushUndo(false); }
        if (llRedoStack.Count == 0) return;
        bUndoInProgress = true;
        try
        {
            var ueEntry = llRedoStack.Last!.Value;
            llRedoStack.RemoveLast();

            var ueUndoEntry = new UndoEntry
            {
                LeftScriptName = wCurrentLeft?.ScriptName,
                RightScriptName = wCurrentRight?.ScriptName,
                LeftData = new WeaponData(),
                RightData = new WeaponData(),
                ShowingAltStats = bShowingAltStats,
                AltMode = amCurrentAltStat
            };
            SaveControlsToWeapon(ueUndoEntry.LeftData, true);
            SaveControlsToWeapon(ueUndoEntry.RightData, false);
            llUndoStack.AddLast(ueUndoEntry);
            if (llUndoStack.Count > iMaxUndo) llUndoStack.RemoveFirst();

            RestoreUndoEntry(ueEntry);
            LogService.Debug($"PopRedo after Restore: C64 text='{lblC64_3.Text}'");

            wSnapshotLeft = ueEntry.LeftData;
            wSnapshotRight = ueEntry.RightData;
            SetC64Status("REDONE.");
            ScheduleSnapshotCheck(ueEntry.LeftData, ueEntry.RightData, "PopRedo");
            LogService.Debug($"PopRedo: stack={llUndoStack.Count}, redo={llRedoStack.Count}");
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "Form1.PopRedo");
        }
        finally { bUndoInProgress = false; }
    }

    private void RestoreUndoEntry(UndoEntry ueEntry)
    {
        LogService.Debug($"RestoreUndoEntry: L={ueEntry.LeftScriptName}, R={ueEntry.RightScriptName}, altStats={ueEntry.ShowingAltStats}");

        if (!string.IsNullOrEmpty(ueEntry.LeftScriptName))
        {
            var wFound = rgWeapons.FirstOrDefault(x => x.ScriptName == ueEntry.LeftScriptName);
            if (wFound != null && wCurrentLeft?.ScriptName != wFound.ScriptName)
            {
                cmbWeaponsL.SelectedIndexChanged -= (s, ev) => WeaponSelected(true, s, ev);
                wCurrentLeft = wFound;
                cmbWeaponsL.SelectedItem = wFound;
                cmbWeaponsL.SelectedIndexChanged += (s, ev) => WeaponSelected(true, s, ev);
            }
        }
        LoadWeaponToControls(ueEntry.LeftData, true);

        if (!string.IsNullOrEmpty(ueEntry.RightScriptName))
        {
            var wFound = rgWeapons.FirstOrDefault(x => x.ScriptName == ueEntry.RightScriptName);
            if (wFound != null && wCurrentRight?.ScriptName != wFound.ScriptName)
            {
                cmbWeaponsR.SelectedIndexChanged -= (s, ev) => WeaponSelected(false, s, ev);
                wCurrentRight = wFound;
                cmbWeaponsR.SelectedItem = wFound;
                cmbWeaponsR.SelectedIndexChanged += (s, ev) => WeaponSelected(false, s, ev);
            }
        }
        LoadWeaponToControls(ueEntry.RightData, false);

        if (ueEntry.ShowingAltStats != bShowingAltStats || ueEntry.AltMode != amCurrentAltStat)
        {
            bShowingAltStats = ueEntry.ShowingAltStats;
            amCurrentAltStat = ueEntry.AltMode;
            if (bShowingAltStats) HighlightAltStatButton(amCurrentAltStat);
            else ResetAltStatButtons();
        }
        RestoreAltStatState(true);
        RestoreAltStatState(false);
        SetAdsEnabledByIronSight(true);
        SetAdsEnabledByIronSight(false);
        UpdateAllDamage();
        pnlSpread.Invalidate();
        pnlRecoil.Invalidate();
    }

    private void RestoreAltStatState(bool bIsLeft)
    {
        var w = bIsLeft ? wCurrentLeft : wCurrentRight;
        if (bShowingAltStats && WeaponHasAltStats(w, amCurrentAltStat))
            LoadAltStatsToControls(bIsLeft, amCurrentAltStat);
        else
            RestoreAllNudEnabled(bIsLeft);
    }

    private void SetAdsEnabledByIronSight(bool bIsLeft)
    {
        bool bNoAds = (bIsLeft ? nudIronSightL : nudIronSightR).Value == 0;
        (bIsLeft ? nudAdsSpreadL : nudAdsSpreadR).Enabled = !bNoAds;
        (bIsLeft ? nudAdsRecoilUpL : nudAdsRecoilUpR).Enabled = !bNoAds;
        (bIsLeft ? nudAdsRecoilRightL : nudAdsRecoilRightR).Enabled = !bNoAds;
        (bIsLeft ? nudIronsightSpeedScaleL : nudIronsightSpeedScaleR).Enabled = !bNoAds;
    }

    #endregion
    #region 清空

    public void ClearRedo()
    {
        llRedoStack.Clear();
    }

    public void ClearUndoHistory()
    {
        LogService.Debug("ClearUndoHistory");
        llUndoStack.Clear();
        llRedoStack.Clear();
        tmrSnapshotCheck?.Stop(); tmrSnapshotCheck?.Dispose(); tmrSnapshotCheck = null;
        //清空历史时也结束未完成的rapid 否则残留的sRapidStart会阻止下次武器切换的入栈
        sRapidStartLeft = null;
        sRapidStartRight = null;
    }
    #endregion
}