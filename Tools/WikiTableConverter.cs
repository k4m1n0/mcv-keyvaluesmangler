using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using WeaponDamageCalc.Services;

namespace WeaponDamageCalc.Tools;

public static class WikiTableConverter
{
    //匹配脚本行首可缩进的"key" "value" 捕获键与双引号值
    private static readonly Regex reScriptKv = new(
        @"^[ \t]*""([^""]+)""\s+""([^""]*)""", RegexOptions.Multiline | RegexOptions.Compiled);
    //匹配weapon_开头的脚本名片段 排除空白和|等分隔字符
    private static readonly Regex reScriptName = new(
        @"weapon_[^\s|]+", RegexOptions.Compiled);

    private static readonly string[] rgDamageMultiplierKeys = {
        "damagegeneric", "damageheadmultiplier", "damagechestmultiplier",
        "damagestomachmultiplier", "damagelegmultiplier", "damagearmmultiplier"
    };

    #region 入口

    public static string Convert(string sWikiText, string sScriptsDir)
    {
        var mpScripts = LoadAllScripts(sScriptsDir);
        sWikiText = sWikiText.Replace("\r\n", "\n");
        var rgTables = SplitTables(sWikiText);
        var sbResult = new StringBuilder();

        var rgScriptNames = ExtractScriptNames(rgTables);
        LogService.Info($"Convert: {rgTables.Count} tables, {rgScriptNames.Count} script names found");
        foreach (var segTable in rgTables)
        {
            if (!segTable.IsTable) { sbResult.Append(segTable.Content); continue; }
            var rgRows = SplitRows(segTable.Content);
            sbResult.Append(IsIndexTable(rgRows) || rgScriptNames.Count == 0
                ? segTable.Content
                : ProcessDataTable(segTable.Content, mpScripts, rgScriptNames));
        }
        return sbResult.ToString();
    }

    public static string ConvertSummaryPage(string sWikiText, string sScriptsDir,
        Dictionary<string, string> mpTitleToScript)
    {
        var mpScripts = LoadAllScripts(sScriptsDir);
        sWikiText = sWikiText.Replace("\r\n", "\n");
        var rgTables = SplitTables(sWikiText);
        var sbResult = new StringBuilder();

        LogService.Info($"ConvertSummaryPage: {rgTables.Count} tables, {mpTitleToScript.Count} title mappings");
        foreach (var segTable in rgTables)
        {
            if (!segTable.IsTable) { sbResult.Append(segTable.Content); continue; }
            var rgRows = SplitRows(segTable.Content);
            if (IsIndexTable(rgRows) || IsMetaTable(rgRows)) { sbResult.Append(segTable.Content); continue; }
            sbResult.Append(ProcessSummaryTable(segTable.Content, mpScripts, mpTitleToScript));
        }
        return sbResult.ToString();
    }

    private static bool IsMetaTable(List<string> rgRows)
    {
        foreach (var sRow in rgRows)
        {
            if (!sRow.TrimStart().StartsWith("!")) continue;
            foreach (var sCell in ParseRow(sRow))
            {
                string sClean = StripWikiMarkup(sCell);
                if (sClean.Contains("Script") || sClean.Contains("Icon")) return false;
            }
        }
        return true;
    }

    private static string ProcessSummaryTable(string sTableContent,
        Dictionary<string, Dictionary<string, string>> mpScripts,
        Dictionary<string, string> mpTitleToScript)
    {
        var rgLines = sTableContent.Split('\n');
        var sbResult = new StringBuilder();

        for (int i = 0; i < rgLines.Length; i++)
        {
            string sLine = rgLines[i], sTrimmed = sLine.TrimStart();
            bool bIsDataRow = sTrimmed.StartsWith("|") && !sTrimmed.StartsWith("|-")
                && !sTrimmed.StartsWith("{|") && !sTrimmed.StartsWith("|}");

            if (bIsDataRow && TryMatchWeaponRow(sLine, mpTitleToScript, mpScripts, out var mpValues))
                sbResult.Append(RewriteDataLine(sLine, mpValues));
            else
                sbResult.Append(sLine);

            if (i < rgLines.Length - 1) sbResult.Append('\n');
        }
        return sbResult.ToString();
    }

