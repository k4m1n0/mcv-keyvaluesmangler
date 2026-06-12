using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace WeaponDamageCalc.Tools;

public static class ScriptToTemplateConverter
{
    private static readonly Regex KeyValRegex = new Regex(
        @"""([^""]*)""\s+""([^""]*)""",//我完全看不懂这个正则 操你妈
        RegexOptions.Compiled);

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

        for (int i = 0; i < files.Length; i++)
        {
            string path = files[i];
            string name = Path.GetFileName(path);
            try
            {
                string script = File.ReadAllText(path, Encoding.UTF8);
                string result = ConvertSingle(script, templateLines, simpleMode);
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
                var lines = File.ReadAllText(externalPath, Encoding.UTF8)
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
    #region 转换解析

    private static string ConvertSingle(string script, string[] templateLines, bool simpleMode)
    {
        var scriptMap = ParseTopLevelMap(script);
        var scriptBlocks = ExtractAllBlocks(script);

        var templateTree = ParseTemplateToTree(templateLines);
        if (templateTree == null || templateTree.Children.Count == 0)
            return string.Join("\n", templateLines);

        var missingKeys = new HashSet<string>(scriptMap.Keys, StringComparer.OrdinalIgnoreCase);
        FillTreeWithScript(templateTree, scriptMap, scriptBlocks, script, missingKeys);

        var result = new StringBuilder();
        RenderTree(templateTree, result, missingKeys, scriptMap, simpleMode);
        string output = result.ToString();
        output = Regex.Replace(output, @"^\s*//\s*[\{\}]\s*$", "", RegexOptions.Multiline);
        return TrimExcessBlankLines(output);
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
                ParseBlockContent(lines, i + 1, end, wdNode.Children);
                i = end + 1;
                continue;
            }
            root.Children.Add(new AstNode { Type = NodeType.Raw, RawText = lines[i] });
            i++;
        }
        return root;
    }

    private static void ParseBlockContent(string[] lines, int start, int end, List<AstNode> nodes)
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
                ParseBlockContent(lines, headerEnd + 1, subEnd, subNode.Children);
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

                        //解析注释块内部而不是直接当raw处理
                        for (int k = i + 1; k < commentEnd; k++)
                        {
                            string innerLine = lines[k];
                            string innerTrimmed = innerLine.TrimStart();
                            if (string.IsNullOrWhiteSpace(innerLine))
                            {
                                cNode.Children.Add(new AstNode { Type = NodeType.Blank, RawText = innerLine });
                                continue;
                            }
                            //去掉行首注释符号和缩进尝试解析键值对
                            string uncommented = innerTrimmed.StartsWith("//") ? innerTrimmed.Substring(2).TrimStart() : innerTrimmed;
                            var (key, val) = ExtractKeyValue(uncommented);
                            if (key != null)
                            {
                                cNode.Children.Add(new AstNode
                                {
                                    Type = NodeType.CommentedKeyValue,
                                    Indent = ExtractIndent(innerLine),
                                    Name = key,
                                    Value = val,
                                    Separator = GetSeparator(uncommented, key),
                                    Comment = GetLineComment(uncommented, key)
                                });
                            }
                            else
                            {
                                //无法识别的行当raw处理
                                cNode.Children.Add(new AstNode { Type = NodeType.Raw, RawText = innerLine });
                            }
                        }

