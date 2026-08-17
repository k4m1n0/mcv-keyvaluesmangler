using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using WeaponDamageCalc.Services;

namespace WeaponDamageCalc;

public partial class Form1
{
    private CancellationTokenSource? ctsFlash = null;
    private Control? ctrlFlashTarget = null;
    private Color cFlashOldColor;
    private const double dMaxHorizontalDistance = 460;
    private const double dMaxVerticalDistance = 620;
    private const double dCrossAxisPenalty = 1.5;//跨轴距离惩罚系数

    private void NavigateFocus(Keys kDir)
    {
        var ctrlCur = ActiveControl;
        if (ctrlCur == null) return;

        if (ctrlCur is TextBox && ctrlCur.Parent is NumericUpDown nud)
        {
            ctrlCur = nud;
        }

        var ctrlTarget = FindNearest(ctrlCur, kDir);
        if (ctrlTarget == null) return;

        LogService.Debug($"Nav: {ctrlCur.Name ?? ctrlCur.GetType().Name} -> {ctrlTarget.Name ?? ctrlTarget.GetType().Name} ({kDir})");

        ctrlTarget.Focus();
        ScrollToVisible(ctrlTarget);
        FlashControl(ctrlTarget);
    }

    //优先保持行/列不动 跨轴控件只在同轴缺失时才作为兜底
    private Control? FindNearest(Control ctrlCur, Keys kDir)
    {
        var rctCurBounds = BoundsInForm(ctrlCur);
        bool bVertical = kDir is Keys.Up or Keys.Down;

        Control? ctrlBestSame = null;
        double dBestSameDist = double.MaxValue;
        Control? ctrlBestAny = null;
        double dBestAnyDist = double.MaxValue;
        bool bCurIsButton = ctrlCur is Button;

        foreach (var ctrl in GetNavigableControls())
        {
            if (ctrl == ctrlCur) continue;
            var rctCtrlBounds = BoundsInForm(ctrl);

            //textbox和其宿主nud同时出现在遍历中会互相干扰
            if (ctrlCur.Parent == ctrl || ctrl.Parent == ctrlCur)
            {
                continue;
            }

            double dDx, dDy;
            if (kDir == Keys.Right)
                dDx = rctCtrlBounds.Left - rctCurBounds.Right;
            else if (kDir == Keys.Left)
                dDx = rctCurBounds.Left - rctCtrlBounds.Right;
            else
                dDx = rctCtrlBounds.Left + rctCtrlBounds.Width / 2.0 - (rctCurBounds.Left + rctCurBounds.Width / 2.0);

            if (kDir == Keys.Down)
                dDy = rctCtrlBounds.Top - rctCurBounds.Bottom;
            else if (kDir == Keys.Up)
                dDy = rctCurBounds.Top - rctCtrlBounds.Bottom;
            else
                dDy = rctCtrlBounds.Top + rctCtrlBounds.Height / 2.0 - (rctCurBounds.Top + rctCurBounds.Height / 2.0);

            //允许一定重叠 方向键在重叠10px内仍视为有效候选
            const double dOverlapTolerance = 10;
            if (kDir == Keys.Right && dDx < -dOverlapTolerance) continue;
            if (kDir == Keys.Left && dDx < -dOverlapTolerance) continue;
            if (kDir == Keys.Down && dDy < -dOverlapTolerance) continue;
            if (kDir == Keys.Up && dDy < -dOverlapTolerance) continue;

            double dMain = bVertical ? Math.Abs(dDy) : Math.Abs(dDx);
            double dCross = bVertical ? Math.Abs(dDx) : Math.Abs(dDy);

            if (bVertical && Math.Abs(dDy) > dMaxVerticalDistance) continue;
            if (!bVertical && Math.Abs(dDx) > dMaxHorizontalDistance) continue;

            //按钮区紧凑 从按钮出发时抬高非按钮候选的代价 防止漂出按钮区
            if (bCurIsButton && ctrl is not Button && bVertical)
            {
                dMain *= 4.0;
            }

            if (bCurIsButton && ctrl is Button && bVertical)
            {
                //按钮间按X轴重叠比例打折 完全对齐时最优先 微弱重叠的按钮不被误选中
                double dOverlap = Math.Min(rctCurBounds.Right, rctCtrlBounds.Right) - Math.Max(rctCurBounds.Left, rctCtrlBounds.Left);
                double dOverlapRatio = Math.Clamp(dOverlap / rctCurBounds.Width, 0.0, 1.0);
                dMain *= 0.25 + (1.0 - dOverlapRatio) * 0.75;
            }

            bool bSameAxis;
            if (bVertical)
            {
                bSameAxis = !(rctCtrlBounds.Right < rctCurBounds.Left || rctCtrlBounds.Left > rctCurBounds.Right);
            }
            else
            {
                bSameAxis = !(rctCtrlBounds.Bottom < rctCurBounds.Top || rctCtrlBounds.Top > rctCurBounds.Bottom);
            }

            if (bSameAxis)
            {
                if (dMain < dBestSameDist) { dBestSameDist = dMain; ctrlBestSame = ctrl; }
            }
            else
            {
                //跨轴候选必须足够近 否则跳过 防止跳到远处的另一列
                if (!bVertical && Math.Abs(dDy) > Math.Max(rctCurBounds.Height, rctCtrlBounds.Height) * 2.0)
                {
                    continue;
                }
                if (bVertical && Math.Abs(dDx) > Math.Max(rctCurBounds.Width, rctCtrlBounds.Width) * 2.0)
                {
                    continue;
                }
                double dScore = dMain * dCrossAxisPenalty + dCross;
                if (dScore < dBestAnyDist)
                {
                    dBestAnyDist = dScore; ctrlBestAny = ctrl;
                }
            }
        }

        //同轴候选太远时 若跨轴候选显著更近则优先跨轴
        if (ctrlBestSame != null && ctrlBestAny != null && dBestAnyDist < dBestSameDist * 0.5)
        {
            return ctrlBestAny;
        }
        return ctrlBestSame ?? ctrlBestAny;
    }