    private static bool TryMatchWeaponRow(string sLine,
        Dictionary<string, string> mpTitleToScript,
        Dictionary<string, Dictionary<string, string>> mpScripts,
        out Dictionary<string, string> mpValues)
    {
        mpValues = new Dictionary<string, string>();
        //匹配加粗的wikitext链接 <b>[[标题 捕获标题
        var mNameMatch = Regex.Match(sLine, @"<b>\[\[([^\]|]+)");
        if (!mNameMatch.Success) return false;
        string sWikiTitle = mNameMatch.Groups[1].Value.Trim();
        if (!mpTitleToScript.TryGetValue(sWikiTitle, out var sSn) || sSn == null) return false;
        if (sLine.Contains("_riflegrenade") && mpScripts.TryGetValue(sSn + "_riflegrenade", out var mpRgV))
            sSn += "_riflegrenade";
        if (!mpScripts.TryGetValue(sSn, out var mpV)) return false;
        mpValues = new Dictionary<string, string>(mpV, StringComparer.OrdinalIgnoreCase);

        PrecomputeDamageValues(mpValues);
        return true;
    }

    private static List<string> ExtractScriptNames(List<TableSegment> rgTables)
    {
        foreach (var segTable in rgTables)
        {
            if (!segTable.IsTable) continue;
            var rgRows = SplitRows(segTable.Content);
            if (!IsIndexTable(rgRows)) continue;
            var rgNames = new List<string>();
            foreach (var sRow in rgRows)
            {
                if (sRow.TrimStart().StartsWith("!")) continue;
                var rgCells = ParseRow(sRow);
                if (rgCells.Count == 0) continue;
                var mScriptName = reScriptName.Match(StripWikiMarkup(rgCells[rgCells.Count - 1]));
                if (mScriptName.Success) rgNames.Add(mScriptName.Value);
            }
            return rgNames;
        }
        return new List<string>();
    }

    #endregion
    #region 表格处理

    private struct TableSegment { public bool IsTable; public string Content; }

    private static List<TableSegment> SplitTables(string sText)
    {
        var rgSegs = new List<TableSegment>();
        int i = 0;
        while (i < sText.Length)
        {
            int iStart = sText.IndexOf("{|", i, StringComparison.Ordinal);
            if (iStart < 0) { if (i < sText.Length) rgSegs.Add(new TableSegment { IsTable = false, Content = sText[i..] }); break; }
            if (iStart > i) rgSegs.Add(new TableSegment { IsTable = false, Content = sText[i..iStart] });
            int iEnd = FindTableEnd(sText, iStart);
            if (iEnd < 0) iEnd = sText.Length;
            rgSegs.Add(new TableSegment { IsTable = true, Content = sText[iStart..iEnd] });
            i = iEnd;
        }
        return rgSegs;
    }

    //匹配嵌套表格的{| |}对
    private static int FindTableEnd(string sText, int iStart)
    {
        int iDepth = 0;
        for (int j = iStart; j < sText.Length - 1; j++)
        {
            if (sText[j] == '{' && sText[j + 1] == '|') { iDepth++; j++; }
            else if (sText[j] == '|' && sText[j + 1] == '}') { iDepth--; j++; if (iDepth == 0) return j + 1; }
        }
        return -1;
    }

    private static bool IsIndexTable(List<string> rgRows)
    {
        foreach (var sRow in rgRows)
            if (sRow.TrimStart().StartsWith("!") && ParseRow(sRow) is var rgCells
                && rgCells.Count > 0 && StripWikiMarkup(rgCells[rgCells.Count - 1]).Contains("Script"))
                return true;
        return false;
    }

