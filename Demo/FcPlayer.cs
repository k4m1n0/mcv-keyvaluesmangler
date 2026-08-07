using System;

namespace WeaponDamageCalc.Demo;

public class FcPlayer
{
    #region 常量数据

    private const int Channels = 4;
    private const int PatternLength = 0x40;
    private const int TrackTabEntryLength = 13;
    private const int MaxSamples = 10;
    private const int MaxWaveforms = 80;
    private const int RecurseLimit = 64;

    //sub_804530
    private const byte SndModLoop        = 0xE0;//loc_80455D
    private const byte SndModEnd         = 0xE1;//loc_80458F
    private const byte SndModSetWave     = 0xE2;//loc_80459B
    private const byte SndModNewVib      = 0xE3;//loc_8048A0
    private const byte SndModChangeWave  = 0xE4;//loc_8045A0
    private const byte SndModNewSeq      = 0xE7;//loc_8045B2
    private const byte SndModSustain     = 0xE8;//loc_8045D2
    private const byte SndModSetPackWave = 0xE9;//loc_8045A9
    private const byte SndModPitchBend   = 0xEA;//loc_8048CF

    //sub_804930 jumptable
    private const byte EnvelopeLoop    = 0xE0;//jpt_804990 case 224
    private const byte EnvelopeEnd     = 0xE1;//jpt_804990 case 225
    private const byte EnvelopeSustain = 0xE8;//jpt_804990 case 232
    private const byte EnvelopeSlide   = 0xEA;//jpt_804990 case 234

    //word_8062A0 @ sub_804930 ; mov bp, word_8062A0[eax*2]
    private static readonly ushort[] Periods = {
        0x06b0,0x0650,0x05f4,0x05a0,0x054c,0x0500,0x04b8,0x0474,
        0x0434,0x03f8,0x03c0,0x038a,0x0358,0x0328,0x02fa,0x02d0,
        0x02a6,0x0280,0x025c,0x023a,0x021a,0x01fc,0x01e0,0x01c5,
        0x01ac,0x0194,0x017d,0x0168,0x0153,0x0140,0x012e,0x011d,
        0x010d,0x00fe,0x00f0,0x00e2,0x00d6,0x00ca,0x00be,0x00b4,
        0x00aa,0x00a0,0x0097,0x008f,0x0087,0x007f,0x0078,0x0071,
        0x0071,0x0071,0x0071,0x0071,0x0071,0x0071,0x0071,0x0071,
        0x0071,0x0071,0x0071,0x0071,0x0d60,0x0ca0,0x0be8,0x0b40,
        0x0a98,0x0a00,0x0970,0x08e8,0x0868,0x07f0,0x0780,0x0714,
        0x06b0,0x0650,0x05f4,0x05a0,0x054c,0x0500,0x04b8,0x0474,
        0x0434,0x03f8,0x03c0,0x038a,0x0358,0x0328,0x02fa,0x02d0,
        0x02a6,0x0280,0x025c,0x023a,0x021a,0x01fc,0x01e0,0x01c5,
        0x01ac,0x0194,0x017d,0x0168,0x0153,0x0140,0x012e,0x011d,
        0x010d,0x00fe,0x00f0,0x00e2,0x00d6,0x00ca,0x00be,0x00b4,
        0x00aa,0x00a0,0x0097,0x008f,0x0087,0x007f,0x0078,0x0071
    };

    //byte_806298 @ sub_803C30
    private static readonly byte[] SilenceData = {
        0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xE1
    };

    #endregion
    #region 结构与字段

    public class Sound
    {
        public int iStart;
        public int iLen;
        public int iRepOffs;
        public int iRepLen;
    }

    public class Channel
    {
        public PaulaMixer.Voice Voice = null!;

        public int iDmaMask;              //[esi+04]
        public int iTrackStart, iTrackEnd, iTrackPos;
        public int iPattStart, iPattPos;
        public sbyte bTranspose;          //[esi+1Dh]
        public sbyte bSoundTranspose;
        public sbyte bSeqTranspose;       //[esi+1Ch]
        public byte bNoteValue;           //[esi+1Ah]

        public sbyte bPitchBendSpeed;      //[esi+1Eh]
        public byte bPitchBendTime;        //[esi+1Fh]
        public byte bPitchBendDelayFlag;   //[esi+20h]

