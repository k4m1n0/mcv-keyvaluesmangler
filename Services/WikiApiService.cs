using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace WeaponDamageCalc.Services;

public static class WikiApiService
{
    private static readonly HttpClient hcClient = new()
    {
        BaseAddress = new Uri("https://wiki.militaryconflictvietnam.com"),
        Timeout = TimeSpan.FromSeconds(30)
    };

    private static string? sCsrfToken;

    public static bool IsLoggedIn => sCsrfToken != null;

    public static async Task<bool> LoginAsync(string sUsername, string sPassword)
    {
        sCsrfToken = null;
        LogService.Info("Wiki login attempt");

        try
        {
            using var respLoginToken = await hcClient.GetAsync(
                "/api.php?action=query&meta=tokens&type=login&format=json");
            var sLoginTokenJson = await respLoginToken.Content.ReadAsStringAsync();
            string? sLoginToken;
            using (var jdocLogin = JsonDocument.Parse(sLoginTokenJson))
                sLoginToken = jdocLogin.RootElement.GetProperty("query").GetProperty("tokens")
                    .GetProperty("logintoken").GetString();

            if (sLoginToken == null)
            {
                LogService.Error("Wiki login failed: could not get login token");
                return false;
            }

            var contentLogin = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["action"] = "login", ["lgname"] = sUsername, ["lgpassword"] = sPassword,
                ["lgtoken"] = sLoginToken, ["format"] = "json"
            });
            using var respLogin = await hcClient.PostAsync("/api.php", contentLogin);
            var sLoginResult = await respLogin.Content.ReadAsStringAsync();
            using var jdocLoginResult = JsonDocument.Parse(sLoginResult);
            if (jdocLoginResult.RootElement.GetProperty("login").GetProperty("result").GetString() != "Success")
            {
                LogService.Error("Wiki login failed: invalid credentials");
                return false;
            }

            using var respCsrf = await hcClient.GetAsync(
                "/api.php?action=query&meta=tokens&type=csrf&format=json");
            var sCsrfJson = await respCsrf.Content.ReadAsStringAsync();
            using var jdocCsrf = JsonDocument.Parse(sCsrfJson);
            sCsrfToken = jdocCsrf.RootElement.GetProperty("query").GetProperty("tokens")
                .GetProperty("csrftoken").GetString();