    private static string ProcessDataTable(string sContent,
        Dictionary<string, Dictionary<string, string>> mpScripts, List<string> rgScriptNames)
    {
        var rgLines = sContent.Split('\n');
        var sb = new StringBuilder();
        int iRowIdx = 0;
        for (int i = 0; i < rgLines.Length; i++)
        {
            string sLine = rgLines[i], sTrimmed = sLine.TrimStart();
            bool bIsData = sTrimmed.StartsWith("|") && !sTrimmed.StartsWith("|-")
                && !sTrimmed.StartsWith("{|") && !sTrimmed.StartsWith("|}");
            if (bIsData && iRowIdx < rgScriptNames.Count && mpScripts.TryGetValue(rgScriptNames[iRowIdx], out var mpV))
                sb.Append(RewriteDataLine(sLine, mpV));
            else sb.Append(sLine);
            if (bIsData) iRowIdx++;
            if (i < rgLines.Length - 1) sb.Append('\n');
        }
        return sb.ToString();
    }

    private static string RewriteDataLine(string sLine, Dictionary<string, string> mpValues)
    {
        var rgParts = SplitByDelim(sLine, "||");
        for (int i = 0; i < rgParts.Count; i++)
            rgParts[i] = UpdateCell(rgParts[i], mpValues, i);
        return string.Join("||", rgParts);
    }

    #endregion
    #region 行解析

    private static List<string> SplitRows(string sContent)
    {
        var rgRows = new List<string>();
        var sb = new StringBuilder();
        foreach (var sLine in sContent.Split('\n'))
        {
            string sT = sLine.TrimStart();
            if (sT.StartsWith("{|") || sT.StartsWith("|}") || sT.StartsWith("|-"))
            { if (sb.Length > 0) { rgRows.Add(sb.ToString().TrimEnd()); sb.Clear(); } continue; }
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(sLine);
        }
        if (sb.Length > 0) rgRows.Add(sb.ToString().TrimEnd());
        return rgRows;
    }

    private static List<string> ParseRow(string sRow)
    {
        string sRowContent = sRow.TrimStart();
        while (sRowContent.StartsWith("!") || sRowContent.StartsWith("|")) sRowContent = sRowContent[1..].TrimStart();
        var rgCells = SplitByDelim(sRowContent, "!!");
        return rgCells.Count > 0 ? rgCells : SplitByDelim(sRowContent, "||");
    }

    //跳过[[链接]]内的分隔符防止误切
    private static List<string> SplitByDelim(string sText, string sDelim)
    {
        var rgParts = new List<string>();
        int iLast = 0;
        bool bInLink = false;
        for (int i = 0; i < sText.Length; i++)
        {
            if (sText[i] == '[' && i + 1 < sText.Length && sText[i + 1] == '[') bInLink = true;
            if (sText[i] == ']' && i + 1 < sText.Length && sText[i + 1] == ']') bInLink = false;
            if (!bInLink && i + 1 < sText.Length && sText[i..(i + 2)] == sDelim)
            { rgParts.Add(sText[iLast..i]); iLast = i + 2; i++; }
        }
        if (iLast < sText.Length) rgParts.Add(sText[iLast..]);
        return rgParts;
    }

    #endregion
    #region 单元格更新

