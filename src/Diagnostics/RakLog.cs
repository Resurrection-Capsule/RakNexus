namespace RakNexus;

/// <summary>
/// Optional verbose tracing for the RakNet transport. A library should be silent unless the
/// host opts in, so per-packet dispatch / reliability / congestion logs are gated behind
/// <see cref="Verbose"/> (off by default). Set <c>RakLog.Verbose = true</c> from the host
/// (e.g. a --raknet-verbose flag) to surface them while debugging. Errors always print.
/// </summary>
public static class RakLog
{
    public static bool Verbose = false;

    public static void Trace(string message)
    {
        if (Verbose) Console.WriteLine(message);
    }

    public static void Error(string message)
    {
        Console.Error.WriteLine(message);
    }
}
