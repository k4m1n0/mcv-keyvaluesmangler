using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using WeaponDamageCalc.Services;

namespace WeaponDamageCalc.Tools;

public static class ScriptToTemplateConverter
{
    #region 入口
    public static string ConvertAll(string sScriptsDir, bool bSimpleMode)
    {
        LogService.Info($"ConvertAll: {sScriptsDir}, simpleMode={bSimpleMode}");
        string[] rgTemplateLines = ReadEmbeddedTemplate();
        if (rgTemplateLines == null || rgTemplateLines.Length == 0)
        {
            LogService.Error("ConvertAll: embedded template not found");
            return "嵌入的模板文件未找到";
        }

        var rgFiles = Directory.GetFiles(sScriptsDir, "weapon_*.txt");
        var rgLog = new List<string>();
        int iSuccess = 0, iFailed = 0;

        string sExternalPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preset_file.txt");
        if (File.Exists(sExternalPath) && rgTemplateLines.Any(sL => sL.Contains("WeaponData")))
            rgLog.Add("使用外置模板: " + sExternalPath);
        else if (File.Exists(sExternalPath))
            rgLog.Add("外置模板校验失败 (缺少WeaponData块)，已回退到内嵌默认模板");

        rgLog.Add("脚本 -> 模板转换");
        rgLog.Add($"目录: {sScriptsDir}");
        rgLog.Add($"共 {rgFiles.Length} 个文件");
        rgLog.Add(new string('-', 50));

        var roOpts = bSimpleMode ? RenderOptions.Simple : RenderOptions.Full;

        for (int i = 0; i < rgFiles.Length; i++)
        {
            string sPath = rgFiles[i];
            string sName = Path.GetFileName(sPath);
            try
            {
                string sScript = WeaponScriptService.ReadScriptFile(sPath);
                string sResult = ConvertSingle(sScript, rgTemplateLines, roOpts);
                DumpDebugInfo(sName, sScript, rgTemplateLines, sResult, rgLog);
                File.WriteAllText(sPath, sResult, new UTF8Encoding(false));
                iSuccess++;
                rgLog.Add($"[{i + 1}/{rgFiles.Length}] {sName}");
            }
            catch (Exception ex)
            {
                iFailed++;
                rgLog.Add($"[{i + 1}/{rgFiles.Length}] 失败: {sName} - {ex.Message}");
                LogService.Error(ex, $"ConvertAll: {sName}");
            }
        }

        rgLog.Add(new string('-', 50));
        rgLog.Add($"完成: 成功 {iSuccess}, 失败 {iFailed}, 总计 {rgFiles.Length}");
        string sResultLog = string.Join("\n", rgLog);
        LogService.Info($"ConvertAll done: {iSuccess} ok, {iFailed} fail, {rgFiles.Length} total");
        return sResultLog;
    }