    private IEnumerable<Control> GetNavigableControls()
    {
        foreach (var ctrl in GetAllDescendants(this))
            if (ctrl.CanSelect && ctrl.TabStop && ctrl.Visible && ctrl.Enabled && ctrl is not (TextBox and { Parent: NumericUpDown }))
                yield return ctrl;
    }

    private Rectangle BoundsInForm(Control ctrl)
    {
        var ptTopLeft = ctrl.PointToScreen(Point.Empty);
        var ptBottomRight = ctrl.PointToScreen(new Point(ctrl.Width, ctrl.Height));
        var ptTopLeftClient = PointToClient(ptTopLeft);
        var ptBottomRightClient = PointToClient(ptBottomRight);
        return new Rectangle(ptTopLeftClient.X, ptTopLeftClient.Y, ptBottomRightClient.X - ptTopLeftClient.X, ptBottomRightClient.Y - ptTopLeftClient.Y);
    }

    private static void ScrollToVisible(Control ctrl)
    {
        for (var ctrlParent = ctrl.Parent; ctrlParent != null; ctrlParent = ctrlParent.Parent)
            if (ctrlParent is ScrollableControl { AutoScroll: true } sc)
                sc.ScrollControlIntoView(ctrl);
    }

    private void FlashControl(Control ctrl) => FlashControl(ctrl, Color.FromArgb(150, 150, 150), 300);

    //快速连按时先还原上一个 避免残留高亮色
    private async void FlashControl(Control ctrl, Color cFlashColor, int iDelayMs)
    {
        if (ctrlFlashTarget != null)
        {
            ctrlFlashTarget.BackColor = cFlashOldColor;
            ctsFlash?.Cancel();
            ctsFlash?.Dispose();
        }

        ctrlFlashTarget = ctrl;
        cFlashOldColor = ctrl.BackColor;
        ctsFlash = new CancellationTokenSource();
        var ctsCur = ctsFlash;
        var ctrlTarget = ctrl;
        var cOld = cFlashOldColor;

        ctrlTarget.BackColor = cFlashColor;
        try
        {
            await Task.Delay(iDelayMs, ctsCur.Token);
            if (ReferenceEquals(ctrlFlashTarget, ctrlTarget))
            {
                ctrlTarget.BackColor = cOld;
                ctrlFlashTarget = null;
            }
        }
        catch (TaskCanceledException) { }
    }
}