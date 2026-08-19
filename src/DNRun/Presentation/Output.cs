namespace DNRun.Presentation;

/// <summary>
/// All console writing goes through here so colour handling and the banner stay in one place.
/// Colour is suppressed when NO_COLOR is set or stdout is redirected — Orca's output pane
/// may not render escape sequences.
/// </summary>
internal static class Output
{
    private const string Reset = "\u001b[0m";
    private const string BoldSeq = "\u001b[1m";
    private const string DimSeq = "\u001b[2m";
    private const string CyanSeq = "\u001b[36m";
    private const string YellowSeq = "\u001b[33m";
    private const string RedSeq = "\u001b[31m";

    private static readonly bool ColorEnabled = DetectColorSupport();

    private static bool DetectColorSupport()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR")))
        {
            return false;
        }

        if (string.Equals(Environment.GetEnvironmentVariable("TERM"), "dumb", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !Console.IsOutputRedirected;
    }

    private static string Paint(string text, string sequence) =>
        ColorEnabled ? string.Concat(sequence, text, Reset) : text;

    public static string Dim(string text) => Paint(text, DimSeq);

    public static string Cyan(string text) => Paint(text, CyanSeq);

    public static void Banner() =>
        Console.WriteLine(Paint("DNRun", BoldSeq) + Dim(" — Intelligent .NET Project Runner"));

    public static void Blank() => Console.WriteLine();

    public static void Line(string text = "") => Console.WriteLine(text);

    public static void Label(string label) => Console.WriteLine(Dim(label));

    public static void Warn(string text) =>
        Console.Error.WriteLine(Paint("warning: ", YellowSeq) + text);

    public static void Error(string text) =>
        Console.Error.WriteLine(Paint("error: ", RedSeq) + text);

}
