using System.Reflection;

namespace Lamarr;

internal static class Program
{
    const int iOk = 0;
    const int iErrNoPayload = 1;
    const int iErrNoEntryPoint = 2;

    [STAThread]
    static int Main(string[] rgArgs)
    {
        if (rgArgs.Length == 1 && rgArgs[0] == "--test-decode")
            return TestDecode();

        if (rgArgs.Length == 1 && rgArgs[0] == "--test-encode")
            return TestEncode();

        if (StubLoader.TryLoadFromTail(out var asmPayload, out var rgRawBytes))
            return RunPayload(asmPayload, rgRawBytes, rgArgs);

        Console.Error.WriteLine("Lamarr stub: no payload found. Use LamarrPacker to pack an executable.");
        return iErrNoPayload;
    }

    static int TestDecode()
    {
        string sTestFile = Path.Combine(AppContext.BaseDirectory, "test_orig.lzmat");
        if (!File.Exists(sTestFile)) { Console.Error.WriteLine("test_orig.lzmat not found"); return 1; }

        byte[] rgComp = File.ReadAllBytes(sTestFile);
        uint cbOriginal = BitConverter.ToUInt32(rgComp, 0);
        byte[] rgLzmatData = rgComp.AsSpan(4).ToArray();
        uint cbLzmat = (uint)rgLzmatData.Length;

        byte[] rgDecomp = new byte[cbOriginal];
        uint pcbOut = cbOriginal;
        int iResult = LamarrDecoder.Decode(rgDecomp, ref pcbOut, rgLzmatData, cbLzmat);
        Console.Error.WriteLine($"Decode result: {iResult}, output: {pcbOut} bytes");

        string sOutputFile = Path.Combine(AppContext.BaseDirectory, "test_decoded.bin");
        using var fsOut = new FileStream(sOutputFile, FileMode.Create);
        fsOut.Write(rgDecomp, 0, (int)pcbOut);

        string sInputFile = Path.Combine(AppContext.BaseDirectory, "test_input.bin");
        if (!File.Exists(sInputFile)) { Console.Error.WriteLine("test_input.bin not found"); return 1; }

        byte[] rgOriginal = File.ReadAllBytes(sInputFile);
        bool bMatch = pcbOut == rgOriginal.Length && rgDecomp.AsSpan(0, (int)pcbOut).SequenceEqual(rgOriginal);
        Console.Error.WriteLine(bMatch ? "MATCH! Decoder is correct." : "MISMATCH!");

        if (!bMatch)
        {
            for (int i = 0; i < Math.Min(pcbOut, rgOriginal.Length); i++)
            {
                if (rgDecomp[i] != rgOriginal[i])
                {
                    Console.Error.WriteLine($"First diff at offset {i}: expected {rgOriginal[i]:X2}, got {rgDecomp[i]:X2}");
                    break;
                }
            }
        }
        return 0;
    }

    static int TestEncode()
    {
        string sInputFile = Path.Combine(AppContext.BaseDirectory, "test_input.bin");
        if (!File.Exists(sInputFile)) { Console.Error.WriteLine("test_input.bin not found"); return 1; }

        byte[] rgInput = File.ReadAllBytes(sInputFile);
        uint cbOutCap = LamarrEncoder.GetMaxEncodedSize((uint)rgInput.Length);
        byte[] rgCompressed = new byte[cbOutCap];
        uint pcbOut = cbOutCap;
        int iResult = LamarrEncoder.Encode(rgCompressed, ref pcbOut, rgInput, (uint)rgInput.Length);
        Console.Error.WriteLine($"Encode result: {iResult}, input={rgInput.Length}, compressed={pcbOut}");

        string sOutputFile = Path.Combine(AppContext.BaseDirectory, "test_lamarr.lzmat");
        using var fs = new FileStream(sOutputFile, FileMode.Create);
        fs.Write(BitConverter.GetBytes((uint)rgInput.Length), 0, 4);
        fs.Write(rgCompressed, 0, (int)pcbOut);

        return 0;
    }

    static int RunPayload(Assembly asmPayload, byte[] rgRawBytes, string[] rgArgs)
    {
        var asmEntry = asmPayload.EntryPoint;
        if (asmEntry == null)
        {
            Console.Error.WriteLine("Payload has no entry point.");
            return iErrNoEntryPoint;
        }

        var rgParams = asmEntry.GetParameters();
        object[] rgInvokeArgs = rgParams.Length switch
        {
            0 => [],
            _ => [rgArgs]
        };

        object? oRet = asmEntry.Invoke(null, rgInvokeArgs);
        return oRet is int iRet ? iRet : iOk;
    }
}