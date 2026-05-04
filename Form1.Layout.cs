using System.Drawing;
using System.Windows.Forms;

namespace WeaponDamageCalc;

public partial class Form1
{
    private Button btnSave = null!;
    private Button btnCsvToScripts = null!;
    private Button btnScriptsToCsv = null!;
    private Panel pnlSpread = null!;
    private Panel pnlRecoil = null!;
    private Label lblC64_1 = null!;
    private Label lblC64_2 = null!;
    private Label lblC64_3 = null!;

    private void InitC64Labels()
    {
        int cx = 525;
        lblC64_1 = new Label { Location = new Point(cx, 675), Size = new Size(300, 13), Font = new Font("Consolas", 8, FontStyle.Bold), ForeColor = Color.FromArgb(200, 200, 255), BackColor = Color.FromArgb(60, 60, 160), TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0), Padding = new Padding(0) };
        lblC64_2 = new Label { Location = new Point(cx, 686), Size = new Size(300, 13), Font = new Font("Consolas", 8, FontStyle.Bold), ForeColor = Color.FromArgb(200, 200, 255), BackColor = Color.FromArgb(60, 60, 160), TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0), Padding = new Padding(0) };
        lblC64_3 = new Label { Location = new Point(cx, 697), Size = new Size(300, 13), Font = new Font("Consolas", 8, FontStyle.Bold), ForeColor = Color.FromArgb(200, 200, 255), BackColor = Color.FromArgb(60, 60, 160), TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0), Padding = new Padding(0) };
        this.Controls.Add(lblC64_1);
        this.Controls.Add(lblC64_2);
        this.Controls.Add(lblC64_3);
        UpdateC64Labels(weapons.Count > 0);
    }

    private void UpdateC64Labels(bool hasData)
    {
        lblC64_1.Text = hasData ? "        **** COMMODORE 64 BASIC V2 ****" : "";
        lblC64_2.Text = hasData ? "     64K RAM SYSTEM  38911 BASIC BYTES FREE" : "";
        lblC64_3.Text = hasData ? "READY." : "";
    }

    private void CreateDamageMultiplierGroup(int x, bool isLeft)
    {
        var gb = new GroupBox { Text = "Damage Multiplier", Location = new Point(x, 38), Size = new Size(520, 215) };
        int y = 18;
        if (isLeft)
        {
            (trkHeadL, nudHeadL, lblHeadDmgL) = CreateSliderRow(gb, "Head", ref y, true);
            (trkChestL, nudChestL, lblChestDmgL) = CreateSliderRow(gb, "Chest", ref y, true);
            (trkStomachL, nudStomachL, lblStomachDmgL) = CreateSliderRow(gb, "Stomach", ref y, true);
            (trkLegL, nudLegL, lblLegDmgL) = CreateSliderRow(gb, "Leg", ref y, true);
            (trkArmL, nudArmL, lblArmDmgL) = CreateSliderRow(gb, "Arm", ref y, true);
        }
        else
        {
            (trkHeadR, nudHeadR, lblHeadDmgR) = CreateSliderRow(gb, "Head", ref y, false);
            (trkChestR, nudChestR, lblChestDmgR) = CreateSliderRow(gb, "Chest", ref y, false);
            (trkStomachR, nudStomachR, lblStomachDmgR) = CreateSliderRow(gb, "Stomach", ref y, false);
            (trkLegR, nudLegR, lblLegDmgR) = CreateSliderRow(gb, "Leg", ref y, false);
            (trkArmR, nudArmR, lblArmDmgR) = CreateSliderRow(gb, "Arm", ref y, false);
        }
        this.Controls.Add(gb);
    }

    private void CreateRangeGroup(int x, bool isLeft)
    {
        var gb = new GroupBox { Text = "Range", Location = new Point(x, 258), Size = new Size(520, 55) };
        gb.Controls.Add(new Label { Text = "0", Location = new Point(8, 20), Size = new Size(20, 18) });
        if (isLeft)
        {
            trkDistanceL = new TrackBar { Location = new Point(30, 16), Size = new Size(380, 35), Minimum = 0, Maximum = 100 };
            trkDistanceL.ValueChanged += (s, e) => { nudDistanceL.Value = trkDistanceL.Value; UpdateAllDamage(); };
            gb.Controls.Add(trkDistanceL);
            nudDistanceL = new NumericUpDown { Location = new Point(415, 16), Size = new Size(45, 22), DecimalPlaces = 0, Increment = 1, Minimum = 0, Maximum = 100 };
            nudDistanceL.ValueChanged += (s, e) => { trkDistanceL.Value = Math.Max(0, Math.Min(100, (int)nudDistanceL.Value)); UpdateAllDamage(); };            gb.Controls.Add(nudDistanceL);
            chkVestL = new CheckBox { Text = "Vest", Location = new Point(465, 18), Size = new Size(55, 22) };
            chkVestL.CheckedChanged += (s, e) => { UpdateAllDamage(); };
            gb.Controls.Add(chkVestL);
        }
        else
        {
            trkDistanceR = new TrackBar { Location = new Point(30, 16), Size = new Size(380, 35), Minimum = 0, Maximum = 100 };
            trkDistanceR.ValueChanged += (s, e) => { nudDistanceR.Value = trkDistanceR.Value; UpdateAllDamage(); };
            gb.Controls.Add(trkDistanceR);
            nudDistanceR = new NumericUpDown { Location = new Point(415, 16), Size = new Size(45, 22), DecimalPlaces = 0, Increment = 1, Minimum = 0, Maximum = 100 };
            nudDistanceR.ValueChanged += (s, e) => { trkDistanceR.Value = Math.Max(0, Math.Min(100, (int)nudDistanceR.Value)); UpdateAllDamage(); };
            gb.Controls.Add(nudDistanceR);
            chkVestR = new CheckBox { Text = "Vest", Location = new Point(465, 18), Size = new Size(55, 22) };
            chkVestR.CheckedChanged += (s, e) => { UpdateAllDamage(); };
            gb.Controls.Add(chkVestR);
        }
        this.Controls.Add(gb);
    }

    private void CreateSpreadRecoilAndPropertiesGroups(int x, bool isLeft)
    {
        var gbSpread = new GroupBox { Text = "Spread (°)", Location = new Point(x, 318), Size = new Size(175, 130) };
        int y = 20;
        if (isLeft)
        {
            nudHipSpreadL = CreateNullableNumericRow(gbSpread, "Hip", 8, y, 100m); nudHipSpreadL.ValueChanged += SpreadRecoilChangedL; y += 24;
            nudAdsSpreadL = CreateNullableNumericRow(gbSpread, "ADS", 8, y, 100m); nudAdsSpreadL.ValueChanged += SpreadRecoilChangedL; y += 24;
            nudBipodHipSpreadL = CreateNullableNumericRow(gbSpread, "Bipod Hip", 8, y, 100m); nudBipodHipSpreadL.ValueChanged += SpreadRecoilChangedL; y += 24;
            nudBipodAdsSpreadL = CreateNullableNumericRow(gbSpread, "Bipod ADS", 8, y, 100m); nudBipodAdsSpreadL.ValueChanged += SpreadRecoilChangedL;
        }
        else
        {
            nudHipSpreadR = CreateNullableNumericRow(gbSpread, "Hip", 8, y, 100m); nudHipSpreadR.ValueChanged += SpreadRecoilChangedR; y += 24;
            nudAdsSpreadR = CreateNullableNumericRow(gbSpread, "ADS", 8, y, 100m); nudAdsSpreadR.ValueChanged += SpreadRecoilChangedR; y += 24;
            nudBipodHipSpreadR = CreateNullableNumericRow(gbSpread, "Bipod Hip", 8, y, 100m); nudBipodHipSpreadR.ValueChanged += SpreadRecoilChangedR; y += 24;
            nudBipodAdsSpreadR = CreateNullableNumericRow(gbSpread, "Bipod ADS", 8, y, 100m); nudBipodAdsSpreadR.ValueChanged += SpreadRecoilChangedR;
        }
        this.Controls.Add(gbSpread);

        var gbRecoil = new GroupBox { Text = "Recoil (°)", Location = new Point(x + 180, 318), Size = new Size(175, 130) };
        y = 20;
        if (isLeft)
        {
            nudHipRecoilUpL = CreateNullableNumericRow(gbRecoil, "Hip Up", 8, y, 100m); nudHipRecoilUpL.ValueChanged += SpreadRecoilChangedL; y += 24;
            nudHipRecoilRightL = CreateNullableNumericRow(gbRecoil, "Hip Rt", 8, y, 100m); nudHipRecoilRightL.ValueChanged += SpreadRecoilChangedL; y += 24;
            nudAdsRecoilUpL = CreateNullableNumericRow(gbRecoil, "ADS Up", 8, y, 100m); nudAdsRecoilUpL.ValueChanged += SpreadRecoilChangedL; y += 24;
            nudAdsRecoilRightL = CreateNullableNumericRow(gbRecoil, "ADS Rt", 8, y, 100m); nudAdsRecoilRightL.ValueChanged += SpreadRecoilChangedL;
        }
        else
        {
            nudHipRecoilUpR = CreateNullableNumericRow(gbRecoil, "Hip Up", 8, y, 100m); nudHipRecoilUpR.ValueChanged += SpreadRecoilChangedR; y += 24;
            nudHipRecoilRightR = CreateNullableNumericRow(gbRecoil, "Hip Rt", 8, y, 100m); nudHipRecoilRightR.ValueChanged += SpreadRecoilChangedR; y += 24;
            nudAdsRecoilUpR = CreateNullableNumericRow(gbRecoil, "ADS Up", 8, y, 100m); nudAdsRecoilUpR.ValueChanged += SpreadRecoilChangedR; y += 24;
            nudAdsRecoilRightR = CreateNullableNumericRow(gbRecoil, "ADS Rt", 8, y, 100m); nudAdsRecoilRightR.ValueChanged += SpreadRecoilChangedR;
        }
        this.Controls.Add(gbRecoil);

        var gbProp = new GroupBox { Text = "Stats", Location = new Point(x + 360, 318), Size = new Size(160, 130) };
        y = 20;
        if (isLeft)
        {
            txtFireModesL = CreateTextBoxRow(gbProp, "Fire Mode", 8, y); y += 24;
            nudFireRateL = CreateNullableIntNumericRow(gbProp, "ROF", 8, y, 10000m); y += 24;
            nudRangeModifierL = CreateNullableNumericRow(gbProp, "Range Mod", 8, y, 10m);
            nudRangeModifierL.DecimalPlaces = 3; nudRangeModifierL.Increment = 0.001m; nudRangeModifierL.ValueChanged += RangeModifierChangedL; y += 24;
            txtCapacityL = CreateTextBoxRow(gbProp, "Capacity", 8, y);
        }
        else
        {
            txtFireModesR = CreateTextBoxRow(gbProp, "Fire Mode", 8, y); y += 24;
            nudFireRateR = CreateNullableIntNumericRow(gbProp, "ROF", 8, y, 10000m); y += 24;
            nudRangeModifierR = CreateNullableNumericRow(gbProp, "Range Mod", 8, y, 10m);
            nudRangeModifierR.DecimalPlaces = 3; nudRangeModifierR.Increment = 0.001m; nudRangeModifierR.ValueChanged += RangeModifierChangedR; y += 24;
            txtCapacityR = CreateTextBoxRow(gbProp, "Capacity", 8, y);
        }
        this.Controls.Add(gbProp);
    }

    private void CreateSpreadMultiplierGroup(int x, bool isLeft)
    {
        var gb = new GroupBox { Text = "Spread Multiplier", Location = new Point(x, 453), Size = new Size(520, 75) };
        int y = 20;
        if (isLeft)
        {
            nudCrouchSpreadL = CreateNullableNumericRow(gb, "Crouch", 8, y, 100m);
            nudProneSpreadL = CreateNullableNumericRow(gb, "Prone", 188, y, 100m);
            nudStandMoveSpreadL = CreateNullableNumericRow(gb, "Move", 368, y, 100m);
            y += 26;
            nudSneakMoveSpreadL = CreateNullableNumericRow(gb, "SneakMove", 8, y, 100m);
            nudCrouchMoveSpreadL = CreateNullableNumericRow(gb, "CrouchMove", 188, y, 100m);
            nudJumpSpreadL = CreateNullableNumericRow(gb, "Jump", 368, y, 100m);
        }
        else
        {
            nudCrouchSpreadR = CreateNullableNumericRow(gb, "Crouch", 8, y, 100m);
            nudProneSpreadR = CreateNullableNumericRow(gb, "Prone", 188, y, 100m);
            nudStandMoveSpreadR = CreateNullableNumericRow(gb, "Move", 368, y, 100m);
            y += 26;
            nudSneakMoveSpreadR = CreateNullableNumericRow(gb, "SneakMove", 8, y, 100m);
            nudCrouchMoveSpreadR = CreateNullableNumericRow(gb, "CrouchMove", 188, y, 100m);
            nudJumpSpreadR = CreateNullableNumericRow(gb, "Jump", 368, y, 100m);
        }
        this.Controls.Add(gb);
    }

    private void CreateOtherStatsGroup(int x, bool isLeft)
    {
        var gb = new GroupBox { Text = "Other Stats", Location = new Point(x, 533), Size = new Size(520, 180) };
        int y = 20;
        if (isLeft)
        {
            nudExtraBulletChamberL = CreateNullableIntNumericRow(gb, "Chamber", 8, y, 1000m);
            nudBulletsPerShotL = CreateNullableIntNumericRow(gb, "Pellets", 188, y, 100m);
            nudIronsightSpeedScaleL = CreateNullableNumericRow(gb, "ADS Spd", 368, y, 10m);
            y += 26;
            nudWeightL = CreateNullableNumericRow(gb, "Weight", 8, y, 100m);
            nudZMBuyPriceL = CreateNullableIntNumericRow(gb, "ZM Price", 188, y, 1000000m);
            nudZMWeightL = CreateNullableIntNumericRow(gb, "ZM Block", 368, y, 100m);
            y += 26;
            nudMetalPenL = CreateNullableNumericRow(gb, "Metal Pen", 8, y, 10000m);
            nudGlassPenL = CreateNullableNumericRow(gb, "Glass Pen", 188, y, 10000m);
            nudConcretePenL = CreateNullableNumericRow(gb, "Concr Pen", 368, y, 10000m);
            y += 26;
            nudWoodPenL = CreateNullableNumericRow(gb, "Wood Pen", 8, y, 10000m);
            nudOtherPenL = CreateNullableNumericRow(gb, "Other Pen", 188, y, 10000m);
            nudConcreteDmgModL = CreateNullableNumericRow(gb, "Concr Mod", 368, y, 100m);
            y += 26;
            nudMetalDmgModL = CreateNullableNumericRow(gb, "Metal Mod", 8, y, 100m);
            nudGlassDmgModL = CreateNullableNumericRow(gb, "Glass Mod", 188, y, 100m);
            nudWoodDmgModL = CreateNullableNumericRow(gb, "Wood Mod", 368, y, 100m);
            y += 26;
            nudOtherDmgModL = CreateNullableNumericRow(gb, "Other Mod", 8, y, 100m);
        }
        else
        {
            nudExtraBulletChamberR = CreateNullableIntNumericRow(gb, "Chamber", 8, y, 1000m);
            nudBulletsPerShotR = CreateNullableIntNumericRow(gb, "Pellets", 188, y, 100m);
            nudIronsightSpeedScaleR = CreateNullableNumericRow(gb, "ADS Spd", 368, y, 10m);
            y += 26;
            nudWeightR = CreateNullableNumericRow(gb, "Weight", 8, y, 100m);
            nudZMBuyPriceR = CreateNullableIntNumericRow(gb, "ZM Price", 188, y, 1000000m);
            nudZMWeightR = CreateNullableIntNumericRow(gb, "ZM Block", 368, y, 100m);
            y += 26;
            nudMetalPenR = CreateNullableNumericRow(gb, "Metal Pen", 8, y, 10000m);
            nudGlassPenR = CreateNullableNumericRow(gb, "Glass Pen", 188, y, 10000m);
            nudConcretePenR = CreateNullableNumericRow(gb, "Concr Pen", 368, y, 10000m);
            y += 26;
            nudWoodPenR = CreateNullableNumericRow(gb, "Wood Pen", 8, y, 10000m);
            nudOtherPenR = CreateNullableNumericRow(gb, "Other Pen", 188, y, 10000m);
            nudConcreteDmgModR = CreateNullableNumericRow(gb, "Concr Mod", 368, y, 100m);
            y += 26;
            nudMetalDmgModR = CreateNullableNumericRow(gb, "Metal Mod", 8, y, 100m);
            nudGlassDmgModR = CreateNullableNumericRow(gb, "Glass Mod", 188, y, 100m);
            nudWoodDmgModR = CreateNullableNumericRow(gb, "Wood Mod", 368, y, 100m);
            y += 26;
            nudOtherDmgModR = CreateNullableNumericRow(gb, "Other Mod", 8, y, 100m);
        }
        this.Controls.Add(gb);
    }

    private (TrackBar, NumericUpDown, Label) CreateSliderRow(Control parent, string text, ref int y, bool isLeft)
    {
        parent.Controls.Add(new Label { Text = text, Location = new Point(8, y + 8), Size = new Size(35, 18), TextAlign = ContentAlignment.MiddleLeft });
        var tb = new TrackBar { Location = new Point(45, y + 2), Size = new Size(270, 34), Minimum = (int)(SliderMin / SliderStep), Maximum = (int)(SliderMax / SliderStep), TickFrequency = (int)(0.5 / SliderStep), Value = (int)(1.0 / SliderStep) };
        var nud = new NumericUpDown { Location = new Point(320, y + 7), Size = new Size(55, 22), DecimalPlaces = 2, Increment = 0.01m, Minimum = (decimal)SliderMin, Maximum = 1000m, Value = 1.00m };
        var lbl = new Label { Text = "= 0.0 | ∞shots | ∞ms", Location = new Point(380, y + 9), AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.DarkRed, Font = new Font("Arial", 8, FontStyle.Bold) };
        parent.Controls.Add(tb);
        parent.Controls.Add(nud);
        parent.Controls.Add(lbl);

        //用Tag互相引用 事件处理时通过sender.Tag找到对方控件同步值
        tb.Tag = nud;
        nud.Tag = tb;
        if (isLeft)
        {
            tb.ValueChanged += SliderChangedL;
            nud.ValueChanged += NumericChangedL;
        }
        else
        {
            tb.ValueChanged += SliderChangedR;
            nud.ValueChanged += NumericChangedR;
        }

        y += 37;
        return (tb, nud, lbl);
    }

    private NumericUpDown CreateNullableNumericRow(Control parent, string text, int x, int y, decimal max)
    {
        parent.Controls.Add(new Label { Text = text, Location = new Point(x, y + 3), Size = new Size(70, 18), TextAlign = ContentAlignment.MiddleLeft });
        var nud = new NumericUpDown { Location = new Point(x + 72, y + 1), Size = new Size(65, 22), DecimalPlaces = 2, Increment = 0.01m, Minimum = 0m, Maximum = max };
        parent.Controls.Add(nud);
        return nud;
    }

    private NumericUpDown CreateNullableIntNumericRow(Control parent, string text, int x, int y, decimal max)
    {
        parent.Controls.Add(new Label { Text = text, Location = new Point(x, y + 3), Size = new Size(70, 18), TextAlign = ContentAlignment.MiddleLeft });
        var nud = new NumericUpDown { Location = new Point(x + 72, y + 1), Size = new Size(65, 22), DecimalPlaces = 0, Increment = 1, Minimum = 0m, Maximum = max };
        parent.Controls.Add(nud);
        return nud;
    }

    private TextBox CreateTextBoxRow(Control parent, string text, int x, int y)
    {
        parent.Controls.Add(new Label { Text = text, Location = new Point(x, y + 3), Size = new Size(70, 18), TextAlign = ContentAlignment.MiddleLeft });
        var tb = new TextBox { Location = new Point(x + 72, y + 1), Size = new Size(65, 22) };
        parent.Controls.Add(tb);
        return tb;
    }
}