        public byte bPortaInfo;            //[esi+21h]
        public byte bPortDelayFlag;        //[esi+22h]
        public short wPortaOffs;           //[esi+24h]

        public int iVolSeq;               //[esi+28h]
        public int iVolSeqPos;            //[esi+2Ch]
        public byte bVolSlideSpeed;       //[esi+2Eh]
        public byte bVolSlideTime;        //[esi+2Fh]
        public byte bVolSustainTime;      //[esi+30h]
        public byte bVolSlideDelayFlag;   //[esi+40h]
        public byte bEnvelopeSpeed;       //[esi+31h]
        public byte bEnvelopeCount;       //[esi+32h]

        public int iSndSeq;               //[esi+34h]
        public int iSndSeqPos;            //[esi+38h]
        public byte bSndModSustainTime;   //[esi+3Ah]

        public byte bVibFlag;      //[esi+3Bh]
        public byte bVibDelay;     //[esi+3Ch]
        public byte bVibSpeed;     //[esi+3Dh]
        public byte bVibAmpl;      //[esi+3Eh]
        public byte bVibCurOffs;   //[esi+3Fh]

        public sbyte bVolume;      //[esi+41h]
        public int iPeriod;        //[esi+42h]

        public int iPSampleStart;  //[esi+44h]
        public int iRepeatOffset;  //[esi+48h]
        public int iRepeatLength;  //[esi+4Ah]
        public int iRepeatDelay;   //[esi+4Ch]

        public void Reset()
        {
            iDmaMask = 0; iTrackStart = iTrackEnd = iTrackPos = 0;
            iPattStart = iPattPos = 0; bTranspose = bSoundTranspose = bSeqTranspose = 0;
            bNoteValue = 0; bPitchBendSpeed = 0; bPitchBendTime = bPitchBendDelayFlag = 0;
            bPortaInfo = bPortDelayFlag = 0; wPortaOffs = 0;
            iVolSeq = iVolSeqPos = 0; bVolSlideSpeed = bVolSlideTime = bVolSustainTime = bVolSlideDelayFlag = 0;
            bEnvelopeSpeed = bEnvelopeCount = 1; iSndSeq = iSndSeqPos = 0; bSndModSustainTime = 0;
            bVibFlag = bVibDelay = bVibSpeed = bVibAmpl = bVibCurOffs = 0;
            bVolume = 0; iPeriod = 0; iPSampleStart = 0;
            iRepeatOffset = iRepeatLength = iRepeatDelay = 0;
        }
    }

    private byte[] _rgData = Array.Empty<byte>();//off_8763EC ; fcBuf
    private int _iDataLen;
    private readonly Sound[] _rgSounds = new Sound[MaxSamples + MaxWaveforms];
    private readonly Channel[] _rgCh = new Channel[Channels];
    public PaulaMixer Mixer { get; private set; } = null!;

    private int _iTrackTableOffs;  //dword_8763D8
    private int _iPatternsOffs;    //dword_8763DC
    private int _iSndModSeqsOffs;  //dword_8763E0
    private int _iVolModSeqsOffs;  //dword_8763E4
    private int _iSilenceOffs;     //dword_8763E8
    private int _iUsedPatterns;    //dword_8763F4
    private int _iUsedSndModSeqs;  //dword_8763F8
    private int _iUsedVolModSeqs;  //dword_876400

    private byte _bCount;       //byte_8763F1
    private byte _bSpeed;       //byte_876840
    private byte _bRsCount;
    private bool _bIsEnabled;   //byte_8763F0
    private bool _bIsFC14;      //byte_8763FC
    private int _iReadModRecurse;
    private int _iDmaFlags;     //word_8763F2
    public bool SongEnd { get; private set; }//byte_8763D4

    public int UsedPatterns => _iUsedPatterns;      //dword_8763F4
    public int UsedSndModSeqs => _iUsedSndModSeqs;  //dword_8763F8
    public int UsedVolModSeqs => _iUsedVolModSeqs;  //dword_876400
    public bool IsFC14 => _bIsFC14;                  //byte_8763FC

    #endregion
    #region 初始化

    public FcPlayer()
    {
        for (int i = 0; i < _rgSounds.Length; i++)
            _rgSounds[i] = new Sound();
        for (int i = 0; i < Channels; i++)
            _rgCh[i] = new Channel();
    }

