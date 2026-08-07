using System;
using System.Runtime.CompilerServices;

namespace WeaponDamageCalc.Demo;

public class PaulaMixer
{   
    #region 通道状态

    public class Voice
    {
        public byte[]? rgSampleData;//[eax-4] mov [eax-4], ecx
        public int iSampleStart;    //[eax-4]
        public int iSampleEnd;      //[eax+0] mov [eax], edx
        public int iRepeatStart;    //[eax+8] mov [eax+8], ecx
        public int iRepeatEnd;      //[eax+0Ch] mov [eax+0Ch], edx
        public bool bLooping;       //[eax+8769C5] cmp byte_8769C5[ecx], bl
        public int iLength;         //[eax+4] mov dword ptr [eax+4], 1

        public int iCurPeriod;      //[eax+30h] mov [eax+30h], ebx
        public int iVolume;         //[eax+16h] mov [eax+16h], bx
        public bool bIsOn;          //[eax-8] mov [eax-8], bl

        public int iStepSpeed;      //[eax+24h] mov [eax+24h], bx
        public int iStepSpeedPnt;   //[eax+28h] mov [eax+28h], ebx
        public int iStepSpeedAddPnt;//[eax+2Ch] mov [eax+2Ch], ebx
    }

    #endregion
    #region 字段与初始化

    private readonly Voice[] rgVoices;
    private readonly int iNumVoices;
    private readonly int iOutputRate;
    private const int AmigaClock = 3546895;//AMIGA_CLOCK_PAL

    //word_8773C0 @ sub_804C70 ; mov word_8773C0[edx*2], ax
    private readonly int[] rgMix16 = new int[256];

    private int iSamplesPerTick;   //word_8773AA mov word_8773AA, dx
    private int iSamplesPerTickPnt;//dword_8773B0 mov dword_8773B0, edx
    private int iSamplesAdd;       //dword_8773A0 mov dword_8773A0, ebx
    private int iToFill;           //sub_804C70

    public PaulaMixer(int iNumVoices, int iOutputRate)
    {
        this.iNumVoices = iNumVoices;
        this.iOutputRate = iOutputRate;
        rgVoices = new Voice[iNumVoices];
        for (int i = 0; i < iNumVoices; i++)
            rgVoices[i] = new Voice();

        //@dumped__00804D12 loc_804D12 ; idiv ecx; mov word_8773C0[edx*2], ax
        float fVoicesPerChannel = (float)this.iNumVoices;
        for (int i = 0; i < 128; i++)
            rgMix16[i] = (int)(i * 256 / fVoicesPerChannel);
        for (int i = 0; i < 128; i++)
            rgMix16[128 + i] = (int)((-128 + i) * 256 / fVoicesPerChannel);

        InitVoices();
        SetReplayingSpeed(50);//mov ecx, 32h ; div ecx
    }

    //@dumped__00804DA9 loc_804DA9 ; mov [eax-4], ecx; mov [eax], edx
    private void InitVoices()
    {
        foreach (var v in rgVoices)
        {
            v.rgSampleData = null;
            v.iSampleStart = 0;
            v.iSampleEnd = 0;
            v.iRepeatStart = 0;
            v.iRepeatEnd = 0;
            v.bLooping = true;
            v.iLength = 1;         //mov dword ptr [eax+4], 1
            v.iCurPeriod = 0;      //mov [eax+30h], ebx
            v.iVolume = 0;         //mov [eax+16h], bx
            v.bIsOn = false;       //mov [eax-8], bl
            v.iStepSpeed = 0;      //mov [eax+24h], bx
            v.iStepSpeedPnt = 0;   //mov [eax+28h], ebx
            v.iStepSpeedAddPnt = 0;//mov [eax+2Ch], ebx
        }
    }

    //@dumped__00804C70 sub_804C70 ; mov eax, 51EB851Fh; imul ecx; sar edx, 4
    public void SetReplayingSpeed(int iTicksPerSecond)
    {
        iSamplesPerTick = iOutputRate / iTicksPerSecond;
        iSamplesPerTickPnt = ((iOutputRate % iTicksPerSecond) * 65536) / iTicksPerSecond;
        iSamplesAdd = 0;
        iToFill = 0;
    }

    public Voice GetVoice(int iIndex) => rgVoices[iIndex];

    #endregion
    #region 混音填充

