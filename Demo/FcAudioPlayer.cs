using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using WeaponDamageCalc.Services;

namespace WeaponDamageCalc.Demo;

public class FcAudioPlayer : IDisposable
{
    #region 字段常量

    private const int iSampleRate = 28125;
    private const int iBitsPerSample = 8;
    private const int iOutputChannels = 1;
    private const int iBlockAlign = iOutputChannels * (iBitsPerSample / 8);

    private const int iBufferCount = 4;
    private const int iBufferSampleFrames = 8192;
    private const int iBufferByteSize = iBufferSampleFrames * iBlockAlign;

    private readonly FcPlayer _fcPlayer;
    private readonly PaulaMixer _mixer;

    private IntPtr _hWaveOut;
    private WaveNative.WAVEFORMATEX _wfFormat;
    private WaveNative.WaveOutCallback? _pfnCallback;

    private readonly WaveNative.WAVEHDR[] _rgWaveHdr;
    private readonly GCHandle[] _rgHdrHandle;
    private readonly IntPtr[] _rgHdrPtr;
    private readonly GCHandle[] _rgBufHandle;
    private readonly byte[][] _rgPlayBuf;

    private Thread? _thAudio;
    private volatile bool _bAudioRunning;
    private bool _bDisposed;
    private readonly short[] _rgMixBuf;

    public bool IsPlaying => _bAudioRunning;
    public bool SongEnded => _fcPlayer.SongEnd;
    public PaulaMixer Mixer => _mixer;

    #endregion
    #region 初始化
    
    public FcAudioPlayer(byte[] rgFcData)
    {
        if (rgFcData == null || rgFcData.Length <= 4)
            throw new ArgumentException("Invalid .fc file data");

        if (rgFcData[0] != 'F' || rgFcData[1] != 'C' ||
            rgFcData[2] != '1' || rgFcData[3] != '4')
            throw new ArgumentException("Not a valid FC14 file");

        _fcPlayer = new FcPlayer();
        _mixer = new PaulaMixer(4, iSampleRate);
        _fcPlayer.SetMixer(_mixer);

        if (!_fcPlayer.Init(rgFcData))
            throw new InvalidOperationException("FC module init failed");

        LogService.Info($"[FcAudio] FC module loaded: patterns={_fcPlayer.UsedPatterns}, samples=10, waveforms=80");

        _wfFormat = new WaveNative.WAVEFORMATEX
        {
            wFormatTag = 1,//WAVE_FORMAT_PCM
            nChannels = iOutputChannels,
            nSamplesPerSec = iSampleRate,
            nAvgBytesPerSec = (uint)(iSampleRate * iBlockAlign),
            nBlockAlign = iBlockAlign,
            wBitsPerSample = iBitsPerSample,
            cbSize = 0
        };

        _rgWaveHdr = new WaveNative.WAVEHDR[iBufferCount];
        _rgHdrHandle = new GCHandle[iBufferCount];
        _rgHdrPtr = new IntPtr[iBufferCount];
        _rgBufHandle = new GCHandle[iBufferCount];
        _rgPlayBuf = new byte[iBufferCount][];
        _rgMixBuf = new short[iBufferSampleFrames];

        for (int i = 0; i < iBufferCount; i++)
        {
            _rgPlayBuf[i] = new byte[iBufferByteSize];
            _rgBufHandle[i] = GCHandle.Alloc(_rgPlayBuf[i], GCHandleType.Pinned);

            _rgWaveHdr[i] = new WaveNative.WAVEHDR
            {
                lpData = _rgBufHandle[i].AddrOfPinnedObject(),
                dwBufferLength = (uint)iBufferByteSize,
                dwBytesRecorded = 0,
                dwUser = IntPtr.Zero,
                dwFlags = 0,
                dwLoops = 0,
                lpNext = IntPtr.Zero,
                reserved = IntPtr.Zero
            };

            _rgHdrHandle[i] = GCHandle.Alloc(_rgWaveHdr[i], GCHandleType.Pinned);
            _rgHdrPtr[i] = _rgHdrHandle[i].AddrOfPinnedObject();
        }
    }

