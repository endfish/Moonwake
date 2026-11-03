using Moonwake.Services;

internal static class ResourceRetryTests
{
    internal static int Run(string root)
    {
        var source = Path.Combine(root, "resource-retry-source");
        var destination = Path.Combine(root, "resource-retry-target");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "resource.bin"), "verified component");
        var attempts = 0;
        var delays = new List<int>();
        FileOperationRetry.MoveDirectory(source, destination, (a, b) =>
        {
            attempts++;
            if (attempts <= 2) throw new IOException("Temporary directory access denial", unchecked((int)0x80070005));
            Directory.Move(a, b);
        }, delays.Add);
        if (attempts != 3 || delays.Count != 2 || File.ReadAllText(Path.Combine(destination, "resource.bin")) != "verified component")
            throw new Exception("Transient resource move did not recover.");
        Console.WriteLine("PASS: transient Windows directory access failures are retried before activating verified resources");
        attempts = 0;
        try
        {
            FileOperationRetry.MoveDirectory(source, destination, (_, _) => { attempts++; throw new UnauthorizedAccessException("Permanent permissions error"); }, _ => { });
            throw new Exception("Permanent permissions must fail.");
        }
        catch (UnauthorizedAccessException) { if (attempts != 8) throw new Exception("Retry limit was not respected."); }
        Console.WriteLine("PASS: permanent resource permissions errors remain errors after bounded retries");
        attempts = 0;
        try
        {
            FileOperationRetry.MoveDirectory(source, destination, (_, _) => { attempts++; throw new IOException("Invalid parameter", unchecked((int)0x80070057)); }, _ => throw new Exception("Must not retry"));
            throw new Exception("Unrelated IO errors must fail.");
        }
        catch (IOException) { if (attempts != 1) throw new Exception("Unrelated IO error was retried."); }
        Console.WriteLine("PASS: unrelated IO failures are not hidden or retried");
        return 3;
    }
}
