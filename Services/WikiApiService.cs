using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace WeaponDamageCalc.Services;

public static class WikiApiService
{
    private static readonly HttpClient Client = new()
    {
        BaseAddress = new Uri("https://wiki.militaryconflictvietnam.com"),
        Timeout = TimeSpan.FromSeconds(30)
    };

    private static string? _csrfToken;

    public static bool IsLoggedIn => _csrfToken != null;

    public static async Task<bool> LoginAsync(string username, string password)
    {
        _csrfToken = null;
        LogService.Info("Wiki login attempt");

        try
        {
            using var loginTokenResp = await Client.GetAsync(
                "/api.php?action=query&meta=tokens&type=login&format=json");
            var loginTokenJson = await loginTokenResp.Content.ReadAsStringAsync();
            string? loginToken;
            using (var doc = JsonDocument.Parse(loginTokenJson))
                loginToken = doc.RootElement.GetProperty("query").GetProperty("tokens")
                    .GetProperty("logintoken").GetString();

            if (loginToken == null)
            {
                LogService.Error("Wiki login failed: could not get login token");
                return false;
            }

        var loginContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["action"] = "login", ["lgname"] = username, ["lgpassword"] = password,
            ["lgtoken"] = loginToken, ["format"] = "json"
        });
        using var loginResp = await Client.PostAsync("/api.php", loginContent);
        var loginResult = await loginResp.Content.ReadAsStringAsync();
        using var loginDoc = JsonDocument.Parse(loginResult);
        if (loginDoc.RootElement.GetProperty("login").GetProperty("result").GetString() != "Success")
        {
            LogService.Error("Wiki login failed: invalid credentials");
            return false;
        }

        using var csrfResp = await Client.GetAsync(
            "/api.php?action=query&meta=tokens&type=csrf&format=json");
        var csrfJson = await csrfResp.Content.ReadAsStringAsync();
        using var csrfDoc = JsonDocument.Parse(csrfJson);
        _csrfToken = csrfDoc.RootElement.GetProperty("query").GetProperty("tokens")
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

    public static void Logout() => _csrfToken = null;

    public static async Task<string?> GetPageSourceAsync(string pageTitle)
    {
        return await GetPageSourceInternalAsync(pageTitle, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    private static async Task<string?> GetPageSourceInternalAsync(string pageTitle, HashSet<string> visited)
    {
        if (!visited.Add(pageTitle)) return null;

        try
        {
            using var resp = await Client.GetAsync(
                $"/api.php?action=parse&page={Uri.EscapeDataString(pageTitle)}&prop=wikitext&redirects=1&format=json");
            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("error", out _))
            {
                string? raw = await GetRawPageContentAsync(pageTitle);
                if (raw == null) return null;
                if (raw.TrimStart().StartsWith("#REDIRECT", StringComparison.OrdinalIgnoreCase))
                {
                    var redirectMatch = Regex.Match(raw, @"#REDIRECT\s*\[\[([^\]|]+)");
                    if (redirectMatch.Success)
                    {
                        string redirectTarget = redirectMatch.Groups[1].Value.Trim();
                        LogService.Info($"Wiki page redirect: {pageTitle} -> {redirectTarget}");
                        return await GetPageSourceInternalAsync(redirectTarget, visited);
                    }
                }
                return raw;
            }

            return doc.RootElement.GetProperty("parse").GetProperty("wikitext").GetProperty("*").GetString();
        }
        catch (Exception ex)
        {
            LogService.Error(ex, $"WikiApiService.GetPageSourceInternalAsync: {pageTitle}");
            return null;
        }
    }

    private static async Task<string?> GetRawPageContentAsync(string pageTitle)
    {
        using var resp = await Client.GetAsync(
            $"/index.php?title={Uri.EscapeDataString(pageTitle)}&action=raw");
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadAsStringAsync();
    }

    public static async Task<bool> SavePageAsync(string pageTitle, string wikitext, string summary)
    {
        if (_csrfToken == null)
        {
            LogService.Error("WikiApiService.SavePageAsync: not logged in");
            return false;
        }

        try
        {
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["action"] = "edit", ["title"] = pageTitle, ["text"] = wikitext,
                ["summary"] = summary, ["token"] = _csrfToken, ["format"] = "json", ["bot"] = "1"
            });

            using var resp = await Client.PostAsync("/api.php", content);
            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            bool success = doc.RootElement.TryGetProperty("edit", out var edit)
                && edit.TryGetProperty("result", out var result)
                && result.GetString() == "Success";

            if (success)
                LogService.Info($"Wiki page saved: {pageTitle}");
            else
                LogService.Error($"Wiki page save failed: {pageTitle}");

            return success;
        }
        catch (Exception ex)
        {
            LogService.Error(ex, $"WikiApiService.SavePageAsync: {pageTitle}");
            return false;
        }
    }

    public static async Task<bool> IsSameContentAsync(string pageTitle, string localContent)
    {
        var source = await GetPageSourceAsync(pageTitle);
        if (source == null) return true;
        return source.Replace("\r\n", "\n").Trim() == localContent.Replace("\r\n", "\n").Trim();
    }

    public static async Task<string?> FetchTemplateAsync(string url)
    {
        try
        {
            LogService.Info($"Fetching template: {url}");
            var resp = await Client.GetAsync(url);
            if (!resp.IsSuccessStatusCode)
            {
                LogService.Warn($"FetchTemplateAsync failed: HTTP {(int)resp.StatusCode} - {url}");
                return null;
            }
            return await resp.Content.ReadAsStringAsync();
        }
        catch (Exception ex)
        {
            LogService.Error(ex, $"WikiApiService.FetchTemplateAsync: {url}");
            return null;
        }
    }

    public static async Task<HashSet<string>> GetExistingTitlesAsync(IEnumerable<string> titles)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = titles.ToList();
        LogService.Info($"Checking {list.Count} titles on wiki...");
        using var semaphore = new SemaphoreSlim(3);

        async Task ProcessBatch(IEnumerable<string> batch)
        {
            await semaphore.WaitAsync();
            try
            {
                string apiUrl = $"/api.php?action=query&titles={Uri.EscapeDataString(string.Join("|", batch))}&format=json&formatversion=2&redirects=1";

                for (int retry = 0; retry < 3; retry++)
                {
                    try
                    {
                        var json = await (await Client.GetAsync(apiUrl)).Content.ReadAsStringAsync();
                        using var doc = JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("query", out var q) && q.TryGetProperty("pages", out var pages))
                            lock (existing)
                                foreach (var page in pages.EnumerateArray())
                                    if (!page.TryGetProperty("missing", out _))
                                    {
                                        var t = page.GetProperty("title").GetString()!;
                                        existing.Add(t); existing.Add(t.Replace("_", " "));
                                    }
                        break;
                    }
                    catch (Exception ex) when (retry < 2)
                    {
                        LogService.Warn($"GetExistingTitlesAsync retry {retry + 1}/3: {ex.Message}");
                        await Task.Delay(3000);
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "WikiApiService.GetExistingTitlesAsync batch");
            }
            finally { semaphore.Release(); }
        }

        await Task.WhenAll(list.Chunk(20).Select(ProcessBatch));
        LogService.Info($"Existing titles found: {existing.Count}");
        return existing;
    }
}