            LogService.Info("Wiki login successful");
            return true;
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "WikiApiService.LoginAsync");
            return false;
        }
    }

    public static void Logout() => sCsrfToken = null;

    public static async Task<string?> GetPageSourceAsync(string sPageTitle)
    {
        return await GetPageSourceInternalAsync(sPageTitle, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    private static async Task<string?> GetPageSourceInternalAsync(string sPageTitle, HashSet<string> hsVisited)
    {
        if (!hsVisited.Add(sPageTitle)) return null;

        try
        {
            using var resp = await hcClient.GetAsync(
                $"/api.php?action=parse&page={Uri.EscapeDataString(sPageTitle)}&prop=wikitext&redirects=1&format=json");
            var sJson = await resp.Content.ReadAsStringAsync();
            using var jdoc = JsonDocument.Parse(sJson);

            if (jdoc.RootElement.TryGetProperty("error", out _))
            {
                string? sRaw = await GetRawPageContentAsync(sPageTitle);
                if (sRaw == null) return null;
                if (sRaw.TrimStart().StartsWith("#REDIRECT", StringComparison.OrdinalIgnoreCase))
                {
                    //匹配MediaWiki重定向 #REDIRECT [[目标页]]捕获跳转目标标题
                    var mRedirect = Regex.Match(sRaw, @"#REDIRECT\s*\[\[([^\]|]+)");
                    if (mRedirect.Success)
                    {
                        string sRedirectTarget = mRedirect.Groups[1].Value.Trim();
                        LogService.Info($"Wiki page redirect: {sPageTitle} -> {sRedirectTarget}");
                        return await GetPageSourceInternalAsync(sRedirectTarget, hsVisited);
                    }
                }
                return sRaw;
            }

            return jdoc.RootElement.GetProperty("parse").GetProperty("wikitext").GetProperty("*").GetString();
        }
        catch (Exception ex)
        {
            LogService.Error(ex, $"WikiApiService.GetPageSourceInternalAsync: {sPageTitle}");
            return null;
        }
    }

    private static async Task<string?> GetRawPageContentAsync(string sPageTitle)
    {
        using var resp = await hcClient.GetAsync(
            $"/index.php?title={Uri.EscapeDataString(sPageTitle)}&action=raw");
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadAsStringAsync();
    }

    public static async Task<bool> SavePageAsync(string sPageTitle, string sWikitext, string sSummary)
    {
        if (sCsrfToken == null)
        {
            LogService.Error("WikiApiService.SavePageAsync: not logged in");
            return false;
        }

        try
        {
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["action"] = "edit", ["title"] = sPageTitle, ["text"] = sWikitext,
                ["summary"] = sSummary, ["token"] = sCsrfToken, ["format"] = "json", ["bot"] = "1"
            });

            using var resp = await hcClient.PostAsync("/api.php", content);
            var sJson = await resp.Content.ReadAsStringAsync();
            using var jdoc = JsonDocument.Parse(sJson);
            bool bSuccess = jdoc.RootElement.TryGetProperty("edit", out var jelEdit)
                && jelEdit.TryGetProperty("result", out var jelResult)
                && jelResult.GetString() == "Success";

            if (bSuccess)
                LogService.Info($"Wiki page saved: {sPageTitle}");
            else
                LogService.Error($"Wiki page save failed: {sPageTitle}");

            return bSuccess;
        }
        catch (Exception ex)
        {
            LogService.Error(ex, $"WikiApiService.SavePageAsync: {sPageTitle}");
            return false;
        }
    }

    public static async Task<bool> IsSameContentAsync(string sPageTitle, string sLocalContent)
    {
        var sSource = await GetPageSourceAsync(sPageTitle);
        if (sSource == null) return true;
        return sSource.Replace("\r\n", "\n").Trim() == sLocalContent.Replace("\r\n", "\n").Trim();
    }

    public static async Task<string?> FetchTemplateAsync(string sUrl)
    {
        try
        {
            LogService.Info($"Fetching template: {sUrl}");
            var resp = await hcClient.GetAsync(sUrl);
            if (!resp.IsSuccessStatusCode)
            {
                LogService.Warn($"FetchTemplateAsync failed: HTTP {(int)resp.StatusCode} - {sUrl}");
                return null;
            }
            return await resp.Content.ReadAsStringAsync();
        }
        catch (Exception ex)
        {
            LogService.Error(ex, $"WikiApiService.FetchTemplateAsync: {sUrl}");
            return null;
        }
    }

    public static async Task<HashSet<string>> GetExistingTitlesAsync(IEnumerable<string> rgTitles)
    {
        var hsExisting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rgList = rgTitles.ToList();
        LogService.Info($"Checking {rgList.Count} titles on wiki...");
        using var semSlim = new SemaphoreSlim(3);

        async Task ProcessBatch(IEnumerable<string> rgBatch)
        {
            await semSlim.WaitAsync();
            try
            {
                string sApiUrl = $"/api.php?action=query&titles={Uri.EscapeDataString(string.Join("|", rgBatch))}&format=json&formatversion=2&redirects=1";

                for (int iRetry = 0; iRetry < 3; iRetry++)
                {
                    try
                    {
                        var sJson = await (await hcClient.GetAsync(sApiUrl)).Content.ReadAsStringAsync();
                        using var jdoc = JsonDocument.Parse(sJson);
                        if (jdoc.RootElement.TryGetProperty("query", out var jelQ) && jelQ.TryGetProperty("pages", out var jelPages))
                            lock (hsExisting)
                                foreach (var jelPage in jelPages.EnumerateArray())
                                    if (!jelPage.TryGetProperty("missing", out _))
                                    {
                                        var sTitle = jelPage.GetProperty("title").GetString()!;
                                        hsExisting.Add(sTitle); hsExisting.Add(sTitle.Replace("_", " "));
                                    }
                        break;
                    }
                    catch (Exception ex) when (iRetry < 2)
                    {
                        LogService.Warn($"GetExistingTitlesAsync retry {iRetry + 1}/3: {ex.Message}");
                        await Task.Delay(3000);
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "WikiApiService.GetExistingTitlesAsync batch");
            }
            finally { semSlim.Release(); }
        }

        await Task.WhenAll(rgList.Chunk(20).Select(ProcessBatch));
        LogService.Info($"Existing titles found: {hsExisting.Count}");
        return hsExisting;
    }
}