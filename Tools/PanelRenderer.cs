using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using WeaponDamageCalc.Models;

namespace WeaponDamageCalc.Tools;

public class PanelRenderer
{
    private readonly Panel pnlPanel;

    public PanelRenderer(Panel pnlPanel)
    {
        this.pnlPanel = pnlPanel;
    }

    #region 公开绘图入口
    public void DrawSpread(Graphics g, WeaponData? wLeft, WeaponData? wRight,
        double dHipL, double dAdsL, double dBipodHipL, double dBipodAdsL,
        double dHipR, double dAdsR, double dBipodHipR, double dBipodAdsR)
    {
        //仅半自动绘图补偿精度+20% 圆半径缩小到83.3%
        if (IsSemiOnly(wLeft)) { dHipL /= 1.2; dAdsL /= 1.2; dBipodHipL /= 1.2; dBipodAdsL /= 1.2; }
        if (wRight != null && IsSemiOnly(wRight)) { dHipR /= 1.2; dAdsR /= 1.2; dBipodHipR /= 1.2; dBipodAdsR /= 1.2; }
        //全自动后座绘图补偿 按持续射击时间线性增长到最大值
        //if (HasAuto(wLeft))
        //{
        //    dHipUpL = Math.Min(dHipUpL + dAdsUpMaxL * dTransitionL, dAdsUpMaxL);
        //    dHipRtL = Math.Min(dHipRtL + dAdsRtMaxL * dTransitionL, dAdsRtMaxL);
        //}
        //if (wRight != null && HasAuto(wRight))
        //{
        //    dHipUpR = Math.Min(dHipUpR + dAdsUpMaxR * dTransitionR, dAdsUpMaxR);
        //    dHipRtR = Math.Min(dHipRtR + dAdsRtMaxR * dTransitionR, dAdsRtMaxR);
        //}
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Black);
        int iCx = pnlPanel.Width / 2, iCy = pnlPanel.Height / 2;
        float fR = Math.Min(iCx, iCy) - 20;
        float fS = fR / 15f;
        //s是每度散布对应的像素半径 15度对应最大可用半径

        DrawCircle(g, iCx, iCy, (float)(dHipL * fS), Color.Red, DashStyle.Solid);
        DrawCircle(g, iCx, iCy, (float)(dAdsL * fS), Color.Red, DashStyle.Dash);
        if (dBipodHipL > 0)
            DrawCircle(g, iCx, iCy, (float)(dBipodHipL * fS), Color.Lime, DashStyle.Solid);
        if (dBipodAdsL > 0)
            DrawCircle(g, iCx, iCy, (float)(dBipodAdsL * fS), Color.Lime, DashStyle.Dash);

        if (wRight != null)
        {
            DrawCircle(g, iCx, iCy, (float)(dHipR * fS), Color.DodgerBlue, DashStyle.Solid);
            DrawCircle(g, iCx, iCy, (float)(dAdsR * fS), Color.DodgerBlue, DashStyle.Dash);
            if (dBipodHipR > 0)
                DrawCircle(g, iCx, iCy, (float)(dBipodHipR * fS), Color.Yellow, DashStyle.Solid);
            if (dBipodAdsR > 0)
                DrawCircle(g, iCx, iCy, (float)(dBipodAdsR * fS), Color.Yellow, DashStyle.Dash);
        }

        DrawLeftLegend(g, 5, pnlPanel.Height - 5, Color.Red, Color.Lime);
        if (wRight != null)
            DrawRightLegend(g, pnlPanel.Width - 5, pnlPanel.Height - 5, Color.DodgerBlue, Color.Yellow);

