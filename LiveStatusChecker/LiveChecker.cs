using System.Text.Json;
using System.Text.RegularExpressions;

namespace LiveStatusChecker;

public class LiveChecker
{
    private static HttpClient? _httpClient;

    public LiveChecker(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<string> GetRoomIdAsync(string username)
    {
        string roomId = string.Empty;
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"https://www.tiktok.com/@{username}/live");
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

            var response = await _httpClient.SendAsync(request);
            string html = await response.Content.ReadAsStringAsync();

            var match = Regex.Match(html, @"""roomId""\s*:\s*""?(\d+)""?");
            if (match.Success)
            {
                roomId = match.Groups[1].Value;
            }


            if (string.IsNullOrEmpty(roomId))
            {
                Console.WriteLine("No room_id found, writing offline");
                await WriteStatusAsync(false, "", 0);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Profile fetch error: {ex.Message}");
        }

        return roomId;
    }

    public async Task<bool> CheckLiveStatus(string roomId)
    {
        try
        {
            var apiRequest = new HttpRequestMessage(HttpMethod.Get, $"https://webcast.tiktok.com/webcast/room/info/?room_id={roomId}&aid=1988");
            apiRequest.Headers.Add("User-Agent", "Mozilla/5.0");

            var apiResponse = await _httpClient.SendAsync(apiRequest);
            string jsonString = await apiResponse.Content.ReadAsStringAsync();

            // Parse the JSON response
            using JsonDocument doc = JsonDocument.Parse(jsonString);
            var root = doc.RootElement;

            if (root.TryGetProperty("data", out var data))
            {
                int status = data.TryGetProperty("status", out var s) ? s.GetInt32() : 0;
                int userCount = data.TryGetProperty("user_count", out var uc) ? uc.GetInt32() : 0;
                string title = data.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";

                // Clean title string
                title = title.Replace("\"", "").Replace("\n", "");

                bool isLive = status == 2;

                await WriteStatusAsync(isLive, title, userCount);
                Console.WriteLine($"DateTime: {DateTime.Now.ToString("MM/dd HH:mm:ss")} | RoomID: {roomId} | Live: {isLive} | Status: {status} | Viewers: {userCount}");

                return isLive; 
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"API fetch error: {ex.Message}");
        }

        return true;
    }


    private async Task WriteStatusAsync(bool isLive, string title, int viewerCount)
    {
        var result = new
        {
            isLive = isLive,
            title = title,
            viewerCount = viewerCount,
            updatedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
        };

        string jsonString = JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        await File.WriteAllTextAsync("status.json", jsonString);
    }
}
