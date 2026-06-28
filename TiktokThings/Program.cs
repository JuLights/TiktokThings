using LiveStatusChecker;
using LiveRecorder;
using System.Text.Json;

namespace TiktokThings;

public class Program
{
    private static string? _userName;
    private static string? _roomId;
    private static LiveChecker? _liveChecker;
    private static LiveRec? _liveRec;

    static async Task Main(string[] args)
    {
        Console.WriteLine("Username:");

        _userName = Console.ReadLine();

        _liveChecker = new LiveChecker(new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        });


        if (string.IsNullOrWhiteSpace(_userName))
        {
            Console.WriteLine("username is empty");
            return;
        }

        _liveRec = new LiveRec(_userName);

        bool isLive = false;

        while (isLive == false)
        {
            await Task.Delay(10000);

            _roomId = await _liveChecker.GetRoomIdAsync(_userName);

            isLive = await _liveChecker.CheckLiveStatus(_roomId);
        }

        if (isLive)
            await StartRecording();


    }

    private async static Task StartRecording()
    {
        _ = Task.Run(async () =>
        {
            await _liveRec!.InitAsync();
        });
    }

}
