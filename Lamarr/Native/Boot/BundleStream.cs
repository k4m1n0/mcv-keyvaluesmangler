using System.Buffers;
using System.Runtime.CompilerServices;

namespace BundleHost;

internal sealed class BundleStream : Stream
{
    private const uint uCnt1Byte = 0xFF + 0x12;
    private const uint uCnt2Byte = 0xFFFF + uCnt1Byte;//哨兵值0x10110 之后的字节是原始数据直接拷贝 不走压缩
    private const uint uHistMask = 0x7FFFF;//历史环512KB 编码器最大匹配距离0x4443F 取2的幂便于位与

    private readonly byte[] rgIn;
    private readonly uint cbIn;
    private readonly uint cbOut;
    private readonly int iOff;
    private byte[] rgHist;
    private byte[] rgPage;
    private bool bDisposed;
    private uint uInPos;
    private int iCurNib;
    private uint uOutPos = 1;
    private uint uTag;//tag的8bit可能被页边界切断 状态跨调用保留
    private int iBC;
    private bool bPending;
    private bool bFromHist;
    private bool bPSkip;
    private bool bSent;
    private uint uRemain;
    private uint uSrc;

    private uint iHist = 1;

    private int iPageOff = -1;
    private long lPos;

    //多条目容器每条目一段 压缩块从宿主缓冲任意偏移开始
    public BundleStream(byte[] rgCompressed, int iOff, int cbComp, uint cbOrig)
    {
        rgIn = rgCompressed;
        this.iOff = iOff;
        cbIn = (uint)(iOff + cbComp);
        cbOut = cbOrig;
        uInPos = (uint)iOff + 1;//tag从压缩块偏移1开始 0是首字节直拷
        rgHist = ArrayPool<byte>.Shared.Rent(0x80000);
        rgPage = ArrayPool<byte>.Shared.Rent(0x1000);
        rgHist[0] = rgCompressed[iOff];
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => cbOut;
    public override long Position { get => lPos; set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count)
    {
        int n = 0;
        while (n < count && lPos < cbOut)
        {
            int iPage = (int)(lPos / 0x1000);
            EnsurePage(iPage);
            int iInPage = (int)(lPos % 0x1000);
            int cbCopy = Math.Min(count - n, 0x1000 - iInPage);
            Array.Copy(rgPage, iInPage, buffer, offset + n, cbCopy);
            lPos += cbCopy;
            n += cbCopy;
        }
        return n;
    }

    private void EnsurePage(int iPage)
    {
        if (iPage == iPageOff) return;

        if (iPageOff >= 0)
            Array.Clear(rgPage, 0, 0x1000);

        DecodePage(iPage);
        iPageOff = iPage;
    }

    private void DecodePage(int iPage)
    {
        uint uPageStart = (uint)(iPage * 0x1000);
        uint uPageEnd = Math.Min(uPageStart + 0x1000u, cbOut);

        //格式约定 输出offset0=压缩块首字节 直接拷贝
        if (iPage == 0)
            rgPage[0] = rgIn[iOff];

        //跳过前面的页 只推进解码状态 不产出明文
        while (uOutPos < uPageStart)
        {
            if (!DecodeItem(uPageStart, true))
                throw new InvalidDataException("corrupted stream: exhausted");
        }

        while (uOutPos < uPageEnd)
        {
            if (!DecodeItem(uPageEnd, false))
                throw new InvalidDataException("corrupted stream: exhausted");
        }
    }

    //一次处理一个item到页尾 跨页match/原始块挂bPending续传 tag状态跨调用保留
    //返回false表示输入耗尽(数据损坏) 由调用方抛异常
    private bool DecodeItem(uint uPageEnd, bool bSkip)
    {
        if (bPending)
        {
            Pump(uPageEnd);
            if (uRemain > 0)
                return true;
            bPending = false;
            if (bSent)
                iBC = 0;//非压缩块完成 该tag剩余位作废
            else
            {
                uTag <<= 1;
                iBC = (iBC + 1) & 7;
            }
            if (uOutPos >= uPageEnd)
                return true;//续传恰好填满本页 交给外层翻页
        }

        if (iBC == 0)
        {
            if (uInPos >= cbIn - (uint)iCurNib)
                return false;
            uTag = Next();
        }

        if ((uTag & 0x80) != 0)
        {
            uint uDist = ReadDist();
            uint uRCnt = ReadLen();

            if (uRCnt == uCnt2Byte)
            {
                //非压缩块 uCopyCnt=4字节组数×2 与LamarrDecoder一致
                uint uCopyCnt;
                if (iCurNib != 0)
                {
                    uCopyCnt = ((uint)rgIn[uInPos - 4] & 0xFC) << 5;
                    uInPos++;
                    iCurNib = 0;
                }
                else
                {
                    uCopyCnt = (uint)((Read16(uInPos - 5) & 0xFC0) << 1);
                }
                uCopyCnt += (uTag & 0x7F) + 4;
                uCopyCnt <<= 1;

                bPending = true;
                bFromHist = false;
                bPSkip = bSkip;
                bSent = true;
                uRemain = uCopyCnt << 2;

                Pump(uPageEnd);
                if (uRemain > 0)
                    return true;
                bPending = false;
                iBC = 0;//哨兵块后该tag剩余位作废 与参考实现break一致
                return true;
            }

            if (uOutPos < uDist)
                throw new InvalidDataException($"corrupted stream: distance {uDist}@pos{uOutPos}");
            if ((uOutPos + uRCnt) > cbOut)
                throw new InvalidDataException($"corrupted stream: match {uOutPos}+{uRCnt}@{cbOut}");

            bPending = true;
            bFromHist = true;
            bPSkip = bSkip;
            bSent = false;
            uSrc = uOutPos - uDist;
            uRemain = uRCnt;

            Pump(uPageEnd);
            if (uRemain > 0)
                return true;
            bPending = false;
        }
        else
        {
            if (uInPos >= cbIn - (uint)iCurNib)
                return false;
            WriteByte(Next(), bSkip);
        }

        uTag <<= 1;
        iBC = (iBC + 1) & 7;
        return true;
    }

    //非压缩块与不重叠match整段拷贝 重叠时源会被覆盖 只能逐字节
    private void Pump(uint uPageEnd)
    {
        while (uRemain > 0 && uOutPos < uPageEnd)
        {
            int n = (int)Math.Min(uRemain, (uint)(uPageEnd - uOutPos));
            if (n <= 0) break;

            if (!bFromHist)
            {
                int nAvail = (int)(cbIn - uInPos);
                if (nAvail <= 0)
                    throw new InvalidDataException("corrupted stream: overrun");
                if (n > nAvail) n = nAvail;

                int uDst = (int)(iHist & uHistMask);
                int nSeg = Math.Min(n, (int)(uHistMask + 1 - uDst));
                Array.Copy(rgIn, (int)uInPos, rgHist, uDst, nSeg);
                if (n > nSeg)
                    Array.Copy(rgIn, (int)uInPos + nSeg, rgHist, 0, n - nSeg);
                iHist += (uint)n;

                if (!bPSkip)
                    Array.Copy(rgIn, (int)uInPos, rgPage, (int)(uOutPos & 0xFFF), n);

                uInPos += (uint)n;
                uOutPos += (uint)n;
                uRemain -= (uint)n;
            }
            else
            {
                uint uDist = iHist - uSrc;
                if (uDist >= (uint)n)
                {
                    uint uS = uSrc & uHistMask;
                    uint uD = iHist & uHistMask;
                    int done = 0;
                    while (done < n)
                    {
                        int nSeg = Math.Min(n - done, (int)(uHistMask + 1 - uD));
                        nSeg = Math.Min(nSeg, (int)(uHistMask + 1 - uS));
                        Array.Copy(rgHist, (int)uS, rgHist, (int)uD, nSeg);
                        uS = (uS + (uint)nSeg) & uHistMask;
                        uD = (uD + (uint)nSeg) & uHistMask;
                        done += nSeg;
                    }
                    iHist += (uint)n;

                    if (!bPSkip)
                    {
                        int pg = (int)(uOutPos & 0xFFF);
                        uint uS2 = uSrc & uHistMask;
                        int nS1 = (int)Math.Min((uint)n, uHistMask + 1 - uS2);
                        Array.Copy(rgHist, (int)uS2, rgPage, pg, nS1);
                        if (n > nS1)
                            Array.Copy(rgHist, 0, rgPage, pg + nS1, n - nS1);
                    }

                    uSrc += (uint)n;
                    uOutPos += (uint)n;
                    uRemain -= (uint)n;
                }
                else
                {
                    //重叠匹配(dist<len)逐字节 源在覆盖前读取
                    byte b = rgHist[uSrc & uHistMask];
                    uSrc++;
                    WriteByte(b, bPSkip);
                    uRemain--;
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteByte(byte b, bool bSkip)
    {
        rgHist[iHist++ & uHistMask] = b;
        if (!bSkip)
            rgPage[(int)(uOutPos & 0xFFF)] = b;
        uOutPos++;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private uint ReadDist()
    {
        uint uCFlag;
        if (iCurNib != 0)
            uCFlag = (Read32(uInPos) >> 4) & 0xFFFFF;
        else
            uCFlag = Read32(uInPos) & 0xFFFFF;
        uInPos++;

        uint uDist;
        if (uOutPos < 0x881)
        {
            uDist = uCFlag >> 1;
            if ((uCFlag & 1) != 0)
            {
                uInPos += (uint)iCurNib;
                uDist = (uDist & 0x7FF) + 0x81;
                iCurNib ^= 1;
            }
            else
            {
                uDist = (uDist & 0x7F) + 1;
            }
        }
        else
        {
            uDist = uCFlag >> 2;
            switch (uCFlag & 3)
            {
                case 0: uDist = (uDist & 0x3F) + 1; break;
                case 1: uInPos += (uint)iCurNib; uDist = (uDist & 0x3FF) + 0x41; iCurNib ^= 1; break;
                case 2: uDist = (uDist & 0x3FFF) + 0x441; uInPos++; break;
                case 3: uInPos += (uint)(1 + iCurNib); uDist = (uDist & 0x3FFFF) + 0x4441; iCurNib ^= 1; break;
            }
        }
        return uDist;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private uint ReadLen()
    {
        uint uRCnt;
        if (iCurNib != 0)
            uRCnt = (uint)((Read16(uInPos) >> 4) & 0xFFF);
        else
            uRCnt = (uint)(Read16(uInPos) & 0xFFF);

        uInPos += (uint)iCurNib;
        iCurNib ^= 1;

        if ((uRCnt & 0xF) != 0xF)
        {
            uRCnt = (uRCnt & 0xF) + 3;
        }
        else
        {
            uInPos++;
            if (uRCnt != 0xFFF)
            {
                uRCnt = (uRCnt >> 4) + 0x12;
            }
            else
            {
                if (iCurNib != 0)
                    uRCnt = ((uint)((Read32(uInPos) >> 4) & 0xFFFF)) + uCnt1Byte;
                else
                    uRCnt = ((uint)Read16(uInPos)) + uCnt1Byte;
                uInPos += 2;
            }
        }
        return uRCnt;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private byte Next()
    {
        byte b;
        if (iCurNib != 0)
            b = (byte)((rgIn[uInPos] >> 4) | (rgIn[uInPos + 1] << 4));
        else
            b = rgIn[uInPos];
        uInPos++;
        return b;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ushort Read16(uint uOff)
    {
        uint u = 0;
        if (uOff < cbIn) u = rgIn[uOff];
        if (uOff + 1 < cbIn) u |= (uint)(rgIn[uOff + 1] << 8);
        return (ushort)u;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private uint Read32(uint uOff)
    {
        uint u = 0;
        if (uOff < cbIn) u = rgIn[uOff];
        if (uOff + 1 < cbIn) u |= (uint)(rgIn[uOff + 1] << 8);
        if (uOff + 2 < cbIn) u |= (uint)(rgIn[uOff + 2] << 16);
        if (uOff + 3 < cbIn) u |= (uint)(rgIn[uOff + 3] << 24);
        return u;
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing && !bDisposed)
        {
            bDisposed = true;
            //清零后归还池 防明文残留 也减少大对象分配
            Array.Clear(rgHist, 0, rgHist.Length);
            Array.Clear(rgPage, 0, rgPage.Length);
            ArrayPool<byte>.Shared.Return(rgHist);
            ArrayPool<byte>.Shared.Return(rgPage);
        }
        base.Dispose(disposing);
    }
}
