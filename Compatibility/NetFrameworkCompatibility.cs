using System.Diagnostics;
using System.Text;

namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }

    [AttributeUsage(AttributeTargets.All, Inherited = false)]
    internal sealed class RequiredMemberAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = false)]
    internal sealed class CompilerFeatureRequiredAttribute : Attribute
    {
        public CompilerFeatureRequiredAttribute(string featureName) => FeatureName = featureName;
        public string FeatureName { get; }
        public bool IsOptional { get; init; }
    }
}

namespace System.Diagnostics.CodeAnalysis
{
    [AttributeUsage(AttributeTargets.Constructor, Inherited = false)]
    internal sealed class SetsRequiredMembersAttribute : Attribute
    {
    }
}

namespace System
{
    public class TimeProvider
    {
        public static TimeProvider System { get; } = new();
        public virtual long TimestampFrequency => Stopwatch.Frequency;
        public virtual long GetTimestamp() => Stopwatch.GetTimestamp();

        public TimeSpan GetElapsedTime(long startingTimestamp) =>
            GetElapsedTime(startingTimestamp, GetTimestamp());

        public TimeSpan GetElapsedTime(long startingTimestamp, long endingTimestamp) =>
            TimeSpan.FromSeconds((endingTimestamp - startingTimestamp) / (double)TimestampFrequency);
    }
}

namespace System.Threading.Tasks
{
    public sealed class TaskCompletionSource
    {
        private readonly TaskCompletionSource<bool> _source;

        public TaskCompletionSource(TaskCreationOptions creationOptions = TaskCreationOptions.None) =>
            _source = new TaskCompletionSource<bool>(creationOptions);

        public Task Task => _source.Task;
        public void SetResult() => _source.SetResult(true);
        public bool TrySetResult() => _source.TrySetResult(true);
        public bool TrySetException(Exception exception) => _source.TrySetException(exception);
    }

    public static class TaskCompatibilityExtensions
    {
        public static Task WaitAsync(this Task task, CancellationToken cancellationToken) =>
            WaitWithCancellationAsync(task, cancellationToken);

        public static async Task<T> WaitAsync<T>(this Task<T> task, CancellationToken cancellationToken)
        {
            await WaitWithCancellationAsync(task, cancellationToken).ConfigureAwait(false);
            return await task.ConfigureAwait(false);
        }

        public static Task WaitAsync(this Task task, TimeSpan timeout) =>
            WaitWithTimeoutAsync(task, timeout);

        public static async Task<T> WaitAsync<T>(this Task<T> task, TimeSpan timeout)
        {
            await WaitWithTimeoutAsync(task, timeout).ConfigureAwait(false);
            return await task.ConfigureAwait(false);
        }

        private static async Task WaitWithCancellationAsync(Task task, CancellationToken cancellationToken)
        {
            if (task.IsCompleted || !cancellationToken.CanBeCanceled)
            {
                await task.ConfigureAwait(false);
                return;
            }

            var cancellation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (cancellationToken.Register(() => cancellation.TrySetCanceled()))
            {
                if (task != await Task.WhenAny(task, cancellation.Task).ConfigureAwait(false))
                {
                    await cancellation.Task.ConfigureAwait(false);
                }
            }

            await task.ConfigureAwait(false);
        }

        private static async Task WaitWithTimeoutAsync(Task task, TimeSpan timeout)
        {
            if (task.IsCompleted)
            {
                await task.ConfigureAwait(false);
                return;
            }

            using (var timeoutCancellation = new CancellationTokenSource())
            {
                timeoutCancellation.CancelAfter(timeout);
                try
                {
                    await WaitWithCancellationAsync(task, timeoutCancellation.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!task.IsCompleted)
                {
                    throw new TimeoutException();
                }
            }
        }
    }
}

namespace System
{
    public static class StringCompatibilityExtensions
    {
        public static bool Contains(this string source, string value, StringComparison comparisonType) =>
            source.IndexOf(value, comparisonType) >= 0;

        public static bool EndsWith(this string source, char value) =>
            source.Length != 0 && source[source.Length - 1] == value;

        public static string Replace(
            this string source,
            string oldValue,
            string newValue,
            StringComparison comparisonType)
        {
            var index = source.IndexOf(oldValue, comparisonType);
            if (index < 0)
            {
                return source;
            }

            var result = new StringBuilder(source.Length);
            var start = 0;
            while (index >= 0)
            {
                result.Append(source, start, index - start).Append(newValue);
                start = index + oldValue.Length;
                index = source.IndexOf(oldValue, start, comparisonType);
            }

            return result.Append(source, start, source.Length - start).ToString();
        }
    }
}

namespace System.IO
{
    public static class PathCompatibility
    {
        public static bool IsFullyQualified(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var root = Path.GetPathRoot(path);
            return !string.IsNullOrEmpty(root)
                && root.Length > 1
                && (root.EndsWith(Path.DirectorySeparatorChar) || root.EndsWith(Path.AltDirectorySeparatorChar));
        }

        public static string TrimEndingDirectorySeparator(string path)
        {
            var fullPath = Path.GetFullPath(path);
            var root = Path.GetPathRoot(fullPath);
            return string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase)
                ? fullPath
                : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}

namespace System.Diagnostics
{
    public static class ProcessCompatibilityExtensions
    {
        public static Task WaitForExitAsync(this Process process, CancellationToken cancellationToken = default)
        {
            if (process.HasExited)
            {
                return Task.CompletedTask;
            }

            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            EventHandler? handler = null;
            handler = (_, _) => completion.TrySetResult(true);
            process.EnableRaisingEvents = true;
            process.Exited += handler;
            if (process.HasExited)
            {
                completion.TrySetResult(true);
            }

            return AwaitExitAsync();

            async Task AwaitExitAsync()
            {
                try
                {
                    await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    process.Exited -= handler;
                }
            }
        }

        public static void AddArgument(this ProcessStartInfo startInfo, string argument)
        {
            var quoted = QuoteArgument(argument);
            startInfo.Arguments = string.IsNullOrEmpty(startInfo.Arguments)
                ? quoted
                : startInfo.Arguments + " " + quoted;
        }

        private static string QuoteArgument(string argument)
        {
            if (argument.Length != 0 && argument.All(character => !char.IsWhiteSpace(character) && character != '"'))
            {
                return argument;
            }

            var result = new StringBuilder(argument.Length + 2).Append('"');
            var backslashes = 0;
            foreach (var character in argument)
            {
                if (character == '\\')
                {
                    backslashes++;
                    continue;
                }

                if (character == '"')
                {
                    result.Append('\\', backslashes * 2 + 1).Append('"');
                    backslashes = 0;
                    continue;
                }

                result.Append('\\', backslashes).Append(character);
                backslashes = 0;
            }

            return result.Append('\\', backslashes * 2).Append('"').ToString();
        }
    }
}