    private static string[] ReadEmbeddedTemplate()
    {
        try
        {
            string sExternalPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preset_file.txt");
            if (File.Exists(sExternalPath))
            {
                LogService.Info($"ReadEmbeddedTemplate: loading external template: {sExternalPath}");
                var rgLines = WeaponScriptService.ReadScriptFile(sExternalPath)
                               .Replace("\r\n", "\n")
                               .Replace('\r', '\n')
                               .Split('\n');
                if (rgLines.Any(sL => sL.Contains("WeaponData")) && rgLines.Any(sL => sL.Contains("{")) && rgLines.Any(sL => sL.Contains("}")))
                {
                    LogService.Info("ReadEmbeddedTemplate: external template validated");
                    return rgLines;
                }
                LogService.Warn("ReadEmbeddedTemplate: external template validation failed, falling back to embedded");
            }

            LogService.Info("ReadEmbeddedTemplate: loading embedded template");
            using var stmEmbedded = System.Reflection.Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("WeaponDamageCalc.Tools.preset_file.txt");
            if (stmEmbedded == null)
            {
                LogService.Error("ReadEmbeddedTemplate: embedded resource not found");
                return Array.Empty<string>();
            }
            using var srReader = new StreamReader(stmEmbedded, Encoding.UTF8);
            return srReader.ReadToEnd()
                         .Replace("\r\n", "\n")
                         .Replace('\r', '\n')
                         .Split('\n');
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "ReadEmbeddedTemplate");
            return Array.Empty<string>();
        }
    }

    #endregion
    #region 渲染配置

    private struct RenderOptions
    {
        public bool bSkipEmptyValues;
        public bool bSkipSeparators;
        public bool bCompressBlankLines;

        public static readonly RenderOptions Full = new()
        {
            bSkipEmptyValues = false,
            bSkipSeparators = false,
            bCompressBlankLines = false
        };

        public static readonly RenderOptions Simple = new()
        {
            bSkipEmptyValues = true,
            bSkipSeparators = true,
            bCompressBlankLines = true
        };
    }

    #endregion
    #region 转换解析

    private static string ConvertSingle(string sScript, string[] rgTemplateLines, RenderOptions roOpts)
    {
        try
        {
            var mpScriptMap = ParseTopLevelMap(sScript);
            var mpScriptBlocks = ExtractAllBlocks(sScript);

            var anTemplateTree = ParseTemplateToTree(rgTemplateLines);
            if (anTemplateTree == null || anTemplateTree.Children.Count == 0)
                return string.Join("\n", rgTemplateLines);

            var hsMissingKeys = new HashSet<string>(mpScriptMap.Keys, StringComparer.OrdinalIgnoreCase);
            FillTreeWithScript(anTemplateTree, mpScriptMap, mpScriptBlocks, sScript, hsMissingKeys);

            var sbResult = new StringBuilder();
            var rsState = new RenderState();
            RenderTree(anTemplateTree, sbResult, roOpts, rsState);
            string sOutput = sbResult.ToString();
            sOutput = Regex.Replace(sOutput, @"^\s*//\s*[\{\}]\s*$", "", RegexOptions.Multiline);
            sOutput = sOutput.TrimStart('\r', '\n');
            if (!sOutput.EndsWith('\n'))
                sOutput += "\n";
            return sOutput;
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "ConvertSingle");
            return string.Join("\n", rgTemplateLines);
        }
    }

    private enum NodeType { Root, Block, KeyValue, CommentedBlock, CommentedKeyValue, Blank, Raw }

    private class AstNode
    {
        public NodeType Type { get; set; }
        public string Indent { get; set; } = "";
        public string? Name { get; set; }
        public string? Value { get; set; }
        public string? Comment { get; set; }
        public string Separator { get; set; } = "\t\t\t\t";
        public List<AstNode> Children { get; set; } = new();
        public string RawText { get; set; } = "";
        public List<string> HeaderLines { get; set; } = new();

        public override string ToString() => ToString(0);

        private string ToString(int iDepth)
        {
            var sb = new StringBuilder();
            sb.Append(new string(' ', iDepth * 2));
            switch (Type)
            {
                case NodeType.Root:
                    sb.AppendLine($"Root [{Children.Count} children]");
                    break;
                case NodeType.Block:
                    sb.AppendLine($"Block \"{Name}\" [{Children.Count} children]");
                    break;
                case NodeType.CommentedBlock:
                    sb.AppendLine($"CommentedBlock \"{Name}\" [{Children.Count} children]");
                    break;
                case NodeType.KeyValue:
                    sb.Append($"KeyValue \"{Name}\" = \"{Value}\"");
                    if (!string.IsNullOrEmpty(Comment)) sb.Append($" // {Comment}");
                    sb.AppendLine();
                    break;
                case NodeType.CommentedKeyValue:
                    sb.Append($"CommentedKeyValue \"{Name}\" = \"{Value}\"");
                    if (!string.IsNullOrEmpty(Comment)) sb.Append($" // {Comment}");
                    sb.AppendLine();
                    break;
                case NodeType.Blank:
                    sb.AppendLine($"Blank (len={RawText.Length})");
                    break;
                case NodeType.Raw:
                {
                    string sPreview = RawText.Length > 60 ? RawText[..60] + "..." : RawText;
                    sb.AppendLine($"Raw \"{sPreview.Replace("\n", "\\n").Replace("\r", "\\r")}\"");
                    break;
                }
            }
            foreach (var anChild in Children)
                sb.Append(anChild.ToString(iDepth + 1));
            return sb.ToString();
        }
    }

    private class RenderState
    {
        public bool bLastWasBlank;
    }

    private static AstNode ParseTemplateToTree(string[] rgLines)
    {
        try
        {
            var anRoot = new AstNode { Type = NodeType.Root };
            int i = 0;

            while (i < rgLines.Length)
            {
                if (IsBlockStart(rgLines, i, out string sBlockName, out int iEnd) &&
                    sBlockName.Equals("WeaponData", StringComparison.OrdinalIgnoreCase))
                {
                    var anWdNode = new AstNode
                    {
                        Type = NodeType.Block,
                        Indent = ExtractIndent(rgLines[i]),
                        Name = "WeaponData"
                    };
                    anWdNode.HeaderLines.Add(rgLines[i]);

                    anRoot.Children.Add(anWdNode);
                    ParseLines(rgLines, i + 1, iEnd, anWdNode.Children);
                    i = iEnd + 1;
                    continue;
                }
                anRoot.Children.Add(new AstNode { Type = NodeType.Raw, RawText = rgLines[i] });
                i++;
            }
            return anRoot;
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "ParseTemplateToTree");
            return new AstNode { Type = NodeType.Root };
        }
    }

    //统一逐行解析 所有解析都走这条路
    private static void ParseLines(string[] rgLines, int iStart, int iEnd, List<AstNode> rgNodes)
    {
        int i = iStart;
        while (i < iEnd && i < rgLines.Length)
        {
            string sLine = rgLines[i] ?? string.Empty;
            string sTrimmed = sLine.TrimStart();

            if (string.IsNullOrWhiteSpace(sLine))
            {
                rgNodes.Add(new AstNode { Type = NodeType.Blank, RawText = sLine });
                i++; continue;
            }

            if (IsBlockStart(rgLines, i, out string sSubName, out int iSubEnd))
            {
                var anSubNode = new AstNode
                {
                    Type = NodeType.Block,
                    Indent = ExtractIndent(sLine),
                    Name = sSubName
                };

                int iHeaderEnd = i;
                for (int h = i; h <= iSubEnd; h++)
                {
                    anSubNode.HeaderLines.Add(rgLines[h]);
                    if (rgLines[h].Contains('{'))
                    {
                        iHeaderEnd = h;
                        break;
                    }
                }

                rgNodes.Add(anSubNode);
                ParseLines(rgLines, iHeaderEnd + 1, iSubEnd, anSubNode.Children);
                i = iSubEnd + 1;
                continue;
            }

            if (sTrimmed.StartsWith("//") && !sTrimmed.Contains('"'))
            {
                string sAfterSlash = sTrimmed.Substring(2).TrimStart();
                var rgParts = sAfterSlash.Split(new[] { ' ', '\t', '{', '}' }, StringSplitOptions.RemoveEmptyEntries);
                if (rgParts.Length > 0)
                {
                    string sPotentialName = rgParts[0];
                    if (TryFindCommentedBlockRange(rgLines, i, iEnd, out int iCommentEnd))
                    {
                        var anCNode = new AstNode
                        {
                            Type = NodeType.CommentedBlock,
                            Indent = ExtractIndent(sLine),
                            Name = sPotentialName
                        };
                        anCNode.HeaderLines.Add(rgLines[i]);

                        //解析注释块内部 去掉行首//前缀
                        for (int k = i + 1; k < iCommentEnd; k++)
                        {
                            string sInnerLine = rgLines[k];
                            string sInnerTrimmed = sInnerLine.TrimStart();
                            if (string.IsNullOrWhiteSpace(sInnerLine))
                            {
                                anCNode.Children.Add(new AstNode { Type = NodeType.Blank, RawText = sInnerLine });
                                continue;
                            }
                            string sUncommented = sInnerTrimmed.StartsWith("//") ? sInnerTrimmed.Substring(2).TrimStart() : sInnerTrimmed;
                            ExtractKVResult? ekvKvr = ExtractKeyValueFromLine(sUncommented);
                            if (ekvKvr != null)
                            {
                                anCNode.Children.Add(new AstNode
                                {
                                    Type = NodeType.CommentedKeyValue,
                                    Indent = ExtractIndent(sInnerLine),
                                    Name = ekvKvr.Key,
                                    Value = ekvKvr.Value,
                                    Separator = ekvKvr.Separator,
                                    Comment = ekvKvr.Comment
                                });
                            }
                            else
                            {
                                anCNode.Children.Add(new AstNode { Type = NodeType.Raw, RawText = sInnerLine });//无法识别的行当raw处理
                            }
                        }

                        rgNodes.Add(anCNode);
                        i = iCommentEnd + 1;
                        continue;
                    }
                }
                //不是注释块 当作普通注释行
                rgNodes.Add(new AstNode { Type = NodeType.Raw, RawText = sLine });
                i++; continue;
            }

            if (sTrimmed.StartsWith("//") && sTrimmed.Contains('"'))
            {
                string sAfterSlash = sTrimmed.Substring(sTrimmed.IndexOf('"'));
                ExtractKVResult? ekvKvr = ExtractKeyValueFromLine(sAfterSlash);
                if (ekvKvr != null)
                {
                    rgNodes.Add(new AstNode
                    {
                        Type = NodeType.CommentedKeyValue,
                        Indent = ExtractIndent(sLine),
                        Name = ekvKvr.Key,
                        Value = ekvKvr.Value,
                        Separator = ekvKvr.Separator,
                        Comment = ekvKvr.Comment
                    });
                    i++; continue;
                }
                rgNodes.Add(new AstNode { Type = NodeType.Raw, RawText = sLine });
                i++; continue;
            }

            if (sTrimmed.StartsWith("\""))
            {
                ExtractKVResult? ekvKvr = ExtractKeyValueFromLine(sTrimmed);
                if (ekvKvr != null)
                {
                    rgNodes.Add(new AstNode
                    {
                        Type = NodeType.KeyValue,
                        Indent = ExtractIndent(sLine),
                        Name = ekvKvr.Key,
                        Value = ekvKvr.Value,
                        Separator = ekvKvr.Separator,
                        Comment = ekvKvr.Comment
                    });
                    i++; continue;
                }
                rgNodes.Add(new AstNode { Type = NodeType.Raw, RawText = sLine });
                i++; continue;
            }

            rgNodes.Add(new AstNode { Type = NodeType.Raw, RawText = sLine });
            i++;
        }
    }

    //手动扫描提取一行内的键值对 返回null表示该行不是合法kv
    private static ExtractKVResult? ExtractKeyValueFromLine(string sLine)
    {
        if (string.IsNullOrEmpty(sLine)) return null;

        int iFirstQuote = sLine.IndexOf('"');
        if (iFirstQuote < 0) return null;

        int iKeyEnd = FindClosingQuote(sLine, iFirstQuote + 1);
        if (iKeyEnd < 0) return null;

        string sKey = sLine.Substring(iFirstQuote + 1, iKeyEnd - iFirstQuote - 1);
        if (string.IsNullOrEmpty(sKey)) return null;

        int iValOpen = sLine.IndexOf('"', iKeyEnd + 1);
        if (iValOpen < 0) return null;
        string sSeparator = sLine.Substring(iKeyEnd + 1, iValOpen - iKeyEnd - 1);
        if (string.IsNullOrWhiteSpace(sSeparator)) sSeparator = "\t\t\t\t";//回退默认

        int iValEnd = FindClosingQuote(sLine, iValOpen + 1);
        if (iValEnd < 0) return null;

        string sValue = sLine.Substring(iValOpen + 1, iValEnd - iValOpen - 1);

        string? sComment = null;
        int iCommentIdx = sLine.IndexOf("//", iValEnd + 1, StringComparison.Ordinal);
        if (iCommentIdx >= 0)
        {
            sComment = sLine.Substring(iCommentIdx + 2).Trim();
            if (string.IsNullOrEmpty(sComment)) sComment = null;
        }

        return new ExtractKVResult(sKey, sValue, sSeparator, sComment);
    }

    private sealed class ExtractKVResult
    {
        public string Key { get; }
        public string Value { get; }
        public string Separator { get; }
        public string? Comment { get; }
        public ExtractKVResult(string sKey, string sValue, string sSeparator, string? sComment)
        {
            Key = sKey;
            Value = sValue;
            Separator = sSeparator;
            Comment = sComment;
        }
    }

    //找下一个未转义的引号 返回索引 未找到返回-1
    private static int FindClosingQuote(string sLine, int iStart)
    {
        for (int i = iStart; i < sLine.Length; i++)
        {
            if (sLine[i] == '"' && (i == 0 || sLine[i - 1] != '\\'))
                return i;
        }
        return -1;
    }

    private static bool TryFindCommentedBlockRange(string[] rgLines, int iStart, int iBlockEnd, out int iCommentEnd)
    {
        iCommentEnd = iStart;
        string sFirstLine = rgLines[iStart].TrimStart();
        bool bStartsWithBraceOnSameLine = sFirstLine.Contains("{");
        bool bNextLineHasBrace = (iStart + 1 <= iBlockEnd &&
                                 rgLines[iStart + 1].TrimStart().StartsWith("//") &&
                                 rgLines[iStart + 1].Contains('{'));

        if (!bStartsWithBraceOnSameLine && !bNextLineHasBrace)
            return false;

        int iDepth = bStartsWithBraceOnSameLine ? 1 : 0;
        int iStartSearch = iStart + 1;

        for (int k = iStartSearch; k <= iBlockEnd; k++)
        {
            string sNext = rgLines[k].TrimStart();
            if (sNext.StartsWith("//") && sNext.Contains('{')) iDepth++;
            else if (sNext.StartsWith("//") && sNext.Contains('}'))
            {
                iDepth--;
                if (iDepth == 0)
                {
                    iCommentEnd = k;
                    return true;
                }
            }
        }
        return false;
    }

    #endregion
    #region 数据填充

    private static void FillTreeWithScript(AstNode anNode, Dictionary<string, string> mpCurrentMap,
                                           Dictionary<string, string> mpScriptBlocks, string sCurrentScriptText,
                                           HashSet<string> hsMissingKeys)
    {
        try
        {
            if (anNode.Type == NodeType.Root || anNode.Type == NodeType.Block)
            {
                Dictionary<string, string> mpChildMap = mpCurrentMap;
                string sChildScriptText = sCurrentScriptText;

                if (anNode.Type == NodeType.Block && anNode.Name != "WeaponData")
                {
                    if (mpScriptBlocks.TryGetValue(anNode.Name!, out string? sBlockContent) && !string.IsNullOrEmpty(sBlockContent))
                    {
                        mpChildMap = ParseKeyValueMap(sBlockContent);
                        sChildScriptText = sBlockContent;
                    }
                    else
                    {
                        mpChildMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        sChildScriptText = string.Empty;
                    }
                }

                foreach (var anChild in anNode.Children)
                    FillTreeWithScript(anChild, mpChildMap, mpScriptBlocks, sChildScriptText, hsMissingKeys);

                if (anNode.Type == NodeType.Block && anNode.Name == "WeaponData")
                {
                    var rgExtraChildren = new List<AstNode>();
                    foreach (var sKey in hsMissingKeys.ToList())
                    {
                        if (mpCurrentMap.TryGetValue(sKey, out string? sVal) && !string.IsNullOrEmpty(sVal))
                        {
                            rgExtraChildren.Add(new AstNode
                            {
                                Type = NodeType.KeyValue,
                                Indent = "\t",
                                Name = sKey,
                                Value = sVal,
                                Separator = "\t\t\t\t"
                            });
                            hsMissingKeys.Remove(sKey);
                        }
                    }
                    if (rgExtraChildren.Count > 0)
                        LogService.Info($"FillTreeWithScript: {rgExtraChildren.Count} extra keys appended to WeaponData");
                    anNode.Children.AddRange(rgExtraChildren);
                }
            }
            else if (anNode.Type == NodeType.CommentedBlock &&
                     mpScriptBlocks.TryGetValue(anNode.Name!, out string? sCbBlockContent) &&
                     !string.IsNullOrEmpty(sCbBlockContent))
            {
                //解析脚本块内容 提取键值对和子块
                string[] rgBlockLines = sCbBlockContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                var rgTempChildren = new List<AstNode>();
                if (rgBlockLines.Length > 0)
                {
                    ParseLines(rgBlockLines, 0, rgBlockLines.Length, rgTempChildren);
                }

                var mpScriptKeyValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var rgScriptSubBlocks = new List<AstNode>();
                foreach (var anChild in rgTempChildren)
                {
                    if (anChild.Type == NodeType.KeyValue && !string.IsNullOrEmpty(anChild.Value))
                        mpScriptKeyValues[anChild.Name!] = anChild.Value;
                    else if (anChild.Type == NodeType.Block)
                        rgScriptSubBlocks.Add(anChild);
                }

                if (mpScriptKeyValues.Count > 0 || rgScriptSubBlocks.Count > 0)
                {
                    anNode.Type = NodeType.Block;
                    anNode.HeaderLines = new List<string>
                    {
                        $"{anNode.Indent}{anNode.Name}",
                        $"{anNode.Indent}{{"
                    };

                    var rgTemplateChildren = new List<AstNode>(anNode.Children);//保存模板中已有的注释子节点用于保留分隔符等信息
                    anNode.Children.Clear();//清空原有注释子节点 用脚本中的实际键值对和子块重建

                    //先填充模板中已有的键值对 保留模板的分隔符和注释
                    foreach (var anTChild in rgTemplateChildren)
                    {
                        if ((anTChild.Type == NodeType.CommentedKeyValue || anTChild.Type == NodeType.KeyValue)
                            && anTChild.Name != null
                            && mpScriptKeyValues.TryGetValue(anTChild.Name, out string? sVal)
                            && !string.IsNullOrEmpty(sVal))
                        {
                            anNode.Children.Add(new AstNode
                            {
                                Type = NodeType.KeyValue,
                                Indent = anTChild.Indent,
                                Name = anTChild.Name,
                                Value = sVal,
                                Separator = anTChild.Separator,
                                Comment = anTChild.Comment
                            });
                            mpScriptKeyValues.Remove(anTChild.Name);
                            hsMissingKeys.Remove(anTChild.Name);
                        }
                    }

                    //追加模板中没有但脚本中存在的键值对
                    foreach (var kvp in mpScriptKeyValues)
                    {
                        anNode.Children.Add(new AstNode
                        {
                            Type = NodeType.KeyValue,
                            Indent = anNode.Indent + "\t",
                            Name = kvp.Key,
                            Value = kvp.Value,
                            Separator = "\t\t\t\t"
                        });
                        hsMissingKeys.Remove(kvp.Key);
                    }

                    //追加脚本中的子块 如ViewSlideRecoil
                    foreach (var anSubBlock in rgScriptSubBlocks)
                    {
                        anNode.Children.Add(anSubBlock);
                        RemoveSubBlockKeysFromMissing(anSubBlock, hsMissingKeys);
                    }
                }
            }
            else if (anNode.Type == NodeType.CommentedKeyValue || anNode.Type == NodeType.KeyValue)
            {
                if (mpCurrentMap.TryGetValue(anNode.Name!, out string? sScriptVal) && !string.IsNullOrEmpty(sScriptVal))
                {
                    anNode.Value = sScriptVal;//注释键在脚本中有值 激活为普通键 重新计算分隔符以匹配模板对齐宽度
                    if (anNode.Type == NodeType.CommentedKeyValue)
                    {
                        anNode.Type = NodeType.KeyValue;
                    }

                    string? sScriptComment = GetLineCommentFromScript(sCurrentScriptText, anNode.Name!);
                    anNode.Comment = sScriptComment ?? anNode.Comment;

                    hsMissingKeys.Remove(anNode.Name!);
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, $"FillTreeWithScript: node={anNode.Type}, name={anNode.Name}");
        }
    }

    //递归移除子块中所有键值对的键名 防止被当作missingKey追加到WeaponData顶层
    private static void RemoveSubBlockKeysFromMissing(AstNode anBlock, HashSet<string> hsMissingKeys)
    {
        foreach (var anChild in anBlock.Children)
        {
            if (anChild.Type == NodeType.KeyValue && anChild.Name != null)
                hsMissingKeys.Remove(anChild.Name);
            else if (anChild.Type == NodeType.Block)
                RemoveSubBlockKeysFromMissing(anChild, hsMissingKeys);
        }
    }

    #endregion
    #region 序列化

    private static void RenderTree(AstNode anNode, StringBuilder sb, RenderOptions roOpts, RenderState rsState)//this shit was heavily LLM assisted, happy debugging
    {
        switch (anNode.Type)
        {
            case NodeType.Root:
                foreach (var anChild in anNode.Children) RenderTree(anChild, sb, roOpts, rsState);
                break;
            case NodeType.Blank:
                if (roOpts.bCompressBlankLines && rsState.bLastWasBlank) break;//压缩连续空行
                rsState.bLastWasBlank = true;
                if (roOpts.bCompressBlankLines)
                    sb.AppendLine();
                else
                    sb.AppendLine(anNode.RawText);//full模式保留原始空行内容 如带缩进的空行
                break;
            case NodeType.Raw:
                rsState.bLastWasBlank = false;
                if (roOpts.bSkipSeparators && IsSeparatorComment(anNode.RawText))
                    break;
                sb.AppendLine(anNode.RawText);
                break;
            case NodeType.Block:
                if (roOpts.bSkipEmptyValues && !BlockHasContent(anNode)) break;//空块跳过
                rsState.bLastWasBlank = false;
                foreach (var sHeader in anNode.HeaderLines)
                    sb.AppendLine(sHeader);
                foreach (var anChild in anNode.Children) RenderTree(anChild, sb, roOpts, rsState);
                string sCloseIndent = (anNode.Name == "WeaponData") ? "" : anNode.Indent;//最外层WeaponData块闭合顶格 子块保留缩进
                sb.AppendLine($"{sCloseIndent}}}");
                break;
            case NodeType.KeyValue:
                if (roOpts.bSkipEmptyValues && string.IsNullOrEmpty(anNode.Value)) break;
                rsState.bLastWasBlank = false;
                string sCommentStr = string.IsNullOrEmpty(anNode.Comment) ? "" : $" //{anNode.Comment}";
                sb.AppendLine($"{anNode.Indent}\"{anNode.Name}\"{anNode.Separator}\"{anNode.Value}\"{sCommentStr}");
                break;
            case NodeType.CommentedBlock:
                if (roOpts.bSkipEmptyValues && !anNode.Children.Any(anC => anC.Type == NodeType.KeyValue || anC.Type == NodeType.Block))//无激活内容跳过
                    break;
                rsState.bLastWasBlank = false;
                foreach (var sHeader in anNode.HeaderLines)
                    sb.AppendLine(sHeader);
                sb.AppendLine($"{anNode.Indent}//{{");
                foreach (var anChild in anNode.Children) RenderTree(anChild, sb, roOpts, rsState);
                sb.AppendLine($"{anNode.Indent}//}}");
                break;
            case NodeType.CommentedKeyValue:
                if (roOpts.bSkipEmptyValues && string.IsNullOrEmpty(anNode.Value)) break;//空值注释键跳过
                rsState.bLastWasBlank = false;
                string sCkvCommentStr = string.IsNullOrEmpty(anNode.Comment) ? "" : $" //{anNode.Comment}";
                sb.AppendLine($"{anNode.Indent}// \"{anNode.Name}\"{anNode.Separator}\"{anNode.Value}\"{sCkvCommentStr}");
                break;
        }
    }

    //递归检查块是否包含任何有效内容
    private static bool BlockHasContent(AstNode anBlock)
    {
        foreach (var anChild in anBlock.Children)
        {
            switch (anChild.Type)
            {
                case NodeType.KeyValue when !string.IsNullOrEmpty(anChild.Value):
                case NodeType.Block when BlockHasContent(anChild):
                    return true;
                case NodeType.Raw when !string.IsNullOrWhiteSpace(anChild.RawText) && !anChild.RawText.TrimStart().StartsWith("//"):
                    return true;
            }
        }
        return false;
    }

    #endregion
    #region 辅助

    //判断是否为仅由符号组成的装饰性分隔注释 如////////////// 或//--- 或//****
    private static bool IsSeparatorComment(string sLine)
    {
        string sTrimmed = sLine.TrimStart();
        if (!sTrimmed.StartsWith("//")) return false;
        string sBody = sTrimmed.Substring(2).Trim();//去掉//前缀和空白
        if (string.IsNullOrEmpty(sBody)) return false;
        foreach (char c in sBody) //如果剩余字符全部由 / * - = # _ . 空格和tab组成 视为分隔注释
        {
            if (c != '/' && c != '*' && c != '-' && c != '=' && c != '#' && c != '_' && c != '.' && c != ' ' && c != '\t')
                return false;
        }
        return true;
    }

    private static string ExtractIndent(string sLine)
    {
        int iIdx = sLine.Length - sLine.TrimStart().Length;
        return iIdx > 0 ? sLine[..iIdx] : "\t";
    }

    //从脚本全文中查找指定key所在行的行尾注释 用于FillTreeWithScript激活键值对时补注释
    private static string? GetLineCommentFromScript(string sText, string sKey)
    {
        if (string.IsNullOrEmpty(sText)) return null;
        foreach (var sRawLine in sText.Split('\n'))
        {
            string sLine = sRawLine.Trim();
            if (sLine.StartsWith("//")) continue;
            if (!sLine.Contains($"\"{sKey}\"")) continue;
            bool bInQuote = false;
            for (int i = 0; i < sLine.Length - 1; i++)
            {
                if (sLine[i] == '"' && (i == 0 || sLine[i - 1] != '\\')) bInQuote = !bInQuote;
                if (!bInQuote && sLine[i] == '/' && sLine[i + 1] == '/')
                    return sLine.Substring(i + 2).Trim();
            }
            return null;
        }
        return null;
    }

    private static string CleanBlockName(string sRaw)
    {
        if (string.IsNullOrEmpty(sRaw)) return sRaw;
        int iCommentIdx = sRaw.IndexOf("//", StringComparison.Ordinal);
        if (iCommentIdx >= 0) sRaw = sRaw[..iCommentIdx];
        sRaw = Regex.Replace(sRaw, @"[\u200B-\u200D\uFEFF\u00A0]", "");//sb！！！
        return sRaw.Trim();
    }

    //从文本中解析键值对映射 复用ParseLines确保与模板解析使用相同的kv提取逻辑 递归穿透子块
    private static Dictionary<string, string> ParseKeyValueMap(string sText)
    {
        var mpMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(sText)) return mpMap;

        try
        {
            var rgLines = sText.Split('\n');
            var rgNodes = new List<AstNode>();
            ParseLines(rgLines, 0, rgLines.Length, rgNodes);
            CollectKeyValues(rgNodes, mpMap);
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "ParseKeyValueMap");
        }
        return mpMap;
    }

    //递归收集键值对 穿透子块
    private static void CollectKeyValues(List<AstNode> rgNodes, Dictionary<string, string> mpMap)
    {
        foreach (var anNode in rgNodes)
        {
            if (anNode.Type == NodeType.KeyValue && anNode.Name != null && !string.IsNullOrEmpty(anNode.Value))
                mpMap[anNode.Name] = anNode.Value;
            else if (anNode.Type == NodeType.Block)
                CollectKeyValues(anNode.Children, mpMap);
        }
    }

    private static Dictionary<string, string> ParseTopLevelMap(string sContent)
    {
        try
        {
            int iWdIdx = sContent.IndexOf("WeaponData", StringComparison.Ordinal);
            if (iWdIdx < 0) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int iOuterOpen = sContent.IndexOf('{', iWdIdx);
            if (iOuterOpen < 0) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int iOuterClose = FindMatchingBrace(sContent, iOuterOpen);
            if (iOuterClose < 0 || iOuterOpen + 1 >= iOuterClose) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string sInner = sContent.Substring(iOuterOpen + 1, iOuterClose - iOuterOpen - 1);
            return ParseKeyValueMap(sInner);
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "ParseTopLevelMap");
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static Dictionary<string, string> ExtractAllBlocks(string sContent)
    {
        var mpBlocks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            int iPos = 0;
            while (iPos < sContent.Length)
            {
                int iBrace = sContent.IndexOf('{', iPos);
                if (iBrace < 0) break;

                //从{向前找非空白字符确定块名结束位置 再向前找空白或换行确定块名起始
                int iNameEnd = iBrace - 1;
                while (iNameEnd >= 0 && char.IsWhiteSpace(sContent[iNameEnd])) iNameEnd--;
                if (iNameEnd < 0) { iPos = iBrace + 1; continue; }

                int iNameStart = iNameEnd;
                while (iNameStart >= 0 && !char.IsWhiteSpace(sContent[iNameStart]) && sContent[iNameStart] != '\n')
                    iNameStart--;
                iNameStart++;
                if (iNameStart > iNameEnd) { iPos = iBrace + 1; continue; }

                string sBlockName = sContent[iNameStart..(iNameEnd + 1)];
                sBlockName = CleanBlockName(sBlockName);

                if (!string.IsNullOrEmpty(sBlockName) && !sBlockName.StartsWith("//"))
                {
                    int iClose = FindMatchingBrace(sContent, iBrace);
                    if (iClose >= 0)
                    {
                        int iLen = iClose - iBrace - 1;
                        if (iLen < 0) iLen = 0;
                        string sBlockContent = sContent.Substring(iBrace + 1, iLen);
                        if (!mpBlocks.ContainsKey(sBlockName))
                            mpBlocks[sBlockName] = sBlockContent;
                    }
                }
                iPos = iBrace + 1;
            }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "ExtractAllBlocks");
        }
        return mpBlocks;
    }

    private static int FindMatchingBrace(string sContent, int iStart)
    {
        int iDepth = 0;
        bool bInString = false;
        for (int i = iStart; i < sContent.Length; i++)
        {
            if (sContent[i] == '"' && (i == 0 || sContent[i - 1] != '\\'))
                bInString = !bInString;

            if (!bInString)
            {
                if (sContent[i] == '{') iDepth++;
                else if (sContent[i] == '}')
                {
                    iDepth--;
                    if (iDepth == 0) return i;
                }
            }
        }
        return -1;
    }

    private static bool IsBlockStart(string[] rgLines, int i, out string sBlockName, out int iBlockEndLine)
    {
        sBlockName = "";
        iBlockEndLine = i;
        if (i < 0 || i >= rgLines.Length) return false;
        string sLine = rgLines[i]?.TrimStart() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(sLine) || sLine.StartsWith("//")) return false;

        int iBraceIdx = sLine.IndexOf('{');
        if (iBraceIdx >= 0)
        {
            string sBeforeBrace = sLine[..iBraceIdx].Trim();
            if (!string.IsNullOrEmpty(sBeforeBrace))
            {
                sBlockName = CleanBlockName(sBeforeBrace);
                if (FindMatchingBrace(rgLines, i, iBraceIdx, out iBlockEndLine)) return true;
            }
            return false;
        }

        //如果当前行没有{ 跳过空行后找到以{开头的行 说明块名独占一行 匹配到整个块结束
        int iNext = i + 1;
        while (iNext < rgLines.Length && string.IsNullOrWhiteSpace(rgLines[iNext]))
            iNext++;
        if (iNext < rgLines.Length && (rgLines[iNext]?.TrimStart() ?? "").StartsWith("{"))
        {
            string sPotentialName = sLine.TrimEnd();
            if (!string.IsNullOrEmpty(sPotentialName) && !sPotentialName.StartsWith("//"))
            {
                sBlockName = CleanBlockName(sPotentialName);
                if (FindMatchingBrace(rgLines, iNext, 0, out int iClosing))
                {
                    iBlockEndLine = iClosing;
                    return true;
                }
            }
        }
        return false;
    }

    private static bool FindMatchingBrace(string[] rgLines, int iStartLine, int iStartCol, out int iEndLine)
    {
        int iDepth = 0;
        bool bInString = false;
        for (int j = iStartLine; j < rgLines.Length; j++)
        {
            string sL = rgLines[j] ?? string.Empty;
            int k = (j == iStartLine) ? iStartCol : 0;
            for (; k < sL.Length; k++)
            {
                if (sL[k] == '"' && (k == 0 || sL[k - 1] != '\\')) bInString = !bInString; //追踪字符串状态 防止字符串内的{或}干扰深度计数
                if (!bInString)
                {
                    if (sL[k] == '{') iDepth++;
                    else if (sL[k] == '}')
                    {
                        iDepth--;
                        if (iDepth == 0) { iEndLine = j; return true; }
                    }
                }
            }
        }
        iEndLine = iStartLine;
        return false;
    }

    #endregion
    #region 调试

    //把ast dump和脚本解析信息追加到logform
    private static void DumpDebugInfo(string sWeaponName, string sScript, string[] rgTemplateLines, string sResult, List<string> rgLog)
    {
        if (!LogService.Enabled) return;

        rgLog.Add($"--- 调试: {sWeaponName} ---");

        var mpScriptMap = ParseTopLevelMap(sScript);
        var mpScriptBlocks = ExtractAllBlocks(sScript);
        rgLog.Add($"脚本顶层键值对: {mpScriptMap.Count} 个");
        int iShown = 0;
        foreach (var kvp in mpScriptMap)
        {
            if (iShown >= 15) { rgLog.Add($"  ... 共 {mpScriptMap.Count} 个"); break; }
            rgLog.Add($"  \"{kvp.Key}\" = \"{kvp.Value}\"");
            iShown++;
        }
        rgLog.Add($"脚本子块: {mpScriptBlocks.Count} 个");
        foreach (var sBk in mpScriptBlocks.Keys)
            rgLog.Add($"  {sBk} ({mpScriptBlocks[sBk].Length} 字符)");

        var anTemplateTree = ParseTemplateToTree(rgTemplateLines);
        rgLog.Add($"模板AST节点数: {CountNodes(anTemplateTree)}");
        rgLog.Add(anTemplateTree.ToString());

        if (!string.IsNullOrEmpty(sResult))
        {
            var rgResultLines = sResult.Split('\n');
            var anResultTree = ParseTemplateToTree(rgResultLines);
            rgLog.Add($"转换后AST节点数: {CountNodes(anResultTree)}");
            rgLog.Add(anResultTree.ToString());
        }

        rgLog.Add($"--- 调试结束: {sWeaponName} ---");
    }

    private static int CountNodes(AstNode anNode)
    {
        int iN = 1;
        foreach (var anChild in anNode.Children)
            iN += CountNodes(anChild);
        return iN;
    }
    #endregion
}