    private static string UpdateCell(string sCell, Dictionary<string, string> mpV, int iCol)
    {
        string sClean = StripWikiMarkup(sCell).Trim();
        if (sClean.StartsWith("|") && !sClean.StartsWith("||")) sClean = sClean[1..].TrimStart();

        //提取已有的zmstats橙字 计算完新值后重新拼接
        bool bHasZombie = false;
        string sZombieSuffix = "";
        //匹配橙字僵尸值 <span style="color:#ff6905;">...</span> 捕获内部文本
        var mZMatch = Regex.Match(sCell, @"<br>\s*<span style=""color:#ff6905;"">([^<]*)</span>");
        if (mZMatch.Success) { bHasZombie = true; sZombieSuffix = mZMatch.Value; sCell = sCell.Replace(sZombieSuffix, ""); }

        //多弹药武器保留原始格式不转换 如下挂榴弹用<br>分隔
        if (sCell.Contains("<br>"))
            return bHasZombie ? sCell + sZombieSuffix : sCell;

        double dPellets = Math.Max(GetDouble(mpV, "bullets_per_shot"), 1.0);
        if (dPellets > 1 && iCol == 0)
        {
            double dDgVal = GetDouble(mpV, "damagegeneric");
            if (dDgVal > 0)
            {
                //匹配数字或数字x数字形式的伤害 替换为伤害x弹丸
                return Regex.Replace(sCell, @"\d+\.?\d*[Xx]?\d*\.?\d*",
                    $"{FormatDouble(dDgVal)}x{FormatDouble(dPellets)}");
            }
        }
        //检测整格是否为数字x数字的多弹药伤害格式
        else if (dPellets > 1 && iCol == 5 && Regex.IsMatch(sClean, @"^\d+\.?\d*[Xx]\d+\.?\d*$"))
        {
            double dDgVal = GetDouble(mpV, "damagegeneric");
            if (dDgVal > 0)
            {
                //匹配数字x数字伤害格式并替换为伤害x弹丸
                return Regex.Replace(sCell, @"\d+\.?\d*[Xx]\d+\.?\d*",
                    $"{FormatDouble(dDgVal)}x{FormatDouble(dPellets)}");
            }
        }
        else if (iCol == 0)
        {
            double dDgVal = GetDouble(mpV, "damagegeneric");
            //检测整格是否为纯整数/小数伤害数值
            if (dDgVal > 0 && Regex.IsMatch(sClean, @"^[1-9]\d*\.?\d*$")
                && Math.Abs(double.Parse(sClean, CultureInfo.InvariantCulture) - dDgVal) < 100)
            {
                //匹配数字并替换为新的伤害值
                return Regex.Replace(sCell, @"\d+\.?\d*", FormatDouble(dDgVal));
            }
        }

        bool bIsExplosive = GetDouble(mpV, "explosiondamage") > 0;

        //匹配伤害倍率行x倍率=伤害 捕获倍率和伤害
        var mDmg = Regex.Match(sClean, @"^[x×](\d+\.?\d*)\s*=\s*(\d+\.?\d*)$");
        if (mDmg.Success)
        {
            double dMult = double.Parse(mDmg.Groups[1].Value, CultureInfo.InvariantCulture);
            if (iCol < rgDamageMultiplierKeys.Length && GetDouble(mpV, rgDamageMultiplierKeys[iCol]) is double dSm && dSm > 0) dMult = dSm;
            if (GetDouble(mpV, "damagegeneric") is double dBd && dBd > 0)
            {
                double dTotalDmg = Math.Round(dBd * dMult, 2);
                //匹配x倍率=伤害并替换为重新计算后的值
                return Regex.Replace(sCell, @"[x×]\d+\.?\d*\s*=\s*\d+\.?\d*",
                    $"x{FormatDouble(dMult)} = {FormatDouble(dTotalDmg)}")
                    + MakeZombie(mpV, bHasZombie, "<br><span style=\"color:#ff6905;\">x{0} = {1}</span>",
                        FormatDouble(GetDouble(mpV, "zombie_" + rgDamageMultiplierKeys[Math.Min(iCol, rgDamageMultiplierKeys.Length - 1)])),
                        FormatDouble(Math.Round(
                            Math.Max(GetDouble(mpV, "zombie_damagegeneric"), 0)
                            * Math.Max(GetDouble(mpV, "zombie_" + rgDamageMultiplierKeys[Math.Min(iCol, rgDamageMultiplierKeys.Length - 1)]), dMult), 2)));
            }
            return sCell + (bHasZombie ? sZombieSuffix : "");
        }

        //匹配散布格hip/ads ADS形式 捕获两个数值
        var mSpread = Regex.Match(sClean, @"^(\d+\.?\d*)\s*/\s*(\d+\.?\d*)\s*(ADS|\[\[ADS\]\])$");
        if (mSpread.Success && GetDouble(mpV, "bulletspreaddegrees") is double dHip && GetDouble(mpV, "bulletspreaddegreesironsighted") is double dAds && (dHip > 0 || dAds > 0))
            //匹配数值/数值[[ADS]]并替换为hip/ads散布
            return Regex.Replace(sCell, @"\d+\.?\d*\s*/\s*\d+\.?\d*\s*\[\[ADS\]\]", $"{FormatDouble(dHip)} / {FormatDouble(dAds)} [[ADS]]")
                + MakeZombie(mpV, bHasZombie, "<br><span style=\"color:#ff6905;\">{0} / {1} [[ADS]]</span>",
                    FormatDouble(GetDouble(mpV, "zombie_bulletspreaddegrees")),
                    FormatDouble(GetDouble(mpV, "zombie_bulletspreaddegreesironsighted")));

        //匹配脚架散布格数值°&数值°[[ADS]] 捕获两个数值
        var mBipod = Regex.Match(sClean, @"^(\d+\.?\d*)°?\s*&\s*(\d+\.?\d*)°?\s*\[\[ADS\]\]$");
        if (mBipod.Success)
        {
            double dV1 = double.Parse(mBipod.Groups[1].Value, CultureInfo.InvariantCulture);
            double dHipSpread = GetDouble(mpV, "bulletspreaddegrees");
            double dAdsSpread = GetDouble(mpV, "bulletspreaddegreesironsighted");
            //用第一个数值更接近hip还是ads来判断行类型
            if (Math.Abs(dV1 - dHipSpread) <= Math.Abs(dV1 - dAdsSpread) && dHipSpread > 0)
            {
                double dBipod = GetDouble(mpV, "bulletspreaddegreesbipod");
                //匹配脚架散布格式并替换为hip&bipod 第一值更接近hip时
                return Regex.Replace(sCell, @"\d+\.?\d*°?\s*&\s*\d+\.?\d*°?\s*\[\[ADS\]\]",
                    $"{FormatDouble(dHipSpread)}° & {FormatDouble(dBipod)}° [[ADS]]");
            }
            else if (dAdsSpread > 0)
            {
                double dBipodAds = GetDouble(mpV, "bulletspreaddegreesbipodironsighted");
                //匹配脚架散布格式并替换为ads&bipodAds 第一值更接近ads时
                return Regex.Replace(sCell, @"\d+\.?\d*°?\s*&\s*\d+\.?\d*°?\s*\[\[ADS\]\]",
                    $"{FormatDouble(dAdsSpread)}° & {FormatDouble(dBipodAds)}° [[ADS]]");
            }
        }

        //检测整格是否为 0.xxx形式的射程倍率
        if (Regex.IsMatch(sClean, @"^0\.\d+$") && GetDouble(mpV, "rangemodifier") is double dRm && dRm > 0)
            //匹配 0.xxx 数值并替换为新的射程倍率
            return Regex.Replace(sCell, @"0\.\d+", FormatDouble(dRm))
                + MakeZombie(mpV, bHasZombie, "<br><span style=\"color:#ff6905;\">{0}</span>", FormatDouble(GetDouble(mpV, "zombie_rangemodifier")));

        //匹配射速格数字RPM 捕获数值
        var mRpm = Regex.Match(sClean, @"^(\d+)\s*RPM$");
        if (mRpm.Success && mpV.TryGetValue("firerate", out var sFr) && int.TryParse(sFr, out int iIr) && iIr > 0)
            //匹配数字RPM并替换为脚本射速
            return Regex.Replace(sCell, @"\d+\s*RPM", $"{iIr} RPM")
                + MakeZombie(mpV, bHasZombie, "<br><span style=\"color:#ff6905;\">{0} RPM</span>", mpV.TryGetValue("zombie_firerate", out var sZfr) ? sZfr : "");

        //匹配重量格kg (lbs)捕获kg和lbs数值
        var mWlm = Regex.Match(sClean, @"^(\d+\.?\d*)\s*kg\s*\((\d+\.?\d*)\s*lbs\)$");
        if (mWlm.Success && GetDouble(mpV, "weight") is double dWk && dWk > 0)
            //匹配kg (lbs)形式并替换为重新计算的重量
            return Regex.Replace(sCell, @"\d+\.?\d*\s*kg\s*\(\d+\.?\d*\s*lbs\)",
                $"{FormatDouble(dWk)} kg ({FormatDouble(Math.Round(dWk * 2.20462, 2))} lbs)")
                + MakeZombie(mpV, bHasZombie, "<br><span style=\"color:#ff6905;\">{0} kg ({1} lbs)</span>",
                    FormatDouble(GetDouble(mpV, "zombie_weight")),
                    FormatDouble(Math.Round(GetDouble(mpV, "zombie_weight") * 2.20462, 2)));

        //匹配纯kg重量格数字kg 捕获数值
        var mWm = Regex.Match(sClean, @"^(\d+\.?\d*)\s*kg$");
        if (mWm.Success && GetDouble(mpV, "weight") is double dW && dW > 0)
            //匹配数字kg并替换为新重量
            return Regex.Replace(sCell, @"\d+\.?\d*\s*kg", $"{FormatDouble(dW)} kg")
                + MakeZombie(mpV, bHasZombie, "<br><span style=\"color:#ff6905;\">{0} kg</span>", FormatDouble(GetDouble(mpV, "zombie_weight")));

        //匹配弹重格g (gr)捕获g和gr数值
        var mBwm = Regex.Match(sClean, @"^(\d+\.?\d*)\s*g\s*\((\d+\.?\d*)\s*gr\)$");
        if (mBwm.Success && GetDouble(mpV, "bullet_weight") is double dBwk && dBwk > 0)
            //匹配g (gr)形式并替换为重新计算的弹重
            return Regex.Replace(sCell, @"\d+\.?\d*\s*g\s*\(\d+\.?\d*\s*gr\)",
                $"{FormatDouble(Math.Round(dBwk * 1000, 1))} g ({FormatDouble(Math.Round(dBwk * 15432.36, 2))} gr)")
                + MakeZombie(mpV, bHasZombie, "<br><span style=\"color:#ff6905;\">{0} g ({1} gr)</span>",
                    FormatDouble(Math.Round(GetDouble(mpV, "zombie_bullet_weight") * 1000, 1)),
                    FormatDouble(Math.Round(GetDouble(mpV, "zombie_bullet_weight") * 15432.36, 2)));

        //匹配纯字母的射击模式格 如Auto或Auto+Semi
        var mFireMode = Regex.Match(sClean, @"^[A-Za-z]+(\+[A-Za-z]+)*$");
        if (mFireMode.Success && mpV.TryGetValue("SupportedFireModes", out var sFm) && !string.IsNullOrEmpty(sFm))
        {
            if (sClean != sFm)
                //匹配字母射击模式字符串并替换为脚本值
                return Regex.Replace(sCell, @"[A-Za-z]+(\+[A-Za-z]+)*", sFm);
        }

        if (!bIsExplosive)
        {
            //匹配弹匣格数字/数字形式 捕获前后两个数值
            var mClip = Regex.Match(sClean, @"^(\d+).*?/\s*(\d+)$");
            if (mClip.Success && mpV.TryGetValue("clip_size", out var sClip) && !string.IsNullOrEmpty(sClip) && sClip != "-1" && sClip != "0/0" && !sClip.StartsWith("-1/") && sClip.Contains('/'))
            {
                var rgParts = sClip.Split('/');
                if (rgParts.Length == 2)
                {
                    string sExtra = mpV.TryGetValue("extrabulletchamber", out var sExc) && sExc == "1" ? "[[+1]]" : "";
                    //匹配弹匣格式 含可选的[[+1]]标记 并替换为脚本弹量
                    return Regex.Replace(sCell, @"\d+\[\[.*?\]\]?\s*/\s*\d+|\d+\s*/\s*\d+", $"{rgParts[0].Trim()}{sExtra} / {rgParts[1].Trim()}")
                        + MakeZombieClip(mpV, bHasZombie);
                }
            }
        }

        bool bIsStandardGun = GetDouble(mpV, "damagegeneric") > 0 && GetDouble(mpV, "damageheadmultiplier") > 0;
        bool bIsDamageColumn = iCol == 5 || iCol == 6;
        //检测整格是否为纯整数/小数伤害数值 用于胸/头伤害列
        if (bIsDamageColumn && (bIsExplosive || bIsStandardGun) && Regex.IsMatch(sClean, @"^[1-9]\d*\.?\d*$"))
        {
            //容差<100防止替换弹药数量等非伤害数字
            string sKey = (iCol == 6 && mpV.ContainsKey("__head_dmg")) ? "__head_dmg" : "__chest_dmg";
            if (mpV.TryGetValue(sKey, out var sDmgStr) && double.TryParse(sDmgStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double dDmgVal)
                && Math.Abs(double.Parse(sClean, CultureInfo.InvariantCulture) - dDmgVal) < 100)
                //匹配数字并替换为预计算的伤害值
                return Regex.Replace(sCell, @"\d+\.?\d*", sDmgStr)
                    + MakeZombiePure(mpV, bHasZombie, sKey);
        }

        return bHasZombie ? sCell + sZombieSuffix : sCell;
    }

