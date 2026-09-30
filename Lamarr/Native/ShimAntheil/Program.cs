using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;

namespace A0;

internal static class H
{
    private static readonly byte[] rgSig = new byte[]//bundle标记的签名取自打包端rgSignature
    {
        0x8b,0x12,0x02,0xb9,0x6a,0x61,0x20,0x38,0x72,0x7b,0x93,0x02,0x14,0xd7,0xa0,0x32,
        0x13,0xf5,0xb9,0xe6,0xef,0xae,0x33,0x18,0xee,0x3b,0x2d,0xce,0x24,0xb3,0x6a,0xae
    };

    [STAThread]
    private static int Main(string[] rgArgs)
    {
        try
        {
            string sSelf = SelfPath();
            byte[] rg = File.ReadAllBytes(sSelf);//条目偏移在头部里按文件偏移存放 所以整文件读进来
            int iMarker = FindMarker(rg);
            if (iMarker < 0) return Fail(0);
            long lHeader = BitConverter.ToInt64(rg, iMarker - 8);
            if (lHeader <= 0 || lHeader >= rg.Length) return Fail(1);

            int iPos = (int)lHeader;//解析宿主bundle头部只要条目表
            uint uMajor = ReadU32(rg, ref iPos);
            ReadU32(rg, ref iPos);
            int iCount = ReadI32(rg, ref iPos);
            ReadStr(rg, ref iPos);
            if (uMajor >= 2) for (int i = 0; i < 5; i++) ReadI64(rg, ref iPos);
            if (iCount <= 0 || iCount > 0x1000) return Fail(2);
            var rgSz = new long[iCount];
            long lOff0 = 0, lSz0 = 0;
            for (int i = 0; i < iCount; i++)
            {
                long lOff = ReadI64(rg, ref iPos);
                long lSz = ReadI64(rg, ref iPos);
                if (uMajor >= 6) ReadI64(rg, ref iPos);
                ReadU8(rg, ref iPos);
                ReadStr(rg, ref iPos);
                rgSz[i] = lSz;
                if (i == 0) { lOff0 = lOff; lSz0 = lSz; }
            }
            if (lSz0 <= Q.iTailLen || lOff0 <= 0 || lOff0 + lSz0 > rg.Length) return Fail(3);

            byte[] rgEntry = new byte[lSz0];
            Array.Copy(rg, lOff0, rgEntry, 0, lSz0);
            Array.Clear(rg, 0, rg.Length);

            ulong uLen = BitConverter.ToUInt64(rgEntry, (int)lSz0 - 16) ^ Q.uLenMask;//尾部记录 [密文长度 ^ 掩码][密文校验 ^ 掩码] 前面是本层镜像与密文
            ulong uChk = BitConverter.ToUInt64(rgEntry, (int)lSz0 - 8) ^ Q.uChkMask;
            int iCipher = (int)uLen;
            int iLayer = (int)lSz0 - Q.iTailLen - iCipher;
            if (iCipher <= 0 || iLayer <= 0) return Fail(4);

            byte[] rgHead = new byte[Math.Min(Q.iHeadSample, iLayer)];
            Array.Copy(rgEntry, 0, rgHead, 0, rgHead.Length);
            byte[] rgKey = Q.KeyStream(rgHead, lSz0, iCipher, Q.TableDigest(rgSz));
            Array.Clear(rgHead, 0, rgHead.Length);
            if (Q.Hash64(rgEntry, iLayer, iCipher) != uChk)//校验落在密文上解不开与改过都走同一条失败路径
            {
                Array.Clear(rgKey, 0, rgKey.Length);
                return Fail(5);
            }
            Q.Xor(rgEntry, iLayer, rgKey);
            Array.Clear(rgKey, 0, rgKey.Length);

            byte[] rgImage = new byte[iCipher];
            Array.Copy(rgEntry, iLayer, rgImage, 0, iCipher);
            Array.Clear(rgEntry, 0, rgEntry.Length);

            Assembly asm = AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(rgImage, 0, rgImage.Length));
            Array.Clear(rgImage, 0, rgImage.Length);//镜像交给运行时后本体可以丢掉 与载荷装载同一条路径
            MethodInfo? miEntry = asm.EntryPoint;
            if (miEntry == null) return Fail(6);
            object[] rgParams = miEntry.GetParameters().Length == 0 ? Array.Empty<object>() : new object[] { rgArgs };
            object? oRet = miEntry.Invoke(null, rgParams);
            return oRet is int iRet ? iRet : 0;
        }
        catch (Exception ex)
        {
#if DEBUG
            Log("host: " + ex.GetType().Name);
#else
            GC.KeepAlive(ex);
#endif
            Environment.FailFast(null);
            return 1;
        }
    }

    private static int Fail(int iStage)//失败编号仅Debug构建 据此写日志 0 marker 1 header 2 count 3 bounds 4 tail 5 check 6 entry point 9 exception
    {
#if DEBUG
        Log("h:" + iStage + " " + rgStage[iStage]);
#endif
        Environment.FailFast(null);
        return 1;
    }

    private static string SelfPath()
    {
#if NET6_0_OR_GREATER
        return Environment.ProcessPath ?? throw new InvalidOperationException();
#else
        return Process.GetCurrentProcess().MainModule?.FileName ?? throw new InvalidOperationException();
#endif
    }

    private static int FindMarker(byte[] rg)//签名扫描 命中处前8字节就是头部偏移
    {
        for (int i = 8; i + 32 <= rg.Length; i++)
        {
            if (rg[i] != rgSig[0]) continue;
            int k = 1;
            while (k < 32 && rg[i + k] == rgSig[k]) k++;
            if (k == 32) return i;
        }
        return -1;
    }

    private static uint ReadU32(byte[] rg, ref int iPos)
    {
        uint u = BitConverter.ToUInt32(rg, iPos);
        iPos += 4;
        return u;
    }

    private static int ReadI32(byte[] rg, ref int iPos)
    {
        int i = BitConverter.ToInt32(rg, iPos);
        iPos += 4;
        return i;
    }

    private static long ReadI64(byte[] rg, ref int iPos)
    {
        long l = BitConverter.ToInt64(rg, iPos);
        iPos += 8;
        return l;
    }

    private static byte ReadU8(byte[] rg, ref int iPos) => rg[iPos++];

    private static string ReadStr(byte[] rg, ref int iPos)
    {
        int iLen = rg[iPos++];
        if ((iLen & 0x80) != 0) iLen = (iLen & 0x7F) << 8 | rg[iPos++];
        string s = Encoding.UTF8.GetString(rg, iPos, iLen);
        iPos += iLen;
        return s;
    }

#if DEBUG
    private const string sLogVar = "LAMARR_BOOT_LOG";//LAMARR_BOOT_LOG指向文件时落盘 整块只进Debug构建
    private static readonly string[] rgStage =
    {
        "marker not found", "bad header offset", "bad entry count", "bad entry bounds",
        "bad entry tail", "entry check failed", "no entry point"
    };

    private static void Log(string sLine)
    {
        try
        {
            string sPath = Environment.GetEnvironmentVariable(sLogVar) ?? "";
            if (sPath.Length == 0) return;
            File.AppendAllText(sPath, sLine + Environment.NewLine);
        }
        catch { }
    }
#endif
}
