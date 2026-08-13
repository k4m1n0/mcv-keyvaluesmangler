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

    private void NavigateFocus(Keys dir)
    {
        var cur = ActiveControl;
        if (cur == null) return;

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
        var curC = CenterInForm(cur);
        bool vertical = dir is Keys.Up or Keys.Down;
        double axisThreshold = vertical ? cur.Width / 2.0 : cur.Height / 2.0;
        const double mainThreshold = 12;//低于此距离视为同轴抖动

        Control? bestSame = null;
        double bestSameDist = double.MaxValue;
        Control? bestAny = null;
        double bestAnyDist = double.MaxValue;

        foreach (var c in GetNavigableControls())
        {
            if (c == cur) continue;
            var cc = CenterInForm(c);
            double dx = cc.X - curC.X;
            double dy = cc.Y - curC.Y;

            if (dir == Keys.Right && dx <= mainThreshold) continue;
            if (dir == Keys.Left && dx >= -mainThreshold) continue;
            if (dir == Keys.Down && dy <= mainThreshold) continue;
            if (dir == Keys.Up && dy >= -mainThreshold) continue;

            double main = vertical ? Math.Abs(dy) : Math.Abs(dx);
            double cross = vertical ? Math.Abs(dx) : Math.Abs(dy);

            if (cross <= axisThreshold)
            {
                if (main < bestSameDist) { bestSameDist = main; bestSame = c; }
            }
            else if (main * 1.5 + cross < bestAnyDist)
            {
                bestAnyDist = main * 1.5 + cross; bestAny = c;
            }
        }

        return bestSame ?? bestAny;
    }

    private IEnumerable<Control> GetNavigableControls()
    {
        foreach (var c in GetAllDescendants(this))
            if (c.CanSelect && c.TabStop && c.Visible && c.Enabled)
                yield return c;
    }

    private Point CenterInForm(Control c)
    {
        var screen = c.PointToScreen(new Point(c.Width / 2, c.Height / 2));
        return PointToClient(screen);
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
