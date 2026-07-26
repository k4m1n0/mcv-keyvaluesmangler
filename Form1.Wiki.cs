using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using WeaponDamageCalc.Services;
using WeaponDamageCalc.Tools;

namespace WeaponDamageCalc;

public partial class Form1
{
    private void BtnWiki_Click(object? sender, EventArgs e)
    {
        var dlg = new Form
        {
            Text = "Wiki Stats Updater", Size = new Size(660, 580),
            StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedSingle,
            MinimizeBox = false, MaximizeBox = false
        };

        var lblPage = new Label { Text = "Page:", Location = new Point(12, 14), Size = new Size(38, 20), TextAlign = ContentAlignment.MiddleRight };
        var txtPage = new TextBox { Location = new Point(56, 12), Size = new Size(200, 22), Text = "Weapons of Vietnam" };
        string lastPageText = txtPage.Text;
        var btnFetch = new Button { Text = "Fetch", Location = new Point(262, 11), Size = new Size(55, 24) };
        var lblStatus = new Label { Location = new Point(324, 14), AutoSize = true, ForeColor = Color.DarkGreen };

        var lblUser = new Label { Text = "User:", Location = new Point(12, 42), Size = new Size(38, 20), TextAlign = ContentAlignment.MiddleRight };
        var txtUser = new TextBox { Location = new Point(56, 40), Size = new Size(80, 22) };
        var lblPw = new Label { Text = "Pw:", Location = new Point(142, 42), Size = new Size(24, 20), TextAlign = ContentAlignment.MiddleRight };
        var txtPw = new TextBox { Location = new Point(170, 40), Size = new Size(80, 22), PasswordChar = '*' };
        var btnDryRun = new Button { Text = "DryRun", Location = new Point(256, 39), Size = new Size(75, 24) };
        var btnBatchDR = new Button { Text = "BatchDR", Location = new Point(336, 39), Size = new Size(75, 24) };
        var btnGenerate = new Button { Text = "Generate", Location = new Point(416, 39), Size = new Size(75, 24) };

        var lblInput = new Label { Text = "Source:", Location = new Point(12, 74), AutoSize = true };
        var txtInput = new TextBox { Location = new Point(12, 92), Size = new Size(620, 100), Multiline = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9), MaxLength = 0 };
        var lblOutput = new Label { Text = "Result:", Location = new Point(12, 198), AutoSize = true };
        var txtOutput = new TextBox { Location = new Point(12, 216), Size = new Size(620, 240), Multiline = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9), ReadOnly = true, MaxLength = 0 };

        var btnSelectDir = new Button { Text = "Scripts...", Location = new Point(12, 464), Size = new Size(85, 26) };
        var lblDir = new Label { Location = new Point(98, 469), AutoSize = true, ForeColor = Color.Gray };
        var btnConvert = new Button { Text = "Convert", Location = new Point(12, 494), Size = new Size(85, 26) };
        var btnCopy = new Button { Text = "Copy", Location = new Point(103, 494), Size = new Size(85, 26) };
        var btnReset = new Button { Text = "Reset", Location = new Point(194, 494), Size = new Size(85, 26) };

        string? selectedDir = string.IsNullOrEmpty(lastScriptsDir) ? null : lastScriptsDir;
        if (selectedDir != null) lblDir.Text = selectedDir;
        Dictionary<string, string>? _titleToScript = null;
        bool dryRunDone = false, batchDryDone = false;
        CancellationTokenSource? dryRunCts = null, batchCts = null;

        //page改变时清空source和状态
        txtPage.TextChanged += (_, _) =>
        {
            string t = txtPage.Text;
            var m = Regex.Match(t, @"(?:wiki/|title=)([^?#&]+)");
            if (m.Success)
            {
                string extracted = Uri.UnescapeDataString(m.Groups[1].Value).Replace('_', ' ');
                if (t != extracted)
                {
                    txtPage.Text = extracted;
                    txtPage.SelectionStart = extracted.Length;
                    t = extracted;
                }
            }
            if (t != lastPageText)
            {
                lastPageText = t;
                txtInput.Clear();
                txtOutput.Clear();
                _titleToScript = null;
                if (dryRunDone) { dryRunDone = false; btnDryRun.Text = "DryRun"; btnDryRun.BackColor = SystemColors.Control; SetEditControlsEnabled(btnConvert, btnSelectDir, btnFetch, true); }
                if (batchDryDone) { batchDryDone = false; btnBatchDR.Text = "BatchDR"; btnBatchDR.BackColor = SystemColors.Control; SetEditControlsEnabled(btnConvert, btnSelectDir, btnFetch, true); }
            }
        };

        void ResetBatchState()
        {
            batchDryDone = false; btnBatchDR.Text = "BatchDR"; btnBatchDR.BackColor = SystemColors.Control;
            dryRunDone = false; btnDryRun.Text = "DryRun"; btnDryRun.BackColor = SystemColors.Control;
            SetEditControlsEnabled(btnConvert, btnSelectDir, btnFetch, true);
        }

        void SetEditControlsEnabled(Button conv, Button dir, Button fetch, bool enabled)
        {
            conv.Enabled = enabled;
            dir.Enabled = enabled;
            fetch.Enabled = enabled;
        }

        void PickDir()
        {
            if (selectedDir != null && Directory.Exists(selectedDir)) return;
            using var fbd = new FolderBrowserDialog();
            if (fbd.ShowDialog() == DialogResult.OK) { selectedDir = fbd.SelectedPath; lblDir.Text = selectedDir; }
        }

        //反查脚本名
        string? ReverseLookup(string input)
        {
            if (_titleToScript == null || _titleToScript.Count == 0) return null;
            string inputNoExt = Path.GetFileNameWithoutExtension(input);
            if (_titleToScript.ContainsKey(input)) return input;
            foreach (var kv in _titleToScript)
            {
                string sn = kv.Value;
                string snNoExt = Path.GetFileNameWithoutExtension(sn);
                string snStem = snNoExt.StartsWith("weapon_", StringComparison.OrdinalIgnoreCase) ? snNoExt.Substring(7) : snNoExt;
                if (sn.Equals(input, StringComparison.OrdinalIgnoreCase)
                    || snNoExt.Equals(input, StringComparison.OrdinalIgnoreCase)
                    || snNoExt.Equals(inputNoExt, StringComparison.OrdinalIgnoreCase)
                    || snStem.Equals(inputNoExt, StringComparison.OrdinalIgnoreCase))
                    return kv.Key;
            }
            return _titleToScript.Keys.FirstOrDefault(k => k.Equals(input, StringComparison.OrdinalIgnoreCase));
        }

        async Task<bool> EnsureSource()
        {
            if (!string.IsNullOrWhiteSpace(txtInput.Text)) return true;
            var src = await WikiApiService.GetPageSourceAsync(txtPage.Text);
            if (src == null)
            {
                if (_titleToScript == null) { try { _titleToScript = await BuildTitleToScriptMap(); } catch { } }
                string? foundTitle = ReverseLookup(txtPage.Text.Trim());
                if (foundTitle != null)
                {
                    lastPageText = foundTitle;
                    txtPage.Text = foundTitle;
                    src = await WikiApiService.GetPageSourceAsync(foundTitle);
                }
                if (src == null) { lblStatus.Text = "Page not found"; return false; }
            }
            if (_titleToScript == null) { try { _titleToScript = await BuildTitleToScriptMap(); } catch { } }
            txtInput.Text = src;
            lblStatus.Text = $"OK: {txtPage.Text}" + (_titleToScript?.Count > 0 ? $" (+{_titleToScript.Count} idx)" : "");
            return true;
        }

        void EnterCancel(Button btn, CancellationTokenSource cts, ref EventHandler? h)
        {
            btn.Text = "Cancel"; btn.BackColor = Color.LightCoral;
            h = (_, _) => { if (cts is { IsCancellationRequested: false }) { btn.Text = "Cancel"; btn.BackColor = Color.LightCoral; } };
            btn.MouseLeave += h;
        }

        void ExitCancel(Button btn, string text, Color color, EventHandler? h)
        {
            if (h != null) btn.MouseLeave -= h;
            btn.Text = text; btn.BackColor = color;
        }

        void ToggleDryRun()
        {
            dryRunDone = !dryRunDone;
            btnDryRun.Text = dryRunDone ? "Upload" : "DryRun";
            btnDryRun.BackColor = dryRunDone ? Color.LightSalmon : SystemColors.Control;
            SetEditControlsEnabled(btnConvert, btnSelectDir, btnFetch, !dryRunDone);
        }

        void ToggleBatch()
        {
            batchDryDone = !batchDryDone;
            btnBatchDR.Text = batchDryDone ? "BatchUp" : "BatchDR";
            btnBatchDR.BackColor = batchDryDone ? Color.LightSalmon : SystemColors.Control;
            SetEditControlsEnabled(btnConvert, btnSelectDir, btnFetch, !batchDryDone);
        }

        btnSelectDir.Click += (_, _) =>
        {
            using var fbd = new FolderBrowserDialog();
            if (selectedDir != null && Directory.Exists(selectedDir))
                fbd.InitialDirectory = selectedDir;
            if (fbd.ShowDialog() == DialogResult.OK) { selectedDir = fbd.SelectedPath; lblDir.Text = selectedDir; }
        };

        btnGenerate.Click += async (_, _) =>
        {
            // Upload 模式
            if (btnGenerate.Tag is List<Tools.WikiPageGenerator.GeneratedPage> uploadList)
            {
                btnGenerate.Enabled = false;
                lblStatus.Text = "Uploading...";
                try
                {
                    if (!await EnsureLogin(txtUser.Text, txtPw.Text, lblStatus)) return;
                    int upOk = 0, upFail = 0;
                    foreach (var p in uploadList)
                    {
                        bool ok = await WikiApiService.SavePageAsync(p.Title, p.Content, "Create weapon page from game data");
                        if (ok) upOk++; else upFail++;
                        lblStatus.Text = $"Upload [{upOk + upFail}/{uploadList.Count}]";
                    }
                    txtOutput.AppendText($"\r\n\r\n=== Upload: {upOk} ok, {upFail} fail ===");
                    lblStatus.Text = $"Upload done: {upOk} ok, {upFail} fail";
                }
                catch (Exception ex) { lblStatus.Text = $"Upload error: {ex.Message}"; }
                finally
                {
                    btnGenerate.Text = "Generate";
                    btnGenerate.BackColor = SystemColors.Control;
                    btnGenerate.Tag = null;
                    btnGenerate.Enabled = true;
                }
                return;
            }

            if (selectedDir == null || !Directory.Exists(selectedDir)) PickDir();
            if (selectedDir == null) return;
            string resourceDir = LoadoutService.GetResourceDir(selectedDir);
            if (!Directory.Exists(resourceDir))
            {
                lblStatus.Text = $"Resource folder not found: {resourceDir}";
                return;
            }

            btnGenerate.Enabled = false;
            lblStatus.Text = "Loading...";
            try
            {
                var tokens = LocalizationService.LoadTokens(Path.Combine(resourceDir, "vietnam_english.txt"));
                var loadout = LoadoutService.LoadAll(resourceDir);
                // 构建索引
                if (_titleToScript == null) { try { _titleToScript = await BuildTitleToScriptMap(); } catch { } }
                string detailTemplate = await WikiApiService.FetchTemplateAsync("https://wiki.militaryconflictvietnam.com/index.php?title=Template:Weapon_New&action=raw")
                                        ?? "Template fetch failed";
                string shortTemplate = await WikiApiService.FetchTemplateAsync("https://wiki.militaryconflictvietnam.com/index.php?title=Template:WeaponShort&action=raw")
                                        ?? "Template fetch failed";

                var generated = Tools.WikiPageGenerator.GenerateAll(selectedDir, resourceDir, tokens, loadout, detailTemplate, shortTemplate, new HashSet<string>(), _titleToScript);
                
                // 用索引映射构建Wiki标题列表去查已存在页面
                var checkTitles = new List<string>();
                foreach (var p in generated)
                {
                    string? wikiTitle = null;
                    if (_titleToScript != null)
                    {
                        var match = _titleToScript.FirstOrDefault(kv => kv.Value.Equals(p.ScriptName, StringComparison.OrdinalIgnoreCase));
                        if (!string.IsNullOrEmpty(match.Key)) wikiTitle = match.Key;
                    }
                    checkTitles.Add(wikiTitle ?? p.Title);
                }
                var existing = await WikiApiService.GetExistingTitlesAsync(checkTitles);
                
                // 用脚本名判定是否已存在
                var newPages = new List<Tools.WikiPageGenerator.GeneratedPage>();
                foreach (var p in generated)
                {
                    string? wikiTitle = null;
                    if (_titleToScript != null)
                    {
                        var match = _titleToScript.FirstOrDefault(kv => kv.Value.Equals(p.ScriptName, StringComparison.OrdinalIgnoreCase));
                        if (!string.IsNullOrEmpty(match.Key)) wikiTitle = match.Key;
                    }
                    bool exists = existing.Contains(wikiTitle ?? p.Title)
                               || (wikiTitle != null && _titleToScript != null && _titleToScript.ContainsKey(wikiTitle));
                    if (!exists) newPages.Add(p);
                }
                
                string genDir = Path.Combine(AppContext.BaseDirectory, "generated_pages");
                Directory.CreateDirectory(genDir);
                var log = new StringBuilder();
                log.AppendLine($"生成了 {generated.Count} 个武器");
                log.AppendLine($"Tokens: {tokens.Count}  Loadout: {loadout.Count}  索引: {_titleToScript?.Count ?? 0}");
                log.AppendLine($"已存在: {existing.Count}  新页面: {newPages.Count}");
                log.AppendLine();
                foreach (var p in newPages)
                {
                    string filename = p.Title.Replace(" ", "_").Replace("/", "_") + ".txt";
                    File.WriteAllText(Path.Combine(genDir, filename), p.Content, new UTF8Encoding(false));
                    log.AppendLine($"OK: {p.ScriptName} → {p.Title}");
                }

                txtOutput.Text = log.ToString();
                if (newPages.Count > 0)
                {
                    btnGenerate.Text = "Upload New";
                    btnGenerate.BackColor = Color.LightSalmon;
                    btnGenerate.Tag = newPages;
                }
                lblStatus.Text = $"Done: {newPages.Count} new, {existing.Count} existing";
            }
            catch (Exception ex) { txtOutput.Text = $"Error: {ex.Message}"; lblStatus.Text = "Generate failed"; }
            finally { btnGenerate.Enabled = true; }
        };

        btnConvert.Click += async (_, _) =>
        {
            if (dryRunDone || batchDryDone) { lblStatus.Text = "Cannot convert while upload is pending."; return; }
            if (selectedDir == null || !Directory.Exists(selectedDir)) PickDir();
            if (selectedDir == null) return;
            if (_titleToScript == null && !string.IsNullOrWhiteSpace(txtInput.Text))
            {
                try { _titleToScript = await BuildTitleToScriptMap(); if (_titleToScript != null) lblStatus.Text = $"索引已加载 ({_titleToScript.Count} 个武器)"; } catch { }
            }
            try { txtOutput.Text = DoConvert(txtInput.Text, selectedDir, _titleToScript).Replace("\n", "\r\n"); }
            catch (Exception ex) { txtOutput.Text = $"Error: {ex.Message}"; }
        };

        btnCopy.Click += (_, _) => { if (!string.IsNullOrEmpty(txtOutput.Text)) Clipboard.SetText(txtOutput.Text); };

        btnReset.Click += (_, _) =>
        {
            if (dryRunCts != null) { dryRunCts.Cancel(); dryRunCts.Dispose(); dryRunCts = null; }
            if (batchCts != null) { batchCts.Cancel(); batchCts.Dispose(); batchCts = null; }
            txtPage.Text = "Weapons of Vietnam";
            txtInput.Clear(); txtOutput.Clear(); _titleToScript = null;
            dryRunDone = false; btnDryRun.Text = "DryRun"; btnDryRun.BackColor = SystemColors.Control;
            batchDryDone = false; btnBatchDR.Text = "BatchDR"; btnBatchDR.BackColor = SystemColors.Control;
            SetEditControlsEnabled(btnConvert, btnSelectDir, btnFetch, true);
            lblStatus.Text = "";
        };

        btnFetch.Click += async (_, _) =>
        {
            if (dryRunDone || batchDryDone) { lblStatus.Text = "Cannot fetch while upload is pending."; return; }
            if (_titleToScript == null) { try { _titleToScript = await BuildTitleToScriptMap(); } catch { } }
            var source = await WikiApiService.GetPageSourceAsync(txtPage.Text);
            if (source == null && _titleToScript != null)
            {
                string? foundTitle = ReverseLookup(txtPage.Text.Trim());
                if (foundTitle != null) { txtPage.Text = foundTitle; source = await WikiApiService.GetPageSourceAsync(foundTitle); }
            }
            if (source == null) { lblStatus.Text = "Page not found"; return; }
            _titleToScript = await BuildTitleToScriptMap();
            txtInput.Text = source; txtOutput.Clear(); ResetBatchState();
            lblStatus.Text = $"OK: {txtPage.Text}" + (_titleToScript?.Count > 0 ? $" (+{_titleToScript.Count} idx)" : "");
        };

        btnDryRun.Click += async (_, _) =>
        {
            if (dryRunCts != null) { dryRunCts.Cancel(); dryRunCts.Dispose(); dryRunCts = null; btnDryRun.Text = dryRunDone ? "Upload" : "DryRun"; btnDryRun.BackColor = dryRunDone ? Color.LightSalmon : SystemColors.Control; lblStatus.Text = "Cancelled"; return; }
            if (batchCts != null) { lblStatus.Text = "Batch is running"; return; }
            if (batchDryDone) { batchDryDone = false; btnBatchDR.Text = "BatchDR"; btnBatchDR.BackColor = SystemColors.Control; }
            if (dryRunDone && string.IsNullOrWhiteSpace(txtOutput.Text)) { lblStatus.Text = "Result is empty."; return; }

            if (!dryRunDone && string.IsNullOrWhiteSpace(txtOutput.Text))
            {
                if (selectedDir == null || !Directory.Exists(selectedDir)) PickDir();
                if (selectedDir == null) return;
                if (!await EnsureSource()) return;
                try { txtOutput.Text = DoConvert(txtInput.Text, selectedDir, _titleToScript).Replace("\n", "\r\n"); }
                catch (Exception ex) { txtOutput.Text = $"Error: {ex.Message}"; return; }
            }

            dryRunCts = new CancellationTokenSource(); var token = dryRunCts.Token;
            EventHandler? h = null; EnterCancel(btnDryRun, dryRunCts, ref h);
            try
            {
                if (!dryRunDone) { await Task.Run(() => token.ThrowIfCancellationRequested(), token); lblStatus.Text = $"Ready: {txtPage.Text} (click Upload)"; }
                else
                {
                    if (!await EnsureLogin(txtUser.Text, txtPw.Text, lblStatus)) return;
                    token.ThrowIfCancellationRequested();
                    if (await WikiApiService.IsSameContentAsync(txtPage.Text, txtOutput.Text)) { lblStatus.Text = "Unchanged, skip"; }
                    else { lblStatus.Text = await WikiApiService.SavePageAsync(txtPage.Text, txtOutput.Text, "Update weapon data from scripts") ? "Saved!" : "Save failed"; }
                }
                ToggleDryRun(); ExitCancel(btnDryRun, btnDryRun.Text, btnDryRun.BackColor, h);
            }
            catch (OperationCanceledException) { lblStatus.Text = "Cancelled"; ExitCancel(btnDryRun, dryRunDone ? "Upload" : "DryRun", dryRunDone ? Color.LightSalmon : SystemColors.Control, h); }
            finally { dryRunCts?.Dispose(); dryRunCts = null; }
        };

        btnBatchDR.Click += async (_, _) =>
        {
            if (batchCts != null) { batchCts.Cancel(); batchCts.Dispose(); batchCts = null; btnBatchDR.Text = batchDryDone ? "BatchUp" : "BatchDR"; btnBatchDR.BackColor = batchDryDone ? Color.LightSalmon : SystemColors.Control; lblStatus.Text = "Batch cancelled"; return; }
            if (dryRunCts != null) { lblStatus.Text = "DryRun is running"; return; }
            if (dryRunDone) { dryRunDone = false; btnDryRun.Text = "DryRun"; btnDryRun.BackColor = SystemColors.Control; }

            batchCts = new CancellationTokenSource(); var token = batchCts.Token;
            EventHandler? h = null; EnterCancel(btnBatchDR, batchCts, ref h);
            try
            {
                if (selectedDir == null || !Directory.Exists(selectedDir)) PickDir();
                if (selectedDir == null) return;
                if (!await EnsureSource()) return;
                if (!Regex.IsMatch(txtInput.Text, @"^=\[\[.+\]\]=\s*$", RegexOptions.Multiline)) { lblStatus.Text = "Not a summary page."; return; }
                var links = ExtractWeaponLinks(txtInput.Text, _titleToScript);
                if (links.Count == 0) { lblStatus.Text = "No weapon links found"; return; }

                string wikiDir = Path.Combine(AppContext.BaseDirectory, "wiki"); Directory.CreateDirectory(wikiDir);
                var log = new StringBuilder(); int done = 0, fail = 0, skip = 0;

                if (!batchDryDone)
                {
                    if (!await EnsureLogin(txtUser.Text, txtPw.Text, lblStatus)) return;
                    log.AppendLine($"=== Batch DryRun: {links.Count} pages ===");
                    foreach (var link in links)
                    {
                        token.ThrowIfCancellationRequested();
                        try
                        {
                            string? src = await WikiApiService.GetPageSourceAsync(link);
                            if (src == null) { fail++; log.AppendLine($"FAIL fetch: {link}"); }
                            else { string converted = Tools.WikiTableConverter.Convert(src, selectedDir); SaveToWikiDir(link.Replace(" ", "_").Replace("/", "_") + ".txt", converted); done++; log.AppendLine($"OK: {link}"); }
                        }
                        catch { fail++; log.AppendLine($"FAIL: {link}"); }
                        lblStatus.Text = $"DR [{done + fail}/{links.Count}]";
                    }
                    log.AppendLine($"Done: {done} ok, {fail} fail");
                }
                else
                {
                    if (!await EnsureLogin(txtUser.Text, txtPw.Text, lblStatus)) return;
                    log.AppendLine($"=== Batch Upload: {links.Count} pages ===");
                    foreach (var link in links)
                    {
                        token.ThrowIfCancellationRequested();
                        string fp = Path.Combine(wikiDir, link.Replace(" ", "_").Replace("/", "_") + ".txt");
                        if (!File.Exists(fp)) { skip++; log.AppendLine($"SKIP (no file): {link}"); continue; }
                        string content = File.ReadAllText(fp);
                        if (await WikiApiService.IsSameContentAsync(link, content)) { skip++; log.AppendLine($"SKIP (unchanged): {link}"); continue; }
                        if (await WikiApiService.SavePageAsync(link, content, "Update weapon data from scripts")) { done++; log.AppendLine($"OK: {link}"); } else { fail++; log.AppendLine($"FAIL upload: {link}"); }
                        lblStatus.Text = $"Up [{done + fail}/{links.Count - skip}]";
                    }
                    log.AppendLine($"Done: {done} ok, {fail} fail, {skip} skip");
                }
                txtOutput.Text = log.ToString();
                ToggleBatch(); ExitCancel(btnBatchDR, btnBatchDR.Text, btnBatchDR.BackColor, h);
            }
            catch (OperationCanceledException) { lblStatus.Text = "Batch cancelled"; ExitCancel(btnBatchDR, batchDryDone ? "BatchUp" : "BatchDR", batchDryDone ? Color.LightSalmon : SystemColors.Control, h); }
            finally { batchCts?.Dispose(); batchCts = null; }
        };

        dlg.Controls.AddRange(new Control[] {
            lblPage, txtPage, btnFetch, lblStatus,
            lblUser, txtUser, lblPw, txtPw, btnDryRun, btnBatchDR,
            lblInput, txtInput, lblOutput, txtOutput,
            btnSelectDir, lblDir, btnConvert, btnCopy, btnReset, btnGenerate
        });
        dlg.ShowDialog(this);
    }

    private static string DoConvert(string input, string scriptsDir, Dictionary<string, string>? titleToScript)
    {
        input = input.Replace("\r\n", "\n").Replace('\r', '\n');
        if (Regex.IsMatch(input, @"^=\[\[.+\]\]=\s*$", RegexOptions.Multiline))
        {
            var map = titleToScript != null ? new Dictionary<string, string>(titleToScript, StringComparer.OrdinalIgnoreCase) : new();
            foreach (var path in Directory.GetFiles(scriptsDir, "weapon_*.txt"))
            {
                string sn = Path.GetFileNameWithoutExtension(path);
                string c = WeaponScriptService.ReadScriptFile(path).Replace("\r\n", "\n");
                var pm = Regex.Match(c, @"""printname""\s+""([^""]*)""");
                string d = pm.Success ? pm.Groups[1].Value.TrimStart('#') : sn;
                if (!map.ContainsKey(d.Replace("_", " "))) map[d.Replace("_", " ")] = sn;
            }
            return Tools.WikiTableConverter.ConvertSummaryPage(input, scriptsDir, map);
        }
        return Tools.WikiTableConverter.Convert(input, scriptsDir);
    }

    private static async Task<bool> EnsureLogin(string user, string pw, Label status)
    {
        if (WikiApiService.IsLoggedIn) return true;
        if (!await WikiApiService.LoginAsync(user, pw)) { status.Text = "Login failed"; return false; }
        status.Text = "Logged in"; return true;
    }

    private static async Task<Dictionary<string, string>?> BuildTitleToScriptMap()
    {
        try
        {
            string? idx = await WikiApiService.GetPageSourceAsync("Weapon Script Name");
            if (idx == null) return null;
            idx = idx.Replace("\r\n", "\n").Replace('\r', '\n');
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in Regex.Matches(idx, @"\|\s*(weapon_[^\s|]+)\s*\n\|\s*\[\[([^\]]+)\]\]"))
                map[m.Groups[2].Value.Trim()] = m.Groups[1].Value;
            return map;
        }
        catch { return null; }
    }

    private static List<string> ExtractWeaponLinks(string pageSource, Dictionary<string, string>? titleToScript)
    {
        if (titleToScript == null || titleToScript.Count == 0) return new();
        var links = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(pageSource, @"\[\[([^\]|:#<>]+)\]\]"))
            if (titleToScript.ContainsKey(m.Groups[1].Value.Trim()))
                links.Add(m.Groups[1].Value.Trim());
        return links.OrderBy(x => x).ToList();
    }

    private static void SaveToWikiDir(string fileName, string content)
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "wiki");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, fileName), content);
    }
}