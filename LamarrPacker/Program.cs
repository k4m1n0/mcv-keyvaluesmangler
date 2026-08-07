using Lamarr;

namespace LamarrPacker;

internal static class Program
{
    const int iOk = 0;
    const int iErrUsage = 1;
    const int iErrException = 5;

    static int Main(string[] rgArgs)
    {
        string? sStub = Opt(rgArgs, "--stub");
        string? sInput = Opt(rgArgs, "--input");
        string? sOutput = Opt(rgArgs, "--output");

        if (sStub == null || sInput == null || sOutput == null)
        {
            Console.Error.WriteLine("Usage: LamarrPacker --stub <stub.exe> --input <input.exe> --output <output.exe>");
            return iErrUsage;
        }

        if (!File.Exists(sStub)) { Console.Error.WriteLine($"Stub not found: {sStub}"); return iErrUsage; }
        if (!File.Exists(sInput)) { Console.Error.WriteLine($"Input not found: {sInput}"); return iErrUsage; }

        try
        {
            Console.WriteLine($"Compressing {sInput}...");
            Packer.Pack(sStub, sInput, sOutput);
            Console.WriteLine($"Packed: {sOutput}");
            return iOk;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return iErrException;
        }
    }

    static string? Opt(string[] rgArgs, string sName)
    {
        int iIdx = Array.FindIndex(rgArgs, sA => sA.Equals(sName, StringComparison.OrdinalIgnoreCase));
        return iIdx >= 0 && iIdx + 1 < rgArgs.Length ? rgArgs[iIdx + 1] : null;
    }
}