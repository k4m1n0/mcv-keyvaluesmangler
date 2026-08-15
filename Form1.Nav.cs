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
    private CancellationTokenSource? _flashCts;
    private Control? _flashTarget;
    private Color _flashOldColor;
    private const double MaxHorizontalDistance = 460;
    private const double MaxVerticalDistance = 620;
    private const double CrossAxisPenalty = 1.5;//跨轴距离惩罚系数
    private const double SameAxisFarThreshold = 60;//bestSame超过此距离时考虑bestAny

    private void NavigateFocus(Keys dir)
    {
        var cur = ActiveControl;
        if (cur == null) return;

        if (cur is TextBox && cur.Parent is NumericUpDown nud)
        {
            cur = nud;
        }

        var target = FindNearest(cur, dir);
        if (target == null) return;

        LogService.Debug($"Nav: {cur.Name ?? cur.GetType().Name} -> {target.Name ?? target.GetType().Name} ({dir})");

        target.Focus();
        ScrollToVisible(target);
        FlashControl(target);
    }

    //优先保持行/列不动 跨轴控件只在同轴缺失时才作为兜底
    private Control? FindNearest(Control cur, Keys dir)
    {
        var curBounds = BoundsInForm(cur);
        bool vertical = dir is Keys.Up or Keys.Down;

        Control? bestSame = null;
        double bestSameDist = double.MaxValue;
        Control? bestAny = null;
        double bestAnyDist = double.MaxValue;
        bool curIsButton = cur is Button;

        foreach (var c in GetNavigableControls())
        {
            if (c == cur) continue;
            var cBounds = BoundsInForm(c);

            if (cur.Parent == c || c.Parent == cur)
            {
                continue;
            }

            double dx, dy;
            if (dir == Keys.Right)
                dx = cBounds.Left - curBounds.Right;
            else if (dir == Keys.Left)
                dx = curBounds.Left - cBounds.Right;
            else
                dx = cBounds.Left + cBounds.Width / 2.0 - (curBounds.Left + curBounds.Width / 2.0);

            if (dir == Keys.Down)
                dy = cBounds.Top - curBounds.Bottom;
            else if (dir == Keys.Up)
                dy = curBounds.Top - cBounds.Bottom;
            else
                dy = cBounds.Top + cBounds.Height / 2.0 - (curBounds.Top + curBounds.Height / 2.0);

            const double overlapTolerance = 10;
            if (dir == Keys.Right && dx < -overlapTolerance) continue;
            if (dir == Keys.Left && dx < -overlapTolerance) continue;
            if (dir == Keys.Down && dy < -overlapTolerance) continue;
            if (dir == Keys.Up && dy < -overlapTolerance) continue;

            double main = vertical ? Math.Abs(dy) : Math.Abs(dx);
            double cross = vertical ? Math.Abs(dx) : Math.Abs(dy);

            if (vertical && Math.Abs(dy) > MaxVerticalDistance) continue;
            if (!vertical && Math.Abs(dx) > MaxHorizontalDistance) continue;

            if (curIsButton && c is not Button && vertical)
            {
                main *= 10.0;
            }

            if (curIsButton && c is Button && vertical)
            {
                main *= 0.3;
            }

            bool sameAxis;
            if (vertical)
            {
                sameAxis = !(cBounds.Right < curBounds.Left || cBounds.Left > curBounds.Right);
            }
            else
            {
                sameAxis = !(cBounds.Bottom < curBounds.Top || cBounds.Top > curBounds.Bottom);
            }

            if (sameAxis)
            {
                if (main < bestSameDist) { bestSameDist = main; bestSame = c; }
            }
            else
            {
                if (!vertical && Math.Abs(dy) > Math.Max(curBounds.Height, cBounds.Height) * 2.0)
                {
                    continue;
                }
                if (vertical && Math.Abs(dx) > Math.Max(curBounds.Width, cBounds.Width) * 2.0)
                {
                    continue;
                }
                double score = main * CrossAxisPenalty + cross;
                if (score < bestAnyDist)
                {
                    bestAnyDist = score; bestAny = c;
                }
            }
        }

        if (bestSame != null && bestAny != null && bestAnyDist < bestSameDist * 0.5)
        {
            return bestAny;
        }
        return bestSame ?? bestAny;
    }

    private IEnumerable<Control> GetNavigableControls()
    {
        foreach (var c in GetAllDescendants(this))
            if (c.CanSelect && c.TabStop && c.Visible && c.Enabled && c is not (TextBox and { Parent: NumericUpDown }))
                yield return c;
    }

    private Rectangle BoundsInForm(Control c)
    {
        var tl = c.PointToScreen(Point.Empty);
        var br = c.PointToScreen(new Point(c.Width, c.Height));
        var tlClient = PointToClient(tl);
        var brClient = PointToClient(br);
        return new Rectangle(tlClient.X, tlClient.Y, brClient.X - tlClient.X, brClient.Y - tlClient.Y);
    }

    private static void ScrollToVisible(Control c)
    {
        for (var p = c.Parent; p != null; p = p.Parent)
            if (p is ScrollableControl { AutoScroll: true } sc)
                sc.ScrollControlIntoView(c);
    }

    //快速连按时先还原上一个 避免残留高亮色
    private async void FlashControl(Control c)
    {
        if (_flashTarget != null)
        {
            _flashTarget.BackColor = _flashOldColor;
            _flashCts?.Cancel();
            _flashCts?.Dispose();
        }

        _flashTarget = c;
        _flashOldColor = c.BackColor;
        _flashCts = new CancellationTokenSource();
        var cts = _flashCts;
        var target = c;
        var old = _flashOldColor;

        target.BackColor = Color.FromArgb(150, 150, 150);
        try
        {
            await Task.Delay(300, cts.Token);
            if (ReferenceEquals(_flashTarget, target))
            {
                target.BackColor = old;
                _flashTarget = null;
            }
        }
        catch (TaskCanceledException) { }
    }
}