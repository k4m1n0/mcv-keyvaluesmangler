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
    public static string ConvertAll(string scriptsDir, bool simpleMode)
    {
        string[] templateLines = ReadEmbeddedTemplate();
        if (templateLines == null || templateLines.Length == 0)
            return "嵌入的模板文件未找到";

        var files = Directory.GetFiles(scriptsDir, "weapon_*.txt");
        var log = new List<string>();
        int success = 0, failed = 0;

        string externalPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preset_file.txt");
        if (File.Exists(externalPath) && templateLines.Any(l => l.Contains("WeaponData")))
            log.Add("使用外置模板: " + externalPath);
        else if (File.Exists(externalPath))
            log.Add("外置模板校验失败（缺少WeaponData块），已回退到内嵌默认模板");

        log.Add("脚本 -> 模板转换");
        log.Add($"目录: {scriptsDir}");
        log.Add($"共 {files.Length} 个文件");
        log.Add(new string('-', 50));

        var opts = simpleMode ? RenderOptions.Simple : RenderOptions.Full;

        for (int i = 0; i < files.Length; i++)
        {
            string path = files[i];
            string name = Path.GetFileName(path);
            try
            {
                string script = WeaponScriptService.ReadScriptFile(path);
                string result = ConvertSingle(script, templateLines, opts);
                DumpDebugInfo(name, script, templateLines, result, log);
                File.WriteAllText(path, result, new UTF8Encoding(false));
                success++;
                log.Add($"[{i + 1}/{files.Length}] {name}");
            }
            catch (Exception ex)
            {
                failed++;
                log.Add($"[{i + 1}/{files.Length}] 失败: {name} - {ex.Message}");
            }
        }

        log.Add(new string('-', 50));
        log.Add($"完成: 成功 {success}, 失败 {failed}, 总计 {files.Length}");
        return string.Join("\n", log);
    }

    private static string[] ReadEmbeddedTemplate()
    {
        try
        {
            string externalPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preset_file.txt");
            if (File.Exists(externalPath))
            {
                var lines = WeaponScriptService.ReadScriptFile(externalPath)
                               .Replace("\r\n", "\n")
                               .Replace('\r', '\n')
                               .Split('\n');
                if (lines.Any(l => l.Contains("WeaponData")) && lines.Any(l => l.Contains("{")) && lines.Any(l => l.Contains("}")))
                    return lines;
            }

            using var stream = System.Reflection.Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("WeaponDamageCalc.Tools.preset_file.txt");
            if (stream == null) return Array.Empty<string>();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd()
                         .Replace("\r\n", "\n")
                         .Replace('\r', '\n')
                         .Split('\n');
        }
        catch { return Array.Empty<string>(); }
    }

    #endregion
    #region 渲染配置

    private struct RenderOptions
    {
        public bool SkipEmptyValues;
        public bool SkipSeparators;
        public bool CompressBlankLines;

        public static readonly RenderOptions Full = new()
        {
            SkipEmptyValues = false,
            SkipSeparators = false,
            CompressBlankLines = false
        };

        public static readonly RenderOptions Simple = new()
        {
            SkipEmptyValues = true,
            SkipSeparators = true,
            CompressBlankLines = true
        };
    }

    #endregion
    #region 转换解析

    private static string ConvertSingle(string script, string[] templateLines, RenderOptions opts)
    {
        var scriptMap = ParseTopLevelMap(script);
        var scriptBlocks = ExtractAllBlocks(script);

        var templateTree = ParseTemplateToTree(templateLines);
        if (templateTree == null || templateTree.Children.Count == 0)
            return string.Join("\n", templateLines);

        var missingKeys = new HashSet<string>(scriptMap.Keys, StringComparer.OrdinalIgnoreCase);
        FillTreeWithScript(templateTree, scriptMap, scriptBlocks, script, missingKeys);

        var result = new StringBuilder();
        var state = new RenderState();
        RenderTree(templateTree, result, opts, state);
        string output = result.ToString();
        output = Regex.Replace(output, @"^\s*//\s*[\{\}]\s*$", "", RegexOptions.Multiline);
        output = output.TrimStart('\r', '\n');
        if (!output.EndsWith('\n'))
            output += "\n";
        return output;
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

        private string ToString(int depth)
        {
            var sb = new StringBuilder();
            sb.Append(new string(' ', depth * 2));
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
                    string preview = RawText.Length > 60 ? RawText[..60] + "..." : RawText;
                    sb.AppendLine($"Raw \"{preview.Replace("\n", "\\n").Replace("\r", "\\r")}\"");
                    break;
                }
            }
            foreach (var child in Children)
                sb.Append(child.ToString(depth + 1));
            return sb.ToString();
        }
    }

    private class RenderState
    {
        public bool LastWasBlank;
    }

    private static AstNode ParseTemplateToTree(string[] lines)
    {
        var root = new AstNode { Type = NodeType.Root };
        int i = 0;

        while (i < lines.Length)
        {
            if (IsBlockStart(lines, i, out string bn, out int end) &&
                bn.Equals("WeaponData", StringComparison.OrdinalIgnoreCase))
            {
                var wdNode = new AstNode
                {
                    Type = NodeType.Block,
                    Indent = ExtractIndent(lines[i]),
                    Name = "WeaponData"
                };
                wdNode.HeaderLines.Add(lines[i]);

                root.Children.Add(wdNode);
                ParseLines(lines, i + 1, end, wdNode.Children);
                i = end + 1;
                continue;
            }
            root.Children.Add(new AstNode { Type = NodeType.Raw, RawText = lines[i] });
            i++;
        }
        return root;
    }

    //统一逐行解析 所有解析都走这条路
    private static void ParseLines(string[] lines, int start, int end, List<AstNode> nodes)
    {
        int i = start;
        while (i < end && i < lines.Length)
        {
            string line = lines[i] ?? string.Empty;
            string trimmed = line.TrimStart();

            if (string.IsNullOrWhiteSpace(line))
            {
                nodes.Add(new AstNode { Type = NodeType.Blank, RawText = line });
                i++; continue;
            }

            if (IsBlockStart(lines, i, out string subName, out int subEnd))
            {
                var subNode = new AstNode
                {
                    Type = NodeType.Block,
                    Indent = ExtractIndent(line),
                    Name = subName
                };

                int headerEnd = i;
                for (int h = i; h <= subEnd; h++)
                {
                    subNode.HeaderLines.Add(lines[h]);
                    if (lines[h].Contains('{'))
                    {
                        headerEnd = h;
                        break;
                    }
                }

                nodes.Add(subNode);
                ParseLines(lines, headerEnd + 1, subEnd, subNode.Children);
                i = subEnd + 1;
                continue;
            }

            if (trimmed.StartsWith("//") && !trimmed.Contains('"'))
            {
                string afterSlash = trimmed.Substring(2).TrimStart();
                var parts = afterSlash.Split(new[] { ' ', '\t', '{', '}' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0)
                {
                    string potentialName = parts[0];
                    if (TryFindCommentedBlockRange(lines, i, end, out int commentEnd))
                    {
                        var cNode = new AstNode
                        {
                            Type = NodeType.CommentedBlock,
                            Indent = ExtractIndent(line),
                            Name = potentialName
                        };
                        cNode.HeaderLines.Add(lines[i]);

                        //解析注释块内部 去掉行首//前缀
                        for (int k = i + 1; k < commentEnd; k++)
                        {
                            string innerLine = lines[k];
                            string innerTrimmed = innerLine.TrimStart();
                            if (string.IsNullOrWhiteSpace(innerLine))
                            {
                                cNode.Children.Add(new AstNode { Type = NodeType.Blank, RawText = innerLine });
                                continue;
                            }
                            string uncommented = innerTrimmed.StartsWith("//") ? innerTrimmed.Substring(2).TrimStart() : innerTrimmed;
                            ExtractKVResult? kvr = ExtractKeyValueFromLine(uncommented);
                            if (kvr != null)
                            {
                                cNode.Children.Add(new AstNode
                                {
                                    Type = NodeType.CommentedKeyValue,
                                    Indent = ExtractIndent(innerLine),
                                    Name = kvr.Key,
                                    Value = kvr.Value,
                                    Separator = kvr.Separator,
                                    Comment = kvr.Comment
                                });
                            }
                            else
                            {
                                cNode.Children.Add(new AstNode { Type = NodeType.Raw, RawText = innerLine });//无法识别的行当raw处理
                            }
                        }

                        nodes.Add(cNode);
                        i = commentEnd + 1;
                        continue;
                    }
                }
                //不是注释块 当作普通注释行
                nodes.Add(new AstNode { Type = NodeType.Raw, RawText = line });
                i++; continue;
            }

            if (trimmed.StartsWith("//") && trimmed.Contains('"'))
            {
                string afterSlash = trimmed.Substring(trimmed.IndexOf('"'));
                ExtractKVResult? kvr = ExtractKeyValueFromLine(afterSlash);
                if (kvr != null)
                {
                    nodes.Add(new AstNode
                    {
                        Type = NodeType.CommentedKeyValue,
                        Indent = ExtractIndent(line),
                        Name = kvr.Key,
                        Value = kvr.Value,
                        Separator = kvr.Separator,
                        Comment = kvr.Comment
                    });
                    i++; continue;
                }
                nodes.Add(new AstNode { Type = NodeType.Raw, RawText = line });
                i++; continue;
            }

            if (trimmed.StartsWith("\""))
            {
                ExtractKVResult? kvr = ExtractKeyValueFromLine(trimmed);
                if (kvr != null)
                {
                    nodes.Add(new AstNode
                    {
                        Type = NodeType.KeyValue,
                        Indent = ExtractIndent(line),
                        Name = kvr.Key,
                        Value = kvr.Value,
                        Separator = kvr.Separator,
                        Comment = kvr.Comment
                    });
                    i++; continue;
                }
                nodes.Add(new AstNode { Type = NodeType.Raw, RawText = line });
                i++; continue;
            }

            nodes.Add(new AstNode { Type = NodeType.Raw, RawText = line });
            i++;
        }
    }

    //手动扫描提取一行内的键值对 返回null表示该行不是合法kv
    private static ExtractKVResult? ExtractKeyValueFromLine(string line)
    {
        if (string.IsNullOrEmpty(line)) return null;

        int firstQuote = line.IndexOf('"');
        if (firstQuote < 0) return null;

        int keyEnd = FindClosingQuote(line, firstQuote + 1);
        if (keyEnd < 0) return null;

        string key = line.Substring(firstQuote + 1, keyEnd - firstQuote - 1);
        if (string.IsNullOrEmpty(key)) return null;

        int valOpen = line.IndexOf('"', keyEnd + 1);
        if (valOpen < 0) return null;
        string separator = line.Substring(keyEnd + 1, valOpen - keyEnd - 1);
        if (string.IsNullOrWhiteSpace(separator)) separator = "\t\t\t\t";//回退默认

        int valEnd = FindClosingQuote(line, valOpen + 1);
        if (valEnd < 0) return null;

        string value = line.Substring(valOpen + 1, valEnd - valOpen - 1);

        string? comment = null;
        int commentIdx = line.IndexOf("//", valEnd + 1, StringComparison.Ordinal);
        if (commentIdx >= 0)
        {
            comment = line.Substring(commentIdx + 2).Trim();
            if (string.IsNullOrEmpty(comment)) comment = null;
        }

        return new ExtractKVResult(key, value, separator, comment);
    }

    private sealed class ExtractKVResult
    {
        public string Key { get; }
        public string Value { get; }
        public string Separator { get; }
        public string? Comment { get; }
        public ExtractKVResult(string key, string value, string separator, string? comment)
        {
            Key = key;
            Value = value;
            Separator = separator;
            Comment = comment;
        }
    }

    //找下一个未转义的引号 返回索引 未找到返回-1
    private static int FindClosingQuote(string line, int start)
    {
        for (int i = start; i < line.Length; i++)
        {
            if (line[i] == '"' && (i == 0 || line[i - 1] != '\\'))
                return i;
        }
        return -1;
    }

    private static bool TryFindCommentedBlockRange(string[] lines, int start, int blockEnd, out int commentEnd)
    {
        commentEnd = start;
        string firstLine = lines[start].TrimStart();
        bool startsWithBraceOnSameLine = firstLine.Contains("{");
        bool nextLineHasBrace = (start + 1 <= blockEnd &&
                                 lines[start + 1].TrimStart().StartsWith("//") &&
                                 lines[start + 1].Contains('{'));

        if (!startsWithBraceOnSameLine && !nextLineHasBrace)
            return false;

        int depth = startsWithBraceOnSameLine ? 1 : 0;
        int startSearch = start + 1;

        for (int k = startSearch; k <= blockEnd; k++)
        {
            string next = lines[k].TrimStart();
            if (next.StartsWith("//") && next.Contains('{')) depth++;
            else if (next.StartsWith("//") && next.Contains('}'))
            {
                depth--;
                if (depth == 0)
                {
                    commentEnd = k;
                    return true;
                }
            }
        }
        return false;
    }

    #endregion
    #region 数据填充

    private static void FillTreeWithScript(AstNode node, Dictionary<string, string> currentMap,
                                           Dictionary<string, string> scriptBlocks, string currentScriptText,
                                           HashSet<string> missingKeys)
    {
        if (node.Type == NodeType.Root || node.Type == NodeType.Block)
        {
            Dictionary<string, string> childMap = currentMap;
            string childScriptText = currentScriptText;

            if (node.Type == NodeType.Block && node.Name != "WeaponData")
            {
                if (scriptBlocks.TryGetValue(node.Name!, out string? blockContent) && !string.IsNullOrEmpty(blockContent))
                {
                    childMap = ParseKeyValueMap(blockContent);
                    childScriptText = blockContent;
                }
                else
                {
                    childMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    childScriptText = string.Empty;
                }
            }

            foreach (var child in node.Children)
                FillTreeWithScript(child, childMap, scriptBlocks, childScriptText, missingKeys);

            if (node.Type == NodeType.Block && node.Name == "WeaponData")
            {
                var extraChildren = new List<AstNode>();
                foreach (var key in missingKeys.ToList())
                {
                    if (currentMap.TryGetValue(key, out string? val) && !string.IsNullOrEmpty(val))
                    {
                        extraChildren.Add(new AstNode
                        {
                            Type = NodeType.KeyValue,
                            Indent = "\t",
                            Name = key,
                            Value = val,
                            Separator = "\t\t\t\t"
                        });
                        missingKeys.Remove(key);
                    }
                }
                node.Children.AddRange(extraChildren);
            }
        }
        else if (node.Type == NodeType.CommentedBlock &&
                 scriptBlocks.TryGetValue(node.Name!, out string? cbBlockContent) &&
                 !string.IsNullOrEmpty(cbBlockContent))
        {
            //解析脚本块内容 提取键值对和子块
            string[] blockLines = cbBlockContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var tempChildren = new List<AstNode>();
            if (blockLines.Length > 0)
            {
                ParseLines(blockLines, 0, blockLines.Length, tempChildren);
            }

            var scriptKeyValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var scriptSubBlocks = new List<AstNode>();
            foreach (var child in tempChildren)
            {
                if (child.Type == NodeType.KeyValue && !string.IsNullOrEmpty(child.Value))
                    scriptKeyValues[child.Name!] = child.Value;
                else if (child.Type == NodeType.Block)
                    scriptSubBlocks.Add(child);
            }

            if (scriptKeyValues.Count > 0 || scriptSubBlocks.Count > 0)
            {
                node.Type = NodeType.Block;
                node.HeaderLines = new List<string>
                {
                    $"{node.Indent}{node.Name}",
                    $"{node.Indent}{{"
                };

                var templateChildren = new List<AstNode>(node.Children);//保存模板中已有的注释子节点用于保留分隔符等信息
                node.Children.Clear();//清空原有注释子节点 用脚本中的实际键值对和子块重建

                //先填充模板中已有的键值对 保留模板的分隔符和注释
                foreach (var tChild in templateChildren)
                {
                    if ((tChild.Type == NodeType.CommentedKeyValue || tChild.Type == NodeType.KeyValue)
                        && tChild.Name != null
                        && scriptKeyValues.TryGetValue(tChild.Name, out string? val)
                        && !string.IsNullOrEmpty(val))
                    {
                        node.Children.Add(new AstNode
                        {
                            Type = NodeType.KeyValue,
                            Indent = tChild.Indent,
                            Name = tChild.Name,
                            Value = val,
                            Separator = tChild.Separator,
                            Comment = tChild.Comment
                        });
                        scriptKeyValues.Remove(tChild.Name);
                        missingKeys.Remove(tChild.Name);
                    }
                }

                //追加模板中没有但脚本中存在的键值对
                foreach (var kvp in scriptKeyValues)
                {
                    node.Children.Add(new AstNode
                    {
                        Type = NodeType.KeyValue,
                        Indent = node.Indent + "\t",
                        Name = kvp.Key,
                        Value = kvp.Value,
                        Separator = "\t\t\t\t"
                    });
                    missingKeys.Remove(kvp.Key);
                }

                //追加脚本中的子块 如ViewSlideRecoil
                foreach (var subBlock in scriptSubBlocks)
                {
                    node.Children.Add(subBlock);
                    RemoveSubBlockKeysFromMissing(subBlock, missingKeys);
                }
            }
        }
        else if (node.Type == NodeType.CommentedKeyValue || node.Type == NodeType.KeyValue)
        {
            if (currentMap.TryGetValue(node.Name!, out string? scriptVal) && !string.IsNullOrEmpty(scriptVal))
            {
                node.Value = scriptVal;//注释键在脚本中有值 激活为普通键 重新计算分隔符以匹配模板对齐宽度
                if (node.Type == NodeType.CommentedKeyValue)
                {
                    node.Type = NodeType.KeyValue;
                }

                string? scriptComment = GetLineCommentFromScript(currentScriptText, node.Name!);
                node.Comment = scriptComment ?? node.Comment;

                missingKeys.Remove(node.Name!);
            }
        }
    }

    //递归移除子块中所有键值对的键名 防止被当作missingKey追加到WeaponData顶层
    private static void RemoveSubBlockKeysFromMissing(AstNode block, HashSet<string> missingKeys)
    {
        foreach (var child in block.Children)
        {
            if (child.Type == NodeType.KeyValue && child.Name != null)
                missingKeys.Remove(child.Name);
            else if (child.Type == NodeType.Block)
                RemoveSubBlockKeysFromMissing(child, missingKeys);
        }
    }

    #endregion
    #region 序列化

    private static void RenderTree(AstNode node, StringBuilder sb, RenderOptions opts, RenderState state)//this shit was heavily LLM assisted, happy debugging
    {
        switch (node.Type)
        {
            case NodeType.Root:
                foreach (var child in node.Children) RenderTree(child, sb, opts, state);
                break;
            case NodeType.Blank:
                if (opts.CompressBlankLines && state.LastWasBlank) break;//压缩连续空行
                state.LastWasBlank = true;
                if (opts.CompressBlankLines)
                    sb.AppendLine();
                else
                    sb.AppendLine(node.RawText);//full模式保留原始空行内容 如带缩进的空行
                break;
            case NodeType.Raw:
                state.LastWasBlank = false;
                if (opts.SkipSeparators && IsSeparatorComment(node.RawText))
                    break;
                sb.AppendLine(node.RawText);
                break;
            case NodeType.Block:
                if (opts.SkipEmptyValues && !BlockHasContent(node)) break;//空块跳过
                state.LastWasBlank = false;
                foreach (var header in node.HeaderLines)
                    sb.AppendLine(header);
                foreach (var child in node.Children) RenderTree(child, sb, opts, state);
                string closeIndent = (node.Name == "WeaponData") ? "" : node.Indent;//最外层WeaponData块闭合顶格 子块保留缩进
                sb.AppendLine($"{closeIndent}}}");
                break;
            case NodeType.KeyValue:
                if (opts.SkipEmptyValues && string.IsNullOrEmpty(node.Value)) break;
                state.LastWasBlank = false;
                string commentStr = string.IsNullOrEmpty(node.Comment) ? "" : $" //{node.Comment}";
                sb.AppendLine($"{node.Indent}\"{node.Name}\"{node.Separator}\"{node.Value}\"{commentStr}");
                break;
            case NodeType.CommentedBlock:
                if (opts.SkipEmptyValues && !node.Children.Any(c => c.Type == NodeType.KeyValue || c.Type == NodeType.Block))//无激活内容跳过
                    break;
                state.LastWasBlank = false;
                foreach (var header in node.HeaderLines)
                    sb.AppendLine(header);
                sb.AppendLine($"{node.Indent}//{{");
                foreach (var child in node.Children) RenderTree(child, sb, opts, state);
                sb.AppendLine($"{node.Indent}//}}");
                break;
            case NodeType.CommentedKeyValue:
                if (opts.SkipEmptyValues && string.IsNullOrEmpty(node.Value)) break;//空值注释键跳过
                state.LastWasBlank = false;
                string ckvCommentStr = string.IsNullOrEmpty(node.Comment) ? "" : $" //{node.Comment}";
                sb.AppendLine($"{node.Indent}// \"{node.Name}\"{node.Separator}\"{node.Value}\"{ckvCommentStr}");
                break;
        }
    }

    //递归检查块是否包含任何有效内容
    private static bool BlockHasContent(AstNode block)
    {
        foreach (var child in block.Children)
        {
            switch (child.Type)
            {
                case NodeType.KeyValue when !string.IsNullOrEmpty(child.Value):
                case NodeType.Block when BlockHasContent(child):
                    return true;
                case NodeType.Raw when !string.IsNullOrWhiteSpace(child.RawText) && !child.RawText.TrimStart().StartsWith("//"):
                    return true;
            }
        }
        return false;
    }

    #endregion
    #region 辅助

    //判断是否为仅由符号组成的装饰性分隔注释 如////////////// 或//--- 或//****
    private static bool IsSeparatorComment(string line)
    {
        string trimmed = line.TrimStart();
        if (!trimmed.StartsWith("//")) return false;
        string body = trimmed.Substring(2).Trim();//去掉//前缀和空白
        if (string.IsNullOrEmpty(body)) return false;
        foreach (char c in body) //如果剩余字符全部由 / * - = # _ . 空格和tab组成 视为分隔注释
        {
            if (c != '/' && c != '*' && c != '-' && c != '=' && c != '#' && c != '_' && c != '.' && c != ' ' && c != '\t')
                return false;
        }
        return true;
    }

    private static string ExtractIndent(string line)
    {
        int idx = line.Length - line.TrimStart().Length;
        return idx > 0 ? line[..idx] : "\t";
    }

    //从脚本全文中查找指定key所在行的行尾注释 用于FillTreeWithScript激活键值对时补注释
    private static string? GetLineCommentFromScript(string text, string key)
    {
        if (string.IsNullOrEmpty(text)) return null;
        foreach (var rawLine in text.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.StartsWith("//")) continue;
            if (!line.Contains($"\"{key}\"")) continue;
            bool inQuote = false;
            for (int i = 0; i < line.Length - 1; i++)
            {
                if (line[i] == '"' && (i == 0 || line[i - 1] != '\\')) inQuote = !inQuote;
                if (!inQuote && line[i] == '/' && line[i + 1] == '/')
                    return line.Substring(i + 2).Trim();
            }
            return null;
        }
        return null;
    }

    private static string CleanBlockName(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return raw;
        int c = raw.IndexOf("//", StringComparison.Ordinal);
        if (c >= 0) raw = raw[..c];
        raw = Regex.Replace(raw, @"[\u200B-\u200D\uFEFF\u00A0]", "");//sb！！！
        return raw.Trim();
    }

    //从文本中解析键值对映射 复用ParseLines确保与模板解析使用相同的kv提取逻辑 递归穿透子块
    private static Dictionary<string, string> ParseKeyValueMap(string text)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(text)) return map;

        var lines = text.Split('\n');
        var nodes = new List<AstNode>();
        ParseLines(lines, 0, lines.Length, nodes);
        CollectKeyValues(nodes, map);
        return map;
    }

    //递归收集键值对 穿透子块
    private static void CollectKeyValues(List<AstNode> nodes, Dictionary<string, string> map)
    {
        foreach (var node in nodes)
        {
            if (node.Type == NodeType.KeyValue && node.Name != null && !string.IsNullOrEmpty(node.Value))
                map[node.Name] = node.Value;
            else if (node.Type == NodeType.Block)
                CollectKeyValues(node.Children, map);
        }
    }

    private static Dictionary<string, string> ParseTopLevelMap(string content)
    {
        int wdIdx = content.IndexOf("WeaponData", StringComparison.Ordinal);
        if (wdIdx < 0) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int outerOpen = content.IndexOf('{', wdIdx);
        if (outerOpen < 0) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int outerClose = FindMatchingBrace(content, outerOpen);
        if (outerClose < 0 || outerOpen + 1 >= outerClose) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string inner = content.Substring(outerOpen + 1, outerClose - outerOpen - 1);
        return ParseKeyValueMap(inner);
    }

    private static Dictionary<string, string> ExtractAllBlocks(string content)
    {
        var blocks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int pos = 0;
        while (pos < content.Length)
        {
            int brace = content.IndexOf('{', pos);
            if (brace < 0) break;

            //从{向前找非空白字符确定块名结束位置 再向前找空白或换行确定块名起始
            int nameEnd = brace - 1;
            while (nameEnd >= 0 && char.IsWhiteSpace(content[nameEnd])) nameEnd--;
            if (nameEnd < 0) { pos = brace + 1; continue; }

            int nameStart = nameEnd;
            while (nameStart >= 0 && !char.IsWhiteSpace(content[nameStart]) && content[nameStart] != '\n')
                nameStart--;
            nameStart++;
            if (nameStart > nameEnd) { pos = brace + 1; continue; }

            string blockName = content[nameStart..(nameEnd + 1)];
            blockName = CleanBlockName(blockName);

            if (!string.IsNullOrEmpty(blockName) && !blockName.StartsWith("//"))
            {
                int close = FindMatchingBrace(content, brace);
                if (close >= 0)
                {
                    int len = close - brace - 1;
                    if (len < 0) len = 0;
                    string blockContent = content.Substring(brace + 1, len);
                    if (!blocks.ContainsKey(blockName))
                        blocks[blockName] = blockContent;
                }
            }
            pos = brace + 1;
        }
        return blocks;
    }

    private static int FindMatchingBrace(string content, int start)
    {
        int depth = 0;
        bool inString = false;
        for (int i = start; i < content.Length; i++)
        {
            if (content[i] == '"' && (i == 0 || content[i - 1] != '\\'))
                inString = !inString;

            if (!inString)
            {
                if (content[i] == '{') depth++;
                else if (content[i] == '}')
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
        }
        return -1;
    }

    private static bool IsBlockStart(string[] lines, int i, out string blockName, out int blockEndLine)
    {
        blockName = "";
        blockEndLine = i;
        if (i < 0 || i >= lines.Length) return false;
        string line = lines[i]?.TrimStart() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//")) return false;

        int braceIdx = line.IndexOf('{');
        if (braceIdx >= 0)
        {
            string beforeBrace = line[..braceIdx].Trim();
            if (!string.IsNullOrEmpty(beforeBrace))
            {
                blockName = CleanBlockName(beforeBrace);
                if (FindMatchingBrace(lines, i, braceIdx, out blockEndLine)) return true;
            }
            return false;
        }

        //如果当前行没有{ 跳过空行后找到以{开头的行 说明块名独占一行 匹配到整个块结束
        int next = i + 1;
        while (next < lines.Length && string.IsNullOrWhiteSpace(lines[next]))
            next++;
        if (next < lines.Length && (lines[next]?.TrimStart() ?? "").StartsWith("{"))
        {
            string potentialName = line.TrimEnd();
            if (!string.IsNullOrEmpty(potentialName) && !potentialName.StartsWith("//"))
            {
                blockName = CleanBlockName(potentialName);
                if (FindMatchingBrace(lines, next, 0, out int closing))
                {
                    blockEndLine = closing;
                    return true;
                }
            }
        }
        return false;
    }

    private static bool FindMatchingBrace(string[] lines, int startLine, int startCol, out int endLine)
    {
        int depth = 0;
        bool inString = false;
        for (int j = startLine; j < lines.Length; j++)
        {
            string l = lines[j] ?? string.Empty;
            int k = (j == startLine) ? startCol : 0;
            for (; k < l.Length; k++)
            {
                if (l[k] == '"' && (k == 0 || l[k - 1] != '\\')) inString = !inString; //追踪字符串状态 防止字符串内的{或}干扰深度计数
                if (!inString)
                {
                    if (l[k] == '{') depth++;
                    else if (l[k] == '}')
                    {
                        depth--;
                        if (depth == 0) { endLine = j; return true; }
                    }
                }
            }
        }
        endLine = startLine;
        return false;
    }

    #endregion
    #region 调试

    private static bool s_dumpAst = false; //调试开关 需要时改true

    //把ast dump和脚本解析信息追加到logform
    private static void DumpDebugInfo(string weaponName, string script, string[] templateLines, string result, List<string> log)
    {
        if (!s_dumpAst) return;

        log.Add($"--- 调试: {weaponName} ---");

        var scriptMap = ParseTopLevelMap(script);
        var scriptBlocks = ExtractAllBlocks(script);
        log.Add($"脚本顶层键值对: {scriptMap.Count} 个");
        int shown = 0;
        foreach (var kv in scriptMap)
        {
            if (shown >= 15) { log.Add($"  ... 共 {scriptMap.Count} 个"); break; }
            log.Add($"  \"{kv.Key}\" = \"{kv.Value}\"");
            shown++;
        }
        log.Add($"脚本子块: {scriptBlocks.Count} 个");
        foreach (var bk in scriptBlocks.Keys)
            log.Add($"  {bk} ({scriptBlocks[bk].Length} 字符)");

        var templateTree = ParseTemplateToTree(templateLines);
        log.Add($"模板AST节点数: {CountNodes(templateTree)}");
        log.Add(templateTree.ToString());

        if (!string.IsNullOrEmpty(result))
        {
            var resultLines = result.Split('\n');
            var resultTree = ParseTemplateToTree(resultLines);
            log.Add($"转换后AST节点数: {CountNodes(resultTree)}");
            log.Add(resultTree.ToString());
        }

        log.Add($"--- 调试结束: {weaponName} ---");
    }

    private static int CountNodes(AstNode node)
    {
        int n = 1;
        foreach (var child in node.Children)
            n += CountNodes(child);
        return n;
    }
    #endregion
}