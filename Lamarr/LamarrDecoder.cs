using System;
using System.Runtime.CompilerServices;

namespace Lamarr;

public static class LamarrDecoder
{
    //编码阈值
    private const uint uCntDefault = 0x12;
    private const uint uCnt1Byte = 0xFF + uCntDefault;
    private const uint uCnt2Byte = 0xFFFF + uCnt1Byte;

    public static int Decode(byte[] rgOut, ref uint pcbOut, byte[] rgIn, uint cbIn)
    {
        uint uOutPos = 1;
        uint uInPos = 1;
        int iCurNib = 0;

        //首字节直接复制 格式要求
        rgOut[0] = rgIn[0];

        //一个tag byte用8个bit管后续8个item bit=1是match bit=0是literal
        while (uInPos < (cbIn - (uint)iCurNib))
        {
            int iBC;
            uint uTag;

            if (iCurNib != 0)
                uTag = (uint)((rgIn[uInPos] >> 4) | (rgIn[uInPos + 1] << 4));
            else
                uTag = rgIn[uInPos];
            uInPos++;

            for (iBC = 0; iBC < 8 && uInPos < (cbIn - (uint)iCurNib) && uOutPos < pcbOut; iBC++, uTag <<= 1)
            {
                if ((uTag & 0x80) != 0)
                {
                    uint uRPos, uRCnt, uDist;

                    uint uCFlag = iCurNib != 0
                        ? (ReadLE32Safe(rgIn, uInPos, cbIn) >> 4) & 0xFFFFF
                        : ReadLE32Safe(rgIn, uInPos, cbIn) & 0xFFFFF;
                    uInPos++;

                    //outPos<0x881用短距离编码 少1bit分两个bucket
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
                            uDist = (uDist & 0x7F) + 1;
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

                    if (iCurNib != 0)
                        uRCnt = (uint)((ReadLE16Safe(rgIn, uInPos, cbIn) >> 4) & 0xFFF);
                    else
                        uRCnt = (uint)(ReadLE16Safe(rgIn, uInPos, cbIn) & 0xFFF);
                    uInPos += (uint)iCurNib;
                    iCurNib ^= 1;

                    //4bit<15直接+3 得到3..17
                    if ((uRCnt & 0xF) != 0xF)
                    {
                        uRCnt = (uRCnt & 0xF) + 3;
                    }
                    else
                    {
                        uInPos++;
                        //4bit=15 读1字节扩展
                        if (uRCnt != 0xFFF)
                        {
                            uRCnt = (uRCnt >> 4) + 0x12;
                        }
                        else
                        {
                            if (iCurNib != 0)
                                uRCnt = (uint)((ReadLE32Safe(rgIn, uInPos, cbIn) >> 4) & 0xFFFF) + uCnt1Byte;
                            else
                                uRCnt = (uint)(ReadLE16Safe(rgIn, uInPos, cbIn) + uCnt1Byte);
                            uInPos += 2;
                            //哨兵值0x111+0xFFFF 触发非压缩块回退
                            if (uRCnt == uCnt2Byte)
                            {
                                uint uCopyCnt;
                                if (iCurNib != 0)
                                {
                                    uCopyCnt = ((uint)rgIn[uInPos - 4] & 0xFC) << 5;
                                    uInPos++;
                                    iCurNib = 0;
                                }
                                else
                                {
                                    uCopyCnt = (uint)((ReadLE16Safe(rgIn, uInPos - 5, cbIn) & 0xFC0) << 1);
                                }
                                uCopyCnt += (uTag & 0x7F) + 4;
                                uCopyCnt <<= 1;
                                while (uCopyCnt-- > 0 && uOutPos < pcbOut)
                                {
                                    rgOut[uOutPos++] = rgIn[uInPos++];
                                    rgOut[uOutPos++] = rgIn[uInPos++];
                                    rgOut[uOutPos++] = rgIn[uInPos++];
                                    rgOut[uOutPos++] = rgIn[uInPos++];
                                }
                                break;
                            }
                        }
                    }

                    if (uOutPos < uDist) return 0x104;//距离越过输出起始 数据损坏
                    if ((uOutPos + uRCnt) > pcbOut) return 0x111;//输出缓冲区不够

                    uRPos = uOutPos - uDist;
                    while (uRCnt-- > 0 && uOutPos < pcbOut)
                        rgOut[uOutPos++] = rgOut[uRPos++];
                }
                else
                {
                    rgOut[uOutPos++] = (byte)((iCurNib != 0) ? ((rgIn[uInPos] >> 4) | (rgIn[uInPos + 1] << 4)) : rgIn[uInPos]);
                    uInPos++;
                }
            }
        }

        pcbOut = uOutPos;
        return 0;
    }

    //逐字节尽力读 缺位补0 允许部分越界
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ushort ReadLE16Safe(byte[] rgBuf, uint uOff, uint cbIn)
    {
        uint uVal = 0;
        if (uOff < cbIn) uVal = rgBuf[uOff];
        if (uOff + 1 < cbIn) uVal |= (uint)(rgBuf[uOff + 1] << 8);
        return (ushort)uVal;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ReadLE32Safe(byte[] rgBuf, uint uOff, uint cbIn)
    {
        uint uVal = 0;
        if (uOff < cbIn) uVal = rgBuf[uOff];
        if (uOff + 1 < cbIn) uVal |= (uint)(rgBuf[uOff + 1] << 8);
        if (uOff + 2 < cbIn) uVal |= (uint)(rgBuf[uOff + 2] << 16);
        if (uOff + 3 < cbIn) uVal |= (uint)(rgBuf[uOff + 3] << 24);
        return uVal;
    }
}