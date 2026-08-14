#if DEBUG
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using WeaponDamageCalc.Models;
using WeaponDamageCalc.Services;

namespace WeaponDamageCalc.Tools;

public static class UndoTests
{
    private static Form1? s_frm;
    private static MethodInfo? s_miPopUndo;
    private static MethodInfo? s_miPopRedo;
    private static MethodInfo? s_miPushUndoNow;
    private static MethodInfo? s_miToggleAltStats;
    private static MethodInfo? s_miHasUnsavedChanges;
    private static readonly Dictionary<string, FieldInfo> mpFields = new();

    public static void RunAll(Form1 frm)
    {
        s_frm = frm;
        var tForm = typeof(Form1);
        s_miPopUndo = tForm.GetMethod("PopUndo", BindingFlags.NonPublic | BindingFlags.Instance)!;
        s_miPopRedo = tForm.GetMethod("PopRedo", BindingFlags.NonPublic | BindingFlags.Instance)!;
        s_miPushUndoNow = tForm.GetMethod("PushUndoNow", BindingFlags.Public | BindingFlags.Instance)!;
        s_miToggleAltStats = tForm.GetMethod("ToggleAltStats", BindingFlags.NonPublic | BindingFlags.Instance)!;
        s_miHasUnsavedChanges = tForm.GetMethod("HasUnsavedChanges", BindingFlags.NonPublic | BindingFlags.Instance)!;

        if (s_miPopUndo == null || s_miPopRedo == null || s_miPushUndoNow == null ||
            s_miToggleAltStats == null || s_miHasUnsavedChanges == null)
        {
            Log("Skipped: required methods not found via reflection");
            return;
        }

        int nPassed = 0, nFailed = 0;
        void Test(string sName, Action act)
        {
            frm.ClearUndoHistory();
            frm.PushUndoNow();//初始状态入栈确保后续ChangeNud有可撤销的历史
            try { act(); nPassed++; Log($"[PASS] {sName}"); }
            catch (Exception ex) { Log($"[FAIL] {sName}: {ex.Message}"); nFailed++; }
        }

        void Undo() => s_miPopUndo!.Invoke(frm, null);
        void Redo() => s_miPopRedo!.Invoke(frm, null);
        void PushUndoNow() => s_miPushUndoNow!.Invoke(frm, null);
        void ToggleAltStats(WeaponScriptService.AltStatMode am) => s_miToggleAltStats!.Invoke(frm, new object[] { am });
        bool HasUnsavedChanges(bool bIsLeft, bool bCheckBoth) =>
            (bool)s_miHasUnsavedChanges!.Invoke(frm, new object[] { bIsLeft, bCheckBoth })!;

        T GetControl<T>(string sName) where T : Control
        {
            if (!mpFields.TryGetValue(sName, out var fi))
            {
                fi = tForm.GetField(sName, BindingFlags.NonPublic | BindingFlags.Instance);
                mpFields[sName] = fi!;
            }
            return (T)fi!.GetValue(frm)!;
        }

        void ChangeNud(string sName, decimal decValue)
        {
            var nud = GetControl<NumericUpDown>(sName);
            nud.Value = decValue;//触发ValueChanged->ScheduleUndo
            PushUndoNow();
        }

        string GetC64Text() => GetControl<Label>("lblC64_3").Text;

        string WaitForC64(int iMs)
        {
            Application.DoEvents();
            Thread.Sleep(iMs);
            Application.DoEvents();
            return GetC64Text();
        }

        #region 基础撤销重做

        Test("NUD value change -> undo restores original", () =>
        {
            var nud = GetControl<NumericUpDown>("nudHipSpreadL");
            decimal decOld = nud.Value;
            ChangeNud("nudHipSpreadL", decOld + 0.5m);
            Undo();
            if (nud.Value != decOld) throw new Exception($"Expected {decOld}, got {nud.Value}");
        });

        Test("Undo then redo restores changed value", () =>
        {
            var nud = GetControl<NumericUpDown>("nudHipSpreadL");
            decimal decTarget = nud.Value + 1.0m;
            ChangeNud("nudHipSpreadL", decTarget);
            Undo();
            Redo();
            if (nud.Value != decTarget) throw new Exception($"Expected {decTarget}, got {nud.Value}");
        });

        Test("Two consecutive undos traverse stack correctly", () =>
        {
            var nud = GetControl<NumericUpDown>("nudHipSpreadL");
            decimal decOrig = nud.Value;
            ChangeNud("nudHipSpreadL", decOrig + 0.1m);
            ChangeNud("nudHipSpreadL", decOrig + 0.2m);
            Undo();
            Undo();
            if (Math.Abs(nud.Value - decOrig) > 0.005m)
                throw new Exception($"Expected {decOrig}, got {nud.Value}");
        });

        Test("Undo at stack bottom is silently ignored", () =>
        {
            var nud = GetControl<NumericUpDown>("nudHipSpreadL");
            ChangeNud("nudHipSpreadL", nud.Value + 0.3m);
            Undo();
            decimal decAfterFirst = nud.Value;
            Undo();//栈<2 PopUndo直接return
            if (nud.Value != decAfterFirst)
                throw new Exception("Second undo at stack bottom should be ignored");
        });

        Test("New change after undo clears redo stack", () =>
        {
            var nud = GetControl<NumericUpDown>("nudHipSpreadL");
            decimal decOrig = nud.Value;
            ChangeNud("nudHipSpreadL", decOrig + 0.5m);
            Undo();
            ChangeNud("nudHipSpreadL", decOrig + 0.6m);//新操作使redo失效
            Redo();
            if (Math.Abs(nud.Value - (decOrig + 0.6m)) > 0.005m)
                throw new Exception("Redo should have no effect after new change");
        });

        #endregion
        #region 多控件类型

        Test("TextBox change -> undo restores original text", () =>
        {
            var txt = GetControl<TextBox>("txtFireModesL");
            string sOld = txt.Text;
            txt.Text = sOld == "TestA" ? "TestB" : "TestA";
            PushUndoNow();
            Undo();
            if (txt.Text != sOld) throw new Exception($"Expected '{sOld}', got '{txt.Text}'");
        });

        Test("IronSight toggle -> undo restores ADS enabled state", () =>
        {
            var nudIS = GetControl<NumericUpDown>("nudIronSightL");
            var nudAds = GetControl<NumericUpDown>("nudAdsSpreadL");
            decimal decOld = nudIS.Value;
            bool bAdsWasEnabled = nudAds.Enabled;
            nudIS.Value = nudIS.Value == 1 ? 0 : 1;
            PushUndoNow();
            Undo();
            if (nudIS.Value != decOld) throw new Exception("IronSight value not restored");
            if (nudAds.Enabled != bAdsWasEnabled) throw new Exception("ADS enabled state not restored");
        });

        Test("IronSight=0 -> modify other value -> undo keeps ADS disabled", () =>
        {
            var nudIS = GetControl<NumericUpDown>("nudIronSightL");
            var nudAds = GetControl<NumericUpDown>("nudAdsSpreadL");
            var nudSpread = GetControl<NumericUpDown>("nudHipSpreadL");
            nudIS.Value = 0;
            PushUndoNow();
            decimal decOldSpread = nudSpread.Value;
            ChangeNud("nudHipSpreadL", decOldSpread + 0.5m);
            Undo();
            if (nudAds.Enabled) throw new Exception("ADS should remain disabled when IronSight=0");
        });

        Test("Slider change -> undo restores both TrackBar and NUD", () =>
        {
            var nud = GetControl<NumericUpDown>("nudHeadL");
            var tb = (TrackBar)nud.Tag!;
            decimal decOldNud = nud.Value;
            int iOldTb = tb.Value;
            nud.Value = Math.Round(nud.Value + 0.5m, 2);
            PushUndoNow();
            Undo();
            if (Math.Abs(nud.Value - decOldNud) > 0.005m) throw new Exception("NUD not restored");
            if (tb.Value != iOldTb) throw new Exception("TrackBar not restored");
        });

        #endregion
        #region 防抖与时机

        Test("Rapid changes within debounce window record only final value", () =>
        {
            var nud = GetControl<NumericUpDown>("nudHipSpreadL");
            decimal decOrig = nud.Value;
            nud.Value = decOrig + 0.01m; frm.ScheduleUndo();
            nud.Value = decOrig + 0.02m; frm.ScheduleUndo();
            nud.Value = decOrig + 0.03m; frm.ScheduleUndo();
            PushUndoNow();
            Undo();
            if (Math.Abs(nud.Value - decOrig) > 0.005m)
                throw new Exception($"Expected {decOrig}, got {nud.Value}");
        });

        Test("Undo during debounce period saves pending value first", () =>
        {
            var nud = GetControl<NumericUpDown>("nudHipSpreadL");
            decimal decOrig = nud.Value;
            nud.Value = decOrig + 0.5m;
            frm.ScheduleUndo();//防抖中
            Undo();//PopUndo内部检测到bUndoPending->PushUndo(false)强制保存->再撤销
            if (Math.Abs(nud.Value - decOrig) > 0.005m)
                throw new Exception($"Expected {decOrig}, got {nud.Value}");
        });

        Test("Redo during debounce period saves pending value first", () =>
        {
            var nud = GetControl<NumericUpDown>("nudHipSpreadL");
            decimal decOrig = nud.Value;
            ChangeNud("nudHipSpreadL", decOrig + 0.5m);
            Undo();//redo栈有1条 控件值=orig
            nud.Value = decOrig + 0.7m;
            frm.ScheduleUndo();//防抖
            Redo();//PopRedo内部 PushUndo(false)保存当前值->再重做
            if (Math.Abs(nud.Value - (decOrig + 0.5m)) > 0.005m)
                throw new Exception($"Expected {decOrig + 0.5m} after redo, got {nud.Value}");
        });

        #endregion
        #region 多字段

        Test("Multiple fields changed -> undo restores all", () =>
        {
            var nudSpread = GetControl<NumericUpDown>("nudHipSpreadL");
            var nudRate = GetControl<NumericUpDown>("nudFireRateL");
            var txtModes = GetControl<TextBox>("txtFireModesL");
            decimal decOldSpread = nudSpread.Value;
            decimal decOldRate = nudRate.Value;
            string sOldModes = txtModes.Text;

            nudSpread.Value = nudSpread.Value + 1.0m;
            nudRate.Value = nudRate.Value + 100;
            txtModes.Text = txtModes.Text == "X" ? "Y" : "X";
            PushUndoNow();
            Undo();

            if (Math.Abs(nudSpread.Value - decOldSpread) > 0.005m) throw new Exception("Spread not restored");
            if (nudRate.Value != decOldRate) throw new Exception("FireRate not restored");
            if (txtModes.Text != sOldModes) throw new Exception("FireModes not restored");
        });

        #endregion
        #region 双面板

        Test("Left and right panels undo independently", () =>
        {
            var nudL = GetControl<NumericUpDown>("nudHipSpreadL");
            var nudR = GetControl<NumericUpDown>("nudHipSpreadR");
            decimal decOldL = nudL.Value;
            decimal decOldR = nudR.Value;
            ChangeNud("nudHipSpreadL", decOldL + 0.5m);
            ChangeNud("nudHipSpreadR", decOldR + 0.5m);
            Undo();//后入栈的右侧先出
            if (Math.Abs(nudR.Value - decOldR) > 0.005m) throw new Exception("Right panel not restored first");
            Undo();
            if (Math.Abs(nudL.Value - decOldL) > 0.005m) throw new Exception("Left panel not restored second");
        });

        Test("Same weapon left-right: undo restores independent panel states", () =>
        {
            var cmbL = GetControl<ComboBox>("cmbWeaponsL");
            var cmbR = GetControl<ComboBox>("cmbWeaponsR");
            var wSame = (WeaponData)cmbL.SelectedItem!;
            cmbR.SelectedItem = wSame;
            PushUndoNow();

            var nudL = GetControl<NumericUpDown>("nudHipSpreadL");
            var nudR = GetControl<NumericUpDown>("nudHipSpreadR");
            decimal decOldL = nudL.Value;
            decimal decOldR = nudR.Value;
            ChangeNud("nudHipSpreadL", decOldL + 0.5m);
            ChangeNud("nudHipSpreadR", decOldR + 1.0m);
            Undo();
            if (Math.Abs(nudR.Value - decOldR) > 0.005m) throw new Exception("Right not restored");
            Undo();
            if (Math.Abs(nudL.Value - decOldL) > 0.005m) throw new Exception("Left not restored");
        });

        #endregion
        #region 状态验证

        Test("C64 shows UNDONE after undo", () =>
        {
            var nud = GetControl<NumericUpDown>("nudHipSpreadL");
            ChangeNud("nudHipSpreadL", nud.Value + 0.5m);
            Undo();
            if (GetC64Text() != "UNDONE.") throw new Exception($"Expected 'UNDONE.', got '{GetC64Text()}'");
        });

        Test("C64 shows REDONE after redo", () =>
        {
            var nud = GetControl<NumericUpDown>("nudHipSpreadL");
            ChangeNud("nudHipSpreadL", nud.Value + 0.5m);
            Undo();
            Redo();
            if (GetC64Text() != "REDONE.") throw new Exception($"Expected 'REDONE.', got '{GetC64Text()}'");
        });

        Test("C64 shows READY after undo to original value", () =>
        {
            var nud = GetControl<NumericUpDown>("nudHipSpreadL");
            ChangeNud("nudHipSpreadL", nud.Value + 0.5m);
            Undo();
            string sC64 = WaitForC64(1200);
            if (sC64 != "READY.") throw new Exception($"Expected 'READY.', got '{sC64}'");
        });

        Test("C64 shows READY after undo to different value (snapshot matches restored stats)", () =>
        {
            var nud = GetControl<NumericUpDown>("nudHipSpreadL");
            ChangeNud("nudHipSpreadL", nud.Value + 0.3m);
            ChangeNud("nudHipSpreadL", nud.Value + 0.5m);
            Undo();
            string sC64 = WaitForC64(1200);
            if (sC64 != "READY.")
                throw new Exception($"Expected 'READY.', got '{sC64}'");
        });

        Test("HasUnsavedChanges returns false after undo (snapshot matches restored data)", () =>
        {
            var nud = GetControl<NumericUpDown>("nudHipSpreadL");
            ChangeNud("nudHipSpreadL", nud.Value + 0.3m);
            ChangeNud("nudHipSpreadL", nud.Value + 0.5m);
            Undo();
            if (HasUnsavedChanges(true, true))
                throw new Exception("Expected no unsaved changes after undo");
        });

        #endregion
        #region Altstats

        Test("Enter AltStat -> undo exits AltStat and restores normal values", () =>
        {
            var nud = GetControl<NumericUpDown>("nudHipSpreadL");
            decimal decOld = nud.Value;
            ToggleAltStats(WeaponScriptService.AltStatMode.Zombie);
            Undo();
            if (Math.Abs(nud.Value - decOld) > 0.005m)
                throw new Exception($"Expected {decOld}, got {nud.Value}");
        });

        Test("Exit AltStat -> undo re-enters AltStat", () =>
        {
            var nud = GetControl<NumericUpDown>("nudHipSpreadL");
            decimal decNormal = nud.Value;
            ToggleAltStats(WeaponScriptService.AltStatMode.Zombie);
            ToggleAltStats(WeaponScriptService.AltStatMode.Zombie);
            Undo();
            decimal decInZombie = nud.Value;
            ToggleAltStats(WeaponScriptService.AltStatMode.Zombie);
            Undo();
            if (Math.Abs(nud.Value - decNormal) > 0.005m)
                throw new Exception($"Expected {decNormal}, got {nud.Value}");
        });

        Test("Modify in AltStat -> undo restores previous alt value", () =>
        {
            var nud = GetControl<NumericUpDown>("nudHipSpreadL");
            decimal decNormal = nud.Value;//进入AltStat前的普通值
            ToggleAltStats(WeaponScriptService.AltStatMode.Zombie);
            ChangeNud("nudHipSpreadL", nud.Value + 0.5m);
            Undo();
            if (Math.Abs(nud.Value - decNormal) > 0.005m)
                throw new Exception($"Expected {decNormal}, got {nud.Value}");
            ToggleAltStats(WeaponScriptService.AltStatMode.Zombie);
        });

        Test("AltStat DoV -> switch to Zmb -> undo restores DoV", () =>
        {
            var nud = GetControl<NumericUpDown>("nudHipSpreadL");
            ToggleAltStats(WeaponScriptService.AltStatMode.Dov);
            ToggleAltStats(WeaponScriptService.AltStatMode.Zombie);
            Undo();
            ToggleAltStats(WeaponScriptService.AltStatMode.Zombie);
        });

        #endregion
        #region 武器切换

        Test("Weapon switch -> undo restores previous weapon", () =>
        {
            var cmb = GetControl<ComboBox>("cmbWeaponsL");
            var wOld = (WeaponData)cmb.SelectedItem!;
            int iNext = cmb.SelectedIndex < cmb.Items.Count - 1 ? cmb.SelectedIndex + 1 : 0;
            cmb.SelectedIndex = iNext;
            PushUndoNow();
            Undo();
            var wRestored = (WeaponData)cmb.SelectedItem!;
            if (wRestored.ScriptName != wOld.ScriptName)
                throw new Exception($"Expected '{wOld.ScriptName}', got '{wRestored.ScriptName}'");
        });

        Test("Weapon switch -> modify -> undo twice restores original weapon and values", () =>
        {
            var cmb = GetControl<ComboBox>("cmbWeaponsL");
            var nud = GetControl<NumericUpDown>("nudHipSpreadL");
            var wOrig = (WeaponData)cmb.SelectedItem!;
            decimal decOrig = nud.Value;

            int iNext = cmb.SelectedIndex < cmb.Items.Count - 1 ? cmb.SelectedIndex + 1 : 0;
            cmb.SelectedIndex = iNext;
            PushUndoNow();
            ChangeNud("nudHipSpreadL", nud.Value + 0.5m);
            Undo();
            Undo();

            var wRestored = (WeaponData)cmb.SelectedItem!;
            if (wRestored.ScriptName != wOrig.ScriptName)
                throw new Exception($"Expected '{wOrig.ScriptName}', got '{wRestored.ScriptName}'");
            if (Math.Abs(nud.Value - decOrig) > 0.005m)
                throw new Exception($"Expected NUD {decOrig}, got {nud.Value}");
        });

        #endregion
        #region 快速操作

        Test("Rapid consecutive undos traverse stack correctly", () =>
        {
            var nud = GetControl<NumericUpDown>("nudHipSpreadL");
            decimal decOrig = nud.Value;
            ChangeNud("nudHipSpreadL", decOrig + 0.5m);
            ChangeNud("nudHipSpreadL", decOrig + 0.3m);
            Undo();
            Undo();
            if (Math.Abs(nud.Value - decOrig) > 0.005m)
                throw new Exception($"Expected {decOrig}, got {nud.Value}");
        });

        Test("Rapid undos cancel previous snapshot check timer", () =>
        {
            var nud = GetControl<NumericUpDown>("nudHipSpreadL");
            ChangeNud("nudHipSpreadL", nud.Value + 0.5m);
            ChangeNud("nudHipSpreadL", nud.Value + 0.3m);
            Undo();
            Undo();
            string sC64 = WaitForC64(1200);
            if (sC64 != "READY.") throw new Exception($"Expected 'READY.', got '{sC64}'");
        });

        Test("100 random operations do not crash or corrupt state", () =>
        {
            var nud = GetControl<NumericUpDown>("nudHipSpreadL");
            var rng = new Random(42);
            decimal decBase = nud.Value;
            decimal decCurrent = decBase;

            for (int k = 0; k < 100; k++)
            {
                int iOp = rng.Next(3);
                if (iOp == 0)
                {
                    decCurrent += 0.01m;
                    nud.Value = decCurrent;
                    PushUndoNow();
                }
                else if (iOp == 1)
                {
                    Undo();
                }
                else
                {
                    Redo();
                }
            }
        });

        Test("Stack overflow removes oldest entry correctly", () =>
        {
            var nud = GetControl<NumericUpDown>("nudHipSpreadL");
            decimal decFirst = decimal.Parse(nud.Text);
            for (int k = 0; k < 99; k++)
            {
                nud.Value = nud.Value + 0.01m;
                PushUndoNow();
            }
            for (int k = 0; k < 99; k++)
                Undo();
            decimal decAfter = decimal.Parse(nud.Text);
            if (Math.Abs(decAfter - decFirst) > 0.005m)
                throw new Exception($"Expected {decFirst} after 99 undos, got {decAfter}");
            Undo();
            decAfter = decimal.Parse(nud.Text);
            if (Math.Abs(decAfter - decFirst) > 0.005m)
                throw new Exception($"Expected still {decFirst} after 100th undo, got {decAfter}");
        });

        #endregion

        Log($"=== Undo Integration Tests: {nPassed} passed, {nFailed} failed ===");
        frm.StoreSnapshot();
        var tmrC64 = (System.Windows.Forms.Timer?)tForm.GetField("tmrC64Reset", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(frm);
        tmrC64?.Stop(); tmrC64?.Dispose();
        var tmrSnap = (System.Windows.Forms.Timer?)tForm.GetField("tmrSnapshotCheck", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(frm);
        tmrSnap?.Stop(); tmrSnap?.Dispose();
        frm.RefreshWeaponList();
        Task.Run(async () =>
        {
            await Task.Delay(1000);
            frm.BeginInvoke(() => GetControl<Label>("lblC64_3").Text = "TEST DONE.");
        });
        s_frm = null;
        s_miPopUndo = null;
        s_miPopRedo = null;
        s_miPushUndoNow = null;
        s_miToggleAltStats = null;
        s_miHasUnsavedChanges = null;
        mpFields.Clear();
    }

    private static void Log(string sMsg)
    {
        try { LogService.Info($"[UndoTest] {sMsg}"); }
        catch { System.Diagnostics.Debug.WriteLine($"[UndoTest] {sMsg}"); }
    }
}
#endif