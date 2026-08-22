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

    public static async Task RunAll(Form1 frm)
    {
        s_frm = frm;
        var tForm = typeof(Form1);
        var wDummy = new WeaponData
        {
            ScriptName = "weapon_test",
            PrintName = "#weapon_TEST",
            FireModes = "Auto+Semi",
            DefaultClip = 30,
            ExtraBulletChamber = 1,
            FireRate = 600,
            BulletSpread = 7.5,
            BulletSpreadDegreesIronsighted = 1.5,
            RangeModifier = 0.94,
            IronSight = 1,
            CrouchSpreadMultiplier = 0.8,
            Weight = 3.8,
            ZMBuyPrice = 2700,
            ZMWeight = 3,
            MetalPenetrationDepth = 8,
            ConcretePenetrationDepth = 10,
            WoodPenetrationDepth = 18,
            WoodDamageModifier = 1.25,
            DamageHeadMultiplier = 2.75,
            DamageGeneric = 40,
            ClipSize = "30/90",
        };
        var rgWeaponsField = tForm.GetField("rgWeapons", BindingFlags.NonPublic | BindingFlags.Instance)!;
        rgWeaponsField.SetValue(frm, new List<WeaponData> { wDummy });
        var cmbL = GetControl<ComboBox>("cmbWeaponsL");
        var cmbR = GetControl<ComboBox>("cmbWeaponsR");
        cmbL.DataSource = null; cmbL.DataSource = new List<WeaponData> { wDummy }; cmbL.DisplayMember = "PrintName";
        cmbR.DataSource = null; cmbR.DataSource = new List<WeaponData> { wDummy }; cmbR.DisplayMember = "PrintName";
        cmbL.SelectedIndex = 0; cmbR.SelectedIndex = 0;
        var wCurrentLeftField = tForm.GetField("wCurrentLeft", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var wCurrentRightField = tForm.GetField("wCurrentRight", BindingFlags.NonPublic | BindingFlags.Instance)!;
        wCurrentLeftField.SetValue(frm, wDummy);
        wCurrentRightField.SetValue(frm, wDummy);
        frm.StoreSnapshot();
        frm.ClearUndoHistory();
        frm.PushUndoNow();
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
            frm.PushUndoNow();
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
            nud.Value = decValue;
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

        Test("NUD undo restores original", () =>
        {
            var nud = GetControl<NumericUpDown>("nudHipSpreadL");
            decimal decOld = nud.Value;
            ChangeNud("nudHipSpreadL", decOld + 0.5m);
            Undo();
            if (nud.Value != decOld) throw new Exception($"Expected {decOld}, got {nud.Value}");
        });

        Test("Redo restores changed value", () =>
        {
            var nud = GetControl<NumericUpDown>("nudHipSpreadL");
            decimal decTarget = nud.Value + 1.0m;
            ChangeNud("nudHipSpreadL", decTarget);
            Undo();
            Redo();
            if (nud.Value != decTarget) throw new Exception($"Expected {decTarget}, got {nud.Value}");
        });

        Test("Consecutive undos traverse correctly", () =>
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

        Test("CSV remove/restore cycle blocks undo of zeroed state", () =>
        {
            var nud = GetControl<NumericUpDown>("nudHipSpreadL");
            var rgWeaponsField = tForm.GetField("rgWeapons", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var wDummy = (WeaponData)GetControl<ComboBox>("cmbWeaponsL").SelectedItem!;
            var wCurrentLeftField = tForm.GetField("wCurrentLeft", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var wCurrentRightField = tForm.GetField("wCurrentRight", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var bSuppressUndoField = tForm.GetField("bSuppressUndo", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var bUpdatingControlsField = tForm.GetField("bUpdatingControls", BindingFlags.NonPublic | BindingFlags.Instance)!;
            bSuppressUndoField.SetValue(frm, false);
            bUpdatingControlsField.SetValue(frm, false);
            frm.ClearUndoHistory();
            decimal decDummy = 3.34m;
            nud.Value = decDummy;
            PushUndoNow();
            PushUndoNow();

            for (int k = 0; k < 2; k++)
            {
                rgWeaponsField.SetValue(frm, new List<WeaponData>());
                wCurrentLeftField.SetValue(frm, null);
                wCurrentRightField.SetValue(frm, null);
                rgWeaponsField.SetValue(frm, new List<WeaponData> { wDummy });
                wCurrentLeftField.SetValue(frm, wDummy);
                wCurrentRightField.SetValue(frm, wDummy);
                var miLoad = tForm.GetMethod("LoadWeaponToControls", BindingFlags.NonPublic | BindingFlags.Instance)!;
                miLoad.Invoke(frm, new object[] { wDummy, true });
                miLoad.Invoke(frm, new object[] { wDummy, false });
            }

            Undo();
            if (nud.Value == 0m)
                throw new Exception("Undo restored zeroed state from no CSV refresh");
            if (Math.Abs(nud.Value - decDummy) > 0.005m)
                throw new Exception($"Expected {decDummy}, got {nud.Value}");
        });

        Test("New change clears redo stack", () =>
        {
            var nud = GetControl<NumericUpDown>("nudHipSpreadL");
            decimal decOrig = nud.Value;
            ChangeNud("nudHipSpreadL", decOrig + 0.5m);
            Undo();
            ChangeNud("nudHipSpreadL", decOrig + 0.6m);
            Redo();
            if (Math.Abs(nud.Value - (decOrig + 0.6m)) > 0.005m)
                throw new Exception("Redo should have no effect after new change");
        });

        #endregion
        #region 多控件类型

        Test("TextBox undo restores text", () =>
        {
            var txt = GetControl<TextBox>("txtFireModesL");
            string sOld = txt.Text;
            txt.Text = sOld == "TestA" ? "TestB" : "TestA";
            PushUndoNow();
            Undo();
            if (txt.Text != sOld) throw new Exception($"Expected '{sOld}', got '{txt.Text}'");
        });

        Test("IronSight undo restores ADS state", () =>
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

        Test("IronSight=0 undo keeps ADS disabled", () =>
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

        Test("Slider undo restores both", () =>
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

        Test("Debounce records final value only", () =>
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

        Test("Undo flushes pending debounce", () =>
        {
            var nud = GetControl<NumericUpDown>("nudHipSpreadL");
            decimal decOrig = nud.Value;
            nud.Value = decOrig + 0.5m;
            frm.ScheduleUndo();
            Undo();
            if (Math.Abs(nud.Value - decOrig) > 0.005m)
                throw new Exception($"Expected {decOrig}, got {nud.Value}");
        });

        Test("Redo flushes pending debounce", () =>
        {
            var nud = GetControl<NumericUpDown>("nudHipSpreadL");
            decimal decOrig = nud.Value;
            ChangeNud("nudHipSpreadL", decOrig + 0.5m);
            Undo();
            nud.Value = decOrig + 0.7m;
            frm.ScheduleUndo();
            Redo();
            if (Math.Abs(nud.Value - (decOrig + 0.5m)) > 0.005m)
                throw new Exception($"Expected {decOrig + 0.5m} after redo, got {nud.Value}");
        });

        #endregion
        #region 多字段

        Test("Multi field undo restores all", () =>
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

        Test("Panels undo independently", () =>
        {
            var nudL = GetControl<NumericUpDown>("nudHipSpreadL");
            var nudR = GetControl<NumericUpDown>("nudHipSpreadR");
            decimal decOldL = nudL.Value;
            decimal decOldR = nudR.Value;
            ChangeNud("nudHipSpreadL", decOldL + 0.5m);
            ChangeNud("nudHipSpreadR", decOldR + 0.5m);
            Undo();
            if (Math.Abs(nudR.Value - decOldR) > 0.005m) throw new Exception("Right panel not restored first");
            Undo();
            if (Math.Abs(nudL.Value - decOldL) > 0.005m) throw new Exception("Left panel not restored second");
        });

        Test("Same weapon panel states independent", () =>
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

        Test("C64 READY after undo to original", () =>
        {
            var nud = GetControl<NumericUpDown>("nudHipSpreadL");
            ChangeNud("nudHipSpreadL", nud.Value + 0.5m);
            Undo();
            string sC64 = WaitForC64(1200);
            if (sC64 != "READY.") throw new Exception($"Expected 'READY.', got '{sC64}'");
        });

        Test("C64 READY after undo to restored stats", () =>
        {
            var nud = GetControl<NumericUpDown>("nudHipSpreadL");
            ChangeNud("nudHipSpreadL", nud.Value + 0.3m);
            ChangeNud("nudHipSpreadL", nud.Value + 0.5m);
            Undo();
            string sC64 = WaitForC64(1200);
            if (sC64 != "READY.")
                throw new Exception($"Expected 'READY.', got '{sC64}'");
        });

        Test("Unsaved false after undo", () =>
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

        Test("Undo exits AltStat", () =>
        {
            var nud = GetControl<NumericUpDown>("nudHipSpreadL");
            decimal decOld = nud.Value;
            ToggleAltStats(WeaponScriptService.AltStatMode.Zombie);
            Undo();
            if (Math.Abs(nud.Value - decOld) > 0.005m)
                throw new Exception($"Expected {decOld}, got {nud.Value}");
        });

        Test("Undo reenters Altstat", () =>
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

        Test("AltStat modify undo restores", () =>
        {
            var nud = GetControl<NumericUpDown>("nudHipSpreadL");
            decimal decNormal = nud.Value;
            ToggleAltStats(WeaponScriptService.AltStatMode.Zombie);
            ChangeNud("nudHipSpreadL", nud.Value + 0.5m);
            Undo();
            if (Math.Abs(nud.Value - decNormal) > 0.005m)
                throw new Exception($"Expected {decNormal}, got {nud.Value}");
            ToggleAltStats(WeaponScriptService.AltStatMode.Zombie);
        });

        Test("AltStat mode undo restores", () =>
        {
            var nud = GetControl<NumericUpDown>("nudHipSpreadL");
            ToggleAltStats(WeaponScriptService.AltStatMode.Dov);
            ToggleAltStats(WeaponScriptService.AltStatMode.Zombie);
            Undo();
            ToggleAltStats(WeaponScriptService.AltStatMode.Zombie);
        });

        #endregion
        #region 武器切换

        Test("Undo restores weapon", () =>
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

        Test("Weapon+modify double undo restores", () =>
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

        Test("Rapid undos traverse correctly", () =>
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

        Test("Rapid undos cancel snapshot timer", () =>
        {
            var nud = GetControl<NumericUpDown>("nudHipSpreadL");
            ChangeNud("nudHipSpreadL", nud.Value + 0.5m);
            ChangeNud("nudHipSpreadL", nud.Value + 0.3m);
            Undo();
            Undo();
            string sC64 = WaitForC64(1200);
            if (sC64 != "READY.") throw new Exception($"Expected 'READY.', got '{sC64}'");
        });

        Test("Random operations preserve integrity", () =>
        {
            var nud = GetControl<NumericUpDown>("nudHipSpreadL");
            var rng = new Random(42);

            decimal decOriginal = nud.Value;
            decimal decBase = 3.00m;
            nud.Value = decBase;
            PushUndoNow();

            int iPushCount = 0;

            for (int k = 0; k < 100; k++)
            {
                int iOp = rng.Next(3);
                if (iOp == 0)
                {
                    nud.Value = nud.Value + 0.01m;
                    PushUndoNow();
                    iPushCount++;
                }
                else if (iOp == 1)
                {
                    Undo();
                }
                else
                {
                    Redo();
                }

                decimal decMaxExpected = decBase + iPushCount * 0.01m;
                if (nud.Value < decBase || nud.Value > decMaxExpected)
                    throw new Exception($"k={k}: value {nud.Value} out of range [{decBase}, {decMaxExpected}]");
            }

            if (iPushCount < 30)
                throw new Exception($"Too few pushes: {iPushCount}");

            nud.Value = decOriginal;
            frm.ClearUndoHistory();
            frm.PushUndoNow();
        });

        Test("Overflow removes oldest entry", () =>
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
        await frm.RefreshWeaponList();
        _ = Task.Run(async () =>
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