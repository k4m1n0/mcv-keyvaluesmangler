using Lamarr;
using System.Runtime.InteropServices;

namespace LamarrPacker;

internal static class Program
{
    const int iOk = 0;
    const int iErrUsage = 1;
    const int iErrException = 5;

    [DllImport("lamdec.dll")]
    private static extern int lamdec(byte[] rgDst, byte[] rgSrc, uint cbSrc, uint cbDst);

    [DllImport("Iamdec.dll")]
    private static extern int Iamdec(byte[] state, byte[] hist, byte[] page, byte[] src);

    static int Main(string[] rgArgs)
    {
        string? sStub = Opt(rgArgs, "--stub");
        string? sInput = Opt(rgArgs, "--input");
        string? sOutput = Opt(rgArgs, "--output");

        if (rgArgs.Length == 2 && rgArgs[0] == "--test-page")
        {
            string sFile = rgArgs[1];
            if (!File.Exists(sFile)) { Console.Error.WriteLine($"File not found: {sFile}"); return iErrUsage; }
            byte[] rgIn = File.ReadAllBytes(sFile);
            Console.Error.WriteLine($"Input: {rgIn.Length} bytes");
            uint cbCap = LamarrEncoder.GetMaxEncodedSize((uint)rgIn.Length);
            byte[] rgComp = new byte[cbCap]; uint pcb = cbCap;
            int iE = LamarrEncoder.Encode(rgComp, ref pcb, rgIn, (uint)rgIn.Length);
            Console.Error.WriteLine($"Encode: {iE}, comp={pcb}");

            //asm分页解码器测试
            byte[] rgHist = new byte[0x80000];
            byte[] rgPage = new byte[0x1000];
            byte[] st = new byte[0x38];
            BitConverter.GetBytes(1u).CopyTo(st, 0x00);//uInPos
            BitConverter.GetBytes(1u).CopyTo(st, 0x04);//iHist
            BitConverter.GetBytes(1u).CopyTo(st, 0x08);//uOutPos
            BitConverter.GetBytes(0u).CopyTo(st, 0x0C);//uTag
            BitConverter.GetBytes(0u).CopyTo(st, 0x10);//iBC
            BitConverter.GetBytes(0u).CopyTo(st, 0x14);//flags
            BitConverter.GetBytes(0u).CopyTo(st, 0x18);//uRemain
            BitConverter.GetBytes(0u).CopyTo(st, 0x1C);//uSrc
            BitConverter.GetBytes(0u).CopyTo(st, 0x20);//iNib
            BitConverter.GetBytes((uint)pcb).CopyTo(st, 0x28);//srcLen
            BitConverter.GetBytes((uint)rgIn.Length).CopyTo(st, 0x34);//dstLen
            rgHist[0] = rgComp[0];

            byte[] rgAsm = new byte[rgIn.Length];
            int rc = 0;
            bool bPage = true;
            for (uint pg = 0; pg * 0x1000 < (uint)rgIn.Length; pg++)
            {
                uint ps = pg * 0x1000;
                uint pe = Math.Min(ps + 0x1000, (uint)rgIn.Length);
                if (pg == 0) rgPage[0] = rgComp[0];
                else Array.Clear(rgPage, 0, 0x1000);
                BitConverter.GetBytes(ps).CopyTo(st, 0x2C);//pageStart
                BitConverter.GetBytes(pe).CopyTo(st, 0x30);//pageEnd
                rc = Iamdec(st, rgHist, rgPage, rgComp);
                if (rc != 0) { bPage = false; Console.Error.WriteLine($"page {pg} rc={rc:X}"); break; }
                Array.Copy(rgPage, 0, rgAsm, (int)ps, (int)(pe - ps));
            }
            if (bPage) bPage = rgAsm.AsSpan().SequenceEqual(rgIn);
            if (!bPage)
                for (int i = 0; i < rgIn.Length; i++)
                    if (rgAsm[i] != rgIn[i]) { Console.Error.WriteLine($"diff@{i}: exp={rgIn[i]:X2} got={rgAsm[i]:X2}"); break; }

            Console.Error.WriteLine($"Iamdec: {bPage} (rc={rc:X})");
            Console.Error.WriteLine(bPage ? "PAGE OK!" : "PAGE FAILED!");
            return bPage ? iOk : 2;
        }

        if (rgArgs.Length == 2 && rgArgs[0] == "--test-asm")
        {
            string sFile = rgArgs[1];
            if (!File.Exists(sFile)) { Console.Error.WriteLine($"File not found: {sFile}"); return iErrUsage; }
            byte[] rgIn = File.ReadAllBytes(sFile);
            Console.Error.WriteLine($"Input: {rgIn.Length} bytes");
            uint cbCap = LamarrEncoder.GetMaxEncodedSize((uint)rgIn.Length);
            byte[] rgComp = new byte[cbCap]; uint pcb = cbCap;
            int iRes = LamarrEncoder.Encode(rgComp, ref pcb, rgIn, (uint)rgIn.Length);
            Console.Error.WriteLine($"Encode: {iRes}, comp={pcb} ({pcb*100.0/rgIn.Length:F1}%)");
            Console.Error.WriteLine("comp: " + string.Join(" ", rgComp.AsSpan(0, Math.Min(64, (int)pcb)).ToArray().Select(x => x.ToString("X2"))));

            //asm解码器测试 lamdec平铺接口
            byte[] rgAsm = new byte[rgIn.Length];
            int rcAsm = 0;
            bool bAsm = false;
            try
            {
                rcAsm = lamdec(rgAsm, rgComp, pcb, (uint)rgIn.Length);
                bAsm = rcAsm == 0 && rgAsm.AsSpan().SequenceEqual(rgIn);
                if (!bAsm)
                    for (int i = 0; i < rgIn.Length; i++)
                        if (rgAsm[i] != rgIn[i]) { Console.Error.WriteLine($"diff@{i}: exp={rgIn[i]:X2} got={rgAsm[i]:X2}"); break; }
            }
            catch (Exception ex) { Console.Error.WriteLine($"lamdec dll error: {ex.Message}"); }
            Console.Error.WriteLine($"lamdec: rc={rcAsm}, match={bAsm}");

            //分页解码对照 Iamdec
            byte[] rgRef = new byte[rgIn.Length];
            bool bRef = false;
            try
            {
                rgRef = PageDecode(rgComp, 0, (int)pcb, (uint)rgIn.Length);
                bRef = rgRef.AsSpan().SequenceEqual(rgIn);
            }
            catch (Exception ex) { Console.Error.WriteLine($"PageDecode error: {ex.Message}"); }
            Console.Error.WriteLine($"paged: match={bRef}");

            Console.Error.WriteLine((bAsm && bRef) ? "ASM+REF OK!" : "ASM+REF FAILED!");
            return (bAsm && bRef) ? iOk : 2;
        }
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

            //参考解码 LamarrDecoder
            byte[] rgRef = new byte[rgIn.Length]; uint pcbRef = (uint)rgRef.Length;
            iRes = LamarrDecoder.Decode(rgRef, ref pcbRef, rgComp, pcb);
            bool bRef = iRes == 0 && pcbRef == rgIn.Length &&
                        rgRef.AsSpan(0, (int)pcbRef).SequenceEqual(rgIn);

            //分页解码 PageDecode
            byte[] rgStm = new byte[rgIn.Length];
            int n = 0;
            string? sErr = null;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                rgStm = PageDecode(rgComp, 0, (int)pcb, (uint)rgIn.Length);
                n = rgStm.Length;
            }
            catch (Exception ex) { sErr = ex.Message; }
            sw.Stop();
            Console.Error.WriteLine($"paged decode: {sw.Elapsed.TotalMilliseconds:F1} ms");
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

