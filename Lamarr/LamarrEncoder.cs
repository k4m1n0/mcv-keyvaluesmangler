using System;
using System.Runtime.CompilerServices;

namespace Lamarr
{
    public class LamarrEncoder
    {
        private const int DICT_SIZE = 0x20000;
        private const int IDX_SIZE  = 0x20000;
        private const int DICT_MSK  = DICT_SIZE - 1;
        private const int IDX_MSK   = IDX_SIZE - 1;
        private const int RS_HASH_BITS = 9;

        private const uint DEFAULT_CNT   = 0x12;
        private const uint _1BYTE_CNT    = 0xFF + DEFAULT_CNT;
        private const uint _2BYTE_CNT    = 0xFFFF + _1BYTE_CNT;
        private const uint MAX_2BYTE_CNT = _2BYTE_CNT - 1;

        private const uint SHORT_DIST0 = 0x80;
        private const uint SHORT_DIST1 = 0x800 | SHORT_DIST0;
        private const uint LONG_DIST0  = 0x40;
        private const uint LONG_DIST1  = 0x400 | LONG_DIST0;
        private const uint LONG_DIST2  = 0x4000 | LONG_DIST1;
        private const uint MAX_GAMMA_DIST = (0x40000 | LONG_DIST2) - 1;

        private byte[] rgIn = null!, rgOut = null!;
        private uint cbIn, cbOutCap;
        private int[] rgPtr = null!, rgIdx = null!;

        private int iOutPos;
        private int iCurNib;
        private int iTagNib;

        private uint uInPtr, uProcessedData;
        private uint uGammaDist, uMatchCnt;
        private int iTagPos, iUCTagPos, iUCNib, iCpyTag;
        private uint cbUCData;
        private byte bBitMsk, bThisTag;

        public static uint GetMaxEncodedSize(uint cbIn)
        {
            return cbIn + ((cbIn + 7) >> 3) + 0x21;
        }

        public static int Encode(byte[] rgOut, ref uint pcbOut, byte[] rgIn, uint cbIn)
        {
            var encoder = new LamarrEncoder();
            return encoder.EncodeInternal(rgOut, ref pcbOut, rgIn, cbIn);
        }