                        nodes.Add(cNode);
                        i = commentEnd + 1;
                        continue;
                    }
                }
            }

            if (trimmed.StartsWith("//") && trimmed.Contains('"'))
            {
                //若前一个节点是纯注释行且当前键值为空 标记该注释行冗余
                string afterSlash = trimmed.Substring(trimmed.IndexOf('"'));
                var (key, val) = ExtractKeyValue(afterSlash);
                if (key != null)
                {
                    nodes.Add(new AstNode
                    {
                        Type = NodeType.CommentedKeyValue,
                        Indent = ExtractIndent(line),
                        Name = key,
                        Value = val,
                        Separator = GetSeparator(afterSlash, key),
                        Comment = GetLineComment(afterSlash, key)
                    });
                    i++; continue;
                }
            }

            if (trimmed.StartsWith("\""))
            {
                var (key, val) = ExtractKeyValue(trimmed);
                if (key != null)
                {
                    nodes.Add(new AstNode
                    {
                        Type = NodeType.KeyValue,
                        Indent = ExtractIndent(line),
                        Name = key,
                        Value = val,
                        Separator = GetSeparator(trimmed, key),
                        Comment = GetLineComment(trimmed, key)
                    });
                    i++; continue;
                }
            }

            nodes.Add(new AstNode { Type = NodeType.Raw, RawText = line });
            i++;
        }
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
                foreach (var key in missingKeys.ToList())
                {
                    if (currentMap.TryGetValue(key, out string? val) && !string.IsNullOrEmpty(val))
                    {
                        node.Children.Add(new AstNode
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
                ParseBlockContent(blockLines, 0, blockLines.Length, tempChildren);
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

                //保存模板中已有的注释子节点用于保留分隔符等信息
                var templateChildren = new List<AstNode>(node.Children);

                //清空原有注释子节点 用脚本中的实际键值对和子块重建
                node.Children = new List<AstNode>();

                //先填充模板中已有的键值对（保留模板的分隔符和注释）
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

                //追加脚本中的子块（如 ViewSlideRecoil, ViewSlideRecoilIronsight 等）
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
                //注释键在脚本中有值 激活为普通键 重新计算分隔符以匹配模板对齐宽度
                node.Value = scriptVal;
                if (node.Type == NodeType.CommentedKeyValue)
                {
                    node.Type = NodeType.KeyValue;
                }

                string? scriptComment = GetLineComment(currentScriptText, node.Name!);
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

    private static void RenderTree(AstNode node, StringBuilder sb, HashSet<string> missingKeys, Dictionary<string, string> scriptMap, bool simpleMode)//this shit was heavily LLM assisted, happy debugging
    {
        switch (node.Type)
        {
            case NodeType.Root:
                foreach (var child in node.Children) RenderTree(child, sb, missingKeys, scriptMap, simpleMode);
                break;
            case NodeType.Blank:
                if (simpleMode) { sb.Append('\n'); break; }
                sb.AppendLine(node.RawText);
                break;
            case NodeType.Raw:
                if (simpleMode && node.RawText.TrimStart().StartsWith("//") && !node.RawText.Contains('"'))
                    break;
                sb.AppendLine(node.RawText);
                break;
            case NodeType.Block:
                //检查块是否有内容 无内容则整体跳过
                if (simpleMode && !BlockHasContent(node)) break;
                foreach (var header in node.HeaderLines)
                    sb.AppendLine(header);
                int bodyStart = sb.Length;
                foreach (var child in node.Children) RenderTree(child, sb, missingKeys, scriptMap, simpleMode);
                if (simpleMode)
                {
                    string body = sb.ToString(bodyStart, sb.Length - bodyStart);
                    body = CompressBlankLines(body);
                    sb.Length = bodyStart;
                    sb.Append(body);
                }
                //最外层WeaponData块闭合顶格 子块保留缩进
                string closeIndent = (node.Name == "WeaponData") ? "" : node.Indent;
                sb.AppendLine($"{closeIndent}}}");
                break;
            case NodeType.KeyValue:
                if (simpleMode && string.IsNullOrEmpty(node.Value)) break;
                string commentStr = string.IsNullOrEmpty(node.Comment) ? "" : $" //{node.Comment}";
                sb.AppendLine($"{node.Indent}\"{node.Name}\"{node.Separator}\"{node.Value}\"{commentStr}");
                break;
            case NodeType.CommentedBlock:
                //若注释块内没有复活且脚本中也不存在该块则跳过
                if (simpleMode && !node.Children.Any(c => c.Type == NodeType.KeyValue || c.Type == NodeType.Block))
                    break;
                foreach (var header in node.HeaderLines)
                    sb.AppendLine(header);
                // 输出注释块的左大括号行
                sb.AppendLine($"{node.Indent}//{{");
                foreach (var child in node.Children) RenderTree(child, sb, missingKeys, scriptMap, simpleMode);
                // 输出注释块的右大括号行
                sb.AppendLine($"{node.Indent}//}}");
                break;
            case NodeType.CommentedKeyValue:
                //空值注释键不输出但保留在模板中的位置
                if (simpleMode && string.IsNullOrEmpty(node.Value)) break;
                sb.AppendLine($"{node.Indent}// \"{node.Name}\"{node.Separator}\"{node.Value}\"");
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

    private static string ExtractIndent(string line)
    {
        int idx = line.Length - line.TrimStart().Length;
        return idx > 0 ? line[..idx] : "\t";
    }

    private static string GetSeparator(string line, string key)
    {
        string pattern = $@"""{Regex.Escape(key)}""(\s+)""";
        var m = Regex.Match(line, pattern);
        return m.Success ? m.Groups[1].Value : "\t\t\t\t";
    }

    private static string? GetLineComment(string text, string key)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var lines = text.Split('\n');
        foreach (var rawLine in lines)
        {
            string line = rawLine.Trim();
            if (line.StartsWith("//")) continue;
            if (line.Contains($"\"{key}\""))
            {
                int commentIdx = line.IndexOf("//", StringComparison.Ordinal);
                if (commentIdx >= 0) return line.Substring(commentIdx + 2).Trim();
                return null;
            }
        }
        return null;
    }

    private static (string? key, string? value) ExtractKeyValue(string line)
    {
        var m = Regex.Match(line, @"""([^""]*)""");
        if (!m.Success) return (null, null);
        string key = m.Groups[1].Value;
        m = m.NextMatch();
        return m.Success ? (key, m.Groups[1].Value) : (key, null);
    }

    private static string CleanBlockName(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return raw;
        int c = raw.IndexOf("//", StringComparison.Ordinal);
        if (c >= 0) raw = raw[..c];
        raw = Regex.Replace(raw, @"[\u200B-\u200D\uFEFF\u00A0]", "");//sb
        return raw.Trim();
    }

    private static string CompressBlankLines(string text)
    {
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        text = Regex.Replace(text, @"(\n\s*\n)(\s*//---)", "\n<<SECTION_BREAK>>$2");//sb！！！
        text = Regex.Replace(text, @"\n{2,}", "\n\n");
        text = text.Replace("<<SECTION_BREAK>>", "\n\n");
        return text;
    }

    private static string TrimExcessBlankLines(string text)
    {
        text = CompressBlankLines(text);
        text = text.TrimStart('\r', '\n');
        if (!text.EndsWith('\n'))
            text += "\n";
        return text;
    }

    private static Dictionary<string, string> ParseKeyValueMap(string text)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int depth = 0;
        bool inString = false;
        int keyStart = -1;
        //记录字符串起始引号位置 在字符串闭合时从该位置尝试匹配键值对 而非从闭合引号处
        int i = 0;
        while (i < text.Length)
        {
            char ch = text[i];
            if (ch == '"' && (i == 0 || text[i - 1] != '\\'))
            {
                inString = !inString;
                if (inString) { keyStart = i; }
                else if (keyStart >= 0)
                {
                    if (depth == 0)
                    {
                        int lineStart = text.LastIndexOf('\n', keyStart) + 1;
                        string lineBefore = text.Substring(lineStart, keyStart - lineStart).TrimStart();
                        //行首为//说明该键被注释就跳过
                        if (!lineBefore.StartsWith("//"))
                        {
                            var m = KeyValRegex.Match(text, keyStart);
                            if (m.Success && m.Index == keyStart)
                            {
                                map[m.Groups[1].Value] = m.Groups[2].Value;
                                //匹配成功后跳转到值结束位置 循环末尾i++所以减1
                                i = m.Index + m.Length - 1;
                                keyStart = -1;
                            }
                        }
                    }
                    keyStart = -1;
                }
                i++;
                continue;
            }

            if (!inString)
            {
                if (ch == '{') { depth++; }
                else if (ch == '}')
                {
                    depth--;
                    if (depth < 0) depth = 0;
                }
            }
            i++;
        }
        return map;
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

        //如果当前行没有{ 但下一行以{开头 说明块名独占一行 匹配到整个块结束
        if (i + 1 < lines.Length && (lines[i + 1]?.TrimStart() ?? "").StartsWith("{"))
        {
            string potentialName = line.TrimEnd();
            if (!string.IsNullOrEmpty(potentialName) && !potentialName.StartsWith("//"))
            {
                blockName = CleanBlockName(potentialName);
                if (FindMatchingBrace(lines, i + 1, 0, out int closing))
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
                //追踪字符串状态 防止字符串内的{或}干扰深度计数
                if (l[k] == '"' && (k == 0 || l[k - 1] != '\\')) inString = !inString;
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
}