    #endregion
    #region 僵尸橙字辅助

    private static string MakeZombie(Dictionary<string, string> mpV, bool bHasZombie, string sFmt, params string[] rgVals)
    {
        if (!bHasZombie) return "";
        if (rgVals.All(sX => string.IsNullOrEmpty(sX) || sX == "0" || sX == "-1")) return "";
        return string.Format(sFmt, rgVals.Select(sX => (object)sX).ToArray());
    }

    private static string MakeZombieClip(Dictionary<string, string> mpV, bool bHasZombie)
    {
        if (!bHasZombie || !mpV.TryGetValue("zombie_clip_size", out var sZc) || string.IsNullOrEmpty(sZc) || sZc == "-1" || !sZc.Contains('/'))
            return "";
        var rgParts = sZc.Split('/');
        if (rgParts.Length != 2) return "";
        string sExtra = mpV.TryGetValue("extrabulletchamber", out var sExc) && sExc == "1" ? "[[+1]]" : "";
        return $"<br><span style=\"color:#ff6905;\">{rgParts[0].Trim()}{sExtra} / {rgParts[1].Trim()}</span>";
    }

    //使用预计算的__z_chest_dmg/__z_head_dmg值
    private static string MakeZombiePure(Dictionary<string, string> mpV, bool bHasZombie, string sKey)
    {
        if (!bHasZombie) return "";
        string sZKey = sKey == "__chest_dmg" ? "__z_chest_dmg" : "__z_head_dmg";
        if (mpV.TryGetValue(sZKey, out var sZv) && !string.IsNullOrEmpty(sZv) && sZv != "0")
            return $"<br><span style=\"color:#ff6905;\">{sZv}</span>";
        return "";
    }