        private int EncodeInternal(byte[] rgOut, ref uint pcbOut, byte[] rgIn, uint cbIn)
        {
            this.rgIn = rgIn; this.rgOut = rgOut; this.cbIn = cbIn;
            cbOutCap = pcbOut;

            rgPtr = new int[DICT_SIZE];
            rgIdx = new int[IDX_SIZE];
            Array.Fill(rgPtr, -1);
            Array.Fill(rgIdx, -1);

            uGammaDist = 0;
            bBitMsk = 0x80; bThisTag = 0;
            iCurNib = 0; iTagNib = 0;

            int iEndOut = (int)cbOutCap - 0x21;

            rgOut[0] = rgIn[0];
            iTagPos = 1;
            iUCTagPos = iTagPos;
            iUCNib = 0;
            iOutPos = iTagPos + 1;

            iCpyTag = 0;
            cbUCData = 0; uProcessedData = 1; uInPtr = 1;

            rgPtr[Hash(0)] = 0;

            while (cbIn > uInPtr)
            {
                uint uCurMatchCnt = FindMatch();
                if (uCurMatchCnt > cbIn - uInPtr)
                    uCurMatchCnt = cbIn - uInPtr;

                if (uCurMatchCnt < 3)
                {
                    uCurMatchCnt = 1;
                    WriteU8(rgOut, ref iOutPos, ref iCurNib, rgIn[uInPtr]);
                    goto set_next_tag;
                }

                EncodeDistance(uCurMatchCnt);
                EncodeLength(uCurMatchCnt);

                bThisTag |= bBitMsk;

            set_next_tag:
                bBitMsk >>= 1;
                if (bBitMsk == 0)
                {
                    if (iCpyTag != 0 && cbUCData > 0xFFF8)
                        goto copy_uncmp;

                    uint i = uInPtr - (uProcessedData + uCurMatchCnt);
                    uint cbCompressed = (uint)(iOutPos - iUCTagPos);
                    if (bThisTag != 0)
                    {
                        if (iCpyTag > 0xFF)
                            goto copy_uncmp;
                        else if (cbCompressed < i)
                        {
                            if (iCpyTag > 0x3F)
                                goto copy_uncmp;
                            iCpyTag = 0;
                            iUCTagPos = iOutPos;
                            iUCNib = iCurNib;
                            uProcessedData = uInPtr + uCurMatchCnt;
                        }
                    }
                    else
                    {
                        if (iCpyTag != 0 || (i + 4) < cbCompressed)
                        {
                            cbUCData = uInPtr - uProcessedData;
                            iCpyTag++;
                        }
                    }

                    if ((iTagNib & 1) != 0)
                    {
                        rgOut[iTagPos++] |= (byte)(bThisTag << 4);
                        rgOut[iTagPos] |= (byte)(bThisTag >> 4);
                    }
                    else
                    {
                        rgOut[iTagPos] = bThisTag;
                    }

                    bBitMsk = 0x80; bThisTag = 0;
                    iTagPos = iOutPos++;
                    if (iCurNib != 0)
                        rgOut[iOutPos] = 0;
                    iTagNib = iCurNib;

                    if (iOutPos >= iEndOut)
                        return 0x100;
                }

                UpdateHash(uCurMatchCnt);
                uMatchCnt = uCurMatchCnt;
                continue;

            copy_uncmp:
                FlushUCChunk();
                uInPtr = uProcessedData;
                iUCTagPos = iOutPos; iUCNib = 0;
                iCpyTag = 0; cbUCData = 0; uProcessedData = uInPtr;
                bBitMsk = 0x80; bThisTag = 0;
                iTagNib = 0; iCurNib = 0;
            }

            if (bBitMsk != 0) { bThisTag |= bBitMsk; bThisTag |= (byte)(bBitMsk - 1); }
            if ((iTagNib & 1) != 0)
            {
                rgOut[iTagPos++] |= (byte)(bThisTag << 4);
                rgOut[iTagPos] |= (byte)(bThisTag >> 4);
            }
            else rgOut[iTagPos] = bThisTag;

            pcbOut = (uint)iOutPos + (uint)iCurNib;
            return 0;
        }

        private void EncodeDistance(uint uCurMatchCnt)
        {
            if (uInPtr > SHORT_DIST1)
            {
                uint uStoreDist = uGammaDist << 2;
                if (uGammaDist < LONG_DIST0)
                    WriteU8(rgOut, ref iOutPos, ref iCurNib, uStoreDist);
                else if (uGammaDist < LONG_DIST1)
                {
                    uStoreDist -= LONG_DIST0 << 2; uStoreDist |= 1;
                    WriteLE12(rgOut, ref iOutPos, ref iCurNib, uStoreDist);
                }
                else if (uGammaDist < LONG_DIST2)
                {
                    uStoreDist -= LONG_DIST1 << 2; uStoreDist |= 2;
                    WriteLE16(rgOut, ref iOutPos, ref iCurNib, uStoreDist);
                }
                else
                {
                    if (uCurMatchCnt < 4) { uMatchCnt = 1; return; }
                    uStoreDist -= LONG_DIST2 << 2; uStoreDist |= 3;
                    WriteLE20(rgOut, ref iOutPos, ref iCurNib, uStoreDist);
                }
            }
            else
            {
                uint uStoreDist = uGammaDist << 1;
                if (uGammaDist >= SHORT_DIST0)
                {
                    uStoreDist -= SHORT_DIST0 << 1; uStoreDist |= 1;
                    WriteLE12(rgOut, ref iOutPos, ref iCurNib, uStoreDist);
                }
                else
                    WriteU8(rgOut, ref iOutPos, ref iCurNib, uStoreDist);
            }
        }

