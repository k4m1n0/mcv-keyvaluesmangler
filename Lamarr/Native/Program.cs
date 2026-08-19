namespace Lamarr.NativePack;

internal static class Program
{
    static int Main(string[] rgArgs)
    {
        string? sStub = Opt(rgArgs, "--stub");
        string? sInput = Opt(rgArgs, "--input");
        string? sOutput = Opt(rgArgs, "--output");
        string? sBoot = Opt(rgArgs, "--boot");
        if (sStub == null || sInput == null || sOutput == null)
        {
            Console.Error.WriteLine("Usage: LamarrNativePack --stub <stub.dll> --input <input.exe> --output <output.exe> [--boot <bootstrapper.dll>]");
            return 1;
        }
        if (!File.Exists(sStub)) { Console.Error.WriteLine($"Stub not found: {sStub}"); return 1; }
        if (!File.Exists(sInput)) { Console.Error.WriteLine($"Input not found: {sInput}"); return 1; }
        if (sBoot != null && !File.Exists(sBoot)) { Console.Error.WriteLine($"Boot not found: {sBoot}"); return 1; }

        try
        {
            Console.WriteLine($"Patching {Path.GetFileName(sInput)}...");
            var pe = new PeWriter();
            pe.LoadStub(sStub);
            if (sBoot != null) pe.LoadBoot(sBoot);
            pe.LoadPayload(sInput);
            pe.Pack(sOutput);
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