    public void SetMixer(PaulaMixer mixer)
    {
        Mixer = mixer;
        for (int i = 0; i < Channels; i++)
            _rgCh[i].Voice = mixer.GetVoice(i);
    }

    //@dumped__00803C30 sub_803C30
    public bool Init(byte[] rgData)
    {
        _rgData = rgData;
        _iDataLen = rgData.Length;

        if (_iDataLen < 5) return false;
        //loc_803C77
        _bIsFC14 = (_rgData[0] == 0x46 && _rgData[1] == 0x43 && _rgData[2] == 0x31 && _rgData[3] == 0x34);//FC14

        //loc_803CE6 ; neg al; sbb eax, eax; and al, 0B0h; add eax, 0B4h
        _iTrackTableOffs = _bIsFC14 ? 0x00B4 : 0x0064;

        _iPatternsOffs = Read32(8);
        _iUsedPatterns = Read32(12) / PatternLength;
        _iSndModSeqsOffs = Read32(16);
        _iUsedSndModSeqs = Read32(20) / 64;
        _iVolModSeqsOffs = Read32(24);
        _iUsedVolModSeqs = Read32(28) / 64;

        //loc_803CB0 ; add eax, 0FFFFFFF8h
        _iSilenceOffs = _iDataLen;
        byte[] rgNewData = new byte[_iDataLen + 8];
        Array.Copy(_rgData, rgNewData, _iDataLen);
        Array.Copy(SilenceData, 0, rgNewData, _iDataLen, 8);
        _rgData = rgNewData;
        _iDataLen = rgNewData.Length;

        //loc_803E11
        int iSampleOffset = Read32(32);
        int iSampleHeader = 0x0028;
        for (int sam = 0; sam < 10; sam++)
        {
            int iSampleLength = Read16(iSampleHeader);
            _rgSounds[sam].iStart = iSampleOffset;
            _rgSounds[sam].iLen = iSampleLength;
            _rgSounds[sam].iRepOffs = Read16(iSampleHeader + 2);
            _rgSounds[sam].iRepLen = Read16(iSampleHeader + 4);

            iSampleOffset += iSampleLength * 2;
            iSampleHeader += 6;
        }

        //loc_803F6C
        if (_bIsFC14)
        {
            int iWaveOffset = Read32(36);
            int iWaveHeader = 0x0064;
            for (int wave = 0; wave < 80; wave++)
            {
                int sam = 10 + wave;
                int iWaveLength = _rgData[iWaveHeader++];
                _rgSounds[sam].iStart = iWaveOffset;
                _rgSounds[sam].iLen = iWaveLength;
                _rgSounds[sam].iRepOffs = 0;
                _rgSounds[sam].iRepLen = iWaveLength;
                iWaveOffset += iWaveLength;
            }
        }

        return Restart(0, 0);
    }

    public bool Restart(int iStartStep, int iEndStep)
    {
        _bRsCount = 4;

        int iTrackTabLen = Read32(4);
        if (iTrackTabLen == 0)
            iTrackTabLen = _iPatternsOffs - _iTrackTableOffs;

        //loc_804021 ; channel init
        for (int c = 0; c < Channels; c++)
        {
            var ch = _rgCh[c];
            ch.Reset();
            ch.iDmaMask = (1 << c);
            ch.iTrackStart = _iTrackTableOffs + c * 3;

            if (iStartStep >= 0 && (iStartStep * TrackTabEntryLength) < iTrackTabLen)
                ch.iTrackStart += iStartStep * TrackTabEntryLength;

            if (iEndStep > 0 && (iEndStep * TrackTabEntryLength) <= iTrackTabLen)
                ch.iTrackEnd = ch.iTrackStart + iEndStep * TrackTabEntryLength;
            else
                ch.iTrackEnd = ch.iTrackStart + iTrackTabLen;

            ch.iVolSeq = _iSilenceOffs;
            ch.iSndSeq = _iSilenceOffs + 1;

            int tt = ch.iTrackStart + ch.iTrackPos;
            ch.iPattStart = _iPatternsOffs + (_rgData[tt++] << 6);
            ch.bTranspose = (sbyte)_rgData[tt++];
            ch.bSoundTranspose = (sbyte)_rgData[tt];
        }

        //loc_804153 ; speed init, default 3
        _bSpeed = 3;
        int t = _rgCh[0].iTrackStart;
        while (t >= _iTrackTableOffs)
        {
            byte s = _rgData[t + 12];
            if (s > 0) { _bSpeed = s; break; }
            t -= TrackTabEntryLength;
        }
        _bCount = _bSpeed;
        _bIsEnabled = true;
        SongEnd = false;
        return true;
    }