        private void EncodeLength(uint uCurMatchCnt)
        {
            if (uCurMatchCnt < DEFAULT_CNT)
                WriteU4(rgOut, ref iOutPos, ref iCurNib, uCurMatchCnt - 3);
            else if (uCurMatchCnt < _1BYTE_CNT)
                WriteLE12(rgOut, ref iOutPos, ref iCurNib, ((uCurMatchCnt - DEFAULT_CNT) << 4) | 0xF);
            else
            {
                WriteLE12(rgOut, ref iOutPos, ref iCurNib, 0xFFF);
                WriteLE16(rgOut, ref iOutPos, ref iCurNib, uCurMatchCnt - 0x111);
            }
        }

        private uint FindMatch()
        {
            uint uCurPtr = uInPtr;
            byte[] pIn = rgIn;
            uint uRemaining = cbIn - uInPtr;

            if (uRemaining < 5)
            {
                uGammaDist = 0;
                return 1;
            }

            uint uHash = Hash(uInPtr);

            if (uRemaining >= 4 && GetLE32(rgIn, uCurPtr) == GetLE32(rgIn, uCurPtr - 1))
            {
                uint uRem = cbIn - 4 - uCurPtr;
                uint uCur = uCurPtr + 4;
                uMatchCnt = 4;
                uGammaDist = 0;
                if (uRem > MAX_2BYTE_CNT - 4) uRem = MAX_2BYTE_CNT - 4;
                while (uRem-- > 0 && pIn[uCur] == pIn[uCur - 4]) uCur++;
                return uCur - uCurPtr;
            }

            int iDictPtr = rgPtr[uHash];
            int iStartPos = (uCurPtr < MAX_GAMMA_DIST) ? 0 : (int)(uCurPtr - MAX_GAMMA_DIST);

            uint uBestCnt = 1;
            uint uBestDist = 0;

            if (iDictPtr < iStartPos)
                return uBestCnt;

            if (uCurPtr + 2 >= cbIn)
            {
                uGammaDist = 0;
                return 1;
            }

            ushort uCmpVal = (ushort)(pIn[uCurPtr + 1] | (pIn[uCurPtr + 2] << 8));

            int iChainDepth = 0;
            const int MAX_CHAIN = 256;
            while (iDictPtr < (int)uCurPtr)
            {
                iChainDepth++;
                if (iChainDepth > MAX_CHAIN) break;

                int iCurIdx = iDictPtr;
                if ((ushort)(pIn[iDictPtr + 1] | (pIn[iDictPtr + 2] << 8)) == uCmpVal)
                {
                    uint uRem = uRemaining;
                    if (uRem > MAX_2BYTE_CNT) uRem = MAX_2BYTE_CNT;

                    uint uFound = 0;
                    while (uRem-- > 0 && pIn[iDictPtr + uFound] == pIn[uCurPtr + uFound])
                        uFound++;

                    uint uNewDist = uCurPtr - (uint)iDictPtr - 1;
                    if (IsBetterMatch(uNewDist, uFound, uBestDist, uBestCnt))
                    {
                        uBestDist = uNewDist; uBestCnt = uFound;
                        if (uBestCnt >= MAX_2BYTE_CNT) break;
                    }
                }
                iDictPtr = rgIdx[iCurIdx & IDX_MSK];
                if (iDictPtr < iStartPos) break;
            }

            uGammaDist = uBestDist;
            return uBestCnt;
        }

        private void FlushUCChunk()
        {
            uint cbCopy = cbUCData >> 3;
            uint cbCompressed = cbCopy - 4;
            cbCompressed = (cbCompressed & 0xFF) | 0x80 | ((cbCompressed << 3) & 0xFC00);

            rgOut[iUCTagPos] &= 0xF;
            int iPos = iUCTagPos; int iNib = iUCNib;
            WriteLE16(rgOut, ref iPos, ref iNib, cbCompressed);
            if (iNib != 0)
                WriteLE12(rgOut, ref iPos, ref iNib, 0xFFF);
            else
                WriteLE16(rgOut, ref iPos, ref iNib, 0xFFFF);
            WriteLE16(rgOut, ref iPos, ref iNib, 0xFFFF);

            int iDwords = (int)cbCopy << 1;
            uint uSrc = uProcessedData;
            for (int j = 0; j < iDwords; j++)
            {
                rgOut[iPos++] = rgIn[uSrc++];
                rgOut[iPos++] = rgIn[uSrc++];
                rgOut[iPos++] = rgIn[uSrc++];
                rgOut[iPos++] = rgIn[uSrc++];
            }

            iOutPos = iPos;
            iCurNib = iNib;
            iTagPos = iOutPos;
        }

