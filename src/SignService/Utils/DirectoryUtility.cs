using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights;

namespace SignService.Utils
{
    public static class DirectoryUtility
    {
        static readonly TelemetryClient tc = new TelemetryClient();

        public static void SafeDelete(string path)
        {
            PerformSafeAction(() => DeleteDirectory(path));
        }

        public static Task SafeDeleteAsync(string path)
        {
            return PerformSafeActionAsync(() => DeleteDirectoryAsync(path));
        }

        // Deletes an empty folder from disk and the project
        static void DeleteDirectory(string fullPath)
        {
            if (!Directory.Exists(fullPath))
            {
                return;
            }

            Directory.Delete(fullPath, recursive: true);

            // The directory is not guaranteed to be gone since there could be
            // other open handles. Wait, up to half a second, until the directory is gone.
            for (var i = 0; Directory.Exists(fullPath) && i < 5; ++i)
            {
                Thread.Sleep(100);
            }
        }

        static async Task DeleteDirectoryAsync(string fullPath)
        {
            if (!Directory.Exists(fullPath))
            {
                return;
            }

            Directory.Delete(fullPath, recursive: true);

            // The directory is not guaranteed to be gone since there could be
            // other open handles. Wait, up to half a second, until the directory is gone.
            for (var i = 0; Directory.Exists(fullPath) && i < 5; ++i)
            {
                await Task.Delay(100);
            }
        }

        static void PerformSafeAction(Action action)
        {
            try
            {
                Attempt(action);
            }
            catch (Exception e)
            {
                tc.TrackException(e);
            }
        }

        static async Task PerformSafeActionAsync(Func<Task> action)
        {
            try
            {
                await AttemptAsync(action);
            }
            catch (Exception e)
            {
                tc.TrackException(e);
            }
        }

        static void Attempt(Action action, int retries = 3, int delayBeforeRetry = 150)
        {
            while (retries > 0)
            {
                try
                {
                    action();
                    break;
                }
                catch
                {
                    retries--;
                    if (retries == 0)
                    {
                        throw;
                    }
                }
                Thread.Sleep(delayBeforeRetry);
            }
        }

        static async Task AttemptAsync(Func<Task> action, int retries = 3, int delayBeforeRetry = 150)
        {
            while (retries > 0)
            {
                try
                {
                    await action();
                    break;
                }
                catch
                {
                    retries--;
                    if (retries == 0)
                    {
                        throw;
                    }
                }
                await Task.Delay(delayBeforeRetry);
            }
        }
    }
}
