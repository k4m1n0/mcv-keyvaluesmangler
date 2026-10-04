namespace A0;

internal static class Q
{
    public static uint uDomainSeed = 0x5EED0001u;//逐产物域 打包期把发布种子派生的值写进来 其余域常量由它现算 占位值必须是长形式立即数

    private static ulong Mix(uint uSeed, ulong uSalt)
    {
        ulong u = 0xCBF29CE484222325uL ^ uSalt;
        u = (u ^ uSeed) * 0x100000001B3uL;
        u ^= u >> 29;
        u *= 0xBF58476D1CE4E5B9uL;
        u ^= u >> 32;
        return u;
    }

    private static ulong uDomainA => Mix(uDomainSeed, 0xA3F1C7D95E2B6849uL);//域常量随发布种子走 换种子即换域
    private static ulong uDomainB => Mix(uDomainSeed, 0x5D2E8B4173C9AF06uL);
    public static ulong uLenMask => Mix(uDomainSeed, 0x1F2E3D4C5B6A7988uL);//条目尾部长度字段的掩码与密文校验 各占8字节
    public static ulong uChkMask => Mix(uDomainSeed, 0x88796A5B4C3D2E1FuL);
    public static uint TailKeyMaskOf(uint uPre) => (uint)Mix(uDomainSeed ^ uPre, 0x4E6D2A9F13C7B58DuL);//掩码掺前置内核输出 不执行那枚内核就读不到钥匙字段
    public static uint uPrekMask => (uint)Mix(uDomainSeed, 0x3C5A9E71D28B4F60uL);//前置内核长度字段的掩码 不涉密钥
    public static uint uRollSumMask => (uint)Mix(uDomainSeed, 0x71B3C5E79D2F4068uL);
    public static uint uEngSumMask => (uint)Mix(uDomainSeed, 0x2A6F8C4E1B73D950uL);
    public static uint uLayoutMask => (uint)Mix(uDomainSeed, 0xD3A5F0C7B19E2468uL);//布局策略码的掩码 策略码只允许低3位bit0交换顺序bit1反转roll bit2反转引擎
    public const int iRollUnits = 10;//roll单元数 7内核加32位摘要加64位摘要加ChaCha12流

    public const int iWindowBytes = 0x1000;//数据页里的材料窗口上限 其余空间留给VM栈 引擎在序言里核对 超窗即h_bad 整个数据页等于一次窗口
    public const int CtxWinBytes = 160;   //ctx私有槽从128起 0密钥 8引擎基址 16代码长 20链入 24链出 28解码暂存
    public const int CtxMatLen = 164;     //序言存下的材料长度 退出时按它擦窗口
    public const int CtxMatIo = 168;      //1=材料进出 结果落在材料里 引擎不擦由调用方回读

    public static byte[] ChainMat()//链材料 按序走链时喂给每枚单元的固定输入 打包端与运行期与自测三处必须逐字节相同 只解密不执行就拿不到下一单元的键
    {
        byte[] rg = new byte[32];
        for (int i = 0; i < rg.Length; i++) rg[i] = (byte)(i * 37 + 11);
        return rg;
    }

    public const int iPageBytes = 0x400;//数据页窗口化 页切成4个槽ctx[172]指向驱动给的置换表 0表示恒等 表项是虚拟页到物理槽 每进程不同
    public const int iPageShift = 10;
    public const int iPageMask = 0x3FF;
    public const int iPageSlots = 4;
    public const int CtxRunTbl = 180;//dispatch表由调用方逐单元提供ctx+180是它的指针 +0为perm256B +256为handler表8B每槽 引擎先验ctx+188的opt-in标签
    public const int CtxRunTag = 188;
    public const uint CtxRunTagVal = 0x5A17C0DEu;

    public const int CtxPageTbl = 172;

    public static IntPtr CtxAlloc() { return CtxAlloc(192); }//ctx必须整块清零再填槽 有垃圾值会让引擎走进按表翻译分支 打包期链与运行期链就此分叉
    public static IntPtr CtxAlloc(int cb)
    {
        IntPtr p = System.Runtime.InteropServices.Marshal.AllocHGlobal(cb);
        int i = 0;
        for (; i + 8 <= cb; i += 8) System.Runtime.InteropServices.Marshal.WriteInt64(p, i, 0);
        for (; i < cb; i++) System.Runtime.InteropServices.Marshal.WriteByte(p, i, 0);
        return p;
    }
    public static uint[] PageIdent()//恒等表 与不设表逐字节等价
    {
        uint[] rg = new uint[iPageSlots];
        for (int i = 0; i < rg.Length; i++) rg[i] = (uint)i;
        return rg;
    }

    public static int PagePhys(uint[] rgTbl, int iVAddr)//虚拟地址到物理偏移 驱动读结果与回写材料时用 引擎页翻译按同一算式
    {
        int iSlot = rgTbl == null ? (iVAddr >> iPageShift) : (int)(rgTbl[iVAddr >> iPageShift] & (uint)(iPageSlots - 1));
        return (iSlot << iPageShift) | (iVAddr & iPageMask);
    }

