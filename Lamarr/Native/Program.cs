namespace Lamarr.NativePack;

internal static class Program
{
    static int Main(string[] rgArgs)
    {
        string? sStub = Opt(rgArgs, "--stub");
        string? sInput = Opt(rgArgs, "--input");
        string? sOutput = Opt(rgArgs, "--output");
        string? sBoot = Opt(rgArgs, "--boot");
        string? sDecoder = Opt(rgArgs, "--decoder");
        string? sJitHook = Opt(rgArgs, "--jithook");
        string? sPheropod = Opt(rgArgs, "--pheropod");
        string sMode = Opt(rgArgs, "--mode") ?? "clean";
        if (sStub == null || sInput == null || sOutput == null)
        {
            Console.Error.WriteLine("Usage: LamarrNativePack --stub <stub.dll> --input <input.exe> --output <output.exe> [--boot <bootstrapper.dll>] [--decoder <Iamdec.dll>] [--jithook <jithook.dll>] [--pheropod <gzip-decoder.dll>] [--mode clean|antheil]");
            return 1;
        }
        if (!File.Exists(sStub)) { Console.Error.WriteLine($"Stub not found: {sStub}"); return 1; }
        if (!File.Exists(sInput)) { Console.Error.WriteLine($"Input not found: {sInput}"); return 1; }
        if (sBoot != null && !File.Exists(sBoot)) { Console.Error.WriteLine($"Boot not found: {sBoot}"); return 1; }
        if (sDecoder != null && !File.Exists(sDecoder)) { Console.Error.WriteLine($"Decoder not found: {sDecoder}"); return 1; }
        if (sJitHook != null && !File.Exists(sJitHook)) { Console.Error.WriteLine($"JitHook not found: {sJitHook}"); return 1; }
        if (sPheropod != null && !File.Exists(sPheropod)) { Console.Error.WriteLine($"Pheropod not found: {sPheropod}"); return 1; }

        try
        {
            Console.WriteLine($"Patching {Path.GetFileName(sInput)} ({sMode} mode)...");
            if (sMode.Equals("clean", StringComparison.OrdinalIgnoreCase))
            {
                var pe = new PeWriter();
                pe.LoadStub(sStub);
                if (sBoot != null) pe.LoadBoot(sBoot);
                pe.LoadPayload(sInput);
                pe.Pack(sOutput);
            }
            else
            {
                var pe = new PeWriterAntheil();
                pe.LoadStub(sStub);
                if (sBoot != null) pe.LoadBoot(sBoot);
                if (sDecoder != null) pe.LoadDecoder(sDecoder);
                if (sJitHook != null) pe.LoadJitHook(sJitHook);
                if (sPheropod != null) pe.LoadPheropod(sPheropod);
                pe.LoadPayload(sInput);
                pe.Pack(sOutput);
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex}");
            return 5;
        }
    }

    static string? Opt(string[] rgArgs, string sName)
    {
        int i = Array.FindIndex(rgArgs, a => a.Equals(sName, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < rgArgs.Length ? rgArgs[i + 1] : null;
    }
}