    public FcAudioPlayer(string sFilePath) : this(File.ReadAllBytes(sFilePath))
    {
    }

    #endregion
    #region 播放控制

    public void Play()
    {
        if (_bDisposed) throw new ObjectDisposedException(nameof(FcAudioPlayer));
        if (_bAudioRunning) return;

        LogService.Info("[FcAudio] Starting FC playback");

        _pfnCallback = OnWaveOutCallback;
        int mmr = WaveNative.waveOutOpen(out _hWaveOut, -1, ref _wfFormat,//WAVE_MAPPER
            Marshal.GetFunctionPointerForDelegate(_pfnCallback),
            IntPtr.Zero, 0x30000);//CALLBACK_FUNCTION
        if (mmr != 0)
        {
            LogService.Error($"[FcAudio] waveOutOpen failed: {mmr}");
            throw new InvalidOperationException($"waveOutOpen failed: {mmr}");
        }

        uint uHdrSize = (uint)Marshal.SizeOf<WaveNative.WAVEHDR>();
        for (int i = 0; i < iBufferCount; i++)
        {
            mmr = WaveNative.waveOutPrepareHeaderPtr(_hWaveOut, _rgHdrPtr[i], uHdrSize);
            if (mmr != 0)
            {
                LogService.Error($"[FcAudio] waveOutPrepareHeader[{i}] failed: {mmr}");
                WaveNative.waveOutClose(_hWaveOut);
                _hWaveOut = IntPtr.Zero;
                throw new InvalidOperationException($"waveOutPrepareHeader[{i}] failed: {mmr}");
            }
        }

        _bAudioRunning = true;

        for (int i = 0; i < iBufferCount; i++)
            FillAndSubmitBuffer(i);

        _thAudio = new Thread(AudioThreadProc)
        {
            IsBackground = true,
            Name = "FcAudio"
        };
        _thAudio.Start();

        LogService.Info("[FcAudio] Playback started");
    }

    public void Stop()
    {
        _bAudioRunning = false;

        if (_thAudio != null && _thAudio.IsAlive)
        {
            NativeMethods.PostThreadMessageW((uint)_thAudio.ManagedThreadId, 0x0012, IntPtr.Zero, IntPtr.Zero);//WM_QUIT
            _thAudio.Join(500);
        }

        if (_hWaveOut != IntPtr.Zero)
        {
            WaveNative.waveOutReset(_hWaveOut);

            uint uHdrSize = (uint)Marshal.SizeOf<WaveNative.WAVEHDR>();
            for (int i = 0; i < iBufferCount; i++)
                WaveNative.waveOutUnprepareHeaderPtr(_hWaveOut, _rgHdrPtr[i], uHdrSize);

            WaveNative.waveOutClose(_hWaveOut);
            _hWaveOut = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        if (_bDisposed) return;
        _bDisposed = true;
        Stop();

        for (int i = 0; i < iBufferCount; i++)
        {
            if (_rgHdrHandle[i].IsAllocated)
                _rgHdrHandle[i].Free();
            if (_rgBufHandle[i].IsAllocated)
                _rgBufHandle[i].Free();
        }
    }

    #endregion
    #region 回调输出

    //WOM_DONE = 0x3BD
    private void OnWaveOutCallback(IntPtr hwo, int uMsg, IntPtr dwInstance, IntPtr dwParam1, IntPtr dwParam2)
    {
        if (uMsg == 0x3BD)//WOM_DONE
        {
            for (int i = 0; i < iBufferCount; i++)
            {
                if (_rgHdrPtr[i] == dwParam1)
                {
                    FillAndSubmitBuffer(i);
                    return;
                }
            }
        }
    }

    private void AudioThreadProc()
    {
        var msg = new NativeMethods.MSG();
        while (_bAudioRunning)
        {
            if (NativeMethods.GetMessage(out msg, IntPtr.Zero, 0, 0))
            {
                NativeMethods.TranslateMessage(ref msg);
                NativeMethods.DispatchMessage(ref msg);
            }
        }
    }

    private void FillAndSubmitBuffer(int iBufIdx)
    {
        byte[] rgBuf = _rgPlayBuf[iBufIdx];
        int iSampleFrames = iBufferSampleFrames;

        if (_fcPlayer.SongEnd)
        {
            _fcPlayer.Restart(0, 0);
            LogService.Info("[FcAudio] Song restart");
        }

        _mixer.FillBuffer16bitMono(_rgMixBuf, 0, iSampleFrames, () => _fcPlayer.Run());

        //16bit signed -> 8bit unsigned
        for (int i = 0; i < iSampleFrames; i++)
        {
            int s = _rgMixBuf[i];
            s = s / 256 + 128;
            if (s < 0) s = 0;
            if (s > 255) s = 255;
            rgBuf[i] = (byte)s;
        }

        _rgWaveHdr[iBufIdx].dwFlags = 0;
        _rgWaveHdr[iBufIdx].dwBytesRecorded = 0;
        _rgWaveHdr[iBufIdx].dwLoops = 0;

        uint uHdrSize = (uint)Marshal.SizeOf<WaveNative.WAVEHDR>();
        int mmr = WaveNative.waveOutWritePtr(_hWaveOut, _rgHdrPtr[iBufIdx], uHdrSize);
        if (mmr != 0)
            LogService.Error($"[FcAudio] waveOutWrite[{iBufIdx}] failed: {mmr}");
    }   
}