    #endregion
    #region 播放运行

    //@dumped__00804240 sub_804240
    public void Run()
    {
        //byte_8763F0 ; test al, al
        if (!_bIsEnabled) return;

        //mov word_8763F2, 0
        _iDmaFlags = 0;

        //byte_8763F1 ; dec al
        if (--_bCount == 0)
        {
            //byte_876840 ; mov byte_8763F1, al
            _bCount = _bSpeed;
            NextNote(_rgCh[0]);
            NextNote(_rgCh[1]);
            NextNote(_rgCh[2]);
            NextNote(_rgCh[3]);
        }

        //loc_8042A0 ; lea edi, [esi-4Ch]
        for (int c = 0; c < Channels; c++)
        {
            var ch = _rgCh[c];

            //sub_804C00 ; repeatDelay
            if (ch.iRepeatDelay > 0)
            {
                if (--ch.iRepeatDelay == 1)
                {
                    ch.iRepeatDelay = 0;
                    //mov eax, [esi-8]; mov dx, [esi-4]; add edx, eax
                    ch.Voice.iSampleStart = ch.iPSampleStart + ch.iRepeatOffset;
                    //mov dx, [esi-2]; mov [ecx+4], dx
                    ch.Voice.iSampleEnd = ch.Voice.iSampleStart + ch.iRepeatLength;
                }
            }

            //call sub_804530
            ProcessModulation(ch);

            //mov dx, [esi-0Ah]; mov [ecx+6], dx
            ch.Voice.iCurPeriod = ch.iPeriod;
            //movsx ax, byte ptr [esi-0Bh]; mov [ecx+8], ax
            ch.Voice.iVolume = Math.Clamp((int)ch.bVolume, 0, 64);
        }

        //loc_804311 ; shl eax, cl; test ecx, eax
        for (int c = 0; c < Channels; c++)
        {
            if ((_iDmaFlags & (1 << c)) != 0)
                _rgCh[c].Voice.bIsOn = true;
        }
    }

    //@dumped__00804340 sub_804340
    private void NextNote(Channel ch)
    {
        int iPattOffs = ch.iPattStart + ch.iPattPos;

        //PATTERN_BREAK 0x49
        if (ch.iPattPos >= PatternLength || (_bIsFC14 && _rgData[iPattOffs] == 0x49))
        {
            ch.iPattPos = 0;
            ch.iTrackPos += TrackTabEntryLength;
            int iTrackOffs = ch.iTrackStart + ch.iTrackPos;

            if (iTrackOffs + 12 >= ch.iTrackEnd)
            {
                ch.iTrackPos = 0;
                iTrackOffs = ch.iTrackStart;
                SongEnd = true;
            }

            if (++_bRsCount == 5)
            {
                _bRsCount = 1;
                byte bNewSpeed = _rgData[iTrackOffs + 12];//RS
                if (bNewSpeed != 0) _bCount = _bSpeed = bNewSpeed;
            }

            //loc_8040F6
            ch.iPattStart = _iPatternsOffs + (_rgData[iTrackOffs++] << 6);
            ch.bTranspose = (sbyte)_rgData[iTrackOffs++];
            ch.bSoundTranspose = (sbyte)_rgData[iTrackOffs];
            iPattOffs = ch.iPattStart;
        }

        byte bNote = _rgData[iPattOffs++];
        byte bInfo1 = _rgData[iPattOffs];

        if (bNote != 0)
        {
            ch.wPortaOffs = 0;
            ch.bPortaInfo = 0;
            ch.bNoteValue = (byte)(bNote & 0x7F);

            _iDmaFlags |= ch.iDmaMask;

            int iSound = (bInfo1 & 0x3F) + ch.bSoundTranspose;
            iSound &= 0x3F;

            int iSeqOffs;
            if (iSound > (_iUsedVolModSeqs - 1))
                iSeqOffs = _iSilenceOffs;
            else
                iSeqOffs = _iVolModSeqsOffs + (iSound << 6);

            ch.bEnvelopeSpeed = ch.bEnvelopeCount = _rgData[iSeqOffs++];
            iSound = _rgData[iSeqOffs++];
            ch.bVibSpeed = _rgData[iSeqOffs++];
            ch.bVibFlag = 0x40;
            ch.bVibAmpl = ch.bVibCurOffs = _rgData[iSeqOffs++];
            ch.bVibDelay = _rgData[iSeqOffs++];
            ch.iVolSeq = iSeqOffs;
            ch.iVolSeqPos = 0;
            ch.bVolSustainTime = 0;

            if (iSound > (_iUsedSndModSeqs - 1))
                iSeqOffs = _iSilenceOffs + 1;
            else
                iSeqOffs = _iSndModSeqsOffs + (iSound << 6);

            ch.iSndSeq = iSeqOffs;
            ch.iSndSeqPos = 0;
            ch.bSndModSustainTime = 0;
        }

        //loc_804340
        if ((bInfo1 & 0x40) != 0) ch.bPortaInfo = 0;
        if ((bInfo1 & 0x80) != 0) ch.bPortaInfo = (byte)(_rgData[iPattOffs + 2] & 0x3F);

        ch.iPattPos += 2;
    }