    public static uint[] PagePerm(uint uSeed)//每次运行一张页置换 只置换材料页 栈页保持恒等 因为CALL与RET走裸访存不经页翻译
    {
        uint[] rg = PageIdent();
        uint u = uSeed * 1664525u + 1013904223u;
        u ^= u >> 15;
        if ((u & 1u) != 0) { (rg[0], rg[1]) = (rg[1], rg[0]); }
        return rg;
    }

    public static void PageStage(byte[] rgPhys, byte[] rgVirt, int iLen, uint[] rgT)//按表把虚拟材料落到物理槽 与引擎翻译互逆
    {
        for (int i = 0; i < iLen && i < 0x1000; i++)
        {
            int iSlot = (int)(rgT[i >> iPageShift] & (uint)(iPageSlots - 1));
            rgPhys[(iSlot << iPageShift) | (i & iPageMask)] = rgVirt[i];
        }
    }

    public static void PageUnstage(byte[] rgVirt, byte[] rgPhys, int iLen, uint[] rgT)//材料进出型调用之后 按表把物理槽取回虚拟序
    {
        for (int i = 0; i < iLen && i < 0x1000; i++)
        {
            int iSlot = (int)(rgT[i >> iPageShift] & (uint)(iPageSlots - 1));
            rgVirt[i] = rgPhys[(iSlot << iPageShift) | (i & iPageMask)];
        }
    }

    public static void PageTblToPtr(System.IntPtr p, uint[] rgT)//表的内存形态 引擎按dword读 调用方负责这块内存存活
    {
        for (int i = 0; i < iPageSlots; i++)
            System.Runtime.InteropServices.Marshal.WriteInt32(p, i * 4, unchecked((int)rgT[i]));
    }

    public const int VmMatState = 0, VmMatIdx = 8, VmMatFlag = 16, VmMatData = 48;//VM调用面材料头布局 状态 索引基 索引开关 保留24避开Key区0x20 之后是数据
    public const int iTailLen = 40;//条目尾部40字节 [roll长度][引擎长度][roll钥匙^掩码][布局策略^掩码][前置内核长度^掩码][保留] + [密文长度 ^ 掩码][密文校验 ^ 掩码]
    public const int iHeadSample = 4096;//密钥材料取镜像头部的字节数

    public static uint PreKernKey(byte[] rgHead)//前置内核的at-rest钥匙只由镜像头派生 不需要roll钥匙即可解出并执行 以此打破自举环
    {
        ulong u = Hash64(rgHead, 0, rgHead.Length) ^ uDomainB;
        u = (u ^ (u >> 29)) * 0xBF58476D1CE4E5B9uL;
        u ^= u >> 32;
        return (uint)u | 1u;
    }

    public static ulong Hash64(byte[] rg, int iOff, int iLen)//FNV-1a 64头部样本取摘要密文取校验都用它
    {
        ulong uHash = 0xCBF29CE484222325uL;
        for (int i = 0; i < iLen; i++)
        {
            uHash ^= rg[iOff + i];
            uHash *= 0x100000001B3uL;
        }
        return uHash;
    }

    public static uint Hash32(byte[] rg, int iOff, int iLen)//32位FNV-1a 入口层roll与引擎的明文摘要用它
    {
        uint uHash = 0x811C9DC5u;
        for (int i = 0; i < iLen; i++) { uHash ^= rg[iOff + i]; uHash *= 0x01000193u; }
        return uHash;
    }

    public static ulong TableDigest(long[] rgSz)//条目数与各条目声明长度都进密钥材料
    {
        ulong uHash = 0xCBF29CE484222325uL ^ (ulong)rgSz.Length;
        for (int i = 0; i < rgSz.Length; i++)
        {
            uHash = (uHash ^ (ulong)rgSz[i]) * 0x100000001B3uL;
            uHash ^= (ulong)(uint)i * 0x9E3779B97F4A7C15uL;
        }
        return uHash ^ uDomainB;
    }

    public static ulong SelfMix(byte[] rgA, int cbA, byte[] rgB, int cbB)//自指量 打包端两段独立数组 顺序固定为roll在前
    {
        ulong uHash = 0xCBF29CE484222325uL;
        for (int i = 0; i < cbA; i++) { uHash ^= rgA[i]; uHash *= 0x100000001B3uL; }
        for (int i = 0; i < cbB; i++) { uHash ^= rgB[i]; uHash *= 0x100000001B3uL; }
        return uHash;
    }

    public static ulong SelfMixRange(byte[] rgEnt, int iOffA, int cbA, int iOffB, int cbB)//校验端同一缓冲的两个区间
    {
        ulong uHash = 0xCBF29CE484222325uL;
        for (int i = 0; i < cbA; i++) { uHash ^= rgEnt[iOffA + i]; uHash *= 0x100000001B3uL; }
        for (int i = 0; i < cbB; i++) { uHash ^= rgEnt[iOffB + i]; uHash *= 0x100000001B3uL; }
        return uHash;
    }

