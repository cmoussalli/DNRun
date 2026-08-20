namespace DNRun.Tests;

/// <summary>
/// Flow tests redirect Console.Out and Console.Error to read what the command printed, and the
/// console is process-wide: two such classes running in parallel would read each other's output.
/// Sharing one collection makes xUnit run them one after another.
/// </summary>
[CollectionDefinition(ConsoleCapture.Name)]
public sealed class ConsoleCapture
{
    public const string Name = "console output";
}
