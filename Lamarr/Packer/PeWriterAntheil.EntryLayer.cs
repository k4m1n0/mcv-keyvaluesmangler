namespace Lamarr.Packer;

using System.Text;
using A0;

internal partial class PeWriterAntheil
{
    internal static void ExportBootLayer(byte[] rgExe, string sOutDir)//还原boot镜像供离线核查 并断言这一段在产物里不可读
    {
        int iMarker = FindMarkerOff(rgExe);
        if (iMarker < 0)
        {
            Console.WriteLine("[boot] bundle marker not found (entry layer skipped)");
            return;
        }
        int iPos = (int)BitConverter.ToInt64(rgExe, iMarker - 8);
        if (iPos < 0 || iPos + 16 > rgExe.Length) { Console.WriteLine("[boot] bundle header offset out of range"); return; }
        uint uMajor = BitConverter.ToUInt32(rgExe, iPos); iPos += 4;
        iPos += 4;
        int iCount = BitConverter.ToInt32(rgExe, iPos); iPos += 4;
        if (iCount <= 0 || iCount > 0x1000) { Console.WriteLine("[boot] entry count bad"); return; }
        if (iPos >= rgExe.Length) { Console.WriteLine("[boot] bundle header truncated"); return; }
        iPos += 1 + rgExe[iPos];//bundleId
        if (uMajor >= 2) iPos += 40;
        var rgSz = new long[iCount];
        long lOff0 = 0, lSz0 = 0;
        for (int i = 0; i < iCount && i < 0x1000; i++)
        {
            if (iPos + 17 > rgExe.Length) { Console.WriteLine("[boot] entry table truncated"); return; }
            long lOff = BitConverter.ToInt64(rgExe, iPos); iPos += 8;
            long lSz = BitConverter.ToInt64(rgExe, iPos); iPos += 8;
            if (uMajor >= 6) iPos += 8;
            iPos += 1;
            if (iPos >= rgExe.Length) { Console.WriteLine("[boot] entry table truncated"); return; }
            iPos += 1 + rgExe[iPos];//条目名
            rgSz[i] = lSz;
            if (i == 0) { lOff0 = lOff; lSz0 = lSz; }
        }
        if (lSz0 <= Q.iTailLen || lOff0 <= 0 || lOff0 > rgExe.Length - lSz0)
        {
            Console.WriteLine("[boot] app entry bounds bad");
            return;
        }
        int iCipher = (int)(BitConverter.ToUInt64(rgExe, (int)(lOff0 + lSz0) - 16) ^ Q.uLenMask);
        ulong uChk = BitConverter.ToUInt64(rgExe, (int)(lOff0 + lSz0) - 8) ^ Q.uChkMask;
        int iLayer = (int)(lSz0 - Q.iTailLen - iCipher);
        if (iCipher <= 0 || iLayer <= 0)
        {
            Console.WriteLine("[boot] app entry carries no entry layer (boot plain in the bundle)");
            return;
        }
        byte[] rgHead = new byte[Math.Min(Q.iHeadSample, iLayer)];
        Array.Copy(rgExe, (int)lOff0, rgHead, 0, rgHead.Length);
        byte[] rgKey = Q.KeyStream(rgHead, lSz0, iCipher, Q.TableDigest(rgSz));
        byte[] rgBody = new byte[iCipher];
        Array.Copy(rgExe, (int)(lOff0 + iLayer), rgBody, 0, iCipher);
        bool bCheck = Q.Hash64(rgBody, 0, rgBody.Length) == uChk;
        bool bReadable = rgBody.Length > 2 && rgBody[0] == 0x4D && rgBody[1] == 0x5A;
        Q.Xor(rgBody, 0, rgKey);
        string sOut = Path.Combine(sOutDir, "boot.dll");
        File.WriteAllBytes(sOut, rgBody);
        Console.WriteLine($"[boot] entry layer={iLayer}B cipher={iCipher}B check={(bCheck ? "ok" : "FAIL")}");
        Console.WriteLine($"[boot] -> {sOut}");
        Console.WriteLine(bReadable ? "[boot] FAIL: app entry starts with a readable PE" : "[boot] PASS: no readable PE in the app entry");
        Array.Clear(rgBody, 0, rgBody.Length);
        Array.Clear(rgKey, 0, rgKey.Length);
        Array.Clear(rgHead, 0, rgHead.Length);
    }

    private byte[] BuildShimEntry(long lEntry, long[] rgSzAll)//入口层条目 入口层镜像与加密的boot镜像与16B尾部记录 尾部存密文长度与密文校验
    {
        int iHead = Math.Min(Q.iHeadSample, rgShim!.Length);
        byte[] rgHead = new byte[iHead];
        Array.Copy(rgShim, 0, rgHead, 0, iHead);
        byte[] rgKey = Q.KeyStream(rgHead, lEntry, rgBoot.Length, Q.TableDigest(rgSzAll));
        Array.Clear(rgHead, 0, rgHead.Length);
        byte[] rgBody = (byte[])rgBoot.Clone();
        Q.Xor(rgBody, 0, rgKey);
        Array.Clear(rgKey, 0, rgKey.Length);
        ulong uChk = Q.Hash64(rgBody, 0, rgBody.Length);
        byte[] rgEntry = new byte[lEntry];
        Array.Copy(rgShim, 0, rgEntry, 0, rgShim.Length);
        Array.Copy(rgBody, 0, rgEntry, rgShim.Length, rgBody.Length);
        Array.Clear(rgBody, 0, rgBody.Length);
        BitConverter.GetBytes((ulong)rgBoot.Length ^ Q.uLenMask).CopyTo(rgEntry, (int)lEntry - 16);
        BitConverter.GetBytes(uChk ^ Q.uChkMask).CopyTo(rgEntry, (int)lEntry - 8);
        return rgEntry;
    }

    private static int FindMarkerOff(byte[] rgFile)//签名扫描 命中处前8字节就是头部偏移
    {
        for (int i = 8; i + 32 <= rgFile.Length; i++)
        {
            if (rgFile[i] != rgSignature[0]) continue;
            int k = 1;
            while (k < 32 && rgFile[i + k] == rgSignature[k]) k++;
            if (k == 32) return i;
        }
        return -1;
    }
}
