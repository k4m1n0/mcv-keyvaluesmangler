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
        int iRollLenX = BitConverter.ToInt32(rgExe, (int)(lOff0 + lSz0) - Q.iTailLen + 0);
        int iEngLenX = BitConverter.ToInt32(rgExe, (int)(lOff0 + lSz0) - Q.iTailLen + 4);
        int iPrekLenX = BitConverter.ToInt32(rgExe, (int)(lOff0 + lSz0) - Q.iTailLen + 16) ^ (int)Q.uPrekMask;
        uint uRollKeyEnt = BitConverter.ToUInt32(rgExe, (int)(lOff0 + lSz0) - Q.iTailLen + 8) ^ Q.TailKeyMaskOf(PreKernEcho(rgHead, Q.uDomainSeed));//掩码含前置内核输出 离线用明文回声对出同一值
        uint uPolicyX = BitConverter.ToUInt32(rgExe, (int)(lOff0 + lSz0) - Q.iTailLen + 12) ^ Q.uLayoutMask;
        Q.EntrySegmentOffsets(iLayer - iPrekLenX, iRollLenX, iEngLenX, uPolicyX, out int iRollOffX, out int iEngOffX);
        ulong uSelfMix = Q.SelfMixRange(rgExe, (int)lOff0 + iRollOffX, iRollLenX, (int)lOff0 + iEngOffX, iEngLenX);
        byte[] rgKey = ChMask.Mask(rgHead, lSz0, Q.TableDigest(rgSz), 3, iCipher, VmMaskEcho(rgHead, Q.uDomainSeed, uRollKeyEnt), uSelfMix);
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

    private byte[] BuildShimEntry(long lEntry, long[] rgSzAll)//入口层条目 = 镜像 + roll + 引擎 + 加密boot + 32B尾部 尾部前16B记roll与引擎长度与roll钥匙
    {
        int iHead = Math.Min(Q.iHeadSample, rgShim!.Length);
        byte[] rgHead = new byte[iHead];
        Array.Copy(rgShim, 0, rgHead, 0, iHead);
        uint uVmMix = VmMaskEcho(rgHead, Q.uDomainSeed, uRollKeyEntry);//Host侧由roll里两枚内核算出同一个值
        uint uPre = PreKernEcho(rgHead, Q.uDomainSeed);//前置内核的输出掩住尾部roll钥匙字段 不先跑它读不到钥匙
        uint uTailKeyMask = Q.TailKeyMaskOf(uPre);
        byte[] rgRollB = rgRollEnt == null ? Array.Empty<byte>() : (byte[])rgRollEnt.Clone();//产物里roll与引擎不是明文 掩码由域常量与roll钥匙与条目长度派生
        byte[] rgEngineB = rgEngineEnt == null ? Array.Empty<byte>() : (byte[])rgEngineEnt.Clone();//入口层自带一份引擎与roll 与boot侧那份各自独立
        uint uPolicy = Q.EntryLayout(rgHead, uRollKeyEntry);//布局逐产物 策略码由镜像头与roll钥匙派生 摆放顺序也按它
        uint uPolicyEcho = uPolicy;//同值写进尾部+20供Host直接读
        if ((uPolicy & 2u) != 0 && rgRollB.Length > 0) Array.Reverse(rgRollB);
        if ((uPolicy & 4u) != 0 && rgEngineB.Length > 0) Array.Reverse(rgEngineB);
        if (rgRollB.Length > 0) ChMask.Xor(rgRollB, 0, ChMask.Mask(rgHead, lEntry, uRollKeyEntry, 1, rgRollB.Length, 0, 0));
        if (rgEngineB.Length > 0) ChMask.Xor(rgEngineB, 0, ChMask.Mask(rgHead, lEntry, 0u, 2, rgEngineB.Length, 0, 0));//引擎掩码不含roll钥匙 前置内核要靠它先跑起来
        ulong uSelfMix = Q.SelfMix(rgRollB, rgRollB.Length, rgEngineB, rgEngineB.Length);//产物自指 钥匙流再掺roll与引擎在盘上那一段的摘要
        byte[] rgKey = ChMask.Mask(rgHead, lEntry, Q.TableDigest(rgSzAll), 3, rgBoot.Length, uVmMix, uSelfMix);
        Array.Clear(rgHead, 0, rgHead.Length);
        byte[] rgBody = (byte[])rgBoot.Clone();
        Q.Xor(rgBody, 0, rgKey);
        Array.Clear(rgKey, 0, rgKey.Length);
        ulong uChk = Q.Hash64(rgBody, 0, rgBody.Length);
        if (bLayout)//构建侧入口层摘要 只走stdout
            Console.WriteLine($"[entrylayer] vmmix=0x{uVmMix:X8} seed=0x{Q.uDomainSeed:X8} rollKey=0x{uRollKeyEntry:X8} layout=0x{uPolicy:X} shim={rgShim.Length} roll={rgRollB.Length} eng={rgEngineB.Length} boot={rgBoot.Length} total={lEntry}");
        byte[] rgEntry = new byte[lEntry];
        int iAt = 0;
        Array.Copy(rgShim, 0, rgEntry, iAt, rgShim.Length); iAt += rgShim.Length;
        if ((uPolicy & 1u) != 0)//策略码bit0=交换顺序 引擎在前
        {
            Array.Copy(rgEngineB, 0, rgEntry, iAt, rgEngineB.Length); iAt += rgEngineB.Length;
            Array.Copy(rgRollB, 0, rgEntry, iAt, rgRollB.Length); iAt += rgRollB.Length;
        }
        else
        {
            Array.Copy(rgRollB, 0, rgEntry, iAt, rgRollB.Length); iAt += rgRollB.Length;
            Array.Copy(rgEngineB, 0, rgEntry, iAt, rgEngineB.Length); iAt += rgEngineB.Length;
        }
        Array.Copy(rgPreKern, 0, rgEntry, iAt, rgPreKern.Length); iAt += rgPreKern.Length;//前置内核段紧邻密文之前 尾部加16记长度
        Array.Copy(rgBody, 0, rgEntry, iAt, rgBody.Length); iAt += rgBody.Length;
        Array.Clear(rgBody, 0, rgBody.Length);
        int iTail = (int)lEntry - Q.iTailLen;
        if (iAt != iTail) throw new InvalidOperationException($"entry layer layout mismatch: {iAt} != {iTail} (shim={rgShim.Length} roll={rgRollB.Length} engine={rgEngineB.Length} boot={rgBoot.Length} tail={Q.iTailLen} total={lEntry})");
        BitConverter.GetBytes((uint)rgRollB.Length).CopyTo(rgEntry, iTail + 0);
        BitConverter.GetBytes((uint)rgEngineB.Length).CopyTo(rgEntry, iTail + 4);
        BitConverter.GetBytes(uRollKeyEntry ^ uTailKeyMask).CopyTo(rgEntry, iTail + 8);//钥匙字段存掩码值 掩码含前置内核输出 不跑那枚内核读不到
        BitConverter.GetBytes(uPolicy ^ Q.uLayoutMask).CopyTo(rgEntry, iTail + 12);//布局策略码 掩码存放 派生值在读取侧核对
        BitConverter.GetBytes((uint)rgPreKern.Length ^ Q.uPrekMask).CopyTo(rgEntry, iTail + 16);//前置内核段长度 静态掩码 摆放需要
        BitConverter.GetBytes(uPolicyEcho ^ uTailKeyMask).CopyTo(rgEntry, iTail + 20);//布局策略期望值 掩码来自roll钥匙字段 不跑前置内核读不出来
        BitConverter.GetBytes((ulong)rgBoot.Length ^ Q.uLenMask).CopyTo(rgEntry, (int)lEntry - 16);
        BitConverter.GetBytes(uChk ^ Q.uChkMask).CopyTo(rgEntry, (int)lEntry - 8);
        return rgEntry;
    }

    internal static bool AuditEntryLayer(byte[] rgExe, out string sWhy, string sOutDir = "")//入口镜像之外不得再出现可解析的PE镜像 掩码剥掉后roll与引擎的结构必须对得上
    {
        sWhy = "";
        int iMarker = FindMarkerOff(rgExe);
        if (iMarker < 0) { sWhy = "bundle marker not found"; return false; }
        int iPos = (int)BitConverter.ToInt64(rgExe, iMarker - 8);
        if (iPos < 0 || iPos + 16 > rgExe.Length) { sWhy = "bundle header offset out of range"; return false; }
        uint uMajor = BitConverter.ToUInt32(rgExe, iPos); iPos += 4;
        iPos += 4;
        int iCount = BitConverter.ToInt32(rgExe, iPos); iPos += 4;
        if (iCount <= 0 || iCount > 0x1000) { sWhy = "entry count bad"; return false; }
        if (iPos >= rgExe.Length) { sWhy = "bundle header truncated"; return false; }
        iPos += 1 + rgExe[iPos];//bundleId
        if (uMajor >= 2) iPos += 40;
        long lOff0 = 0, lSz0 = 0;
        for (int i = 0; i < iCount && i < 0x1000; i++)
        {
            if (iPos + 17 > rgExe.Length) { sWhy = "entry table truncated"; return false; }
            long lOff = BitConverter.ToInt64(rgExe, iPos); iPos += 8;
            long lSz = BitConverter.ToInt64(rgExe, iPos); iPos += 8;
            if (uMajor >= 6) iPos += 8;
            iPos += 1;
            if (iPos >= rgExe.Length) { sWhy = "entry table truncated"; return false; }
            iPos += 1 + rgExe[iPos];//条目名
            if (i == 0) { lOff0 = lOff; lSz0 = lSz; }
        }
        if (lSz0 <= Q.iTailLen || lOff0 <= 0 || lOff0 > rgExe.Length - lSz0) { sWhy = "app entry bounds bad"; return false; }
        int iCipher = (int)(BitConverter.ToUInt64(rgExe, (int)(lOff0 + lSz0) - 16) ^ Q.uLenMask);
        int iLayer = (int)(lSz0 - Q.iTailLen - iCipher);
        Console.WriteLine($"[entrylayer] audit domain=0x{Q.uDomainSeed:X8} lenMask=0x{Q.uLenMask:X16} tailLen=0x{BitConverter.ToUInt64(rgExe, (int)(lOff0 + lSz0) - 16):X16} entrySize={lSz0} cipher={iCipher} layer={iLayer}");
        if (iLayer <= 0) { Console.WriteLine("[entrylayer] audit: no entry layer (boot is the entry itself)"); return true; }
        int iRollLen = BitConverter.ToInt32(rgExe, (int)(lOff0 + lSz0) - Q.iTailLen + 0);
        int iEngLen = BitConverter.ToInt32(rgExe, (int)(lOff0 + lSz0) - Q.iTailLen + 4);
        byte[] rgHead = new byte[Math.Min(Q.iHeadSample, iLayer)];
        Array.Copy(rgExe, (int)lOff0, rgHead, 0, rgHead.Length);
        int iPrekLen = BitConverter.ToInt32(rgExe, (int)(lOff0 + lSz0) - Q.iTailLen + 16) ^ (int)Q.uPrekMask;
        uint uRollKey = BitConverter.ToUInt32(rgExe, (int)(lOff0 + lSz0) - Q.iTailLen + 8) ^ Q.TailKeyMaskOf(PreKernEcho(rgHead, Q.uDomainSeed));//掩码含前置内核输出 离线用明文回声对出同一值
        uint uPolicy = BitConverter.ToUInt32(rgExe, (int)(lOff0 + lSz0) - Q.iTailLen + 12) ^ Q.uLayoutMask;//尾部存的策略码必须等于镜像头与roll钥匙派生的值 不符即判错配
        uint uDerived = Q.EntryLayout(rgHead, uRollKey);
        if ((uPolicy & ~7u) != 0 || uPolicy != uDerived)
        {
            Array.Clear(rgHead, 0, rgHead.Length);
            sWhy = $"entry layer layout policy mismatch stored=0x{uPolicy:X} derived=0x{uDerived:X}";
            return false;
        }
        Q.EntrySegmentOffsets(iLayer - iPrekLen, iRollLen, iEngLen, uPolicy, out int iRollOff, out int iEngOff);
        if (iRollOff <= 0 || iEngLen < 0 || iRollOff + iRollLen > iLayer || iEngOff + iEngLen > iLayer)
        {
            Array.Clear(rgHead, 0, rgHead.Length);
            sWhy = $"entry layer layout bad roll={iRollLen} eng={iEngLen} layer={iLayer} policy=0x{uPolicy:X}";
            return false;
        }
        int iMz = 0, iPe = 0;//入口镜像之后的整段区间里不许有能解析成PE的镜像
        for (int i = (int)lOff0 + 1; i + 2 <= lOff0 + iLayer; i++)
        {
            if (rgExe[i] != 0x4D || rgExe[i + 1] != 0x5A) continue;
            iMz++;
            if (i + 0x40 > rgExe.Length) continue;//先框住再索引 否则审计会抛异常而不是判失败
            int iHdr = i + BitConverter.ToInt32(rgExe, i + 0x3C);
            if (iHdr > i && iHdr + 24 <= rgExe.Length && BitConverter.ToUInt32(rgExe, iHdr) == 0x4550) iPe++;
        }
        byte[] rgRoll = new byte[Math.Max(iRollLen, 0)];//掩码剥掉并按策略反转回来后两段必须各自还原成预期形状
        byte[] rgEngine = new byte[Math.Max(iEngLen, 0)];
        if (iRollLen > 0) Array.Copy(rgExe, (int)lOff0 + iRollOff, rgRoll, 0, iRollLen);
        if (iEngLen > 0) Array.Copy(rgExe, (int)lOff0 + iEngOff, rgEngine, 0, iEngLen);
        if (iRollLen > 0) Q.Xor(rgRoll, 0, ChMask.Mask(rgHead, lSz0, uRollKey, 1, iRollLen, 0, 0));
        if (iEngLen > 0) Q.Xor(rgEngine, 0, ChMask.Mask(rgHead, lSz0, 0u, 2, iEngLen, 0, 0));//引擎掩码不含roll钥匙 前置内核要靠它先跑起来
        if ((uPolicy & 2u) != 0 && iRollLen > 0) Array.Reverse(rgRoll);
        if ((uPolicy & 4u) != 0 && iEngLen > 0) Array.Reverse(rgEngine);
        int iPrekOff = (int)lOff0 + iLayer - iPrekLen;//长度来自产物 先算区间再读 且必须有上界
        int iPrekCnt = iPrekLen >= 8 && iPrekOff >= 0 && iPrekOff + 4 <= rgExe.Length ? BitConverter.ToInt32(rgExe, iPrekOff) : 0;
        bool bPrekOk = iPrekLen >= 12 && iPrekOff >= 0 && iPrekCnt >= 1 && iPrekCnt <= 4;
        bool bRollOk = iRollLen >= 12 && BitConverter.ToInt32(rgRoll, 0) == NotenrolleComp.KernRollCount;
        int iLfanew = iEngLen > 0x40 ? BitConverter.ToInt32(rgEngine, 0x3C) : 0;//e_lfanew来自镜像自身 先框住再索引
        bool bEngOk = iEngLen > 0x40 && rgEngine[0] == 0x4D && rgEngine[1] == 0x5A
            && iLfanew > 0 && iLfanew + 4 <= iEngLen && BitConverter.ToUInt32(rgEngine, iLfanew) == 0x4550;
        if (bLayout) Console.WriteLine($"[entrylayer] audit layer={iLayer} policy=0x{uPolicy:X} roll={iRollLen}@{iRollOff} eng={iEngLen}@{iEngOff} prek={iPrekLen}@{iLayer - iPrekLen} plainMz={iMz} plainPe={iPe} rollOk={(bRollOk ? "yes" : "NO")} engOk={(bEngOk ? "yes" : "NO")} prekOk={(bPrekOk ? "yes" : "NO")}");
        if (sOutDir.Length > 0)
        {
            File.WriteAllBytes(Path.Combine(sOutDir, "entry_roll.bin"), rgRoll);
            File.WriteAllBytes(Path.Combine(sOutDir, "entry_engine.bin"), rgEngine);
        }
        Array.Clear(rgHead, 0, rgHead.Length);
        Array.Clear(rgRoll, 0, rgRoll.Length);
        Array.Clear(rgEngine, 0, rgEngine.Length);
        if (iPe != 0) { sWhy = $"{iPe} readable PE image(s) inside the entry layer"; return false; }
        if (!bPrekOk) { sWhy = "pre-kernel segment shape bad"; return false; }
        if (!bRollOk) { sWhy = "roll does not unlock to the expected shape"; return false; }
        if (!bEngOk) { sWhy = "engine does not unlock to a PE image"; return false; }
        Console.WriteLine("[entrylayer] audit PASS: no readable roll/engine in the product");
        return true;
    }

    private static int FindMarkerOff(byte[] rgFile)//签名扫描 命中处前8字节就是头部偏移
    {
        if (rgSigOut == null || rgSigOut.Length != 32) return -1;//锚点键可能未初始化 直接索引会抛NRE把找不到标记误报成崩溃
        for (int i = 8; i + 32 <= rgFile.Length; i++)
        {
            if (rgFile[i] != rgSigOut[0]) continue;
            int k = 1;
            while (k < 32 && rgFile[i + k] == rgSigOut[k]) k++;
            if (k == 32) return i;
        }
        return -1;
    }
}
