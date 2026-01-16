namespace BackupService;
using System;
using System.Threading.Tasks;

public class ServiceScheduler
{
    public static void StartServiceAtConfiguredTime(int hour, int minute, Action startAction)
    {
        Task.Run(async () =>
        {
            var now = DateTime.Now;
            var startTime = new DateTime(now.Year, now.Month, now.Day, hour, minute, 0);

            if (now > startTime)
            {
                startTime = startTime.AddDays(1);
            }

            var delay = startTime - now;

            Console.WriteLine($"Service will start at: {startTime} (in {delay.TotalMinutes:F1} minutes) ");

            await Task.Delay(delay);
            startAction();
        });
    }
}