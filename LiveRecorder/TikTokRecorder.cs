
using System.Collections.Concurrent;
using System.Diagnostics;

namespace LiveRecorder;

public class TikTokRecorder
{
    private readonly RecorderConfig _config;
    private TikTokApi _api;

    // Tracks active recordings in Followers Mode
    private readonly ConcurrentDictionary<string, Task> _activeRecordings = new();

    public TikTokRecorder(RecorderConfig config)
    {
        _config = config;
        _api = new TikTokApi(config.Proxy);
    }

    private async Task SetupAsync()
    {
        if (_config.Mode != RecorderMode.Followers && !string.IsNullOrEmpty(_config.Url))
        {
            var (user, roomId) = await _api.GetRoomAndUserFromUrlAsync(_config.Url);
            _config.User = user ?? _config.User;
            _config.RoomId = roomId ?? _config.RoomId;
        }

        if (!string.IsNullOrEmpty(_config.User) && string.IsNullOrEmpty(_config.RoomId))
        {
            _config.RoomId = await _api.GetRoomIdFromUserAsync(_config.User);
        }

        Console.WriteLine($"[Setup] USER: {_config.User} | ROOM: {_config.RoomId}");

        // Switch to direct connection for recording
        if (!string.IsNullOrEmpty(_config.Proxy))
        {
            _api.Dispose();
            _api = new TikTokApi();
        }
    }

    public async Task RunAsync()
    {
        await SetupAsync();

        switch (_config.Mode)
        {
            case RecorderMode.Manual:
                await ManualModeAsync();
                break;
            case RecorderMode.Automatic:
                await AutomaticModeAsync();
                break;
            case RecorderMode.Followers:
                await FollowersModeAsync(new[] { "streamer1", "streamer2" });
                break;
        }
    }

    private async Task ManualModeAsync()
    {
        if (string.IsNullOrEmpty(_config.RoomId) || !await _api.IsRoomAliveAsync(_config.RoomId))
        {
            throw new Exception($"@{_config.User} is not currently live.");
        }
        await StartRecordingAsync(_config.User!, _config.RoomId);
    }

    private async Task AutomaticModeAsync()
    {
        while (true)
        {
            try
            {
                _config.RoomId = await _api.GetRoomIdFromUserAsync(_config.User!);
                await ManualModeAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Auto] {ex.Message}");
                Console.WriteLine($"Waiting {_config.AutomaticIntervalMinutes} mins...");
                await Task.Delay(TimeSpan.FromMinutes(_config.AutomaticIntervalMinutes));
            }
        }
    }

    private async Task FollowersModeAsync(string[] followers)
    {
        while (true)
        {
            foreach (var follower in followers)
            {
                // Clean up finished tasks
                if (_activeRecordings.TryGetValue(follower, out var t) && t.IsCompleted)
                {
                    Console.WriteLine($"Recording of @{follower} finished.");
                    _activeRecordings.TryRemove(follower, out _);
                }

                if (_activeRecordings.ContainsKey(follower)) continue;

                try
                {
                    string? roomId = await _api.GetRoomIdFromUserAsync(follower);
                    if (roomId != null && await _api.IsRoomAliveAsync(roomId))
                    {
                        Console.WriteLine($"@{follower} is live. Starting recording...");

                        // Launch non-blocking background task
                        var recordingTask = Task.Run(() => StartRecordingAsync(follower, roomId));
                        _activeRecordings.TryAdd(follower, recordingTask);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Followers] Error on @{follower}: {ex.Message}");
                }
                await Task.Delay(2500); // Stagger network requests
            }

            Console.WriteLine($"Waiting {_config.AutomaticIntervalMinutes} mins for next sweep...");
            await Task.Delay(TimeSpan.FromMinutes(_config.AutomaticIntervalMinutes));
        }
    }

    private async Task StartRecordingAsync(string user, string roomId)
    {
        string? liveUrl = await _api.GetLiveUrlAsync(roomId);
        if (string.IsNullOrEmpty(liveUrl))
        {
            throw new Exception("Could not retrieve live URL.");
        }

        string filename = $"TK_{user}_{DateTime.Now:yyyy.MM.dd_HH-mm-ss}_flv.mp4";
        string output = Path.Combine(_config.OutputDirectory, filename);

        Console.WriteLine($"Started recording to {output}...");

        // 512 KB buffer limit
        var buffer = new byte[512 * 1024];
        long startTime = Environment.TickCount64;

        try
        {
            // Streaming connection
            using var response = await _api.RawClient.GetAsync(liveUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            using var stream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(output, FileMode.Create, FileAccess.Write, FileShare.None);

            int bytesRead;
            bool stopRecording = false;

            while (!stopRecording && (bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead);

                // Check Duration Limit
                if (_config.DurationSeconds.HasValue)
                {
                    long elapsed = (Environment.TickCount64 - startTime) / 1000;
                    if (elapsed >= _config.DurationSeconds.Value) stopRecording = true;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Recording error: {ex.Message}");
        }

        Console.WriteLine($"Recording finished: {Path.GetFullPath(output)}");

        // Trigger post-processing conversion
        ConvertFlvToMp4(output);
    }

    private void ConvertFlvToMp4(string filePath)
    {
        try
        {
            string fixedPath = filePath.Replace("_flv.mp4", "_fixed.mp4");
            var psi = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-y -i \"{filePath}\" -c copy -bsf:a aac_adtstoasc \"{fixedPath}\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            Console.WriteLine("[FFmpeg] Fixing FLV metadata containers...");
            using var process = Process.Start(psi);
            process?.WaitForExit();

            if (File.Exists(fixedPath))
            {
                File.Delete(filePath);
                File.Move(fixedPath, filePath);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FFmpeg] Conversion skipped or failed: {ex.Message}");
        }
    }
}