        private void UpdateHash(uint uCurMatchCnt)
        {
            if (uCurMatchCnt == 1)
            {
                uint uHash = Hash(uInPtr);
                rgIdx[uInPtr & IDX_MSK] = rgPtr[uHash];
                rgPtr[uHash] = (int)uInPtr++;
            }
            else
            {
                uint uEnd = uInPtr + uCurMatchCnt;
                uint uLimit = uCurMatchCnt > 0x38 ? 0x38 : uCurMatchCnt;
                while (uLimit-- > 0)
                {
                    uint uHash = Hash(uInPtr);
                    rgIdx[uInPtr & IDX_MSK] = rgPtr[uHash];
                    rgPtr[uHash] = (int)uInPtr++;
                }
                uInPtr = uEnd;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteU8(byte[] rgOut, ref int iPos, ref int iNib, uint uVal)
        {
            if (iNib != 0)
            {
                rgOut[iPos++] |= (byte)(uVal << 4);
                rgOut[iPos] = (byte)(uVal >> 4);
            }
            else
            {
                rgOut[iPos++] = (byte)uVal;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteU4(byte[] rgOut, ref int iPos, ref int iNib, uint uVal)
        {
            iNib ^= 1;
            if (iNib == 1)
                rgOut[iPos] = (byte)(uVal & 0xF);
            else
                rgOut[iPos++] |= (byte)(uVal << 4);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteLE12(byte[] rgOut, ref int iPos, ref int iNib, uint uVal)
        {
            iNib ^= 1;
            if (iNib == 1)
            {
                rgOut[iPos++] = (byte)uVal;
                rgOut[iPos] = (byte)(uVal >> 8);
            }
            else
            {
                rgOut[iPos++] |= (byte)(uVal << 4);
                rgOut[iPos++] = (byte)(uVal >> 4);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteLE16(byte[] rgOut, ref int iPos, ref int iNib, uint uVal)
        {
            if (iNib != 0)
            {
                rgOut[iPos++] |= (byte)(uVal << 4);
                rgOut[iPos++] = (byte)(uVal >> 4);
                rgOut[iPos] = (byte)(uVal >> 12);
            }
            else
            {
                rgOut[iPos] = (byte)uVal; iPos++;
                rgOut[iPos] = (byte)(uVal >> 8); iPos++;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteLE20(byte[] rgOut, ref int iPos, ref int iNib, uint uVal)
        {
            iNib ^= 1;
            if (iNib == 1)
            {
                rgOut[iPos] = (byte)uVal; iPos++;
                rgOut[iPos] = (byte)(uVal >> 8); iPos++;
                rgOut[iPos] = (byte)(uVal >> 16);
            }
            else
            {
                rgOut[iPos++] |= (byte)(uVal << 4);
                rgOut[iPos] = (byte)(uVal >> 4); iPos++;
                rgOut[iPos] = (byte)(uVal >> 12); iPos++;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private uint Hash(uint uPos)
        {
            if (uPos + 3 >= cbIn) return 0;
            return ((uint)(rgIn[uPos] | (rgIn[uPos + 1] << 8)) +
                    (uint)((GetLE32(rgIn, uPos) >> RS_HASH_BITS) & 0xFFFF)) & DICT_MSK;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsBetterMatch(uint uNewDist, uint uNewLen,
                                          uint uOldDist, uint uOldLen)
        {
            if (uNewLen <= uOldLen) return false;
            if (uNewLen > uOldLen + 1) return true;
            if (uOldDist == 0) return true;
            if (uOldDist < 0x880 && (uOldDist << 7) > uNewDist) return true;
            return (uOldDist << 3) > uNewDist;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint GetLE32(byte[] rg, uint uOff)
        {
            return (uint)(rg[uOff] | (rg[uOff + 1] << 8) |
                          (rg[uOff + 2] << 16) | (rg[uOff + 3] << 24));
        }
    }
}