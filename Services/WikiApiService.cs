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

        using var loginTokenResp = await Client.GetAsync(
            "/api.php?action=query&meta=tokens&type=login&format=json");
        var loginTokenJson = await loginTokenResp.Content.ReadAsStringAsync();
        string? loginToken;
        using (var doc = JsonDocument.Parse(loginTokenJson))
            loginToken = doc.RootElement.GetProperty("query").GetProperty("tokens")
                .GetProperty("logintoken").GetString();

        if (loginToken == null) return false;

        var loginContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["action"] = "login", ["lgname"] = username, ["lgpassword"] = password,
            ["lgtoken"] = loginToken, ["format"] = "json"
        });
        using var loginResp = await Client.PostAsync("/api.php", loginContent);
        var loginResult = await loginResp.Content.ReadAsStringAsync();
        using var loginDoc = JsonDocument.Parse(loginResult);
        if (loginDoc.RootElement.GetProperty("login").GetProperty("result").GetString() != "Success")
            return false;

        using var csrfResp = await Client.GetAsync(
            "/api.php?action=query&meta=tokens&type=csrf&format=json");
        var csrfJson = await csrfResp.Content.ReadAsStringAsync();
        using var csrfDoc = JsonDocument.Parse(csrfJson);
        _csrfToken = csrfDoc.RootElement.GetProperty("query").GetProperty("tokens")
            .GetProperty("csrftoken").GetString();

        return true;
    }

    public static void Logout() => _csrfToken = null;

    public static async Task<string?> GetPageSourceAsync(string pageTitle)
    {
        return await GetPageSourceInternalAsync(pageTitle, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    private static async Task<string?> GetPageSourceInternalAsync(string pageTitle, HashSet<string> visited)
    {
        if (!visited.Add(pageTitle)) return null;

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
                    return await GetPageSourceInternalAsync(redirectMatch.Groups[1].Value.Trim(), visited);
            }
            return raw;
        }

        return doc.RootElement.GetProperty("parse").GetProperty("wikitext").GetProperty("*").GetString();
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
        if (_csrfToken == null) return false;

        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["action"] = "edit", ["title"] = pageTitle, ["text"] = wikitext,
            ["summary"] = summary, ["token"] = _csrfToken, ["format"] = "json", ["bot"] = "1"
        });

        using var resp = await Client.PostAsync("/api.php", content);
        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("edit", out var edit)
            && edit.TryGetProperty("result", out var result)
            && result.GetString() == "Success";
    }

    public static async Task<bool> IsSameContentAsync(string pageTitle, string localContent)
    {
        var source = await GetPageSourceAsync(pageTitle);
        if (source == null) return true;
        return source.Replace("\r\n", "\n").Trim() == localContent.Replace("\r\n", "\n").Trim();
    }
}