// WeaponDamageCalc/Lamarr/LamarrDecoder.cs
using System;
using System.Runtime.CompilerServices;

namespace Lamarr;

public static class LamarrDecoder
{
    private const uint LZ_DEFAULT_CNT = 0x12;
    private const uint LZ_1BYTE_CNT = 0xFF + LZ_DEFAULT_CNT;
    private const uint LZ_2BYTE_CNT = 0xFFFF + LZ_1BYTE_CNT;

    public static int Decode(byte[] pbOut, ref uint pcbOut, byte[] pbIn, uint cbIn)
    {
        uint outPos = 1;
        uint inPos = 1;
        int cur_nib = 0;

        pbOut[0] = pbIn[0];

        while (inPos < (cbIn - (uint)cur_nib))
        {
            int bc;
            uint tag;

            if (cur_nib != 0)
                tag = (uint)((pbIn[inPos] >> 4) | (pbIn[inPos + 1] << 4));
            else
                tag = pbIn[inPos];
            inPos++;

            for (bc = 0; bc < 8 && inPos < (cbIn - (uint)cur_nib) && outPos < pcbOut; bc++, tag <<= 1)
            {
                if ((tag & 0x80) != 0)
                {
                    uint r_pos, r_cnt, dist;

                    uint cflag;
                    if (cur_nib != 0)
                    {
                        if (inPos + 2 >= cbIn) break;
                        cflag = (uint)((pbIn[inPos] >> 4) | ((ReadLE16Safe(pbIn, inPos + 1, cbIn) & 0xFFF) << 4));
                    }
                    else
                    {
                        if (inPos + 3 >= cbIn) break;
                        cflag = ReadLE32Safe(pbIn, inPos, cbIn) & 0xFFFFF;
                    }
                    inPos++;

                    if (outPos < 0x881)
                    {
                        dist = cflag >> 1;
                        if ((cflag & 1) != 0)
                        {
                            inPos += (uint)cur_nib;
                            dist = (dist & 0x7FF) + 0x81;
                            cur_nib ^= 1;
                        }
                        else
                            dist = (dist & 0x7F) + 1;
                    }
                    else
                    {
                        dist = cflag >> 2;
                        switch (cflag & 3)
                        {
                            case 0: dist = (dist & 0x3F) + 1; break;
                            case 1: inPos += (uint)cur_nib; dist = (dist & 0x3FF) + 0x41; cur_nib ^= 1; break;
                            case 2: dist = (dist & 0x3FFF) + 0x441; inPos++; break;
                            case 3: inPos += (uint)(1 + cur_nib); dist = (dist & 0x3FFFF) + 0x4441; cur_nib ^= 1; break;
                        }
                    }

                    if (cur_nib != 0)
                        r_cnt = (uint)((ReadLE16Safe(pbIn, inPos, cbIn) >> 4) & 0xFFF);
                    else
                        r_cnt = (uint)(ReadLE16Safe(pbIn, inPos, cbIn) & 0xFFF);
                    inPos += (uint)cur_nib;
                    cur_nib ^= 1;

                    if ((r_cnt & 0xF) != 0xF)
                    {
                        r_cnt = (r_cnt & 0xF) + 3;
                    }
                    else
                    {
                        inPos++;
                        if (r_cnt != 0xFFF)
                        {
                            r_cnt = (r_cnt >> 4) + 0x12;
                        }
                        else
                        {
                            if (inPos + 4 > cbIn) break;
                            if (cur_nib != 0)
                                r_cnt = (uint)((ReadLE32Safe(pbIn, inPos, cbIn) >> 4) & 0xFFFF) + LZ_1BYTE_CNT;
                            else
                                r_cnt = (uint)(ReadLE16Safe(pbIn, inPos, cbIn) + LZ_1BYTE_CNT);
                            inPos += 2;
                            if (r_cnt == LZ_2BYTE_CNT)
                            {
                                uint uCopyCnt;
                                if (cur_nib != 0)
                                {
                                    uCopyCnt = ((uint)pbIn[inPos - 4] & 0xFC) << 5;
                                    inPos++;
                                    cur_nib = 0;
                                }
                                else
                                {
                                    uCopyCnt = (uint)((ReadLE16Safe(pbIn, inPos - 5, cbIn) & 0xFC0) << 1);
                                }
                                uCopyCnt += (tag & 0x7F) + 4;
                                uCopyCnt <<= 1;
                                while (uCopyCnt-- > 0 && outPos < pcbOut)
                                {
                                    pbOut[outPos++] = pbIn[inPos++];
                                    pbOut[outPos++] = pbIn[inPos++];
                                    pbOut[outPos++] = pbIn[inPos++];
                                    pbOut[outPos++] = pbIn[inPos++];
                                }
                                break;
                            }
                        }
                    }

                    if (outPos < dist) return 0x104;
                    if ((outPos + r_cnt) > pcbOut) return 0x111;

                    r_pos = outPos - dist;
                    while (r_cnt-- > 0 && outPos < pcbOut)
                        pbOut[outPos++] = pbOut[r_pos++];
                }
                else
                {
                    pbOut[outPos++] = (byte)((cur_nib != 0) ? ((pbIn[inPos] >> 4) | (pbIn[inPos + 1] << 4)) : pbIn[inPos]);
                    inPos++;
                }
            }
        }

        pcbOut = outPos;
        return 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ushort ReadLE16Safe(byte[] rgBuf, uint uOff, uint cbIn)
    {
        if (uOff + 1 >= cbIn) return 0;
        return (ushort)(rgBuf[uOff] | (rgBuf[uOff + 1] << 8));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ReadLE32Safe(byte[] rgBuf, uint uOff, uint cbIn)
    {
        if (uOff + 3 >= cbIn) return 0;
        return (uint)(rgBuf[uOff] | (rgBuf[uOff + 1] << 8) | (rgBuf[uOff + 2] << 16) | (rgBuf[uOff + 3] << 24));
    }
}