    //@dumped__00804530 sub_804530 ; processModulation
    private void ProcessModulation(Channel ch)
    {
        //[ecx+3Ah] ; test al, al
        if (ch.bSndModSustainTime != 0)
        {
            ch.bSndModSustainTime--;
            ProcessPerVol(ch);
            return;
        }
        _iReadModRecurse = 0;
        ReadModCommand(ch);
    }

    //@dumped__00804550 loc_804550 ; readModCommand
    private void ReadModCommand(Channel ch)
    {
        if (++_iReadModRecurse > RecurseLimit) return;

        int iSeqOffs = ch.iSndSeq + ch.iSndSeqPos;

        //SNDMOD_LOOP
        if (_rgData[iSeqOffs] == SndModLoop)
        {
            ch.iSndSeqPos = _rgData[iSeqOffs + 1] & 0x3F;
            iSeqOffs = ch.iSndSeq + ch.iSndSeqPos;
        }

        byte bCmd = _rgData[iSeqOffs];

        if (bCmd == SndModEnd)
        {
            ProcessPerVol(ch);
        }
        else if (bCmd == SndModSetWave)
        {
            //loc_804603
            ch.Voice.bIsOn = false;
            _iDmaFlags |= ch.iDmaMask;
            ch.iVolSeqPos = 0;
            ch.bEnvelopeCount = 1;

            SetWave(ch, _rgData[iSeqOffs + 1]);
            ch.iSndSeqPos += 2;
            ReadSeqTranspose(ch);
            ProcessPerVol(ch);
        }
        else if (bCmd == SndModChangeWave)
        {
            //loc_804690
            SetWave(ch, _rgData[iSeqOffs + 1]);
            ch.iSndSeqPos += 2;
            ReadSeqTranspose(ch);
            ProcessPerVol(ch);
        }
        else if (bCmd == SndModNewSeq)
        {
            //loc_8045B7 ; shl edx, 6
            int iSeq = _rgData[iSeqOffs + 1];
            ch.iSndSeq = _iSndModSeqsOffs + (iSeq << 6);
            ch.iSndSeqPos = 0;
            ReadModCommand(ch);
        }
        else if (bCmd == SndModSustain)
        {
            //loc_8045DB
            ch.bSndModSustainTime = _rgData[iSeqOffs + 1];
            ch.iSndSeqPos += 2;
            if (ch.bSndModSustainTime != 0)
            {
                ch.bSndModSustainTime--;
                ProcessPerVol(ch);
                return;
            }
            ReadModCommand(ch);
        }
        else if (bCmd == SndModNewVib)
        {
            //loc_8048A8
            ch.bVibSpeed = _rgData[iSeqOffs + 1];
            ch.bVibAmpl = _rgData[iSeqOffs + 2];
            ch.iSndSeqPos += 3;
            ProcessPerVol(ch);
        }
        else if (bCmd == SndModPitchBend)
        {
            //loc_8048D4
            ch.bPitchBendSpeed = (sbyte)_rgData[iSeqOffs + 1];
            ch.bPitchBendTime = _rgData[iSeqOffs + 2];
            ch.iSndSeqPos += 3;
            ReadSeqTranspose(ch);
            ProcessPerVol(ch);
        }
        else
        {
            //loc_8048FE ; transpose value
            ReadSeqTranspose(ch);
            ProcessPerVol(ch);
        }
    }