    #endregion
    #region 脚本加载

    private static Dictionary<string, Dictionary<string, string>> LoadAllScripts(string sScriptsDir)
    {
        var mpResult = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(sScriptsDir))
        {
            LogService.Warn($"LoadAllScripts: scripts directory not found: {sScriptsDir}");
            return mpResult;
        }

        var rgFiles = Directory.GetFiles(sScriptsDir, "weapon_*.txt");
        LogService.Info($"LoadAllScripts: loading {rgFiles.Length} weapon scripts...");
        foreach (var sPath in Directory.GetFiles(sScriptsDir, "weapon_*.txt"))
        {
            try
            {
                var mpValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                string sContent = WeaponScriptService.ReadScriptFile(sPath).Replace("\r\n", "\n");
                int iWd = sContent.IndexOf("WeaponData", StringComparison.Ordinal);
                if (iWd < 0) continue;
                int iBs = sContent.IndexOf('{', iWd);
                if (iBs < 0) continue;
                int iBe = WeaponScriptService.FindMatchingBrace(sContent, iBs);
                if (iBe < 0) continue;
                string sBlock = sContent.Substring(iBs + 1, iBe - iBs - 1);
                foreach (Match m in reScriptKv.Matches(sBlock))
                {
                    //只收集顶层键值对 通过大括号计数跳过嵌套块内的同名键
                    string sBefore = sBlock.Substring(0, m.Index);
                    int iOb = 0, iCb = 0;
                    for (int j = 0; j < sBefore.Length; j++)
                    { if (sBefore[j] == '{') iOb++; else if (sBefore[j] == '}') iCb++; }
                    if (iOb == iCb) mpValues[m.Groups[1].Value] = m.Groups[2].Value;
                }
                int iZi = sContent.IndexOf("zombie_stats", iBe, StringComparison.Ordinal);
                if (iZi >= 0) { LoadSubBlock(mpValues, sContent, iZi, "zombie_"); }

                if (mpValues.Count > 0)
                {
                    PrecomputeDamageValues(mpValues);
                    mpResult[Path.GetFileNameWithoutExtension(sPath)] = mpValues;
                }
            }
            catch (Exception ex)
            {
                LogService.Error(ex, $"WikiTableConverter.LoadAllScripts: {Path.GetFileName(sPath)}");
            }
        }
        LogService.Info($"LoadAllScripts: {mpResult.Count} scripts loaded");
        return mpResult;
    }

    private static void PrecomputeDamageValues(Dictionary<string, string> mpValues)
    {
        double dDg = GetDouble(mpValues, "damagegeneric");
        double dEd = GetDouble(mpValues, "explosiondamage");
        if (dEd > 0)
        {
            mpValues["__chest_dmg"] = FormatDouble(dEd);
            mpValues["__head_dmg"] = FormatDouble(GetDouble(mpValues, "explosionradius"));
            double dZed = GetDouble(mpValues, "zombie_explosiondamage");
            if (dZed > 0)
            {
                mpValues["__z_chest_dmg"] = FormatDouble(dZed);
                mpValues["__z_head_dmg"] = FormatDouble(GetDouble(mpValues, "zombie_explosionradius"));
            }
        }
        else if (dDg > 0)
        {
            double dCm = Math.Max(GetDouble(mpValues, "damagechestmultiplier"), 1.0);
            double dHm = Math.Max(GetDouble(mpValues, "damageheadmultiplier"), 1.0);
            double dPellets = Math.Max(GetDouble(mpValues, "bullets_per_shot"), 1.0);
            mpValues["__chest_dmg"] = FormatDouble(Math.Round(dDg * dCm * dPellets, 2));
            mpValues["__head_dmg"] = FormatDouble(Math.Round(dDg * dHm * dPellets, 2));
            double dZdg = GetDouble(mpValues, "zombie_damagegeneric");
            if (dZdg > 0)
            {
                double dZcm = Math.Max(GetDouble(mpValues, "zombie_damagechestmultiplier"), dCm);
                double dZhm = Math.Max(GetDouble(mpValues, "zombie_damageheadmultiplier"), dHm);
                double dZpellets = Math.Max(GetDouble(mpValues, "zombie_bullets_per_shot"), dPellets);
                mpValues["__z_chest_dmg"] = FormatDouble(Math.Round(dZdg * dZcm * dZpellets, 2));
                mpValues["__z_head_dmg"] = FormatDouble(Math.Round(dZdg * dZhm * dZpellets, 2));
            }
        }
    }

    private static void LoadSubBlock(Dictionary<string, string> mpValues, string sContent, int iBlockIdx, string sPrefix)
    {
        int iBs = sContent.IndexOf('{', iBlockIdx);
        if (iBs < 0) return;
        int iBe = WeaponScriptService.FindMatchingBrace(sContent, iBs);
        if (iBe < 0) return;
        foreach (Match m in reScriptKv.Matches(sContent.Substring(iBs + 1, iBe - iBs - 1)))
            mpValues[sPrefix + m.Groups[1].Value] = m.Groups[2].Value;
    }

    private static double GetDouble(Dictionary<string, string> mpV, string sKey) =>
        WeaponScriptService.GetDoubleVal(mpV, sKey);

    private static string FormatDouble(double d) =>
        WeaponScriptService.FormatDouble(d);

    #endregion
    #region 辅助

    private static string StripWikiMarkup(string sCell)
    {
        //处理wikitext链接[[别名|显示文本]] 保留显示文本
        string sResult = Regex.Replace(sCell, @"\[\[[^\]|]+\|([^\]]+)\]\]", "$1");
        //处理无别名链接[[目标]] 去掉双方括号
        sResult = Regex.Replace(sResult, @"\[\[([^\]]+)\]\]", "$1");
        //去除剩余HTML标签
        return Regex.Replace(sResult, @"<[^>]+>", "");
    }

    #endregion
}