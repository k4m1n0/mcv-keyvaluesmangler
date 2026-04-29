using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using WeaponDamageCalc.Models;

namespace WeaponDamageCalc;

public class PanelRenderer
{
    private readonly Panel panel;

    public PanelRenderer(Panel panel)
    {
        this.panel = panel;
    }

    public void DrawSpread(Graphics g, WeaponData? left, WeaponData? right,
        double hipL, double adsL, double bipodHipL, double bipodAdsL,
        double hipR, double adsR, double bipodHipR, double bipodAdsR)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Black);
        int cx = panel.Width / 2, cy = panel.Height / 2;
        float r = Math.Min(cx, cy) - 20;
        float s = r / 15f;

        DrawCircle(g, cx, cy, (float)(hipL * s), Color.Red, DashStyle.Solid);
        DrawCircle(g, cx, cy, (float)(adsL * s), Color.Red, DashStyle.Dash);
        if (bipodHipL > 0)
            DrawCircle(g, cx, cy, (float)(bipodHipL * s), Color.Lime, DashStyle.Solid);
        if (bipodAdsL > 0)
            DrawCircle(g, cx, cy, (float)(bipodAdsL * s), Color.Lime, DashStyle.Dash);

        if (right != null)
        {
            DrawCircle(g, cx, cy, (float)(hipR * s), Color.DodgerBlue, DashStyle.Solid);
            DrawCircle(g, cx, cy, (float)(adsR * s), Color.DodgerBlue, DashStyle.Dash);
            if (bipodHipR > 0)
                DrawCircle(g, cx, cy, (float)(bipodHipR * s), Color.Yellow, DashStyle.Solid);
            if (bipodAdsR > 0)
                DrawCircle(g, cx, cy, (float)(bipodAdsR * s), Color.Yellow, DashStyle.Dash);
        }

        DrawLeftLegend(g, 5, panel.Height - 5, Color.Red, Color.Lime);
        if (right != null)
            DrawRightLegend(g, panel.Width - 5, panel.Height - 5, Color.DodgerBlue, Color.Yellow);

        string curCal = left?.PrimaryAmmo ?? "";
        string cmpCal = right?.PrimaryAmmo ?? "";
        string calText = !string.IsNullOrEmpty(curCal) && !string.IsNullOrEmpty(cmpCal)
            ? $"{curCal} | {cmpCal}"
            : !string.IsNullOrEmpty(curCal) ? curCal : cmpCal;
        if (!string.IsNullOrEmpty(calText))
        {
            using var cf = new Font("Arial", 7, FontStyle.Bold);
            using var cb = new SolidBrush(Color.FromArgb(180, 180, 180));
            var sz = g.MeasureString(calText, cf);
            g.DrawString(calText, cf, cb, (panel.Width - sz.Width) / 2, 5);
        }
    }

    public void DrawRecoil(Graphics g, WeaponData? left, WeaponData? right,
        double hipUpL, double hipRtL, double adsUpL, double adsRtL,
        double hipUpR, double hipRtR, double adsUpR, double adsRtR)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Black);
        int cx = panel.Width / 2;
        int cy = panel.Height - 30;
        int shots = 30;
        float s = Math.Min(2.5f, (float)((panel.Height - 40) / (Math.Max(hipUpL, 0.01) * shots)));

        DrawSector(g, cx, cy, (float)hipUpL, (float)hipRtL, shots, s,
            Color.FromArgb(80, 255, 0, 0), Color.Red, "L Hip", "left");
        DrawSector(g, cx, cy, (float)adsUpL, (float)adsRtL, shots, s,
            Color.FromArgb(80, 0, 255, 0), Color.Lime, "L ADS", "left");

        if (right != null)
        {
            DrawSector(g, cx, cy, (float)hipUpR, (float)hipRtR, shots, s,
                Color.FromArgb(40, 0, 191, 255), Color.DeepSkyBlue, "R Hip", "right");
            DrawSector(g, cx, cy, (float)adsUpR, (float)adsRtR, shots, s,
                Color.FromArgb(40, 255, 165, 0), Color.Yellow, "R ADS", "right");
        }
    }

    private void DrawCircle(Graphics g, int cx, int cy, float radius, Color color, DashStyle dashStyle)
    {
        using var pen = new Pen(color, 1.2f) { DashStyle = dashStyle };
        g.DrawEllipse(pen, cx - radius, cy - radius, radius * 2, radius * 2);
    }

    private void DrawLeftLegend(Graphics g, int x, int y, Color hipColor, Color bipodColor)
    {
        using var font = new Font("Arial", 7);
        using var hipBrush = new SolidBrush(hipColor);
        using var bipodBrush = new SolidBrush(bipodColor);
        float drawY = y - 56;
        g.DrawString("━ Hip", font, hipBrush, x, drawY);
        g.DrawString("┅ ADS", font, hipBrush, x, drawY + 14);
        g.DrawString("━ Bipod", font, bipodBrush, x, drawY + 28);
        g.DrawString("┅ Bipod ADS", font, bipodBrush, x, drawY + 42);
    }

    private void DrawRightLegend(Graphics g, int rightX, int rightY, Color hipColor, Color bipodColor)
    {
        using var font = new Font("Arial", 7);
        using var hipBrush = new SolidBrush(hipColor);
        using var bipodBrush = new SolidBrush(bipodColor);
        float y = rightY - 56;
        g.DrawString("Hip ━", font, hipBrush,
            rightX - g.MeasureString("Hip ━", font).Width, y);
        g.DrawString("ADS ┅", font, hipBrush,
            rightX - g.MeasureString("ADS ┅", font).Width, y + 14);
        g.DrawString("Bipod ━", font, bipodBrush,
            rightX - g.MeasureString("Bipod ━", font).Width, y + 28);
        g.DrawString("Bipod ADS ┅", font, bipodBrush,
            rightX - g.MeasureString("Bipod ADS ┅", font).Width, y + 42);
    }

    private void DrawSector(Graphics g, int cx, int cy,
        float up, float right, int shots, float scale,
        Color fill, Color line, string label, string side)
    {
        float totalUp = up * shots * scale;
        float totalRight = right * shots * scale;
        float radius = totalUp;
        if (radius <= 0) return;

        float halfAngle = (float)Math.Atan2(totalRight, totalUp);
        float startAngle = 270f - halfAngle * 180f / (float)Math.PI;
        float sweepAngle = 2f * halfAngle * 180f / (float)Math.PI;

        using var fillBrush = new SolidBrush(fill);
        g.FillPie(fillBrush, cx - radius, cy - radius, radius * 2, radius * 2, startAngle, sweepAngle);

        using var outlinePen = new Pen(line, 1.2f);
        g.DrawPie(outlinePen, cx - radius, cy - radius, radius * 2, radius * 2, startAngle, sweepAngle);

        using var labelFont = new Font("Arial", 6);
        using var labelBrush = new SolidBrush(line);
        var textSize = g.MeasureString(label, labelFont);
        float labelX = side == "left"
            ? cx - totalRight - textSize.Width - 4
            : cx + totalRight + 4;
        g.DrawString(label, labelFont, labelBrush, labelX, cy - totalUp - textSize.Height);
    }
}