    //@dumped__00804F60 sub_804F60
    public void FillBuffer16bitMono(short[] rgBuffer, int iOffset, int iSampleCount, Action updateCallback)
    {
        //loc_804F66 ; cmp word ptr dword_8773AC, bx ; jbe loc_80503F
        while (iSampleCount > 0)
        {
            int n = Math.Min(iToFill, iSampleCount);
            if (n > 0)
            {
                Fill16bitMonoBlock(rgBuffer, iOffset, n);
                iOffset += n;
                iToFill -= n;
                iSampleCount -= n;
            }

            if (iToFill == 0)
            {
                updateCallback();//call player->run()

                //add iSamplesAdd, iSamplesPerTickPnt ; cmp 65535 ; sbb edx, edx ; neg edx
                int iTemp = iSamplesAdd + iSamplesPerTickPnt;
                iSamplesAdd = iTemp & 0xFFFF;
                iToFill = iSamplesPerTick + (iTemp > 65535 ? 1 : 0);

                //loc_804F75 ; stepSpeed calc
                foreach (var v in rgVoices)
                {
                    if (v.bIsOn && v.iCurPeriod != 0)
                    {
                        v.iStepSpeed = (AmigaClock / iOutputRate) / v.iCurPeriod;
                        v.iStepSpeedPnt = (((AmigaClock / iOutputRate) % v.iCurPeriod) * 65536) / v.iCurPeriod;
                    }
                    else
                    {
                        v.iStepSpeed = 0;
                        v.iStepSpeedPnt = 0;
                    }
                }
            }
        }
    }

    //@dumped__00804F8B loc_804F8B ; cmp edi, ebx; jnz loc_804F97
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Fill16bitMonoBlock(short[] rgBuffer, int iOffset, int iSampleCount)
    {
        Array.Clear(rgBuffer, iOffset, iSampleCount);//rep stosd

        for (int v = 0; v < iNumVoices; v++)
        {
            var pv = rgVoices[v];
            if (!pv.bIsOn || pv.rgSampleData == null) continue;

            int iVol = pv.iVolume;            //word_8769BA[ecx]
            int iPos = pv.iSampleStart;       //off_8769A0[ecx]
            int iEnd = pv.iSampleEnd;         //off_8769A4[ecx]
            int iStep = pv.iStepSpeed;        //dword_8769CC[ecx]
            int iStepPnt = pv.iStepSpeedPnt;  //dword_8769D0[ecx]
            int iAddPnt = pv.iStepSpeedAddPnt;//dword_8769D4[ecx]
            byte[] rgData = pv.rgSampleData;

            for (int i = 0; i < iSampleCount; i++)
            {
                //add esi, edx ; mov dword_8769D4[ecx], esi ; cmp esi, 0FFFFh ; sbb edx, edx ; neg edx
                iAddPnt += iStepPnt;
                int iCarry = (iAddPnt > 65535) ? 1 : 0;
                iAddPnt &= 65535;      //mov [ecx+8769D6h], bx
                iPos += iStep + iCarry;//add edx, esi ; add off_8769A0[ecx], edx

                int iSample;
                //cmp esi, edx ; jb loc_805003
                if (iPos < iEnd)
                {
                    //mov dl, [esi] ; movsx edx, byte_877298[edx]
                    iSample = rgMix16[rgData[iPos]];
                }
                //cmp byte_8769C5[ecx], bl ; jz loc_80501D
                else if (pv.bLooping)
                {
                    //mov esi, off_8769AC[ecx] ; mov edx, off_8769B0[ecx]
                    iPos = pv.iRepeatStart;
                    iEnd = pv.iRepeatEnd;
                    //cmp esi, edx ; jnb loc_80501D
                    if (iPos < iEnd)
                        iSample = rgMix16[rgData[iPos]];
                    else
                        continue;
                }
                else
                {
                    continue;
                }

                //imul edx, esi ; sar edx, 6 ; add [eax], dl
                int iVal = rgBuffer[iOffset + i] + ((iVol * iSample) >> 6);
                if (iVal > 32767) iVal = 32767;
                if (iVal < -32768) iVal = -32768;
                rgBuffer[iOffset + i] = (short)iVal;
            }

            pv.iSampleStart = iPos;
            pv.iSampleEnd = iEnd;
            pv.iStepSpeedAddPnt = iAddPnt;
        }
    }
    #endregion
}