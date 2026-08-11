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

    private readonly FcPlayer fcPlayer;
    private readonly PaulaMixer mixer;

    private IntPtr hWaveOut;
    private WaveNative.WAVEFORMATEX wfFormat;
    private WaveNative.WaveOutCallback? pfnCallback;

    private readonly WaveNative.WAVEHDR[] rgWaveHdr;
    private readonly GCHandle[] rgHdrHandle;
    private readonly IntPtr[] rgHdrPtr;
    private readonly GCHandle[] rgBufHandle;
    private readonly byte[][] rgPlayBuf;

    private Thread? thAudio;
    private volatile bool bAudioRunning;
    private bool bDisposed;
    private readonly short[] rgMixBuf;

    public bool IsPlaying => bAudioRunning;
    public bool SongEnded => fcPlayer.SongEnd;
    public PaulaMixer Mixer => mixer;

    #endregion
    #region 初始化
    
    public FcAudioPlayer(byte[] rgFcData)
    {
        if (rgFcData == null || rgFcData.Length <= 4)
            throw new ArgumentException("Invalid .fc file data");

        if (rgFcData[0] != 'F' || rgFcData[1] != 'C' ||
            rgFcData[2] != '1' || rgFcData[3] != '4')
            throw new ArgumentException("Not a valid FC14 file");

        fcPlayer = new FcPlayer();
        mixer = new PaulaMixer(4, iSampleRate);
        fcPlayer.SetMixer(mixer);

        if (!fcPlayer.Init(rgFcData))
            throw new InvalidOperationException("FC module init failed");

        LogService.Info($"[FcAudio] FC module loaded: patterns={fcPlayer.UsedPatterns}, samples=10, waveforms=80");

        wfFormat = new WaveNative.WAVEFORMATEX
        {
            wFormatTag = 1,//WAVE_FORMAT_PCM
            nChannels = iOutputChannels,
            nSamplesPerSec = iSampleRate,
            nAvgBytesPerSec = (uint)(iSampleRate * iBlockAlign),
            nBlockAlign = iBlockAlign,
            wBitsPerSample = iBitsPerSample,
            cbSize = 0
        };

        rgWaveHdr = new WaveNative.WAVEHDR[iBufferCount];
        rgHdrHandle = new GCHandle[iBufferCount];
        rgHdrPtr = new IntPtr[iBufferCount];
        rgBufHandle = new GCHandle[iBufferCount];
        rgPlayBuf = new byte[iBufferCount][];
        rgMixBuf = new short[iBufferSampleFrames];

        for (int i = 0; i < iBufferCount; i++)
        {
            rgPlayBuf[i] = new byte[iBufferByteSize];
            rgBufHandle[i] = GCHandle.Alloc(rgPlayBuf[i], GCHandleType.Pinned);

            rgWaveHdr[i] = new WaveNative.WAVEHDR
            {
                lpData = rgBufHandle[i].AddrOfPinnedObject(),
                dwBufferLength = (uint)iBufferByteSize,
                dwBytesRecorded = 0,
                dwUser = IntPtr.Zero,
                dwFlags = 0,
                dwLoops = 0,
                lpNext = IntPtr.Zero,
                reserved = IntPtr.Zero
            };

            rgHdrHandle[i] = GCHandle.Alloc(rgWaveHdr[i], GCHandleType.Pinned);
            rgHdrPtr[i] = rgHdrHandle[i].AddrOfPinnedObject();
        }
    }

    public FcAudioPlayer(string sFilePath) : this(File.ReadAllBytes(sFilePath))
    {
    }

    #endregion
    #region 播放控制

    public void Play()
    {
        if (bDisposed) throw new ObjectDisposedException(nameof(FcAudioPlayer));
        if (bAudioRunning) return;

        LogService.Info("[FcAudio] Starting FC playback");

        pfnCallback = OnWaveOutCallback;
        int mmr = WaveNative.waveOutOpen(out hWaveOut, -1, ref wfFormat,//WAVE_MAPPER
            Marshal.GetFunctionPointerForDelegate(pfnCallback),
            IntPtr.Zero, 0x30000);//CALLBACK_FUNCTION
        if (mmr != 0)
        {
            LogService.Error($"[FcAudio] waveOutOpen failed: {mmr}");
            throw new InvalidOperationException($"waveOutOpen failed: {mmr}");
        }

        uint uHdrSize = (uint)Marshal.SizeOf<WaveNative.WAVEHDR>();
        for (int i = 0; i < iBufferCount; i++)
        {
            mmr = WaveNative.waveOutPrepareHeaderPtr(hWaveOut, rgHdrPtr[i], uHdrSize);
            if (mmr != 0)
            {
                LogService.Error($"[FcAudio] waveOutPrepareHeader[{i}] failed: {mmr}");
                WaveNative.waveOutClose(hWaveOut);
                hWaveOut = IntPtr.Zero;
                throw new InvalidOperationException($"waveOutPrepareHeader[{i}] failed: {mmr}");
            }
        }

        bAudioRunning = true;

        for (int i = 0; i < iBufferCount; i++)
            FillAndSubmitBuffer(i);

        thAudio = new Thread(AudioThreadProc)
        {
            IsBackground = true,
            Name = "FcAudio"
        };
        thAudio.Start();

        LogService.Info("[FcAudio] Playback started");
    }

    public void Stop()
    {
        bAudioRunning = false;
        pfnCallback = null;

        if (hWaveOut != IntPtr.Zero)
        {
            WaveNative.waveOutReset(hWaveOut);

            uint uHdrSize = (uint)Marshal.SizeOf<WaveNative.WAVEHDR>();
            for (int i = 0; i < iBufferCount; i++)
                WaveNative.waveOutUnprepareHeaderPtr(hWaveOut, rgHdrPtr[i], uHdrSize);

            WaveNative.waveOutClose(hWaveOut);
            hWaveOut = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        if (bDisposed) return;
        bDisposed = true;

        bAudioRunning = false;
        pfnCallback = null;

        if (thAudio != null && thAudio.IsAlive)
        {
            NativeMethods.PostThreadMessageW((uint)thAudio.ManagedThreadId, 0x0012, IntPtr.Zero, IntPtr.Zero);
            thAudio = null;
        }

        if (hWaveOut != IntPtr.Zero)
        {
            WaveNative.waveOutReset(hWaveOut);

            uint uHdrSize = (uint)Marshal.SizeOf<WaveNative.WAVEHDR>();
            for (int i = 0; i < iBufferCount; i++)
                WaveNative.waveOutUnprepareHeaderPtr(hWaveOut, rgHdrPtr[i], uHdrSize);

            WaveNative.waveOutClose(hWaveOut);
            hWaveOut = IntPtr.Zero;
        }

        for (int i = 0; i < iBufferCount; i++)
        {
            if (rgHdrHandle[i].IsAllocated)
                rgHdrHandle[i].Free();
            if (rgBufHandle[i].IsAllocated)
                rgBufHandle[i].Free();
        }
    }

    #endregion
    #region 回调输出

    //WOM_DONE = 0x3BD
    private void OnWaveOutCallback(IntPtr hwo, int uMsg, IntPtr dwInstance, IntPtr dwParam1, IntPtr dwParam2)
    {
        if (!bAudioRunning) return;
        if (uMsg == 0x3BD)
        {
            for (int i = 0; i < iBufferCount; i++)
            {
                if (rgHdrPtr[i] == dwParam1)
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
        while (bAudioRunning)
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
        byte[] rgBuf = rgPlayBuf[iBufIdx];
        int iSampleFrames = iBufferSampleFrames;

        if (fcPlayer.SongEnd)
        {
            fcPlayer.Restart(0, 0);
            LogService.Info("[FcAudio] Song restart");
        }

        mixer.FillBuffer16bitMono(rgMixBuf, 0, iSampleFrames, () => fcPlayer.Run());

        //16bit signed -> 8bit unsigned
        for (int i = 0; i < iSampleFrames; i++)
        {
            int s = rgMixBuf[i];
            s = s / 256 + 128;
            if (s < 0) s = 0;
            if (s > 255) s = 255;
            rgBuf[i] = (byte)s;
        }

        rgWaveHdr[iBufIdx].dwFlags = 0;
        rgWaveHdr[iBufIdx].dwBytesRecorded = 0;
        rgWaveHdr[iBufIdx].dwLoops = 0;

        uint uHdrSize = (uint)Marshal.SizeOf<WaveNative.WAVEHDR>();
        int mmr = WaveNative.waveOutWritePtr(hWaveOut, rgHdrPtr[iBufIdx], uHdrSize);
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