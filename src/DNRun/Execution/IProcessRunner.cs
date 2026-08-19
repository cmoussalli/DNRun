namespace DNRun.Execution;

/// <summary>
/// Seam that keeps selection-flow tests from launching anything: they assert the composed
/// command line instead. Only <see cref="DotnetProcessRunner"/> touches a real process.
/// </summary>
internal interface IProcessRunner
{
    /// <summary>Runs the plan to completion and returns the child process's exit code.</summary>
    int Run(RunPlan plan);
}
