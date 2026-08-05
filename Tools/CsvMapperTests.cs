#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using WeaponDamageCalc.Models;

namespace WeaponDamageCalc.Tools;

public static class CsvMapperTests
{
    public static void RunAll()
    {
        CsvMapper.s_bSuppressMessageBox = true;

        int nPassed = 0, nFailed = 0;
        void Test(string sName, Action act)
        {
            try { act(); nPassed++; Log($"[PASS] {sName}"); }
            catch (Exception ex) { Log($"[FAIL] {sName}: {ex.Message}"); nFailed++; }
        }

        #region 读写往返

        Test("Round trip basic", () =>
        {
            string sPath = Path.GetTempFileName();
            try
            {
                File.WriteAllText(sPath, "\"DamageHeadMultiplier\",\"FireRate\",\"SupportedFireModes\"\n2.75,600,\"Auto+Semi\"");
                var rgLoaded = CsvMapper.Read<WeaponData>(sPath);
                if (rgLoaded.Count != 1) throw new Exception($"Expected 1, got {rgLoaded.Count}");
                if (rgLoaded[0].DamageHeadMultiplier != 2.75) throw new Exception("DamageHeadMultiplier mismatch");
                if (rgLoaded[0].FireRate != 600) throw new Exception("FireRate mismatch");
            }
            finally { File.Delete(sPath); }
        });

        Test("Blank lines and all comma lines skipped", () =>
        {
            string sPath = Path.GetTempFileName();
            try
            {
                File.WriteAllText(sPath, "\"SupportedFireModes\"\n\n\n\"Auto\"\n,,,\n\"Semi\"");
                var rgLoaded = CsvMapper.Read<WeaponData>(sPath);
                if (rgLoaded.Count != 2) throw new Exception($"Expected 2, got {rgLoaded.Count}");
            }
            finally { File.Delete(sPath); }
        });

        Test("UTF-8 BOM handling", () =>
        {
            string sPath = Path.GetTempFileName();
            try
            {
                var rgBytes = new List<byte> { 0xEF, 0xBB, 0xBF };
                rgBytes.AddRange(Encoding.UTF8.GetBytes("\"SupportedFireModes\"\n\"bom_test\""));
                File.WriteAllBytes(sPath, rgBytes.ToArray());
                var rgLoaded = CsvMapper.Read<WeaponData>(sPath);
                if (rgLoaded.Count != 1) throw new Exception($"Expected 1, got {rgLoaded.Count}");
                if (rgLoaded[0].FireModes != "bom_test") throw new Exception("FireModes mismatch");
            }
            finally { File.Delete(sPath); }
        });

        #endregion
        #region 字段容错

        Test("Missing fields filled with defaults", () =>
        {
            string sPath = Path.GetTempFileName();
            try
            {
                File.WriteAllText(sPath, "\"SupportedFireModes\",\"FireRate\"\n\"Auto\",600");
                var rgLoaded = CsvMapper.Read<WeaponData>(sPath);
                if (rgLoaded.Count != 1) throw new Exception($"Expected 1, got {rgLoaded.Count}");
                if (rgLoaded[0].DamageHeadMultiplier != null) throw new Exception("Expected null for missing field");
            }
            finally { File.Delete(sPath); }
        });

        Test("Extra fields ignored", () =>
        {
            string sPath = Path.GetTempFileName();
            try
            {
                File.WriteAllText(sPath, "\"SupportedFireModes\",\"FireRate\"\n\"Auto\",600,\"extra\",123");
                var rgLoaded = CsvMapper.Read<WeaponData>(sPath);
                if (rgLoaded.Count != 1) throw new Exception($"Expected 1, got {rgLoaded.Count}");
                if (rgLoaded[0].FireRate != 600) throw new Exception("FireRate mismatch");
            }
            finally { File.Delete(sPath); }
        });

        Test("Non numeric int field defaults to null", () =>
        {
            string sPath = Path.GetTempFileName();
            try
            {
                File.WriteAllText(sPath, "\"SupportedFireModes\",\"FireRate\"\n\"Auto\",\"not_a_number\"");
                var rgLoaded = CsvMapper.Read<WeaponData>(sPath);
                if (rgLoaded.Count != 1) throw new Exception($"Expected 1, got {rgLoaded.Count}");
                if (rgLoaded[0].FireRate != null) throw new Exception($"Expected null, got {rgLoaded[0].FireRate}");
            }
            finally { File.Delete(sPath); }
        });

        Test("Duplicate column names: first wins", () =>
        {
            string sPath = Path.GetTempFileName();
            try
            {
                File.WriteAllText(sPath, "\"FireRate\",\"FireRate\"\n600,700");
                var rgLoaded = CsvMapper.Read<WeaponData>(sPath);
                if (rgLoaded.Count != 1) throw new Exception($"Expected 1, got {rgLoaded.Count}");
                if (rgLoaded[0].FireRate != 600) throw new Exception($"Expected 600 (first), got {rgLoaded[0].FireRate}");
            }
            finally { File.Delete(sPath); }
        });

        Test("Empty quoted fields", () =>
        {
            string sPath = Path.GetTempFileName();
            try
            {
                File.WriteAllText(sPath, "\"SupportedFireModes\",\"FireRate\"\n\"\",600");
                var rgLoaded = CsvMapper.Read<WeaponData>(sPath);
                if (rgLoaded.Count != 1) throw new Exception($"Expected 1, got {rgLoaded.Count}");
                if (rgLoaded[0].FireModes != null) throw new Exception($"Expected null for empty quoted field, got '{rgLoaded[0].FireModes}'");
                if (rgLoaded[0].FireRate != 600) throw new Exception("FireRate mismatch");
            }
            finally { File.Delete(sPath); }
        });

        Test("Negative values", () =>
        {
            string sPath = Path.GetTempFileName();
            try
            {
                File.WriteAllText(sPath, "\"DamageHeadMultiplier\",\"SecondaryFireRate\"\n-0.25,-1");
                var rgLoaded = CsvMapper.Read<WeaponData>(sPath);
                if (rgLoaded.Count != 1) throw new Exception($"Expected 1, got {rgLoaded.Count}");
                if (!(rgLoaded[0].DamageHeadMultiplier is double dVal) || Math.Abs(dVal - (-0.25)) > 0.001)
                    throw new Exception("Negative double mismatch");
                if (rgLoaded[0].SecondaryFireRate != -1) throw new Exception("Negative int mismatch");
            }
            finally { File.Delete(sPath); }
        });

        Test("Clip size format with slash", () =>
        {
            string sPath = Path.GetTempFileName();
            try
            {
                File.WriteAllText(sPath, "\"clip_size\"\n\"30/90\"");
                var rgLoaded = CsvMapper.Read<WeaponData>(sPath);
                if (rgLoaded.Count != 1) throw new Exception($"Expected 1, got {rgLoaded.Count}");
                if (rgLoaded[0].ClipSize != "30/90") throw new Exception($"Expected '30/90', got '{rgLoaded[0].ClipSize}'");
            }
            finally { File.Delete(sPath); }
        });

        Test("Field with trailing spaces inside quotes", () =>
        {
            string sPath = Path.GetTempFileName();
            try
            {
                File.WriteAllText(sPath, "\"SupportedFireModes\"\n\"Auto \"");
                var rgLoaded = CsvMapper.Read<WeaponData>(sPath);
                if (rgLoaded.Count != 1) throw new Exception($"Expected 1, got {rgLoaded.Count}");
                if (rgLoaded[0].FireModes != "Auto") throw new Exception($"Expected 'Auto' after trim, got '{rgLoaded[0].FireModes}'");
            }
            finally { File.Delete(sPath); }
        });

        Test("Real row round trip: write then read matches original", () =>
        {
            string sPath = Path.GetTempFileName();
            try
            {
                var wOrig = new WeaponData
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
                var rgOrig = new List<WeaponData> { wOrig };
                CsvMapper.Write(sPath, rgOrig);
                var rgLoaded = CsvMapper.Read<WeaponData>(sPath);
                if (rgLoaded.Count != 1) throw new Exception($"Expected 1, got {rgLoaded.Count}");
                var wLoaded = rgLoaded[0];
                if (wLoaded.ScriptName != wOrig.ScriptName) throw new Exception("ScriptName mismatch");
                if (wLoaded.FireRate != wOrig.FireRate) throw new Exception("FireRate mismatch");
                if (wLoaded.BulletSpread != wOrig.BulletSpread) throw new Exception("BulletSpread mismatch");
                if (wLoaded.ClipSize != wOrig.ClipSize) throw new Exception("ClipSize mismatch");
                if (wLoaded.DamageHeadMultiplier != wOrig.DamageHeadMultiplier) throw new Exception("DamageHeadMultiplier mismatch");
            }
            finally { File.Delete(sPath); }
        });

        Test("Mixed empty and non-empty fields in single row", () =>
        {
            string sPath = Path.GetTempFileName();
            try
            {
                File.WriteAllText(sPath, "\"ScriptName\",\"FireRate\",\"clip_size\",\"weight\"\n\"test_mixed\",,\"30/90\",");
                var rgLoaded = CsvMapper.Read<WeaponData>(sPath);
                if (rgLoaded.Count != 1) throw new Exception($"Expected 1, got {rgLoaded.Count}");
                if (rgLoaded[0].ScriptName != "test_mixed") throw new Exception("ScriptName mismatch");
                if (rgLoaded[0].FireRate != null) throw new Exception($"Expected null FireRate, got {rgLoaded[0].FireRate}");
                if (rgLoaded[0].ClipSize != "30/90") throw new Exception("ClipSize mismatch");
                if (rgLoaded[0].Weight != null) throw new Exception($"Expected null Weight, got {rgLoaded[0].Weight}");
            }
            finally { File.Delete(sPath); }
        });

        Test("Column name with dot in header", () =>
        {
            string sPath = Path.GetTempFileName();
            try
            {
                File.WriteAllText(sPath, "\"ViewSlideRecoil.Up\"\n1.8");
                var rgLoaded = CsvMapper.Read<WeaponData>(sPath);
                if (rgLoaded.Count != 1) throw new Exception($"Expected 1, got {rgLoaded.Count}");
                if (Math.Abs((rgLoaded[0].ViewSlideRecoilUp ?? 0) - 1.8) > 0.001) throw new Exception("ViewSlideRecoilUp mismatch");
            }
            finally { File.Delete(sPath); }
        });

        Test("Row with more commas than header columns still parses", () =>
        {
            string sPath = Path.GetTempFileName();
            try
            {
                File.WriteAllText(sPath, "\"FireRate\",\"DamageHeadMultiplier\"\n600,2.75,3.0,4.0,5.0");
                var rgLoaded = CsvMapper.Read<WeaponData>(sPath);
                if (rgLoaded.Count != 1) throw new Exception($"Expected 1, got {rgLoaded.Count}");
                if (rgLoaded[0].FireRate != 600) throw new Exception("FireRate mismatch");
                if (rgLoaded[0].DamageHeadMultiplier != 2.75) throw new Exception("DamageHeadMultiplier mismatch");
            }
            finally { File.Delete(sPath); }
        });

        #endregion
        #region 引号与转义

        Test("Quoted field with comma", () =>
        {
            string sPath = Path.GetTempFileName();
            try
            {
                File.WriteAllText(sPath, "\"SupportedFireModes\"\n\"Auto,Semi\"");
                var rgLoaded = CsvMapper.Read<WeaponData>(sPath);
                if (rgLoaded.Count != 1) throw new Exception($"Expected 1, got {rgLoaded.Count}");
                if (rgLoaded[0].FireModes != "Auto,Semi") throw new Exception($"Expected 'Auto,Semi', got '{rgLoaded[0].FireModes}'");
            }
            finally { File.Delete(sPath); }
        });

        Test("Escaped double quote in field", () =>
        {
            string sPath = Path.GetTempFileName();
            try
            {
                File.WriteAllText(sPath, "\"SupportedFireModes\"\n\"Auto\"\"Semi\"");
                var rgLoaded = CsvMapper.Read<WeaponData>(sPath);
                if (rgLoaded.Count != 1) throw new Exception($"Expected 1, got {rgLoaded.Count}");
                if (rgLoaded[0].FireModes != "Auto\"Semi") throw new Exception($"Expected 'Auto\"Semi', got '{rgLoaded[0].FireModes}'");
            }
            finally { File.Delete(sPath); }
        });

        Test("Multiline quoted field", () =>
        {
            string sPath = Path.GetTempFileName();
            try
            {
                File.WriteAllText(sPath, "\"SupportedFireModes\"\n\"Line1\nLine2\"");
                var rgLoaded = CsvMapper.Read<WeaponData>(sPath);
                if (rgLoaded.Count != 1) throw new Exception($"Expected 1, got {rgLoaded.Count}");
                if (rgLoaded[0].FireModes != "Line1\nLine2") throw new Exception($"Expected 'Line1\\nLine2', got '{rgLoaded[0].FireModes}'");
            }
            finally { File.Delete(sPath); }
        });

        Test("Unclosed quote in row skipped", () =>
        {
            string sPath = Path.GetTempFileName();
            try
            {
                File.WriteAllText(sPath, "\"SupportedFireModes\",\"FireRate\"\n\"bad_row,600\n\"good_row\",700");
                var rgLoaded = CsvMapper.Read<WeaponData>(sPath);
                if (rgLoaded.Count != 1) throw new Exception($"Expected 1, got {rgLoaded.Count}");
                if (rgLoaded[0].FireModes != "good_row") throw new Exception($"Expected 'good_row', got '{rgLoaded[0].FireModes}'");
                if (rgLoaded[0].FireRate != 700) throw new Exception("FireRate mismatch");
            }
            finally { File.Delete(sPath); }
        });

        Test("Unclosed multiline quote exceeds limit: broken line discarded, swallowed lines recovered", () =>
        {
            string sPath = Path.GetTempFileName();
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("\"SupportedFireModes\",\"FireRate\"");
                sb.AppendLine("\"bad_start");
                sb.AppendLine("swallowed_line_2");
                sb.AppendLine("swallowed_line_3");
                sb.AppendLine("swallowed_line_4");
                sb.AppendLine("swallowed_line_5");
                sb.AppendLine("\"recovered_row\",800");
                File.WriteAllText(sPath, sb.ToString());

                var rgLoaded = CsvMapper.Read<WeaponData>(sPath);
                if (rgLoaded.Count < 1) throw new Exception($"Expected at least 1 recovered row, got {rgLoaded.Count}");
                if (rgLoaded[^1].FireModes != "recovered_row") throw new Exception($"Expected last row 'recovered_row', got '{rgLoaded[^1].FireModes}'");
                if (rgLoaded[^1].FireRate != 800) throw new Exception("FireRate mismatch on recovered row");
            }
            finally { File.Delete(sPath); }
        });

        #endregion

        Log($"=== CsvMapper Tests: {nPassed} passed, {nFailed} failed ===");
    }

    private static void Log(string sMsg)
    {
        try { WeaponDamageCalc.Services.LogService.Info($"[CsvMapperTest] {sMsg}"); }
        catch { System.Diagnostics.Debug.WriteLine($"[CsvMapperTest] {sMsg}"); }
    }
}
#endif