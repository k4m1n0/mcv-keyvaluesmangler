// Patcher/Program.cs — PE patcher entry point
namespace Patcher;

internal static class Program
{
    static int Main(string[] rgArgs)
    {
        string? sStub = Opt(rgArgs, "--stub");
        string? sInput = Opt(rgArgs, "--input");
        string? sOutput = Opt(rgArgs, "--output");
        if (sStub == null || sInput == null || sOutput == null)
        {
            Console.Error.WriteLine("Usage: Patcher --stub <stub.dll> --input <input.dll> --output <output.exe>");
            return 1;
        }
        if (!File.Exists(sStub)) { Console.Error.WriteLine($"Stub not found: {sStub}"); return 1; }
        if (!File.Exists(sInput)) { Console.Error.WriteLine($"Input not found: {sInput}"); return 1; }

        try
        {
            Console.WriteLine($"Patching {Path.GetFileName(sInput)}...");
            var pe = new PeWriter();
            pe.LoadDll(sInput);
            pe.LoadStub(sStub);
            pe.Compress();
            pe.Write(sOutput);

            Console.WriteLine($"Done: {sOutput} ({new FileInfo(sOutput).Length} bytes)");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 5;
        }
    }

    static string? Opt(string[] rgArgs, string sName)
    {
        int i = Array.FindIndex(rgArgs, a => a.Equals(sName, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < rgArgs.Length ? rgArgs[i + 1] : null;
    }
}