    public static ulong KeyStreamState(byte[] rgHead, long lEntrySize, int iCipherLen, ulong uTableDigest, ulong uVmMix, ulong uSelfMix)//只暴露密钥流的初始状态 运行期用它喂VM单元 由VM生成整条流
    {
        ulong uState = Hash64(rgHead, 0, rgHead.Length) ^ uDomainA ^ uTableDigest ^ uVmMix ^ uSelfMix;
        uState ^= (ulong)lEntrySize * 0x9E3779B97F4A7C15uL;
        uState ^= (ulong)(uint)iCipherLen << 17;
        return uState;
    }

    public static ulong BlobMaskState(byte[] rgHead, long lEntrySize, uint uRollKey, int iKind, int cb)//容器掩码的初始状态 运行期由VM单元生成掩码流
    {
        ulong uState = Hash64(rgHead, 0, rgHead.Length) ^ uDomainB;
        uState ^= (ulong)uRollKey * 0x9E3779B97F4A7C15uL;
        uState ^= (ulong)(uint)iKind << 13;
        uState ^= (ulong)lEntrySize * 0xD6E8FEB86659FD93uL;
        uState ^= (ulong)(uint)cb << 23;
        return uState;
    }

    public static byte[] KeyStream(byte[] rgHead, long lEntrySize, int iCipherLen, ulong uTableDigest, ulong uVmMix, ulong uSelfMix)//密钥流按splitmix64派生 入口层再掺VM内核输出与产物自指量
    {
        ulong uState = KeyStreamState(rgHead, lEntrySize, iCipherLen, uTableDigest, uVmMix, uSelfMix);
        byte[] rgKey = new byte[iCipherLen];
        for (int i = 0; i < rgKey.Length; i += 8)
        {
            uState += 0x9E3779B97F4A7C15uL;
            ulong uZ = uState;
            uZ = (uZ ^ (uZ >> 30)) * 0xBF58476D1CE4E5B9uL;
            uZ = (uZ ^ (uZ >> 27)) * 0x94D049BB133111EBuL;
            uZ ^= uZ >> 31;
            uZ ^= (ulong)(uint)i * 0xD6E8FEB86659FD93uL;
            int n = Math.Min(8, rgKey.Length - i);
            for (int k = 0; k < n; k++) rgKey[i + k] = (byte)(uZ >> (k * 8));
        }
        return rgKey;
    }

    public static byte[] BlobMask(byte[] rgHead, long lEntrySize, uint uRollKey, int iKind, int cb)//入口层容器掩码 产物里roll与引擎不以明文存放 掩码由域常量与roll钥匙与布局量派生 异或自逆
    {
        ulong uState = BlobMaskState(rgHead, lEntrySize, uRollKey, iKind, cb);//状态算法只此一处 避免逐行复制漂移
        byte[] rgKey = new byte[cb];
        for (int i = 0; i < cb; i += 8)
        {
            uState += 0x9E3779B97F4A7C15uL;
            ulong uZ = uState;
            uZ = (uZ ^ (uZ >> 30)) * 0xBF58476D1CE4E5B9uL;
            uZ = (uZ ^ (uZ >> 27)) * 0x94D049BB133111EBuL;
            uZ ^= uZ >> 31;
            int n = Math.Min(8, cb - i);
            for (int k = 0; k < n; k++) rgKey[i + k] = (byte)(uZ >> (k * 8));
        }
        return rgKey;
    }

    public static uint EntryLayout(byte[] rgHead, uint uRollKey)//入口层布局策略 由镜像头与roll钥匙逐产物派生 尾部另存一份掩码值 两者不等即判改过
    {
        ulong u = Hash64(rgHead, 0, rgHead.Length) ^ uDomainA;
        u ^= (ulong)uRollKey * 0x9E3779B97F4A7C15uL;
        u = (u ^ (u >> 29)) * 0xBF58476D1CE4E5B9uL;
        u ^= u >> 32;
        return (uint)u & 7u;
    }

    public static void EntrySegmentOffsets(int iLayer, int iRollLen, int iEngLen, uint uPolicy, out int iRollOff, out int iEngOff)//按策略把两段摆进iLayer减rollLen减engLen 到iLayer 的区间 两端用同一个函数算偏移
    {
        int iAt = iLayer - iRollLen - iEngLen;
        if ((uPolicy & 1u) != 0) { iEngOff = iAt; iRollOff = iAt + iEngLen; }
        else { iRollOff = iAt; iEngOff = iAt + iRollLen; }
    }

    public static void Xor(byte[] rgData, int iOff, byte[] rgKey)
    {
        for (int i = 0; i < rgKey.Length; i++) rgData[iOff + i] ^= rgKey[i];
    }
}
