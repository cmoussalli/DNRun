using System.ComponentModel;
using System.Diagnostics;
using DNRun.Cli;
using DNRun.Presentation;

namespace DNRun.Execution;

/// <summary>
/// Launches <c>dotnet run --project &lt;path&gt;</c> so the app behaves exactly as if the user
/// had typed that line (spec §11, plan §4.6).
///
/// Three details decide whether this feels native:
///   1. The standard streams are never redirected. Redirecting forces relay pumps, which break
///      interactive console apps, spinners, and ANSI colour coming from the child.
///   2. Ctrl+C is delivered by Windows to the whole console process group, so the child gets it
///      directly. DNRun's only job is to not die first.
///   3. The child's exit code is propagated verbatim, so Orca and shell $? see the real result.
/// </summary>
internal sealed class DotnetProcessRunner : IProcessRunner
{
    public int Run(RunPlan plan)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            RedirectStandardInput = false,
            WorkingDirectory = plan.WorkingDirectory,
        };

        // ArgumentList, never a concatenated Arguments string: paths with spaces need no quoting.
        foreach (var argument in plan.BuildArguments())
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                Output.Error("Failed to start 'dotnet'.");
                return ExitCodes.LaunchFailure;
            }

            using var cancellation = new ConsoleCancellationGuard();
            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Win32Exception ex)
        {
            Output.Error("'dotnet' could not be started: " + ex.Message);
            Output.Line();
            Output.Line("Check that the .NET SDK is installed and on PATH:");
            Output.Line("    dotnet --version");
            return ExitCodes.LaunchFailure;
        }
    }

    /// <summary>
    /// Keeps DNRun alive through Ctrl+C so the child can shut down cleanly and report its own
    /// exit code, instead of DNRun dying first and orphaning the run.
    /// </summary>
    private sealed class ConsoleCancellationGuard : IDisposable
    {
        public ConsoleCancellationGuard() => Console.CancelKeyPress += OnCancelKeyPress;

        public void Dispose() => Console.CancelKeyPress -= OnCancelKeyPress;

        private static void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e) => e.Cancel = true;
    }
}
