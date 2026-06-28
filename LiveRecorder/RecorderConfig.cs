namespace LiveRecorder;
public class RecorderConfig
{
    public string? Url { get; set; }
    public string? User { get; set; }
    public string? RoomId { get; set; }
    public RecorderMode Mode { get; set; } = RecorderMode.Manual;
    public int AutomaticIntervalMinutes { get; set; } = 3;
    public int? DurationSeconds { get; set; } = null;
    public string OutputDirectory { get; set; } = "";
    public string? Proxy { get; set; }
}