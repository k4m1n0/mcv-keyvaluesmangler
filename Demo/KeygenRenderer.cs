using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using WeaponDamageCalc.Services;
using WeaponDamageCalc.Tools;

namespace WeaponDamageCalc.Demo;

public class KeygenRenderer : IDisposable
{
    private readonly Control ctrlTarget;
    private readonly System.Windows.Forms.Timer tmrFrame;
    private readonly Random rng = new();

    private const int iBmpWidth = 300;
    private const int iBmpHeight = 300;
    private const int iFbSize = iBmpWidth * iBmpHeight;

    private const double dblSineFreq = 0.000767;
    private const double dbl4096 = 4096.0;

    private const int iParticleCount = 2800;    //0x5780 / 8 = 2800
    private const double dblScale = 1.0 / 256.0;//dbl_806138 = rand()%256 / 256
    private const double dblHalf = 0.5;         //dbl_806150
    private const double dblLimitHalf = 3800.0; //dbl_806148 * (0.5 / dblScale)

    private const double dblStarPhaseInc1 = 0.0006; //dbl_8061E0
    private const double dblStarPhaseInc2 = 0.00055;//dbl_8061D8
    private const double dblStarPhaseInc3 = 0.00045;//dbl_8061D0
    private const double dblStarPhaseInc4 = 0.00035;//dbl_8061C8
    private const double dblStarPhaseInc5 = 0.0003; //dbl_8061C0

    private const double dblStarSinAmp = 4.0;       //dbl_8061B0
    private const double dblStarBoundXY = 2650.0;   //dbl_8061A0
    private const double dblStarBoundZ = 3500.0;    //dbl_806148
    private const double dblStarDepthBase = 1856.0; //dbl_806188
    private const double dblStarDepthScale = 256.0; //dbl_806180
    private const double dblStarBrightScale = 0.09; //dbl_806178
    private const double dblStarMaxZ = 1.0;         //dbl_806198
    private const double dblStarMinZ = -1.0;        //dbl_806190
    private const double dblStarCenterX = 150.0;
    private const double dblStarCenterY = 150.0;

    private const double dblScrollFreq = 0.7853981633974483;//dbl_806170 pi/4
    private const double dblScrollAmp = 5504.0;             //dbl_806168

    // Sin 查找表
    private const int _iSinLutSize = 1024;
    private const double _dblSinLutFactor = _iSinLutSize / (Math.PI * 2);
    private static readonly double[] _sinLut = InitSinLut();

    private const string sScrollText =
        "                                        Keyvalues Mangler (TM) 5000                                        Made with Visual Studio and spite" +
        "                                        礦ision 5                                        Error: Flash Download Failed                                        Target not created";

    private uint[] rgFramebuffer = new uint[iFbSize];
    private Bitmap? bmpFrame;

    private double[] rgParticles1 = new double[iParticleCount];//dword_824558 + 8*N
    private double[] rgParticles2 = new double[iParticleCount];//dword_829D50 + 8*N
    private double[] rgParticles3 = new double[iParticleCount];//dword_81ED50 + 8*N

    private double dblStarPhase1;//dword_824558 (rgParticles1[0])
    private double dblStarPhase2;//dbl_829CF8
    private double dblStarPhase3;//dbl_80E868
    private double dblStarPhase4;//dbl_829CF0
    private double dblStarPhase5;//dbl_80E848

    private double dblScrollOffset;//dword_8759FC
    private int iScrollCharIdx;    //dword_875A00
    private Bitmap? bmpScrollText;
    private int iScrollTextWidth;
    private int iScrollTextHeight;

    private byte[]? _rgScrollPixels;
    private int _iScrollStride;
    private byte[]? _rgManaged;

    private int iAnimCounter;//dword_82F4F0
    private bool bDisposed;
    private FcAudioPlayer? _fcAudio;

