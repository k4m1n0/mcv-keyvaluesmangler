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
    private enum GenState { Ready, Generated }
    private GenState gsState = GenState.Ready;
    private string? sGenDir = null;

    private void SetGenerateState(Button btnGenerate, GenState gsNewState)
    {
        gsState = gsNewState;
        if (gsNewState == GenState.Generated)
        {
            btnGenerate.Text = "UplNew";
            btnGenerate.BackColor = bDarkMode ? Color.FromArgb(180, 80, 60) : Color.LightSalmon;
        }
        else
        {
            btnGenerate.Text = "Generate";
            btnGenerate.BackColor = bDarkMode ? Color.FromArgb(60, 60, 60) : SystemColors.Control;
            sGenDir = null;
        }
    }

    private void BtnWiki_Click(object? sender, EventArgs e)
    {
        LogService.Info("BtnWiki: opening Wiki Stats Updater");
        gsState = GenState.Ready;
        sGenDir = null;

        var frmDlg = new Form
        {
            Text = "Wiki Stats Updater", Size = new Size(660, 600),
            StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedSingle,
            MinimizeBox = false, MaximizeBox = false
        };

        var lblPage = new Label { Text = "Page:", Location = new Point(12, 14), Size = new Size(38, 20), TextAlign = ContentAlignment.MiddleRight };
        var txtPage = new TextBox { Location = new Point(56, 12), Size = new Size(195, 22), Text = "Weapons of Vietnam" };
        string sLastPageText = txtPage.Text;
        var btnFetch = new Button { Text = "Fetch", Location = new Point(256, 11), Size = new Size(75, 24) };
        var lblStatus = new Label { Location = new Point(336, 14), AutoSize = true, ForeColor = Color.DarkGreen };

        var lblUser = new Label { Text = "User", Location = new Point(12, 42), Size = new Size(38, 20), TextAlign = ContentAlignment.MiddleRight };
        var txtUser = new TextBox { Location = new Point(56, 40), Size = new Size(80, 22), Text = sLastWikiUser ?? "" };
        var lblPw = new Label { Text = "Pw", Location = new Point(142, 42), Size = new Size(24, 20), TextAlign = ContentAlignment.MiddleRight };
        var txtPw = new TextBox { Location = new Point(170, 40), Size = new Size(80, 22), PasswordChar = '*', Text = sLastWikiPw ?? "" };
        var btnDryRun = new Button { Text = "DryRun", Location = new Point(256, 39), Size = new Size(75, 24) };
        var btnBatchDR = new Button { Text = "BatchDR", Location = new Point(336, 39), Size = new Size(75, 24) };
        var btnGenerate = new Button { Text = "Generate", Location = new Point(416, 39), Size = new Size(75, 24) };
        var chkOverwriteExisting = new CheckBox { Text = "Overwrite existing", Location = new Point(498, 41), Size = new Size(110, 24), Checked = false, AutoSize = true };

        var lblInput = new Label { Text = "Source:", Location = new Point(12, 74), AutoSize = true };
        var txtInput = new TextBox { Location = new Point(12, 92), Size = new Size(620, 188), Multiline = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9), MaxLength = 0 };
        var lblOutput = new Label { Text = "Result:", Location = new Point(12, 286), AutoSize = true };
        var txtOutput = new TextBox { Location = new Point(12, 304), Size = new Size(620, 188), Multiline = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9), ReadOnly = true, MaxLength = 0 };

        void Out(string s) { txtOutput.AppendText(s + "\r\n"); }

        var btnSelectDir = new Button { Text = "Scripts...", Location = new Point(12, 498), Size = new Size(85, 26) };
        var lblDir = new Label { Location = new Point(98, 503), AutoSize = true, ForeColor = Color.Gray };
        var btnConvert = new Button { Text = "Convert", Location = new Point(12, 528), Size = new Size(85, 26) };
        var btnCopy = new Button { Text = "Copy", Location = new Point(103, 528), Size = new Size(85, 26) };
        var btnReset = new Button { Text = "Reset", Location = new Point(194, 528), Size = new Size(85, 26) };
        var chkSkipCached = new CheckBox { Text = "Skip cached", Location = new Point(290, 530), Size = new Size(100, 24), Checked = false, AutoSize = true };
        Color cWikiInactive = bDarkMode ? Color.FromArgb(60, 60, 60) : SystemColors.Control;
        Color cWikiActive = bDarkMode ? Color.FromArgb(180, 80, 60) : Color.LightSalmon;

        string? sSelectedDir = string.IsNullOrEmpty(sLastScriptsDir) ? null : sLastScriptsDir;
        if (sSelectedDir != null) lblDir.Text = sSelectedDir;
        Dictionary<string, string>? mpTitleToScript = null;
        bool bDryRunDone = false, bBatchDryDone = false;
        CancellationTokenSource? ctsDryRun = null, ctsBatch = null, ctsGen = null;

        //page改变时清空source和状态
        txtPage.TextChanged += (_, _) =>
        {
            string sT = txtPage.Text;
            var mUrl = Regex.Match(sT, @"(?:wiki/|title=)([^?#&]+)");
            if (mUrl.Success)
            {
                string sExtracted = Uri.UnescapeDataString(mUrl.Groups[1].Value).Replace('_', ' ');
                if (sT != sExtracted)
                {
                    txtPage.Text = sExtracted;
                    txtPage.SelectionStart = sExtracted.Length;
                    sT = sExtracted;
                }
            }
            if (sT != sLastPageText)
            {
                sLastPageText = sT;
                txtInput.Clear();
                txtOutput.Clear();
                mpTitleToScript = null;
                if (bDryRunDone) { bDryRunDone = false; btnDryRun.Text = "DryRun"; btnDryRun.BackColor = cWikiInactive; SetEditControlsEnabled(btnConvert, btnSelectDir, true); }
                if (bBatchDryDone) { bBatchDryDone = false; btnBatchDR.Text = "BatchDR"; btnBatchDR.BackColor = cWikiInactive; SetEditControlsEnabled(btnConvert, btnSelectDir, true); }
                if (ctsGen != null) { ctsGen.Cancel(); ctsGen.Dispose(); ctsGen = null; }
                SetGenerateState(btnGenerate, GenState.Ready);
            }
        };

        void ResetBatchState()
        {
            bBatchDryDone = false; btnBatchDR.Text = "BatchDR"; btnBatchDR.BackColor = cWikiInactive;
            bDryRunDone = false; btnDryRun.Text = "DryRun"; btnDryRun.BackColor = cWikiInactive;
            SetEditControlsEnabled(btnConvert, btnSelectDir, true);
        }

        void SetEditControlsEnabled(Button btnConv, Button btnDir, bool bEnabled)
        {
            btnConv.Enabled = bEnabled;
            btnDir.Enabled = bEnabled;
        }

        void PickDir()
        {
            if (sSelectedDir != null && Directory.Exists(sSelectedDir)) return;
            using var fbd = new FolderBrowserDialog();
            if (fbd.ShowDialog() == DialogResult.OK) { sSelectedDir = fbd.SelectedPath; lblDir.Text = sSelectedDir; }
        }

        async Task<bool> EnsureSource()
        {
            if (!string.IsNullOrWhiteSpace(txtInput.Text)) return true;
            var sSrc = await WikiApiService.GetPageSourceAsync(txtPage.Text);
            if (sSrc == null)
            {
                if (mpTitleToScript == null)
                {
                    try { mpTitleToScript = await WikiService.BuildScriptIndexAsync(); }
                    catch (Exception ex) { LogService.Error(ex, "Wiki.EnsureSource: BuildScriptIndexAsync"); }
                }
                string? sFoundTitle = WikiService.ReverseLookup(txtPage.Text.Trim(), mpTitleToScript);
                if (sFoundTitle != null)
                {
                    sLastPageText = sFoundTitle;
                    txtPage.Text = sFoundTitle;
                    sSrc = await WikiApiService.GetPageSourceAsync(sFoundTitle);
                }
                if (sSrc == null) { lblStatus.Text = "Page not found"; return false; }
            }
            if (mpTitleToScript == null)
            {
                try { mpTitleToScript = await WikiService.BuildScriptIndexAsync(); }
                catch (Exception ex) { LogService.Error(ex, "Wiki.EnsureSource: BuildScriptIndexAsync (late)"); }
            }
            txtInput.Text = sSrc.Replace("\n", "\r\n");
            lblStatus.Text = $"OK: {txtPage.Text}" + (mpTitleToScript?.Count > 0 ? $" (+{mpTitleToScript.Count} idx)" : "");
            return true;
        }

        void EnterCancel(Button btn, CancellationTokenSource cts, ref EventHandler? ehHandler)
        {
            btn.Text = "Cancel"; btn.BackColor = Color.LightCoral;
            ehHandler = (_, _) =>
            {
                try { if (cts is { IsCancellationRequested: false }) { btn.Text = "Cancel"; btn.BackColor = Color.LightCoral; } }
                catch (Exception ex) { LogService.Error(ex, "Wiki.EnterCancel"); }
            };
            btn.MouseLeave += ehHandler;
        }

        void ExitCancel(Button btn, string sText, Color cColor, EventHandler? ehHandler)
        {
            if (ehHandler != null) btn.MouseLeave -= ehHandler;
            btn.Text = sText; btn.BackColor = cColor;
        }

        void ToggleDryRun()
        {
            bDryRunDone = !bDryRunDone;
            btnDryRun.Text = bDryRunDone ? "Upload" : "DryRun";
            btnDryRun.BackColor = bDryRunDone ? cWikiActive : cWikiInactive;
            SetEditControlsEnabled(btnConvert, btnSelectDir, !bDryRunDone);
        }

        void ToggleBatch()
        {
            bBatchDryDone = !bBatchDryDone;
            btnBatchDR.Text = bBatchDryDone ? "BatchUp" : "BatchDR";
            btnBatchDR.BackColor = bBatchDryDone ? cWikiActive : cWikiInactive;
            SetEditControlsEnabled(btnConvert, btnSelectDir, !bBatchDryDone);
        }

        btnSelectDir.Click += (_, _) =>
        {
            using var fbd = new FolderBrowserDialog();
            if (sSelectedDir != null && Directory.Exists(sSelectedDir))
                fbd.InitialDirectory = sSelectedDir;
            if (fbd.ShowDialog() == DialogResult.OK) { sSelectedDir = fbd.SelectedPath; lblDir.Text = sSelectedDir; }
        };

        btnGenerate.Click += async (_, _) =>
        {
            if (ctsGen != null && !ctsGen.IsCancellationRequested)
            {
                ctsGen.Cancel();
                return;
            }
            if (bDryRunDone) { bDryRunDone = false; btnDryRun.Text = "DryRun"; btnDryRun.BackColor = cWikiInactive; SetEditControlsEnabled(btnConvert, btnSelectDir, true); }
            if (bBatchDryDone) { bBatchDryDone = false; btnBatchDR.Text = "BatchDR"; btnBatchDR.BackColor = cWikiInactive; SetEditControlsEnabled(btnConvert, btnSelectDir, true); }

            if (gsState == GenState.Generated && sGenDir != null && Directory.Exists(sGenDir))
            {
                btnGenerate.Enabled = false;
                btnGenerate.Text = "Cancel";
                btnGenerate.BackColor = Color.LightCoral;
                lblStatus.Text = "Uploading...";
                LogService.Info($"Wiki Generate: uploading {Directory.GetFiles(sGenDir, "*.txt").Length} files from {sGenDir}");
                ctsGen = new CancellationTokenSource();
                var tokGen = ctsGen.Token;
                try
                {
                    if (!await EnsureLogin(txtUser.Text, txtPw.Text, lblStatus)) return;
                    var rgFiles = Directory.GetFiles(sGenDir, "*.txt");
                    int iUpOk = 0, iUpFail = 0;
                    txtOutput.Clear();
                    Out($"Upload — {rgFiles.Length} files — {DateTime.Now:HH:mm:ss}");
                    Out(new string('-', 40));
                    foreach (string sFp in rgFiles)
                    {
                        tokGen.ThrowIfCancellationRequested();
                        string sTitle = Uri.UnescapeDataString(Path.GetFileNameWithoutExtension(sFp)).Replace("_", " ");
                        string sContent = File.ReadAllText(sFp);
                        var swUpload = System.Diagnostics.Stopwatch.StartNew();
                        bool bOk = await WikiApiService.SavePageAsync(sTitle, sContent, "Create weapon page from game data");
                        swUpload.Stop();
                        if (bOk) { iUpOk++; Out($"OK  {sTitle,-30}  {swUpload.ElapsedMilliseconds}ms"); }
                        else { iUpFail++; Out($"FAIL {sTitle,-30}"); }
                        lblStatus.Text = $"Upload [{iUpOk + iUpFail}/{rgFiles.Length}]";
                    }
                    Out(new string('-', 40));
                    Out($"Upload done: {iUpOk} ok, {iUpFail} fail  {DateTime.Now:HH:mm:ss}");
                    lblStatus.Text = $"Upload done: {iUpOk} ok, {iUpFail} fail";
                    LogService.Info($"Wiki Generate upload done: {iUpOk} ok, {iUpFail} fail");
                }
                catch (OperationCanceledException)
                {
                    lblStatus.Text = "Upload cancelled";
                }
                catch (Exception ex)
                {
                    lblStatus.Text = $"Upload error: {ex.Message}";
                    LogService.Error(ex, "Wiki btnGenerate upload");
                }
                finally
                {
                    SetGenerateState(btnGenerate, GenState.Ready);
                    btnGenerate.Enabled = true;
                    ctsGen?.Dispose();
                    ctsGen = null;
                }
                return;
            }

            if (sSelectedDir == null || !Directory.Exists(sSelectedDir)) PickDir();
            if (sSelectedDir == null) return;
            string sResourceDir = LoadoutService.GetResourceDir(sSelectedDir);
            if (!Directory.Exists(sResourceDir))
            {
                lblStatus.Text = $"Resource folder not found: {sResourceDir}";
                return;
            }

            btnGenerate.Enabled = false;
            lblStatus.Text = "Loading...";
            LogService.Info($"Wiki Generate: scripts={sSelectedDir}, resource={sResourceDir}");
            try
            {
                txtOutput.Clear();
                Out($"Generate started — {DateTime.Now:HH:mm:ss}");
                var mpTokens = LocalizationService.LoadTokens(Path.Combine(sResourceDir, "vietnam_english.txt"));
                Out($"Tokens loaded: {mpTokens.Count}");
                lblStatus.Text = "Loading loadout...";
                var mpLoadout = LoadoutService.LoadAll(sResourceDir);
                Out($"Loadout loaded: {mpLoadout.Count}");
                if (mpTitleToScript == null)
                {
                    try { mpTitleToScript = await WikiService.BuildScriptIndexAsync(); }
                    catch (Exception ex) { LogService.Error(ex, "Wiki Generate: BuildScriptIndexAsync"); }
                }
                Out($"Index: {mpTitleToScript?.Count ?? 0} entries");
                lblStatus.Text = "Fetching templates...";
                string sDefaultTemplate = await WikiApiService.FetchTemplateAsync(Tools.WikiPageGenerator.sDefaultTemplateUrl) ?? "Template fetch failed";
                string sLmgTemplate = await WikiApiService.FetchTemplateAsync(Tools.WikiPageGenerator.sLmgTemplateUrl) ?? sDefaultTemplate;
                string sPistolTemplate = await WikiApiService.FetchTemplateAsync(Tools.WikiPageGenerator.sPistolTemplateUrl) ?? sDefaultTemplate;
                string sShortTemplate = await WikiApiService.FetchTemplateAsync(Tools.WikiPageGenerator.sShortTemplateUrl) ?? "Template fetch failed";
                Out("Templates fetched");
                lblStatus.Text = "Generating pages...";
                var rgGenerated = Tools.WikiPageGenerator.GenerateAll(sSelectedDir, sResourceDir, mpTokens, mpLoadout,
                    sDefaultTemplate, sLmgTemplate, sPistolTemplate, sShortTemplate, new HashSet<string>(), mpTitleToScript);
                Out($"Scripts processed: {rgGenerated.Count}");

                var rgCheckTitles = new List<string>();
                foreach (var gpPage in rgGenerated)
                {
                    string? sWikiTitle = null;
                    if (mpTitleToScript != null)
                    {
                        var kvpMatch = mpTitleToScript.FirstOrDefault(kvp => kvp.Value.Equals(gpPage.ScriptName, StringComparison.OrdinalIgnoreCase));
                        if (!string.IsNullOrEmpty(kvpMatch.Key)) sWikiTitle = kvpMatch.Key;
                    }
                    rgCheckTitles.Add(sWikiTitle ?? gpPage.Title);
                }
                lblStatus.Text = $"Checking {rgCheckTitles.Count} titles on wiki...";
                var hsExisting = await WikiApiService.GetExistingTitlesAsync(rgCheckTitles);
                Out($"Existing on wiki: {hsExisting.Count}");
                //筛选新页面并保存到generated目录

                var rgNewPages = new List<Tools.WikiPageGenerator.GeneratedPage>();
                foreach (var gpPage in rgGenerated)
                {
                    string? sWikiTitle = null;
                    if (mpTitleToScript != null)
                    {
                        var kvpMatch = mpTitleToScript.FirstOrDefault(kvp => kvp.Value.Equals(gpPage.ScriptName, StringComparison.OrdinalIgnoreCase));
                        if (!string.IsNullOrEmpty(kvpMatch.Key)) sWikiTitle = kvpMatch.Key;
                    }
                    bool bExists = hsExisting.Contains(sWikiTitle ?? gpPage.Title)
                               || (sWikiTitle != null && mpTitleToScript != null && mpTitleToScript.ContainsKey(sWikiTitle));
                    if (!bExists || chkOverwriteExisting.Checked) rgNewPages.Add(gpPage);
                }

                string sGenOutputDir = Path.Combine(AppContext.BaseDirectory, "generated");
                Directory.CreateDirectory(sGenOutputDir);
                Out(new string('-', 40));
                Out($"Pages to write: {rgNewPages.Count}{(chkOverwriteExisting.Checked ? " (Overwrite existing)" : "")}");
                foreach (var gpPage in rgNewPages)
                {
                    string sFilename = gpPage.Title.Replace(" ", "_").Replace("/", "_") + ".txt";
                    File.WriteAllText(Path.Combine(sGenOutputDir, sFilename), gpPage.Content, new UTF8Encoding(false));
                    Out($"OK  {gpPage.ScriptName,-30} > {gpPage.Title}");
                }
                Out(new string('-', 40));
                Out($"Done: {rgNewPages.Count} new, {hsExisting.Count} existing  {DateTime.Now:HH:mm:ss}");

                if (rgNewPages.Count > 0)
                {
                    sGenDir = sGenOutputDir;
                    SetGenerateState(btnGenerate, GenState.Generated);
                }
                lblStatus.Text = $"Done: {rgNewPages.Count} new, {hsExisting.Count} existing";
                LogService.Info($"Wiki Generate done: {rgNewPages.Count} new, {hsExisting.Count} existing");
            }
            catch (Exception ex)
            {
                txtOutput.AppendText($"\r\nError: {ex.Message}\r\n");
                lblStatus.Text = "Generate failed";
                LogService.Error(ex, "Wiki btnGenerate");
            }
            finally { btnGenerate.Enabled = true; }
        };

        btnConvert.Click += async (_, _) =>
        {
            if (bDryRunDone || bBatchDryDone) { lblStatus.Text = "Cannot convert while upload is pending."; return; }
            if (sSelectedDir == null || !Directory.Exists(sSelectedDir)) PickDir();
            if (sSelectedDir == null) return;
            if (mpTitleToScript == null && !string.IsNullOrWhiteSpace(txtInput.Text))
            {
                try { mpTitleToScript = await WikiService.BuildScriptIndexAsync(); if (mpTitleToScript != null) lblStatus.Text = $"索引已加载 ({mpTitleToScript.Count} 个武器)"; }
                catch (Exception ex) { LogService.Error(ex, "Wiki Convert: BuildScriptIndexAsync"); }
            }
            try { txtOutput.Text = WikiService.ConvertWikiSource(txtInput.Text, sSelectedDir, mpTitleToScript).Replace("\n", "\r\n"); }
            catch (Exception ex)
            {
                txtOutput.Text = $"Error: {ex.Message}";
                LogService.Error(ex, "Wiki Convert");
            }
        };

        btnCopy.Click += (_, _) => { if (!string.IsNullOrEmpty(txtOutput.Text)) Clipboard.SetText(txtOutput.Text); };

        btnReset.Click += (_, _) =>
        {
            if (ctsDryRun != null) { ctsDryRun.Cancel(); ctsDryRun.Dispose(); ctsDryRun = null; }
            if (ctsBatch != null) { ctsBatch.Cancel(); ctsBatch.Dispose(); ctsBatch = null; }
            if (ctsGen != null) { ctsGen.Cancel(); ctsGen.Dispose(); ctsGen = null; }
            txtPage.Text = "Weapons of Vietnam";
            txtInput.Clear(); txtOutput.Clear(); mpTitleToScript = null;
            bDryRunDone = false; btnDryRun.Text = "DryRun"; btnDryRun.BackColor = cWikiInactive;
            bBatchDryDone = false; btnBatchDR.Text = "BatchDR"; btnBatchDR.BackColor = cWikiInactive;
            SetGenerateState(btnGenerate, GenState.Ready);
            SetEditControlsEnabled(btnConvert, btnSelectDir, true);
            lblStatus.Text = "";
        };

        btnFetch.Click += async (_, _) =>
        {
            if (bDryRunDone || bBatchDryDone) { lblStatus.Text = "Cannot fetch while upload is pending."; return; }
            if (mpTitleToScript == null)
            {
                try { mpTitleToScript = await WikiService.BuildScriptIndexAsync(); }
                catch (Exception ex) { LogService.Error(ex, "Wiki Fetch: BuildScriptIndexAsync"); }
            }
            var sSource = await WikiApiService.GetPageSourceAsync(txtPage.Text);
            if (sSource == null && mpTitleToScript != null)
            {
                string? sFoundTitle = WikiService.ReverseLookup(txtPage.Text.Trim(), mpTitleToScript);
                if (sFoundTitle != null) { txtPage.Text = sFoundTitle; sSource = await WikiApiService.GetPageSourceAsync(sFoundTitle); }
            }
            if (sSource == null) { lblStatus.Text = "Page not found"; return; }
            mpTitleToScript = await WikiService.BuildScriptIndexAsync();
            txtInput.Text = sSource.Replace("\n", "\r\n"); txtOutput.Clear(); ResetBatchState();
            lblStatus.Text = $"OK: {txtPage.Text}" + (mpTitleToScript?.Count > 0 ? $" (+{mpTitleToScript.Count} idx)" : "");
        };

        btnDryRun.Click += async (_, _) =>
        {
            if (ctsDryRun != null) { ctsDryRun.Cancel(); ctsDryRun.Dispose(); ctsDryRun = null; btnDryRun.Text = bDryRunDone ? "Upload" : "DryRun"; btnDryRun.BackColor = bDryRunDone ? cWikiActive : cWikiInactive; lblStatus.Text = "Cancelled"; return; }
            if (ctsBatch != null) { lblStatus.Text = "Batch is running"; return; }
            if (ctsGen != null) { lblStatus.Text = "Generate is running"; return; }
            if (bBatchDryDone) { bBatchDryDone = false; btnBatchDR.Text = "BatchDR"; btnBatchDR.BackColor = cWikiInactive; SetEditControlsEnabled(btnConvert, btnSelectDir, true); }
            if (gsState == GenState.Generated) { SetGenerateState(btnGenerate, GenState.Ready); }
            if (bDryRunDone && string.IsNullOrWhiteSpace(txtOutput.Text)) { lblStatus.Text = "Result is empty."; return; }

            if (!bDryRunDone && string.IsNullOrWhiteSpace(txtOutput.Text))
            {
                if (sSelectedDir == null || !Directory.Exists(sSelectedDir)) PickDir();
                if (sSelectedDir == null) return;
                if (!await EnsureSource()) return;
                try { txtOutput.Text = WikiService.ConvertWikiSource(txtInput.Text, sSelectedDir, mpTitleToScript).Replace("\n", "\r\n"); }
                catch (Exception ex)
                {
                    txtOutput.Text = $"Error: {ex.Message}";
                    LogService.Error(ex, "Wiki DryRun convert");
                    return;
                }
            }

            ctsDryRun = new CancellationTokenSource(); var tokDryRun = ctsDryRun.Token;
            EventHandler? ehCancel = null; EnterCancel(btnDryRun, ctsDryRun, ref ehCancel);
            try
            {
                if (!bDryRunDone) { await Task.Run(() => tokDryRun.ThrowIfCancellationRequested(), tokDryRun); lblStatus.Text = $"Ready: {txtPage.Text} (click Upload)"; }
                else
                {
                    if (!await EnsureLogin(txtUser.Text, txtPw.Text, lblStatus)) { ExitCancel(btnDryRun, bDryRunDone ? "Upload" : "DryRun", bDryRunDone ? cWikiActive : cWikiInactive, ehCancel); return; }
                    tokDryRun.ThrowIfCancellationRequested();
                    //与wiki现有内容比较 未变更则跳过
                    if (await WikiApiService.IsSameContentAsync(txtPage.Text, txtOutput.Text)) { lblStatus.Text = "Unchanged, skip"; }
                    else
                    {
                        var swDryRun = System.Diagnostics.Stopwatch.StartNew();
                        bool bOk = await WikiApiService.SavePageAsync(txtPage.Text, txtOutput.Text, "Update weapon data from scripts");
                        swDryRun.Stop();
                        lblStatus.Text = bOk ? $"Saved! ({swDryRun.ElapsedMilliseconds}ms)" : "Save failed";
                        LogService.Info($"Wiki DryRun upload: {txtPage.Text} {(bOk ? "OK" : "FAIL")} ({swDryRun.ElapsedMilliseconds}ms)");
                    }
                }
                ToggleDryRun(); ExitCancel(btnDryRun, btnDryRun.Text, btnDryRun.BackColor, ehCancel);
            }
            catch (OperationCanceledException) { lblStatus.Text = "Cancelled"; ExitCancel(btnDryRun, bDryRunDone ? "Upload" : "DryRun", bDryRunDone ? cWikiActive : cWikiInactive, ehCancel); }
            finally { ctsDryRun?.Dispose(); ctsDryRun = null; }
        };

        btnBatchDR.Click += async (_, _) =>
        {
            if (ctsBatch != null) { ctsBatch.Cancel(); ctsBatch.Dispose(); ctsBatch = null; btnBatchDR.Text = bBatchDryDone ? "BatchUp" : "BatchDR"; btnBatchDR.BackColor = bBatchDryDone ? cWikiActive : cWikiInactive; lblStatus.Text = "Batch cancelled"; return; }
            if (ctsDryRun != null) { lblStatus.Text = "DryRun is running"; return; }
            if (ctsGen != null) { lblStatus.Text = "Generate is running"; return; }
            if (bDryRunDone) { bDryRunDone = false; btnDryRun.Text = "DryRun"; btnDryRun.BackColor = cWikiInactive; SetEditControlsEnabled(btnConvert, btnSelectDir, true); }
            if (gsState == GenState.Generated) { SetGenerateState(btnGenerate, GenState.Ready); }

            if (sSelectedDir == null || !Directory.Exists(sSelectedDir)) PickDir();
            if (sSelectedDir == null) return;
            if (!await EnsureSource()) return;
            if (!Regex.IsMatch(txtInput.Text, @"^=\[\[.+\]\]=\s*$", RegexOptions.Multiline)) { lblStatus.Text = "Not a summary page."; return; }
            var rgLinks = WikiService.ExtractWeaponLinks(txtInput.Text, mpTitleToScript);
            if (rgLinks.Count == 0) { lblStatus.Text = "No weapon links found"; return; }

            LogService.Info($"Wiki Batch: {rgLinks.Count} links, batchDryDone={bBatchDryDone}");
            ctsBatch = new CancellationTokenSource(); var tokBatch = ctsBatch.Token;
            EventHandler? ehCancel = null; EnterCancel(btnBatchDR, ctsBatch, ref ehCancel);
            try
            {
                string sWikiDir = WikiService.GetWikiDir(); Directory.CreateDirectory(sWikiDir);
                int iDone = 0, iFail = 0, iSkip = 0;
                txtOutput.Clear();

                if (!bBatchDryDone)
                {
                    if (!await EnsureLogin(txtUser.Text, txtPw.Text, lblStatus)) { ExitCancel(btnBatchDR, "BatchDR", cWikiInactive, ehCancel); return; }
                    string sResumeTag = chkSkipCached.Checked ? " [skip cached]" : "";
                    Out($"Batch DryRun — {rgLinks.Count} pages — {DateTime.Now:HH:mm:ss}{sResumeTag}");
                    Out(new string('-', 40));
                    int iSkippedCached = 0;
                    foreach (var sLink in rgLinks)
                    {
                        tokBatch.ThrowIfCancellationRequested();
                        string sFn = sLink.Replace(" ", "_").Replace("/", "_") + ".txt";
                        string sFp = Path.Combine(sWikiDir, sFn);
                        if (chkSkipCached.Checked && File.Exists(sFp))
                        {
                            iSkippedCached++;
                            Out($"SKIP (cached)  {sLink}");
                            lblStatus.Text = $"DR [{iDone + iFail + iSkippedCached}/{rgLinks.Count}]";
                            continue;
                        }
                        try
                        {
                            //拉取页面源码 转换并保存到wiki目录
                            string? sSrc = await WikiApiService.GetPageSourceAsync(sLink);
                            if (sSrc == null) { iFail++; Out($"FAIL fetch: {sLink}"); }
                            else
                            {
                                string sConverted = Tools.WikiTableConverter.Convert(sSrc, sSelectedDir);
                                WikiService.SaveToWikiDir(sFn, sConverted);
                                iDone++;
                                int iOrigLines = sSrc.Split('\n').Length;
                                int iConvLines = sConverted.Split('\n').Length;
                                Out($"OK  {sLink,-30}  {iOrigLines} > {iConvLines} lines");
                            }
                        }
                        catch (Exception ex)
                        {
                            iFail++;
                            Out($"ERR {sLink,-30}  {ex.Message}");
                            LogService.Error(ex, $"Wiki Batch DR: {sLink}");
                        }
                        lblStatus.Text = $"DR [{iDone + iFail + iSkippedCached}/{rgLinks.Count}]";
                    }
                    Out(new string('-', 40));
                    string sCachedInfo = iSkippedCached > 0 ? $", {iSkippedCached} cached" : "";
                    Out($"Done: {iDone} ok, {iFail} fail{sCachedInfo}  {DateTime.Now:HH:mm:ss}");
                    LogService.Info($"Wiki Batch DR done: {iDone} ok, {iFail} fail, {iSkippedCached} cached");
                }
                else
                {
                    if (!await EnsureLogin(txtUser.Text, txtPw.Text, lblStatus)) { ExitCancel(btnBatchDR, "BatchUp", cWikiActive, ehCancel); return; }
                    Out($"Batch Upload — {rgLinks.Count} pages — {DateTime.Now:HH:mm:ss}");
                    Out(new string('-', 40));
                    foreach (var sLink in rgLinks)
                    {
                        tokBatch.ThrowIfCancellationRequested();
                        string sFp = Path.Combine(sWikiDir, sLink.Replace(" ", "_").Replace("/", "_") + ".txt");
                        if (!File.Exists(sFp)) { iSkip++; Out($"SKIP no file: {sLink}"); continue; }
                        string sContent = File.ReadAllText(sFp);
                        if (await WikiApiService.IsSameContentAsync(sLink, sContent)) { iSkip++; Out($"SKIP unchanged: {sLink}"); continue; }
                        try
                        {
                            var swBatch = System.Diagnostics.Stopwatch.StartNew();
                            bool bOk = await WikiApiService.SavePageAsync(sLink, sContent, "Update weapon data from scripts");
                            swBatch.Stop();
                            if (bOk) { iDone++; Out($"OK  {sLink,-30}  {swBatch.ElapsedMilliseconds}ms"); }
                            else { iFail++; Out($"FAIL upload: {sLink,-30}"); }
                        }
                        catch (Exception ex)
                        {
                            iFail++;
                            Out($"ERR {sLink,-30}  {ex.Message}");
                            LogService.Error(ex, $"Wiki Batch Up: {sLink}");
                        }
                        lblStatus.Text = $"Up [{iDone + iFail}/{rgLinks.Count - iSkip}]";
                    }
                    Out(new string('-', 40));
                    Out($"Done: {iDone} ok, {iFail} fail, {iSkip} skip  {DateTime.Now:HH:mm:ss}");
                    LogService.Info($"Wiki Batch Up done: {iDone} ok, {iFail} fail, {iSkip} skip");
                }
                ToggleBatch(); ExitCancel(btnBatchDR, btnBatchDR.Text, btnBatchDR.BackColor, ehCancel);
            }
            catch (OperationCanceledException) { lblStatus.Text = "Batch cancelled"; ExitCancel(btnBatchDR, bBatchDryDone ? "BatchUp" : "BatchDR", bBatchDryDone ? cWikiActive : cWikiInactive, ehCancel); }
            finally { ctsBatch?.Dispose(); ctsBatch = null; }
        };

        if (bDarkMode)
        {
            frmDlg.BackColor = Color.FromArgb(32, 32, 32);
            frmDlg.ForeColor = Color.FromArgb(240, 240, 240);
        }

        var ttTooltip = new ToolTip();
        ttTooltip.SetToolTip(txtPage, "Wiki page name (e.g. AK-47) or script name (e.g. AK47, weapon_akm)\nPaste a full URL to auto extract the page name");
        ttTooltip.SetToolTip(btnFetch, "Fetch page source from the wiki");
        ttTooltip.SetToolTip(btnDryRun, "Dry run: convert local scripts and preview changes\nClick again to upload");
        ttTooltip.SetToolTip(btnBatchDR, "Batch process all weapons linked from the current page\nClick again to upload all");
        ttTooltip.SetToolTip(btnGenerate, "Generate new weapon pages from game script data\nClick again to upload generated pages");
        ttTooltip.SetToolTip(chkOverwriteExisting, "Include existing wiki pages when generating");
        ttTooltip.SetToolTip(chkSkipCached, "Skip pages already saved in the wiki folder");
        ttTooltip.SetToolTip(btnSelectDir, "Select the scripts folder (e.g. .../vietnam/scripts)");
        ttTooltip.SetToolTip(btnConvert, "Convert the current source using script data");
        ttTooltip.SetToolTip(btnCopy, "Copy result to clipboard");
        ttTooltip.SetToolTip(btnReset, "Reset all wiki fields to defaults");

        frmDlg.Controls.AddRange(new Control[] {
            lblPage, txtPage, btnFetch, lblStatus,
            lblUser, txtUser, lblPw, txtPw, btnDryRun, btnBatchDR, btnGenerate, chkOverwriteExisting,
            lblInput, txtInput, lblOutput, txtOutput,
            btnSelectDir, lblDir, chkSkipCached, btnConvert, btnCopy, btnReset
        });

        if (bDarkMode)
        {
            foreach (Control ctrl in frmDlg.Controls)
            {
                if (ctrl is TextBox tb) { tb.BackColor = Color.FromArgb(50, 50, 50); tb.ForeColor = Color.FromArgb(240, 240, 240); }
                else if (ctrl is Button btn) { btn.BackColor = Color.FromArgb(60, 60, 60); btn.ForeColor = Color.FromArgb(240, 240, 240); btn.FlatStyle = FlatStyle.Flat; btn.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80); }
                else if (ctrl is Label lbl) { lbl.ForeColor = Color.FromArgb(240, 240, 240); }
                else if (ctrl is CheckBox chk) { chk.ForeColor = Color.FromArgb(240, 240, 240); }
            }
        }

        frmDlg.FormClosing += (_, _) =>
        {
            if (ctsDryRun != null) { ctsDryRun.Cancel(); ctsDryRun.Dispose(); ctsDryRun = null; }
            if (ctsBatch != null) { ctsBatch.Cancel(); ctsBatch.Dispose(); ctsBatch = null; }
            if (ctsGen != null) { ctsGen.Cancel(); ctsGen.Dispose(); ctsGen = null; }
        };

        if (bDarkMode)
        {
            try
            {
                int iUseDark = 1;
                DwmSetWindowAttribute(frmDlg.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref iUseDark, sizeof(int));
            }
            catch { }
        }
        frmDlg.ShowDialog(this);
    }

    private static async Task<bool> EnsureLogin(string sUser, string sPw, Label lblStatus)
    {
        if (WikiApiService.IsLoggedIn) return true;
        if (string.IsNullOrWhiteSpace(sUser) || string.IsNullOrWhiteSpace(sPw))
        {
            lblStatus.Text = "Please enter username and passwd";
            return false;
        }
        lblStatus.Text = "Logging in...";
        if (!await WikiApiService.LoginAsync(sUser, sPw)) { lblStatus.Text = "Login failed"; return false; }
        sLastWikiUser = sUser;
        sLastWikiPw = sPw;
        lblStatus.Text = "Logged in";
        LogService.Info("Wiki EnsureLogin: logged in");
        return true;
    }
}