using Lamarr;
using BundleHost;

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

        if (rgArgs.Length == 2 && rgArgs[0] == "--test-stream")
        {
            string sFile = rgArgs[1];
            if (!File.Exists(sFile)) { Console.Error.WriteLine($"File not found: {sFile}"); return iErrUsage; }
            byte[] rgIn = File.ReadAllBytes(sFile);
            uint cbCap = LamarrEncoder.GetMaxEncodedSize((uint)rgIn.Length);
            byte[] rgComp = new byte[cbCap]; uint pcb = cbCap;
            int iRes = LamarrEncoder.Encode(rgComp, ref pcb, rgIn, (uint)rgIn.Length);
            Console.Error.WriteLine($"Encode: {iRes}, comp={pcb}");

            //参考解码器
            byte[] rgRef = new byte[rgIn.Length]; uint pcbRef = (uint)rgRef.Length;
            iRes = LamarrDecoder.Decode(rgRef, ref pcbRef, rgComp, pcb);
            bool bRef = iRes == 0 && pcbRef == rgIn.Length &&
                        rgRef.AsSpan(0, (int)pcbRef).SequenceEqual(rgIn);

            //流式解码器 与Boot同一份源码
            byte[] rgStm = new byte[rgIn.Length];
            int n = 0;
            string? sErr = null;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            using (var stm = new BundleStream(rgComp, 0, (int)pcb, (uint)rgIn.Length))
            {
                try
                {
                    while (n < rgStm.Length)
                    {
                        int r = stm.Read(rgStm, n, Math.Min(4096, rgStm.Length - n));
                        if (r <= 0) break;
                        n += r;
                    }
                }
                catch (Exception ex) { sErr = ex.Message; }
            }
            sw.Stop();
            Console.Error.WriteLine($"stream decode: {sw.Elapsed.TotalMilliseconds:F1} ms");
            bool bStm = sErr == null && n == rgIn.Length && rgStm.AsSpan(0, n).SequenceEqual(rgIn);

            Console.Error.WriteLine(bRef && bStm ? "REF+STREAM OK!"
                                 : bRef ? "REF OK, STREAM FAILED!"
                                        : "REF FAILED!");
            if (!bStm)
            {
                Console.Error.WriteLine($"stream n={n}, err={sErr ?? "none"}");
                for (int i = 0; i < Math.Min(Math.Max(n, 0), rgIn.Length); i++)
                    if (rgStm[i] != rgRef[i])
                    { Console.Error.WriteLine($"diff@{i}: ref={rgRef[i]:X2} got={rgStm[i]:X2}"); break; }
            }
            return bRef && bStm ? iOk : 2;
        }

        if (rgArgs.Length == 3 && rgArgs[0] == "--test-raw")
        {
            byte[] rgComp = File.ReadAllBytes(rgArgs[1]);
            uint rawLen = uint.Parse(rgArgs[2]);

            byte[] rgOut = new byte[rawLen];
            int n = 0;
            using (var stm = new BundleStream(rgComp, 0, rgComp.Length, rawLen))
            {
                while (n < rgOut.Length)
                {
                    int r = stm.Read(rgOut, n, Math.Min(4096, rgOut.Length - n));
                    if (r <= 0) break;
                    n += r;
                }
            }

            byte[] rgRef = new byte[rawLen]; uint pcb = rawLen;
            int iRes = LamarrDecoder.Decode(rgRef, ref pcb, rgComp, (uint)rgComp.Length);
            bool bRef = iRes == 0 && pcb == rawLen && rgRef.AsSpan().SequenceEqual(rgOut);
            bool bStm = n == rawLen && rgRef.AsSpan().SequenceEqual(rgOut.AsSpan(0, n));
            Console.Error.WriteLine($"decode n={n}, refOk={iRes == 0 && pcb == rawLen}, match={bRef} ({bStm})");
            if (!bStm)
                for (int i = 0; i < Math.Min(n, rawLen); i++)
                    if (rgOut[i] != rgRef[i])
                    { Console.Error.WriteLine($"diff@{i}: ref={rgRef[i]:X2} got={rgOut[i]:X2}"); break; }
            return bStm ? iOk : 2;
        }

        if (rgArgs.Length == 4 && rgArgs[0] == "--test-raw-off")
        {
            byte[] rg = File.ReadAllBytes(rgArgs[1]);
            int off = int.Parse(rgArgs[2]);
            uint rawLen = uint.Parse(rgArgs[3]);
            int compLen = rg.Length - off;

            byte[] rgOut = new byte[rawLen];
            int n = 0;
            using (var stm = new BundleStream(rg, off, compLen, rawLen))
            {
                while (n < rgOut.Length)
                {
                    int r = stm.Read(rgOut, n, Math.Min(4096, rgOut.Length - n));
                    if (r <= 0) break;
                    n += r;
                }
            }

            byte[] rgSub = new byte[compLen];
            Array.Copy(rg, off, rgSub, 0, compLen);
            byte[] rgRef = new byte[rawLen]; uint pcb = rawLen;
            int iRes = LamarrDecoder.Decode(rgRef, ref pcb, rgSub, (uint)compLen);
            bool bStm = n == rawLen && rgRef.AsSpan(0, (int)pcb).SequenceEqual(rgOut.AsSpan(0, n));
            Console.Error.WriteLine($"decode n={n}, refOk={iRes == 0 && pcb == rawLen}, match={bStm}");
            if (!bStm)
                for (int i = 0; i < Math.Min(n, rawLen); i++)
                    if (rgOut[i] != rgRef[i])
                    { Console.Error.WriteLine($"diff@{i}: ref={rgRef[i]:X2} got={rgOut[i]:X2}"); break; }
            return bStm ? iOk : 2;
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