    //loc_804904 ; inc word ptr [esi+38h]; mov [esi+1Ch], al
    private void ReadSeqTranspose(Channel ch)
    {
        ch.bSeqTranspose = (sbyte)_rgData[ch.iSndSeq + ch.iSndSeqPos];
        ch.iSndSeqPos++;
    }

    //@dumped__00804603 loc_804603 ; setWave
    private void SetWave(Channel ch, byte bNum)
    {
        var snd = _rgSounds[bNum];
        ch.iPSampleStart = snd.iStart;
        ch.Voice.iSampleStart = snd.iStart;
        ch.Voice.rgSampleData = _rgData;

        if (bNum < 10)
        {
            ch.Voice.iSampleEnd = snd.iStart + snd.iLen * 2;
            ch.Voice.iRepeatStart = snd.iStart + snd.iRepOffs;
            ch.Voice.iRepeatEnd = snd.iStart + snd.iRepOffs + snd.iRepLen * 2;
            ch.iRepeatOffset = snd.iRepOffs;
            ch.iRepeatLength = snd.iRepLen * 2;

            if (snd.iRepLen <= 1)
            {
                ch.Voice.bLooping = false;
                ch.iRepeatDelay = 0;
            }
            else
            {
                ch.Voice.bLooping = true;
                ch.iRepeatDelay = 3;//mov word ptr [esi+4Ch], 3
            }
        }
        else
        {
            ch.Voice.bLooping = true;
            ch.Voice.iSampleEnd = snd.iStart + snd.iLen * 2;
            ch.Voice.iRepeatStart = snd.iStart;
            ch.Voice.iRepeatEnd = snd.iStart + snd.iLen * 2;
            ch.iRepeatOffset = 0;
            ch.iRepeatLength = snd.iLen * 2;
            ch.iRepeatDelay = 3;
        }
    }

    #endregion
    #region 调制包络

    //loc_8049CC ; volSlideDelayFlag ^= 0xFF
    private void VolSlide(Channel ch)
    {
        ch.bVolSlideDelayFlag ^= 0xFF;
        if (ch.bVolSlideDelayFlag != 0)
        {
            ch.bVolSlideTime--;
            ch.bVolume += unchecked((sbyte)ch.bVolSlideSpeed);
            if (ch.bVolume < 0) { ch.bVolume = 0; ch.bVolSlideTime = 0; }
            if (ch.bVolume > 64) { ch.bVolume = 64; ch.bVolSlideTime = 0; }
        }
    }

