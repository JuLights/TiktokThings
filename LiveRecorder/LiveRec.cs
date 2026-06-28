namespace LiveRecorder;

public class LiveRec
{
    private RecorderConfig _config;
    public LiveRec(string username)
    {
        _config = new RecorderConfig
        {
            User = username,
            Mode = RecorderMode.Manual,
            OutputDirectory = Environment.CurrentDirectory
        };
    }

    public async Task InitAsync()
    {
        var recorder = new TikTokRecorder(_config);
        await recorder.RunAsync();
    }
}