    #endregion
    #region Win32 API

internal static class WaveNative
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate void WaveOutCallback(IntPtr hwo, int uMsg, IntPtr dwInstance, IntPtr dwParam1, IntPtr dwParam2);

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    public struct WAVEFORMATEX
    {
        public ushort wFormatTag;//WAVE_FORMAT_PCM = 1
        public ushort nChannels;
        public uint nSamplesPerSec;
        public uint nAvgBytesPerSec;
        public ushort nBlockAlign;
        public ushort wBitsPerSample;
        public ushort cbSize;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct WAVEHDR
    {
        public IntPtr lpData;
        public uint dwBufferLength;
        public uint dwBytesRecorded;
        public IntPtr dwUser;
        public uint dwFlags;//WHDR_DONE = 1
        public uint dwLoops;
        public IntPtr lpNext;
        public IntPtr reserved;
    }

    [DllImport("winmm.dll", EntryPoint = "waveOutOpen")]
    public static extern int waveOutOpen(out IntPtr hWaveOut, int uDeviceID,
        ref WAVEFORMATEX lpFormat, IntPtr dwCallback, IntPtr dwCallbackInstance, uint fdwOpen);

    [DllImport("winmm.dll", EntryPoint = "waveOutPrepareHeader")]
    public static extern int waveOutPrepareHeaderPtr(IntPtr hWaveOut, IntPtr lpWaveOutHdr, uint uSize);

    [DllImport("winmm.dll", EntryPoint = "waveOutWrite")]
    public static extern int waveOutWritePtr(IntPtr hWaveOut, IntPtr lpWaveOutHdr, uint uSize);

    [DllImport("winmm.dll", EntryPoint = "waveOutUnprepareHeader")]
    public static extern int waveOutUnprepareHeaderPtr(IntPtr hWaveOut, IntPtr lpWaveOutHdr, uint uSize);

    [DllImport("winmm.dll", EntryPoint = "waveOutReset")]
    public static extern int waveOutReset(IntPtr hWaveOut);

    [DllImport("winmm.dll", EntryPoint = "waveOutClose")]
    public static extern int waveOutClose(IntPtr hWaveOut);
}

internal static class NativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int pt_x;
        public int pt_y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    public static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    public static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    public static extern void PostThreadMessageW(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);
    #endregion
}