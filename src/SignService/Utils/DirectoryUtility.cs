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
            return PerformSafeActionAsync((dir) => DeleteDirectoryAsync(dir), path);
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

        static Task DeleteDirectoryAsync(string fullPath)
        {
            try
            {
                if (!Directory.Exists(fullPath))
                {
                    return Task.CompletedTask;
                }

                Directory.Delete(fullPath, recursive: true);

                // Check if directory still exists, if it does we will wait for a little
                return Directory.Exists(fullPath) ?
                    WaitForDeletion(fullPath) :
                    Task.CompletedTask;
            }
            catch (Exception ex)
            {
                // async will wrap the exception in a Task, however this method is not async.
                // So manually wrap the exception so we can still check the Task for failure.
                return Task.FromException(ex);
            }

            async Task WaitForDeletion(string path)
            {
                // The directory is not guaranteed to be gone since there could be
                // other open handles. Wait, up to half a second, until the directory is gone.
                for (var i = 0; Directory.Exists(path) && i < 5; ++i)
                {
                    await Task.Delay(100);
                }
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

        static async Task PerformSafeActionAsync<TState>(Func<TState, Task> action, TState state)
        {
            try
            {
                await AttemptAsync(action, state);
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

        static Task AttemptAsync<TState>(Func<TState, Task> action, TState state, int retries = 3, int delayBeforeRetry = 150)
        {
            var task = action(state);
            return (!task.IsCompletedSuccessfully && retries > 0) ?
                ReAttemptAsync(task, action, state, retries, delayBeforeRetry) :
                task;
        }

        static async Task ReAttemptAsync<TState>(Task task, Func<TState, Task> action, TState state, int retries, int delayBeforeRetry)
        {
            // Wait for the current attempt to complete
            try
            {
                await task;
                return;
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

            // Start retries
            do
            {
                try
                {
                    await action(state);
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
            } while (retries > 0);
        }
    }
}