    //@dumped__00804930 sub_804930 ; processPerVol
    private void ProcessPerVol(Channel ch)
    {
        bool bRepeat;
        int iJumpCount = 0;

        do
        {
            bRepeat = false;
            if (ch.bVolSustainTime != 0)
            {
                ch.bVolSustainTime--;
            }
            else if (ch.bVolSlideTime != 0)
            {
                VolSlide(ch);
            }
            else if (ch.bEnvelopeSpeed == 0 || --ch.bEnvelopeCount == 0)
            {
                ch.bEnvelopeCount = ch.bEnvelopeSpeed;

                bool bReadNext;
                do
                {
                    bReadNext = false;
                    if (++iJumpCount > RecurseLimit) break;

                    int iSeqOffs = ch.iVolSeq + ch.iVolSeqPos;
                    byte bCmd = _rgData[iSeqOffs];

                    //lea edi, [edx-0E0h]; cmp edi, 0Ah
                    if (bCmd == EnvelopeSustain)
                    {
                        ch.bVolSustainTime = _rgData[iSeqOffs + 1];
                        if (ch.bVolSustainTime == 0) ch.bVolSustainTime = 1;
                        ch.iVolSeqPos += 2;
                        bRepeat = true;
                    }
                    else if (bCmd == EnvelopeSlide)
                    {
                        ch.bVolSlideSpeed = _rgData[iSeqOffs + 1];
                        ch.bVolSlideTime = _rgData[iSeqOffs + 2];
                        ch.iVolSeqPos += 3;
                        VolSlide(ch);
                    }
                    else if (bCmd == EnvelopeLoop)
                    {
                        //sub al, 5; and eax, 3Fh
                        ch.iVolSeqPos = _rgData[iSeqOffs + 1] & 0x3F;
                        if (ch.iVolSeqPos >= 5) ch.iVolSeqPos -= 5;
                        else ch.iVolSeqPos = 0;
                        bReadNext = true;
                    }
                    else if (bCmd == EnvelopeEnd)
                    {
                        break;
                    }
                    else
                    {
                        ch.bVolume = (sbyte)_rgData[iSeqOffs];
                        if (ch.bVolume > 64) ch.bVolume = 64;
                        if (ch.bVolume < 0) ch.bVolume = 0;
                        if (ch.bEnvelopeSpeed != 0) ch.iVolSeqPos++;
                    }
                } while (bReadNext);
            }
        } while (bRepeat);

        //loc_804A05 ; period calc
        //movsx eax, byte ptr [ecx+1Ch]
        int iTmp0 = ch.bSeqTranspose;
        if (iTmp0 >= 0)
        {
            iTmp0 += ch.bNoteValue;
            iTmp0 += ch.bTranspose;
        }
        //and eax, 7Fh
        iTmp0 &= 0x7F;
        int iTmp1 = iTmp0 << 1;
        //mov bp, word_8062A0[eax*2]
        iTmp0 = Periods[iTmp0];

        //loc_804A2B ; vibrato
        if (ch.bVibDelay == 0)
        {
            int iNoteOff = iTmp1;
            int iVibDelta = ch.bVibAmpl << 1;
            iTmp1 = ch.bVibCurOffs;

            //test al, 20h
            if ((ch.bVibFlag & 0x20) == 0)
            {
                iTmp1 -= ch.bVibSpeed;
                if (iTmp1 < 0) { iTmp1 = 0; ch.bVibFlag |= 0x20; }
            }
            else
            {
                iTmp1 += ch.bVibSpeed;
                if (iTmp1 > iVibDelta) { iTmp1 = iVibDelta; ch.bVibFlag &= 0xDF; }
            }
            ch.bVibCurOffs = (byte)iTmp1;
            iTmp1 -= ch.bVibAmpl;

            //add edi, 0A0h; cmp di, 100h
            iNoteOff += 160;
            while (iNoteOff < 256) { iTmp1 <<= 1; iNoteOff += 24; }
            iTmp0 += iTmp1;
        }
        else ch.bVibDelay--;

        //loc_804AC5 ; portamento
        ch.bPortDelayFlag ^= 0xFF;
        if (ch.bPortDelayFlag != 0)
        {
            sbyte bParam = unchecked((sbyte)ch.bPortaInfo);
            if (bParam != 0)
            {
                //cmp al, 1Fh; jle loc_804AEA
                if (bParam > 0x1F) bParam = (sbyte)(-(bParam & 0x1F));
                ch.wPortaOffs -= bParam;
            }
        }

        //loc_804AF2 ; pitchbend
        ch.bPitchBendDelayFlag ^= 0xFF;
        if (ch.bPitchBendDelayFlag != 0 && ch.bPitchBendTime != 0)
        {
            ch.bPitchBendTime--;
            if (ch.bPitchBendSpeed != 0) ch.wPortaOffs -= ch.bPitchBendSpeed;
        }

        //loc_804B19 ; period clamp
        iTmp0 += ch.wPortaOffs;
        //cmp ebp, 70h; jg loc_804B32
        if (iTmp0 <= 0x0070) iTmp0 = 0x0071;
        //cmp ebp, 0D60h; jle loc_804B48
        if (iTmp0 > 0x0D60) iTmp0 = 0x0D60;
        ch.iPeriod = iTmp0;
    }

    #endregion
    #region 辅助读取

    private int Read16(int iOffset)
    {
        return (_rgData[iOffset] << 8) | _rgData[iOffset + 1];
    }

    private int Read32(int iOffset)
    {
        return (_rgData[iOffset] << 24) | (_rgData[iOffset + 1] << 16) |
               (_rgData[iOffset + 2] << 8) | _rgData[iOffset + 3];
    }
    #endregion
}