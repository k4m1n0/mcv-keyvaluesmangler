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
            Text = "Wiki Stats Updater", Size = new Size(660, 680),
            StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedSingle,
            MinimizeBox = false, MaximizeBox = false
        };

        var lblPage = new Label { Text = "Page:", Location = new Point(12, 14), Size = new Size(38, 20), TextAlign = ContentAlignment.MiddleRight };
        var txtPage = new TextBox { Location = new Point(56, 12), Size = new Size(195, 22), Text = "Weapons of Vietnam" };
        string lastPageText = txtPage.Text;
        var btnFetch = new Button { Text = "Fetch", Location = new Point(256, 11), Size = new Size(75, 24) };
        var lblStatus = new Label { Location = new Point(336, 14), AutoSize = true, ForeColor = Color.DarkGreen };

        var lblUser = new Label { Text = "User", Location = new Point(12, 42), Size = new Size(38, 20), TextAlign = ContentAlignment.MiddleRight };
        var txtUser = new TextBox { Location = new Point(56, 40), Size = new Size(80, 22), Text = lastWikiUser ?? "" };
        var lblPw = new Label { Text = "Pw", Location = new Point(142, 42), Size = new Size(24, 20), TextAlign = ContentAlignment.MiddleRight };
        var txtPw = new TextBox { Location = new Point(170, 40), Size = new Size(80, 22), PasswordChar = '*', Text = lastWikiPw ?? "" };
        var btnDryRun = new Button { Text = "DryRun", Location = new Point(256, 39), Size = new Size(75, 24) };
        var btnBatchDR = new Button { Text = "BatchDR", Location = new Point(336, 39), Size = new Size(75, 24) };
        var btnGenerate = new Button { Text = "Generate", Location = new Point(416, 39), Size = new Size(75, 24) };
        var chkOverwriteExisting = new CheckBox { Text = "Overwrite existing", Location = new Point(498, 41), Size = new Size(110, 24), Checked = false, AutoSize = true };

        var lblInput = new Label { Text = "Source:", Location = new Point(12, 74), AutoSize = true };
        var txtInput = new TextBox { Location = new Point(12, 92), Size = new Size(620, 228), Multiline = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9), MaxLength = 0 };
        var lblOutput = new Label { Text = "Result:", Location = new Point(12, 326), AutoSize = true };
        var txtOutput = new TextBox { Location = new Point(12, 344), Size = new Size(620, 228), Multiline = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9), ReadOnly = true, MaxLength = 0 };

        var btnSelectDir = new Button { Text = "Scripts...", Location = new Point(12, 578), Size = new Size(85, 26) };
        var lblDir = new Label { Location = new Point(98, 583), AutoSize = true, ForeColor = Color.Gray };
        var btnConvert = new Button { Text = "Convert", Location = new Point(12, 608), Size = new Size(85, 26) };
        var btnCopy = new Button { Text = "Copy", Location = new Point(103, 608), Size = new Size(85, 26) };
        var btnReset = new Button { Text = "Reset", Location = new Point(194, 608), Size = new Size(85, 26) };
        var chkSkipCached = new CheckBox { Text = "Skip cached", Location = new Point(290, 610), Size = new Size(100, 24), Checked = false, AutoSize = true };

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

        async Task<bool> EnsureSource()
        {
            if (!string.IsNullOrWhiteSpace(txtInput.Text)) return true;
            var src = await WikiApiService.GetPageSourceAsync(txtPage.Text);
            if (src == null)
            {
                if (_titleToScript == null) { try { _titleToScript = await WikiService.BuildScriptIndexAsync(); } catch { } }
                string? foundTitle = WikiService.ReverseLookup(txtPage.Text.Trim(), _titleToScript);
                if (foundTitle != null)
                {
                    lastPageText = foundTitle;
                    txtPage.Text = foundTitle;
                    src = await WikiApiService.GetPageSourceAsync(foundTitle);
                }
                if (src == null) { lblStatus.Text = "Page not found"; return false; }
            }
            if (_titleToScript == null) { try { _titleToScript = await WikiService.BuildScriptIndexAsync(); } catch { } }
            txtInput.Text = src.Replace("\n", "\r\n");
            lblStatus.Text = $"OK: {txtPage.Text}" + (_titleToScript?.Count > 0 ? $" (+{_titleToScript.Count} idx)" : "");
            return true;
        }

        void EnterCancel(Button btn, CancellationTokenSource cts, ref EventHandler? h)
        {
            btn.Text = "Cancel"; btn.BackColor = Color.LightCoral;
            h = (_, _) => { try { if (cts is { IsCancellationRequested: false }) { btn.Text = "Cancel"; btn.BackColor = Color.LightCoral; } } catch { } };
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
            if (btnGenerate.Tag is string uploadDir && Directory.Exists(uploadDir))
            {
                btnGenerate.Enabled = false;
                lblStatus.Text = "Uploading...";
                try
                {
                    if (!await EnsureLogin(txtUser.Text, txtPw.Text, lblStatus)) { btnGenerate.Text = "Generate"; btnGenerate.BackColor = SystemColors.Control; btnGenerate.Tag = null; btnGenerate.Enabled = true; return; }
                    var files = Directory.GetFiles(uploadDir, "*.txt");
                    int upOk = 0, upFail = 0;
                    txtOutput.Clear();
                    void Out(string s) { txtOutput.AppendText(s + "\r\n"); }
                    Out($"Upload — {files.Length} files — {DateTime.Now:HH:mm:ss}");
                    Out(new string('-', 40));
                    foreach (string fp in files)
                    {
                        string title = Uri.UnescapeDataString(Path.GetFileNameWithoutExtension(fp)).Replace("_", " ");
                        string content = File.ReadAllText(fp);
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        bool ok = await WikiApiService.SavePageAsync(title, content, "Create weapon page from game data");
                        sw.Stop();
                        if (ok) { upOk++; Out($"OK  {title,-30}  {sw.ElapsedMilliseconds}ms"); }
                        else { upFail++; Out($"FAIL {title,-30}"); }
                        lblStatus.Text = $"Upload [{upOk + upFail}/{files.Length}]";
                    }
                    Out(new string('-', 40));
                    Out($"Upload done: {upOk} ok, {upFail} fail  {DateTime.Now:HH:mm:ss}");
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
                txtOutput.Clear();
                void Out(string s) { txtOutput.AppendText(s + "\r\n"); }

                Out($"Generate started — {DateTime.Now:HH:mm:ss}");
                var tokens = LocalizationService.LoadTokens(Path.Combine(resourceDir, "vietnam_english.txt"));
                Out($"Tokens loaded: {tokens.Count}");
                lblStatus.Text = "Loading loadout...";
                var loadout = LoadoutService.LoadAll(resourceDir);
                Out($"Loadout loaded: {loadout.Count}");
                if (_titleToScript == null) { try { _titleToScript = await WikiService.BuildScriptIndexAsync(); } catch { } }
                Out($"Index: {_titleToScript?.Count ?? 0} entries");
                lblStatus.Text = "Fetching templates...";
                string defaultTemplate = await WikiApiService.FetchTemplateAsync(Tools.WikiPageGenerator.DefaultTemplateUrl) ?? "Template fetch failed";
                string lmgTemplate = await WikiApiService.FetchTemplateAsync(Tools.WikiPageGenerator.LmgTemplateUrl) ?? defaultTemplate;
                string pistolTemplate = await WikiApiService.FetchTemplateAsync(Tools.WikiPageGenerator.PistolTemplateUrl) ?? defaultTemplate;
                string shortTemplate = await WikiApiService.FetchTemplateAsync(Tools.WikiPageGenerator.ShortTemplateUrl) ?? "Template fetch failed";
                Out("Templates fetched");
                lblStatus.Text = "Generating pages...";
                var generated = Tools.WikiPageGenerator.GenerateAll(selectedDir, resourceDir, tokens, loadout,
                    defaultTemplate, lmgTemplate, pistolTemplate, shortTemplate, new HashSet<string>(), _titleToScript);
                Out($"Scripts processed: {generated.Count}");

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
                lblStatus.Text = $"Checking {checkTitles.Count} titles on wiki...";
                var existing = await WikiApiService.GetExistingTitlesAsync(checkTitles);
                Out($"Existing on wiki: {existing.Count}");
                //筛选新页面并保存到generated目录

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
                    if (!exists || chkOverwriteExisting.Checked) newPages.Add(p);
                }

                string genDir = Path.Combine(AppContext.BaseDirectory, "generated");
                Directory.CreateDirectory(genDir);
                Out(new string('-', 40));
                Out($"Pages to write: {newPages.Count}{(chkOverwriteExisting.Checked ? " (Overwrite existing)" : "")}");
                foreach (var p in newPages)
                {
                    string filename = p.Title.Replace(" ", "_").Replace("/", "_") + ".txt";
                    File.WriteAllText(Path.Combine(genDir, filename), p.Content, new UTF8Encoding(false));
                    Out($"OK  {p.ScriptName,-30} > {p.Title}");
                }
                Out(new string('-', 40));
                Out($"Done: {newPages.Count} new, {existing.Count} existing  {DateTime.Now:HH:mm:ss}");

                if (newPages.Count > 0)
                {
                    btnGenerate.Text = "Upload New";
                    btnGenerate.BackColor = Color.LightSalmon;
                    btnGenerate.Tag = genDir;
                }
                lblStatus.Text = $"Done: {newPages.Count} new, {existing.Count} existing";
            }
            catch (Exception ex) { txtOutput.AppendText($"\r\nError: {ex.Message}\r\n"); lblStatus.Text = "Generate failed"; }
            finally { btnGenerate.Enabled = true; }
        };

        btnConvert.Click += async (_, _) =>
        {
            if (dryRunDone || batchDryDone) { lblStatus.Text = "Cannot convert while upload is pending."; return; }
            if (selectedDir == null || !Directory.Exists(selectedDir)) PickDir();
            if (selectedDir == null) return;
            if (_titleToScript == null && !string.IsNullOrWhiteSpace(txtInput.Text))
            {
                try { _titleToScript = await WikiService.BuildScriptIndexAsync(); if (_titleToScript != null) lblStatus.Text = $"索引已加载 ({_titleToScript.Count} 个武器)"; } catch { }
            }
            try { txtOutput.Text = WikiService.ConvertWikiSource(txtInput.Text, selectedDir, _titleToScript).Replace("\n", "\r\n"); }
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
            btnGenerate.Text = "Generate"; btnGenerate.BackColor = SystemColors.Control; btnGenerate.Tag = null;
            SetEditControlsEnabled(btnConvert, btnSelectDir, btnFetch, true);
            lblStatus.Text = "";
        };

        btnFetch.Click += async (_, _) =>
        {
            if (dryRunDone || batchDryDone) { lblStatus.Text = "Cannot fetch while upload is pending."; return; }
            if (_titleToScript == null) { try { _titleToScript = await WikiService.BuildScriptIndexAsync(); } catch { } }
            var source = await WikiApiService.GetPageSourceAsync(txtPage.Text);
            if (source == null && _titleToScript != null)
            {
                string? foundTitle = WikiService.ReverseLookup(txtPage.Text.Trim(), _titleToScript);
                if (foundTitle != null) { txtPage.Text = foundTitle; source = await WikiApiService.GetPageSourceAsync(foundTitle); }
            }
            if (source == null) { lblStatus.Text = "Page not found"; return; }
            _titleToScript = await WikiService.BuildScriptIndexAsync();
            txtInput.Text = source.Replace("\n", "\r\n"); txtOutput.Clear(); ResetBatchState();
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
                try { txtOutput.Text = WikiService.ConvertWikiSource(txtInput.Text, selectedDir, _titleToScript).Replace("\n", "\r\n"); }
                catch (Exception ex) { txtOutput.Text = $"Error: {ex.Message}"; return; }
            }

            dryRunCts = new CancellationTokenSource(); var token = dryRunCts.Token;
            EventHandler? h = null; EnterCancel(btnDryRun, dryRunCts, ref h);
            try
            {
                if (!dryRunDone) { await Task.Run(() => token.ThrowIfCancellationRequested(), token); lblStatus.Text = $"Ready: {txtPage.Text} (click Upload)"; }
                else
                {
                    if (!await EnsureLogin(txtUser.Text, txtPw.Text, lblStatus)) { ExitCancel(btnDryRun, dryRunDone ? "Upload" : "DryRun", dryRunDone ? Color.LightSalmon : SystemColors.Control, h); return; }
                    token.ThrowIfCancellationRequested();
                    //与wiki现有内容比较 未变更则跳过
                    if (await WikiApiService.IsSameContentAsync(txtPage.Text, txtOutput.Text)) { lblStatus.Text = "Unchanged, skip"; }
                    else
                    {
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        bool ok = await WikiApiService.SavePageAsync(txtPage.Text, txtOutput.Text, "Update weapon data from scripts");
                        sw.Stop();
                        lblStatus.Text = ok ? $"Saved! ({sw.ElapsedMilliseconds}ms)" : "Save failed";
                    }
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

            if (selectedDir == null || !Directory.Exists(selectedDir)) PickDir();
            if (selectedDir == null) return;
            if (!await EnsureSource()) return;
            if (!Regex.IsMatch(txtInput.Text, @"^=\[\[.+\]\]=\s*$", RegexOptions.Multiline)) { lblStatus.Text = "Not a summary page."; return; }
            var links = WikiService.ExtractWeaponLinks(txtInput.Text, _titleToScript);
            if (links.Count == 0) { lblStatus.Text = "No weapon links found"; return; }

            batchCts = new CancellationTokenSource(); var token = batchCts.Token;
            EventHandler? h = null; EnterCancel(btnBatchDR, batchCts, ref h);
            try
            {
                string wikiDir = WikiService.GetWikiDir(); Directory.CreateDirectory(wikiDir);
                int done = 0, fail = 0, skip = 0;
                txtOutput.Clear();
                void Out(string s) { txtOutput.AppendText(s + "\r\n"); }

                if (!batchDryDone)
                {
                    if (!await EnsureLogin(txtUser.Text, txtPw.Text, lblStatus)) { ExitCancel(btnBatchDR, "BatchDR", SystemColors.Control, h); return; }
                    string resumeTag = chkSkipCached.Checked ? " [skip cached]" : "";
                    Out($"Batch DryRun — {links.Count} pages — {DateTime.Now:HH:mm:ss}{resumeTag}");
                    Out(new string('-', 40));
                    int skippedCached = 0;
                    foreach (var link in links)
                    {
                        token.ThrowIfCancellationRequested();
                        string fn = link.Replace(" ", "_").Replace("/", "_") + ".txt";
                        string fp = Path.Combine(wikiDir, fn);
                        if (chkSkipCached.Checked && File.Exists(fp))
                        {
                            skippedCached++;
                            Out($"SKIP (cached)  {link}");
                            lblStatus.Text = $"DR [{done + fail + skippedCached}/{links.Count}]";
                            continue;
                        }
                        try
                        {
                            //拉取页面源码 转换并保存到wiki目录
                            string? src = await WikiApiService.GetPageSourceAsync(link);
                            if (src == null) { fail++; Out($"FAIL fetch: {link}"); }
                            else
                            {
                                string converted = Tools.WikiTableConverter.Convert(src, selectedDir);
                                WikiService.SaveToWikiDir(fn, converted);
                                done++;
                                int origLines = src.Split('\n').Length;
                                int convLines = converted.Split('\n').Length;
                                Out($"OK  {link,-30}  {origLines} > {convLines} lines");
                            }
                        }
                        catch (Exception ex) { fail++; Out($"ERR {link,-30}  {ex.Message}"); }
                        lblStatus.Text = $"DR [{done + fail + skippedCached}/{links.Count}]";
                    }
                    Out(new string('-', 40));
                    string cachedInfo = skippedCached > 0 ? $", {skippedCached} cached" : "";
                    Out($"Done: {done} ok, {fail} fail{cachedInfo}  {DateTime.Now:HH:mm:ss}");
                }
                else
                {
                    if (!await EnsureLogin(txtUser.Text, txtPw.Text, lblStatus)) { ExitCancel(btnBatchDR, "BatchUp", Color.LightSalmon, h); return; }
                    Out($"Batch Upload — {links.Count} pages — {DateTime.Now:HH:mm:ss}");
                    Out(new string('-', 40));
                    foreach (var link in links)
                    {
                        token.ThrowIfCancellationRequested();
                        string fp = Path.Combine(wikiDir, link.Replace(" ", "_").Replace("/", "_") + ".txt");
                        if (!File.Exists(fp)) { skip++; Out($"SKIP no file: {link}"); continue; }
                        string content = File.ReadAllText(fp);
                        if (await WikiApiService.IsSameContentAsync(link, content)) { skip++; Out($"SKIP unchanged: {link}"); continue; }
                        try
                        {
                            var sw = System.Diagnostics.Stopwatch.StartNew();
                            bool ok = await WikiApiService.SavePageAsync(link, content, "Update weapon data from scripts");
                            sw.Stop();
                            if (ok) { done++; Out($"OK  {link,-30}  {sw.ElapsedMilliseconds}ms"); }
                            else { fail++; Out($"FAIL upload: {link,-30}"); }
                        }
                        catch (Exception ex) { fail++; Out($"ERR {link,-30}  {ex.Message}"); }
                        lblStatus.Text = $"Up [{done + fail}/{links.Count - skip}]";
                    }
                    Out(new string('-', 40));
                    Out($"Done: {done} ok, {fail} fail, {skip} skip  {DateTime.Now:HH:mm:ss}");
                }
                ToggleBatch(); ExitCancel(btnBatchDR, btnBatchDR.Text, btnBatchDR.BackColor, h);
            }
            catch (OperationCanceledException) { lblStatus.Text = "Batch cancelled"; ExitCancel(btnBatchDR, batchDryDone ? "BatchUp" : "BatchDR", batchDryDone ? Color.LightSalmon : SystemColors.Control, h); }
            finally { batchCts?.Dispose(); batchCts = null; }
        };

        var tooltip = new ToolTip();
        tooltip.SetToolTip(txtPage, "Wiki page name (e.g. AK-47) or script name (e.g. AK47, weapon_akm)\nPaste a full URL to auto extract the page name");
        tooltip.SetToolTip(btnFetch, "Fetch page source from the wiki");
        tooltip.SetToolTip(btnDryRun, "Dry run: convert local scripts and preview changes\nClick again to upload");
        tooltip.SetToolTip(btnBatchDR, "Batch process all weapons linked from the current page\nClick again to upload all");
        tooltip.SetToolTip(btnGenerate, "Generate new weapon pages from game script data\nClick again to upload generated pages");
        tooltip.SetToolTip(chkOverwriteExisting, "Include existing wiki pages when generating");
        tooltip.SetToolTip(chkSkipCached, "Skip pages already saved in the wiki folder");
        tooltip.SetToolTip(btnSelectDir, "Select the scripts folder (e.g. .../vietnam/scripts)");
        tooltip.SetToolTip(btnConvert, "Convert the current source using script data");
        tooltip.SetToolTip(btnCopy, "Copy result to clipboard");
        tooltip.SetToolTip(btnReset, "Reset all wiki fields to defaults");
        tooltip.SetToolTip(btnFetch, "Fetch the wiki page source");

        dlg.Controls.AddRange(new Control[] {
            lblPage, txtPage, btnFetch, lblStatus,
            lblUser, txtUser, lblPw, txtPw, btnDryRun, btnBatchDR, btnGenerate, chkOverwriteExisting,
            lblInput, txtInput, lblOutput, txtOutput,
            btnSelectDir, lblDir, chkSkipCached, btnConvert, btnCopy, btnReset
        });

        dlg.FormClosing += (_, _) =>
        {
            if (dryRunCts != null) { dryRunCts.Cancel(); dryRunCts.Dispose(); dryRunCts = null; }
            if (batchCts != null) { batchCts.Cancel(); batchCts.Dispose(); batchCts = null; }
        };
        dlg.ShowDialog(this);
    }

    private static async Task<bool> EnsureLogin(string user, string pw, Label status)
    {
        if (WikiApiService.IsLoggedIn) return true;
        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pw))
        {
            status.Text = "Please enter username and passwd";
            return false;
        }
        status.Text = "Logging in...";
        if (!await WikiApiService.LoginAsync(user, pw)) { status.Text = "Login failed"; return false; }
        lastWikiUser = user;
        lastWikiPw = pw;
        status.Text = "Logged in";
        return true;
    }
}