        string sCurCal = wLeft?.PrimaryAmmo ?? "";
        string sCmpCal = wRight?.PrimaryAmmo ?? "";
        string sCalText = !string.IsNullOrEmpty(sCurCal) && !string.IsNullOrEmpty(sCmpCal)
            ? $"{sCurCal} | {sCmpCal}"
            : !string.IsNullOrEmpty(sCurCal) ? sCurCal : sCmpCal;
        if (!string.IsNullOrEmpty(sCalText))
        {
            using var fntCal = new Font("Arial", 7, FontStyle.Bold);
            using var brCal = new SolidBrush(Color.FromArgb(180, 180, 180));
            var szfText = g.MeasureString(sCalText, fntCal);
            g.DrawString(sCalText, fntCal, brCal, (pnlPanel.Width - szfText.Width) / 2, 5);
        }
    }

    public void DrawRecoil(Graphics g, WeaponData? wLeft, WeaponData? wRight,
        double dHipUpL, double dHipRtL, double dAdsUpL, double dAdsRtL,
        double dHipUpR, double dHipRtR, double dAdsUpR, double dAdsRtR,
        float fMaxScale = 2.0f)
    {
        //for game side unused up_max and right_max features
        /*
        if (HasAuto(wLeft))
        {
            double dShotsPerSecL = (wLeft?.FireRate ?? 600) / 60.0;
            int iTransShotsL = Math.Max(1, (int)Math.Ceiling(dTransitionSecL * dShotsPerSecL));
            int iRampL = Math.Min(30, iTransShotsL);
            int iHoldL = 30 - iRampL;
            double dUpStepL = (dAdsUpMaxL - dHipUpL) / iTransShotsL;
            double dRtStepL = (dAdsRtMaxL - dHipRtL) / iTransShotsL;
            dHipUpL = iRampL * dHipUpL + dUpStepL * iRampL * (iRampL - 1) / 2.0 + iHoldL * dAdsUpMaxL;
            dHipRtL = iRampL * dHipRtL + dRtStepL * iRampL * (iRampL - 1) / 2.0 + iHoldL * dAdsRtMaxL;
        }
        if (wRight != null && HasAuto(wRight))
        {
            double dShotsPerSecR = (wRight?.FireRate ?? 600) / 60.0;
            int iTransShotsR = Math.Max(1, (int)Math.Ceiling(dTransitionSecR * dShotsPerSecR));
            int iRampR = Math.Min(30, iTransShotsR);
            int iHoldR = 30 - iRampR;
            double dUpStepR = (dAdsUpMaxR - dHipUpR) / iTransShotsR;
            double dRtStepR = (dAdsRtMaxR - dHipRtR) / iTransShotsR;
            dHipUpR = iRampR * dHipUpR + dUpStepR * iRampR * (iRampR - 1) / 2.0 + iHoldR * dAdsUpMaxR;
            dHipRtR = iRampR * dHipRtR + dRtStepR * iRampR * (iRampR - 1) / 2.0 + iHoldR * dAdsRtMaxR;
        }
        */
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Black);
        int iCx = pnlPanel.Width / 2;
        int iCy = pnlPanel.Height - 30;
        int iShots = 30;
        float fMaxUp = (float)Math.Max(Math.Max(dHipUpL, dAdsUpL),
                        wRight != null ? Math.Max(dHipUpR, dAdsUpR) : 0.01);
        float fS = Math.Min(fMaxScale, (float)((pnlPanel.Height - 40) / (Math.Max(fMaxUp, 0.01) * iShots)));
        //scale钳制到maxScale 防止低后座时扇形过大 取两侧最大后座防溢出

        DrawSector(g, iCx, iCy, (float)dHipUpL, (float)dHipRtL, iShots, fS,
            Color.FromArgb(80, 255, 0, 0), Color.Red, "L Hip", "left");
        DrawSector(g, iCx, iCy, (float)dAdsUpL, (float)dAdsRtL, iShots, fS,
            Color.FromArgb(80, 0, 255, 0), Color.Lime, "L ADS", "left");

        if (wRight != null)
        {
            DrawSector(g, iCx, iCy, (float)dHipUpR, (float)dHipRtR, iShots, fS,
                Color.FromArgb(40, 0, 191, 255), Color.DeepSkyBlue, "R Hip", "right");
            DrawSector(g, iCx, iCy, (float)dAdsUpR, (float)dAdsRtR, iShots, fS,
                Color.FromArgb(40, 255, 165, 0), Color.Yellow, "R ADS", "right");
        }
    }

    #endregion
    #region 绘图辅助

    private void DrawCircle(Graphics g, int iCx, int iCy, float fRadius, Color cColor, DashStyle dsStyle)
    {
        if (fRadius <= 0) return;
        using var penCircle = new Pen(cColor, 1.2f) { DashStyle = dsStyle };
        g.DrawEllipse(penCircle, iCx - fRadius, iCy - fRadius, fRadius * 2, fRadius * 2);
    }

    private void DrawLeftLegend(Graphics g, int iX, int iY, Color cHip, Color cBipod)
    {
        using var fntLegend = new Font("Arial", 7);
        using var brHip = new SolidBrush(cHip);
        using var brBipod = new SolidBrush(cBipod);
        float fDrawY = iY - 56;
        g.DrawString("━ Hip", fntLegend, brHip, iX, fDrawY);
        g.DrawString("┅ ADS", fntLegend, brHip, iX, fDrawY + 14);
        g.DrawString("━ Bipod", fntLegend, brBipod, iX, fDrawY + 28);
        g.DrawString("┅ Bipod ADS", fntLegend, brBipod, iX, fDrawY + 42);
    }

    private void DrawRightLegend(Graphics g, int iRightX, int iRightY, Color cHip, Color cBipod)
    {
        using var fntLegend = new Font("Arial", 7);
        using var brHip = new SolidBrush(cHip);
        using var brBipod = new SolidBrush(cBipod);
        float fY = iRightY - 56;
        g.DrawString("Hip ━", fntLegend, brHip,
            iRightX - g.MeasureString("Hip ━", fntLegend).Width, fY);
        g.DrawString("ADS ┅", fntLegend, brHip,
            iRightX - g.MeasureString("ADS ┅", fntLegend).Width, fY + 14);
        g.DrawString("Bipod ━", fntLegend, brBipod,
            iRightX - g.MeasureString("Bipod ━", fntLegend).Width, fY + 28);
        g.DrawString("Bipod ADS ┅", fntLegend, brBipod,
            iRightX - g.MeasureString("Bipod ADS ┅", fntLegend).Width, fY + 42);
    }

    private void DrawSector(Graphics g, int iCx, int iCy,
        float fUp, float fRight, int iShots, float fScale,
        Color cFill, Color cLine, string sLabel, string sSide)
    {
        float fTotalUp = fUp * iShots * fScale;
        float fTotalRight = fRight * iShots * fScale;
        float fRadius = fTotalUp;
        if (fRadius <= 0) return;

        //以正上方270度为基准 左右扩展halfAngle
        float fHalfAngle = (float)Math.Atan2(fTotalRight, fTotalUp);
        float fStartAngle = 270f - fHalfAngle * 180f / (float)Math.PI;
        float fSweepAngle = 2f * fHalfAngle * 180f / (float)Math.PI;

        using var brFill = new SolidBrush(cFill);
        g.FillPie(brFill, iCx - fRadius, iCy - fRadius, fRadius * 2, fRadius * 2, fStartAngle, fSweepAngle);

        using var penOutline = new Pen(cLine, 1.2f);
        g.DrawPie(penOutline, iCx - fRadius, iCy - fRadius, fRadius * 2, fRadius * 2, fStartAngle, fSweepAngle);

        using var fntLabel = new Font("Arial", 6);
        using var brLabel = new SolidBrush(cLine);
        var szfLabel = g.MeasureString(sLabel, fntLabel);
        float fLabelX = sSide == "left"
            ? iCx - fTotalRight - szfLabel.Width - 4
            : iCx + fTotalRight + 4;
        g.DrawString(sLabel, fntLabel, brLabel, fLabelX, iCy - fTotalUp - szfLabel.Height);
    }

    private static bool IsSemiOnly(WeaponData? w)
    {
        if (w == null || string.IsNullOrEmpty(w.FireModes)) return false;
        string sModes = w.FireModes.ToLowerInvariant();
        bool bFireRateOk = (w.FireRate ?? 0) >= 200;
        bool bSinglePellet = (w.BulletsPerShot ?? 1) <= 1;
        return sModes.Contains("semi") && !sModes.Contains("auto") && !sModes.Contains("burst")
            && bFireRateOk && bSinglePellet;
    }

    //ditto, for game side unused features
    //private static bool HasAuto(WeaponData? w)
    //{
    //    if (w == null || string.IsNullOrEmpty(w.FireModes)) return false;
    //    string sModes = w.FireModes.ToLowerInvariant();
    //    return sModes.Contains("auto");
    //}
    #endregion
}