            byte[] rgOut = PageDecode(rgComp, 0, rgComp.Length, rawLen);
            int n = rgOut.Length;

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

            byte[] rgOut = PageDecode(rg, off, compLen, rawLen);
            int n = rgOut.Length;

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

    private static byte[] PageDecode(byte[] src, int off, int compLen, uint rawLen)
    {
        byte[] comp = new byte[compLen];
        Array.Copy(src, off, comp, 0, compLen);
        byte[] hist = new byte[0x80000];
        byte[] page = new byte[0x1000];
        byte[] st = new byte[0x38];
        BitConverter.GetBytes(1u).CopyTo(st, 0x00);//uInPos
        BitConverter.GetBytes(1u).CopyTo(st, 0x04);//iHist
        BitConverter.GetBytes(1u).CopyTo(st, 0x08);//uOutPos
        BitConverter.GetBytes(0u).CopyTo(st, 0x0C);
        BitConverter.GetBytes(0u).CopyTo(st, 0x10);
        BitConverter.GetBytes(0u).CopyTo(st, 0x14);
        BitConverter.GetBytes(0u).CopyTo(st, 0x18);
        BitConverter.GetBytes(0u).CopyTo(st, 0x1C);
        BitConverter.GetBytes(0u).CopyTo(st, 0x20);
        BitConverter.GetBytes((uint)compLen).CopyTo(st, 0x28);//srcLen
        BitConverter.GetBytes(rawLen).CopyTo(st, 0x34);//dstLen
        hist[0] = comp[0];
        byte[] outBuf = new byte[rawLen];
        for (uint pg = 0; pg * 0x1000 < rawLen; pg++)
        {
            uint ps = pg * 0x1000;
            uint pe = Math.Min(ps + 0x1000, rawLen);
            if (pg == 0) page[0] = comp[0];
            else Array.Clear(page, 0, 0x1000);
            BitConverter.GetBytes(ps).CopyTo(st, 0x2C);//pageStart
            BitConverter.GetBytes(pe).CopyTo(st, 0x30);//pageEnd
            int rc = Iamdec(st, hist, page, comp);
            if (rc != 0)
                throw new InvalidDataException($"Iamdec rc={rc:X}");
            Array.Copy(page, 0, outBuf, (int)ps, (int)(pe - ps));
        }
        return outBuf;
    }

    static string? Opt(string[] rgArgs, string sName)
    {
        int iIdx = Array.FindIndex(rgArgs, sA => sA.Equals(sName, StringComparison.OrdinalIgnoreCase));
        return iIdx >= 0 && iIdx + 1 < rgArgs.Length ? rgArgs[iIdx + 1] : null;
    }
}