    private const string sMainText =
        " *******  ********        **   ******   **    **   ******   **    **              ******   ********  **    **        **   ******   **    **   **    **  ********  **    **" +
        "**    **        **        **  **    **  **   ***  **    **  ***  ***             **    **        **  **    **        **  **    **  **    **   **    **        **   **   **" +
        "**    **        **        **        **  **  ****  **    **  ***  ***                   **        **  **    **        **  **    **  **    **    **  **         **    **  **" +
        " *******    ******        **  ****  **  ** ** **  ********  ** ** **              ******     ******  **    **        **  ********  **    **     ****      ******     *****" +
        "**    **        **        **  **    **  ****  **  **    **  ** ** **             **              **  **    **        **  **    **   **  **       **           **    **  **" +
        "**    **        **        **  **    **  ***   **  **    **  **    **             **    **        **  **    **        **  **    **   **  **       **           **   **   **" +
        "**    **        **        **  **    **  **    **  **    **  **    **             **    **        **  **    **        **  **    **    ****        **           **  **    **" +
        "**    **  ********  ********   ******   **    **  **    **  **    **              ******   ********   ******   ********  **    **     **         **     ********  **    **";

    public KeygenRenderer(Control ctrlTarget)
    {
        this.ctrlTarget = ctrlTarget ?? throw new ArgumentNullException(nameof(ctrlTarget));
        ctrlTarget.Paint += OnPaint;
        ctrlTarget.Resize += (_, _) => ctrlTarget.Invalidate();

        InitParticles();
        InitScrollText();
        _rgManaged = new byte[iFbSize * 4];

        bmpFrame = new Bitmap(iBmpWidth, iBmpHeight, PixelFormat.Format32bppArgb);

        tmrFrame = new System.Windows.Forms.Timer { Interval = 25 };//invoke Sleep,25
        tmrFrame.Tick += OnTick;
        tmrFrame.Start();

        try
        {
            var asm = typeof(KeygenRenderer).Assembly;
            using var stream = asm.GetManifestResourceStream("WeaponDamageCalc.Demo.keil.fc");
            if (stream != null)
            {
                var fcData = new byte[stream.Length];
                stream.Read(fcData, 0, fcData.Length);
                _fcAudio = new FcAudioPlayer(fcData);
                _fcAudio.Play();
            }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "FC audio init failed");
        }
    }

    public void Dispose()
    {
        if (bDisposed) return;
        bDisposed = true;
        tmrFrame.Stop();
        tmrFrame.Dispose();
        bmpFrame?.Dispose();
        bmpScrollText?.Dispose();
        _rgScrollPixels = null;
        _rgManaged = null;
        _fcAudio?.Dispose();
    }

    private static double[] InitSinLut()
    {
        var lut = new double[_iSinLutSize];
        for (int i = 0; i < _iSinLutSize; i++)
            lut[i] = Math.Sin(i * 2.0 * Math.PI / _iSinLutSize);
        return lut;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static double Sin(double phase)
    {
        phase = phase % (Math.PI * 2);
        int idx = (int)(phase * _dblSinLutFactor + _iSinLutSize * 1024.0) & (_iSinLutSize - 1);
        return _sinLut[idx];
    }

    private static int Ftol(double dblVal) => (int)Math.Truncate(dblVal);
    private int Rand() => rng.Next(0x10000);//nrandom, 0FFFFh

    //@dumped__0080200a
    private void InitParticles()
    {
        //Rand()%256 * dblScale
        dblStarPhase1 = (Rand() % 256) * dblScale;
        dblStarPhase2 = (Rand() % 256) * dblScale;
        dblStarPhase3 = (Rand() % 256) * dblScale;
        dblStarPhase4 = (Rand() % 256) * dblScale;
        dblStarPhase5 = (Rand() % 256) * dblScale;

        rgParticles1[0] = dblStarPhase1;//dword_824558[0]
        rgParticles3[0] = 0.0;//dword_81ED50[0] = 0

        double dblTemp = 1.0;//dbl_806140 = 1.0
        for (int i = 1; i < iParticleCount; i++)
        {
            //(Rand()%256 * dblScale - 0.5) * 3800.0
            rgParticles2[i] = ((Rand() % 256) * dblScale - dblHalf) * dblLimitHalf;
            rgParticles3[i] = ((Rand() % 256) * dblScale - dblHalf) * dblLimitHalf;
            rgParticles1[i] = dblTemp;
            dblTemp += 1.0;//fadd dbl_806140(1.0)
        }
    }

    private void InitScrollText()
    {
        dblScrollOffset = 0.0;//dword_8759FC
        iScrollCharIdx = 0;//dword_875A00

        const int iFontSize = 16;
        using var font = new Font("Consolas", iFontSize, FontStyle.Bold, GraphicsUnit.Pixel);
        using var bmpTemp = new Bitmap(1, 1);
        using var g = Graphics.FromImage(bmpTemp);
        var sizef = g.MeasureString(sScrollText, font);
        iScrollTextWidth = (int)Math.Ceiling(sizef.Width);
        iScrollTextHeight = (int)Math.Ceiling(sizef.Height);

        bmpScrollText = new Bitmap(iScrollTextWidth, iScrollTextHeight, PixelFormat.Format32bppArgb);
        using var g2 = Graphics.FromImage(bmpScrollText);
        g2.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
        g2.Clear(Color.Black);
        g2.DrawString(sScrollText, font, Brushes.White, 0, 0);

        var bmpData = bmpScrollText.LockBits(
            new Rectangle(0, 0, iScrollTextWidth, iScrollTextHeight),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        _rgScrollPixels = new byte[bmpData.Stride * iScrollTextHeight];
        _iScrollStride = bmpData.Stride;
        Marshal.Copy(bmpData.Scan0, _rgScrollPixels, 0, _rgScrollPixels.Length);
        bmpScrollText.UnlockBits(bmpData);
        if (iScrollTextWidth <= 0) iScrollTextWidth = 1;
        if (iScrollTextHeight <= 0) iScrollTextHeight = 1;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        ClearFrame();//rep stosd
        DrawStar();//call DrawStar
        DrawMainText();//call Draw_MainText
        Draw_ScrollText();//call Draw_ScrollText
        iAnimCounter += 2;//add dword_82F4F0, 2
        ctrlTarget.Invalidate();
    }

    private void ClearFrame()
    {
        Array.Clear(rgFramebuffer, 0, iFbSize);//rep stosd with eax=0
    }

    private void FillRect(int iX, int iY, int iW, int iH, uint uColor)
    {
        int iX0 = Math.Max(iX, 0);
        int iY0 = Math.Max(iY, 0);
        int iX1 = Math.Min(iX + iW, iBmpWidth);
        int iY1 = Math.Min(iY + iH, iBmpHeight);

        for (int y = iY0; y < iY1; y++)
        for (int x = iX0; x < iX1; x++)
            rgFramebuffer[y * iBmpWidth + x] = uColor;
    }

    private void DrawHollowRect(int iX, int iY, int iW, int iH, uint uColor)
    {
        FillRect(iX, iY, iW, 1, uColor);
        FillRect(iX, iY + iH - 1, iW, 1, uColor);
        FillRect(iX, iY, 1, iH, uColor);
        FillRect(iX + iW - 1, iY, 1, iH, uColor);
    }

    private void DrawStar()
    {
        dblStarPhase1 += dblStarPhaseInc1;
        dblStarPhase2 += dblStarPhaseInc2;
        dblStarPhase3 += dblStarPhaseInc3;
        dblStarPhase4 += dblStarPhaseInc4;
        dblStarPhase5 += dblStarPhaseInc5;

        //(sin(A) + sin(B)) * 4.0
        double dblOffset1 = (Sin(dblStarPhase3) + Sin(dblStarPhase1)) * dblStarSinAmp;
        double dblOffset2 = (Sin(dblStarPhase4) + Sin(dblStarPhase2)) * dblStarSinAmp;
        double dblOffset3 = (Sin(dblStarPhase5) + Sin(dblStarPhase3)) * dblStarSinAmp;

        //@dumped__00801c63
        for (int i = 1; i < iParticleCount; i++)
        {
            rgParticles1[i] -= dblOffset1;
            if (rgParticles1[i] < 0.0)
                rgParticles1[i] += dblStarBoundXY;//fadd dbl_8061A0
            else if (rgParticles1[i] > dblStarBoundXY)
                rgParticles1[i] -= dblStarBoundXY;//fsub dbl_8061A0

            rgParticles2[i] -= dblOffset2;
            if (rgParticles2[i] < 0.0)
                rgParticles2[i] += dblStarBoundZ;//fadd dbl_806148
            else if (rgParticles2[i] > dblStarBoundZ)
                rgParticles2[i] -= dblStarBoundZ;

            rgParticles3[i] -= dblOffset3;
            if (rgParticles3[i] < 0.0)
                rgParticles3[i] += dblStarBoundZ;
            else if (rgParticles3[i] > dblStarBoundZ)
                rgParticles3[i] -= dblStarBoundZ;
        }

        //@dumped__00801d67
        for (int i = 1; i < iParticleCount; i++)
        {
            double dblZ = rgParticles1[i];
            if (dblZ < 0.0) dblZ = dblStarMaxZ;//fld dbl_806198 (1.0)

            double dblDepthFactor = dblStarMinZ / dblZ;//dbl_806190 (-1.0) / Z

            double dblScreenY = dblStarCenterY -
                Ftol((rgParticles2[i] - dblStarDepthBase) * dblDepthFactor * dblStarDepthScale);

            double dblScreenX = dblStarCenterX -
                Ftol((rgParticles3[i] - dblStarDepthBase) * dblDepthFactor * dblStarDepthScale);

            int iBrightness = 255 - (int)(dblZ * dblStarBrightScale);//0xFF - ftol(Z * 0.09), dblZ>0时(int)=ftol

            int iSX = (int)dblScreenX;
            int iSY = (int)dblScreenY;

            if (iSX < 0 || iSX >= iBmpWidth) continue;
            if (iSY < 0 || iSY >= iBmpHeight) continue;
            if (iBrightness <= 0) continue;

            byte bC = (byte)Math.Min(255, iBrightness);
            uint uColor = 0xFF000000 | ((uint)bC << 16) | ((uint)bC << 8) | bC;

            int iFbIdx = iSY * iBmpWidth + iSX;
            rgFramebuffer[iFbIdx] = uColor;//mov [ecx*4 + dword_82F4F8], edx
        }
    }

    private void DrawMainText()
    {
        string sText = sMainText;
        int iStride = 170;
        int iTotalRows = iStride;

        //@dumped__00801e70
        for (int row = 0; row < iTotalRows; row++)
        {
            for (int col = 0; col < 8; col++)
            {
                int iIdx = row + col * iStride;
                if (iIdx >= sText.Length) break;
                if (sText[iIdx] != '*') continue;//cmp byte ptr [ebp], 02ah

                //fild row+iAnimCounter*2, fmul dbl_806170, fsin, fmul dbl_806168, ftol, sar 8
                float fArgY = (row + iAnimCounter * 2.0f) * 60f;
                double dblSinY = Sin(fArgY * dblSineFreq) * dbl4096;
                int iY = (Ftol(dblSinY) >> 8) + col * 8 + 120;//sar eax,8; + col*8 + offset

                //fild row+iAnimCounter, fmul dbl_806170, fsin, fmul dbl_806168, ftol, sar 5
                float fArgX = (row + iAnimCounter * 0.5f) * 45f + iAnimCounter * 4f;
                double dblSinX = Sin(fArgX * dblSineFreq) * dbl4096;
                int iX = 145 - (Ftol(dblSinX) >> 5);//0xAA - (sin>>5), sar eax,5

                int iR = (int)(Sin((iX + iAnimCounter) * 0.02) * 40 + 40);
                int iG = (int)(Sin((iX + iAnimCounter) * 0.03) * 60 + 80);
                int iB = (int)(Sin((iX + iAnimCounter) * 0.01) * 80 + 175);
                iR = Math.Clamp(iR, 0, 255);
                iG = Math.Clamp(iG, 0, 255);
                iB = Math.Clamp(iB, 0, 255);

                uint uColor = 0xFF000000 | ((uint)iR << 16) | ((uint)iG << 8) | (uint)iB;
                DrawHollowRect(iX, iY, 7, 7, uColor);
            }
        }
    }

    private void Draw_ScrollText()
    {
        if (_rgScrollPixels == null) return;

        //dec dword_8759FC; cmp -10h
        dblScrollOffset -= 1.0;
        if (dblScrollOffset <= -16.0)//cmp eax, -10h
        {
            dblScrollOffset = -8.0;//mov dword_8759FC, -8
            iScrollCharIdx++;//inc dword_875A00
            if (iScrollCharIdx >= sScrollText.Length || sScrollText[iScrollCharIdx] == '~')
            {
                iScrollCharIdx = 0;//cmp '~', jnz; xor eax,eax
                dblScrollOffset = 0.0;
            }
        }

        double dblBaseX = dblScrollOffset - iScrollCharIdx * 9.0;//add ebx,9

        byte[] rgPixels = _rgScrollPixels;
        int iStride = _iScrollStride;
        int iTextTop = (iBmpHeight - iScrollTextHeight) / 2;

        for (int iScreenX = 0; iScreenX < iBmpWidth; iScreenX++)
        {
            double dblSrcX = (iScreenX - dblBaseX) * 1.0;
            int iSrcX = (int)(dblSrcX % iScrollTextWidth);
            if (iSrcX < 0) iSrcX += iScrollTextWidth;

            //sin * 5504 >> 6, 127
            double dblA = iScreenX * 0.015 + iAnimCounter * 0.06;
            double dblB = iScreenX * 0.02 + iAnimCounter * 0.08;
            double dblD = iScreenX * 0.025 + iAnimCounter * 0.10;

            int iV1 = (int)(Sin(dblA * dblScrollFreq) * dblScrollAmp) >> 6;//sin * 5504 >> 6
            int iV2 = (int)(Sin(dblB * dblScrollFreq) * dblScrollAmp) >> 6;
            int iV4 = (int)(Sin(dblD * dblScrollFreq) * dblScrollAmp) >> 6;

            int iR = Math.Clamp(127 - iV1, 30, 225);//0x7F 30~225
            int iG = Math.Clamp(127 - iV2, 30, 225);
            int iB = Math.Clamp(127 - iV4, 30, 225);
            uint uColor = 0xFF000000 | ((uint)iR << 16) | ((uint)iG << 8) | (uint)iB;

            //0x60 - sin>>8
            double dblPhaseShift = Sin((iScreenX * 0.01 + iAnimCounter * 0.03) * dblScrollFreq) * 0.5;
            double dblAmp = 18 + dblPhaseShift * 8;
            int iYOffset = (int)(Sin((iScreenX * 0.03 + iAnimCounter * 0.12 + dblPhaseShift) * dblScrollFreq) * dblAmp);

            //Out_ScreenText
            int iSrcX4 = iSrcX * 4;
            for (int iSrcY = 0; iSrcY < iScrollTextHeight; iSrcY++)
            {
                int iFbY = iTextTop + iSrcY + iYOffset;
                if (iFbY < 0 || iFbY >= iBmpHeight) continue;

                int iPixelOff = iSrcY * iStride + iSrcX4;
                if (iPixelOff + 2 >= rgPixels.Length) continue;
                if (rgPixels[iPixelOff] < 128 && rgPixels[iPixelOff + 1] < 128 && rgPixels[iPixelOff + 2] < 128) continue;

                rgFramebuffer[iFbY * iBmpWidth + iScreenX] = uColor;//mov [eax*4 + dword_82F4F8], ecx
            }
        }
    }

    private void OnPaint(object? sender, PaintEventArgs e)
    {
        if (bDisposed || bmpFrame == null) return;
        UpdateBitmapFromFramebuffer();

        float fScale = Math.Min(
            (float)ctrlTarget.ClientSize.Width / iBmpWidth,
            (float)ctrlTarget.ClientSize.Height / iBmpHeight
        );
        int iDrawW = (int)(iBmpWidth * fScale);
        int iDrawH = (int)(iBmpHeight * fScale);
        int iDrawX = (ctrlTarget.ClientSize.Width - iDrawW) / 2;
        int iDrawY = (ctrlTarget.ClientSize.Height - iDrawH) / 2;

        e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        e.Graphics.DrawImage(bmpFrame, iDrawX, iDrawY, iDrawW, iDrawH);
    }

    private void UpdateBitmapFromFramebuffer()
    {
        if (bmpFrame == null || _rgManaged == null) return;

        var rect = new Rectangle(0, 0, iBmpWidth, iBmpHeight);
        var bmpData = bmpFrame.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

        Buffer.BlockCopy(rgFramebuffer, 0, _rgManaged, 0, iFbSize * 4);
        Marshal.Copy(_rgManaged, 0, bmpData.Scan0, _rgManaged.Length);

        bmpFrame.UnlockBits(bmpData);
    }
}