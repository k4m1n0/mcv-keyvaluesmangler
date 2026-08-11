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
        if (rgArgs.Length == 2 && rgArgs[0] == "--test-roundtrip")
        {
            string sFile = rgArgs[1];
            if (!File.Exists(sFile)) { Console.Error.WriteLine($"File not found: {sFile}"); return iErrUsage; }
            byte[] rgIn = File.ReadAllBytes(sFile);
            Console.Error.WriteLine($"Input: {rgIn.Length} bytes");
            uint cbCap = LamarrEncoder.GetMaxEncodedSize((uint)rgIn.Length);
            byte[] rgComp = new byte[cbCap]; uint pcb = cbCap;
            int iRes = LamarrEncoder.Encode(rgComp, ref pcb, rgIn, (uint)rgIn.Length);
            Console.Error.WriteLine($"Encode: {iRes}, comp={pcb} ({pcb*100.0/rgIn.Length:F1}%)");
            byte[] rgDec = new byte[rgIn.Length]; uint pcbDec = (uint)rgDec.Length;
            iRes = LamarrDecoder.Decode(rgDec, ref pcbDec, rgComp, pcb);
            Console.Error.WriteLine($"Decode: {iRes}, out={pcbDec}");
            bool bOk = pcbDec == rgIn.Length && rgDec.AsSpan(0,(int)pcbDec).SequenceEqual(rgIn);
            Console.Error.WriteLine(bOk ? "ROUNDTRIP OK!" : "ROUNDTRIP FAILED!");
            if (!bOk)
                for (int i=0;i<Math.Min(pcbDec,rgIn.Length);i++)
                    if (rgDec[i]!=rgIn[i]) { Console.Error.WriteLine($"Diff@{i}: exp={rgIn[i]:X2} got={rgDec[i]:X2}"); break; }
            return bOk ? iOk : 2;
        }

        if (sStub == null || sInput == null || sOutput == null)
        {
            Console.Error.WriteLine("Usage: LamarrPacker --stub <stub.exe> --input <input.exe> --output <output.exe>");
            Console.Error.WriteLine("       LamarrPacker --test-roundtrip <file>");
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