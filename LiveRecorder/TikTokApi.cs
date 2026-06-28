using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LiveRecorder;

public class TikTokApi : IDisposable
{
    private const string BaseUrl = "https://www.tiktok.com";
    private const string WebcastUrl = "https://webcast.tiktok.com";
    private const string TikRecApi = "https://tikrec.com";

    private readonly HttpClient _client;
    private readonly HttpClientHandler _handler;

    public TikTokApi(string? proxyUrl = null)
    {
        _handler = new HttpClientHandler { UseCookies = true };

        if (!string.IsNullOrEmpty(proxyUrl))
        {
            Console.WriteLine($"[API] Testing proxy {proxyUrl}...");
            _handler.Proxy = new WebProxy(proxyUrl);
        }

        _client = new HttpClient(_handler);

        // Replicating headers
        _client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.6478.127 Safari/537.36");
        _client.DefaultRequestHeaders.Add("Accept-Language", "en-US");
        _client.DefaultRequestHeaders.Add("Origin", BaseUrl);
        _client.DefaultRequestHeaders.Add("Referer", BaseUrl + "/");
    }

    public async Task<bool> IsRoomAliveAsync(string roomId)
    {
        if (string.IsNullOrEmpty(roomId)) return false;

        string url = $"{WebcastUrl}/webcast/room/check_alive/?aid=1988&region=CH&room_ids={roomId}&user_is_login=true";
        string json = await _client.GetStringAsync(url);

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("data", out var dataArr) && dataArr.GetArrayLength() > 0)
        {
            return dataArr[0].TryGetProperty("alive", out var alive) && alive.GetBoolean();
        }
        return false;
    }

    public async Task<(string? user, string? roomId)> GetRoomAndUserFromUrlAsync(string liveUrl)
    {
        string? user = null;
        var match = Regex.Match(liveUrl, @"https?://(?:www\.)?tiktok\.com/@([^/]+)/live");
        if (match.Success)
        {
            user = match.Groups[1].Value;
        }

        string? roomId = user != null ? await GetRoomIdFromUserAsync(user) : null;
        return (user, roomId);
    }

    public async Task<string?> GetRoomIdFromUserAsync(string user)
    {
        // TikRec route
        try
        {
            string signUrl = $"{TikRecApi}/tiktok/room/api/sign?unique_id={user}";
            string signJson = await _client.GetStringAsync(signUrl);
            using var signDoc = JsonDocument.Parse(signJson);

            if (signDoc.RootElement.TryGetProperty("signed_path", out var path))
            {
                string fullUrl = BaseUrl + path.GetString();
                string finalJson = await _client.GetStringAsync(fullUrl);

                using var doc = JsonDocument.Parse(finalJson);
                if (doc.RootElement.TryGetProperty("data", out var d) &&
                    d.TryGetProperty("user", out var u) &&
                    u.TryGetProperty("roomId", out var roomId))
                {
                    return roomId.ToString();
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[API] Failed retrieving room_id for @{user}: {ex.Message}");
        }
        return null;
    }

    public async Task<string?> GetLiveUrlAsync(string roomId)
    {
        string url = $"{WebcastUrl}/webcast/room/info/?aid=1988&room_id={roomId}";
        string json = await _client.GetStringAsync(url);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (json.Contains("This account is private"))
            throw new Exception("ACCOUNT_PRIVATE");

        if (!root.TryGetProperty("data", out var data) || !data.TryGetProperty("stream_url", out var streamUrl))
            return null;

        // Deep JSON extraction matching the Python sdk_data loop
        if (streamUrl.TryGetProperty("live_core_sdk_data", out var sdkNode) &&
            sdkNode.TryGetProperty("pull_data", out var pullData))
        {
            // 1. Build Quality Level Map
            var levelMap = new Dictionary<string, int>();
            if (pullData.TryGetProperty("options", out var options) && options.TryGetProperty("qualities", out var qualities))
            {
                foreach (var q in qualities.EnumerateArray())
                {
                    if (q.TryGetProperty("sdk_key", out var key) && q.TryGetProperty("level", out var level))
                        levelMap[key.GetString()!] = level.GetInt32();
                }
            }

            // 2. Extract best FLV from stringified JSON block
            if (pullData.TryGetProperty("stream_data", out var streamDataStr))
            {
                string nestedJson = streamDataStr.GetString() ?? "{}";
                using var nestedDoc = JsonDocument.Parse(nestedJson);

                int bestLevel = -1;
                string? bestFlv = null;

                if (nestedDoc.RootElement.TryGetProperty("data", out var streamData))
                {
                    foreach (var prop in streamData.EnumerateObject())
                    {
                        int level = levelMap.GetValueOrDefault(prop.Name, -1);
                        if (level > bestLevel && prop.Value.TryGetProperty("main", out var main))
                        {
                            if (main.TryGetProperty("flv", out var flv))
                            {
                                bestLevel = level;
                                bestFlv = flv.GetString();
                            }
                        }
                    }
                }
                if (!string.IsNullOrEmpty(bestFlv)) return bestFlv;
            }
        }

        // Fallback URLs
        if (streamUrl.TryGetProperty("flv_pull_url", out var flvUrls))
        {
            string[] fallbacks = { "FULL_HD1", "HD1", "SD2", "SD1" };
            foreach (var fb in fallbacks)
            {
                if (flvUrls.TryGetProperty(fb, out var u) && !string.IsNullOrEmpty(u.GetString()))
                    return u.GetString();
            }
        }

        return streamUrl.TryGetProperty("rtmp_pull_url", out var rtmp) ? rtmp.GetString() : null;
    }

    // Exposing raw HttpClient for streaming download
    public HttpClient RawClient => _client;

    public void Dispose()
    {
        _client.Dispose();
        _handler.Dispose();
    }
}