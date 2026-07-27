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

        var tadaPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "tada.wav");
        System.Media.SoundPlayer? tada = File.Exists(tadaPath) ? new System.Media.SoundPlayer(tadaPath) : null;
        void PlayTada() { try { tada?.Play(); } catch { } }
        lblC64_1.Click += (_, _) => PlayTada();
        lblC64_2.Click += (_, _) => PlayTada();
        lblC64_3.Click += (_, _) => PlayTada();

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

    #region 伤害倍率和衰减

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
            trkDistanceL.ValueChanged += (s, e) => { ScheduleUndo(); nudDistanceL.Value = trkDistanceL.Value; UpdateAllDamage(); };
            trkDistanceL.MouseUp += (_, _) => PushUndoNow();
            gb.Controls.Add(trkDistanceL);
            nudDistanceL = new NumericUpDown { Location = new Point(415, 16), Size = new Size(45, 22), DecimalPlaces = 0, Increment = 1, Minimum = 0, Maximum = 100 };
            nudDistanceL.ValueChanged += (s, e) => { ScheduleUndo(); trkDistanceL.Value = Math.Max(0, Math.Min(100, (int)nudDistanceL.Value)); UpdateAllDamage(); };
            nudDistanceL.MouseUp += (_, _) => PushUndoNow();
            gb.Controls.Add(nudDistanceL);
            chkVestL = new CheckBox { Text = "Vest", Location = new Point(465, 18), Size = new Size(55, 22) };
            chkVestL.CheckedChanged += (s, e) => { ScheduleUndo(); UpdateAllDamage(); };
            gb.Controls.Add(chkVestL);
        }
        else
        {
            trkDistanceR = new TrackBar { Location = new Point(30, 16), Size = new Size(380, 35), Minimum = 0, Maximum = 100 };
            trkDistanceR.ValueChanged += (s, e) => { ScheduleUndo(); nudDistanceR.Value = trkDistanceR.Value; UpdateAllDamage(); };
            trkDistanceR.MouseUp += (_, _) => PushUndoNow();
            gb.Controls.Add(trkDistanceR);
            nudDistanceR = new NumericUpDown { Location = new Point(415, 16), Size = new Size(45, 22), DecimalPlaces = 0, Increment = 1, Minimum = 0, Maximum = 100 };
            nudDistanceR.ValueChanged += (s, e) => { ScheduleUndo(); trkDistanceR.Value = Math.Max(0, Math.Min(100, (int)nudDistanceR.Value)); UpdateAllDamage(); };
            nudDistanceR.MouseUp += (_, _) => PushUndoNow();
            gb.Controls.Add(nudDistanceR);
            chkVestR = new CheckBox { Text = "Vest", Location = new Point(465, 18), Size = new Size(55, 22) };
            chkVestR.CheckedChanged += (s, e) => { ScheduleUndo(); UpdateAllDamage(); };
            gb.Controls.Add(chkVestR);
        }
        this.Controls.Add(gb);
    }

    #endregion
    #region 散布后座主属性

    private void CreateSpreadRecoilAndPropertiesGroups(int x, bool isLeft)
    {
        var gbSpread = new GroupBox { Text = "Spread (°)", Location = new Point(x, 318), Size = new Size(175, 130) };
        int y = 20;
        if (isLeft)
        {
            nudHipSpreadL = CreateNullableNumericRow(gbSpread, "Hip", 8, y, 100m); BindNudUndo(nudHipSpreadL, SpreadRecoilChangedL, isLeft); y += 24;
            nudAdsSpreadL = CreateNullableNumericRow(gbSpread, "ADS", 8, y, 100m); BindNudUndo(nudAdsSpreadL, SpreadRecoilChangedL, isLeft); y += 24;
            nudBipodHipSpreadL = CreateNullableNumericRow(gbSpread, "Bipod Hip", 8, y, 100m); BindNudUndo(nudBipodHipSpreadL, SpreadRecoilChangedL, isLeft); y += 24;
            nudBipodAdsSpreadL = CreateNullableNumericRow(gbSpread, "Bipod ADS", 8, y, 100m); BindNudUndo(nudBipodAdsSpreadL, SpreadRecoilChangedL, isLeft);
        }
        else
        {
            nudHipSpreadR = CreateNullableNumericRow(gbSpread, "Hip", 8, y, 100m); BindNudUndo(nudHipSpreadR, SpreadRecoilChangedR, isLeft); y += 24;
            nudAdsSpreadR = CreateNullableNumericRow(gbSpread, "ADS", 8, y, 100m); BindNudUndo(nudAdsSpreadR, SpreadRecoilChangedR, isLeft); y += 24;
            nudBipodHipSpreadR = CreateNullableNumericRow(gbSpread, "Bipod Hip", 8, y, 100m); BindNudUndo(nudBipodHipSpreadR, SpreadRecoilChangedR, isLeft); y += 24;
            nudBipodAdsSpreadR = CreateNullableNumericRow(gbSpread, "Bipod ADS", 8, y, 100m); BindNudUndo(nudBipodAdsSpreadR, SpreadRecoilChangedR, isLeft);
        }
        this.Controls.Add(gbSpread);

        var gbRecoil = new GroupBox { Text = "Recoil (°)", Location = new Point(x + 180, 318), Size = new Size(175, 130) };
        y = 20;
        if (isLeft)
        {
            nudHipRecoilUpL = CreateNullableNumericRow(gbRecoil, "Hip Up", 8, y, 100m); BindNudUndo(nudHipRecoilUpL, SpreadRecoilChangedL, isLeft); y += 24;
            nudHipRecoilRightL = CreateNullableNumericRow(gbRecoil, "Hip Rt", 8, y, 100m); BindNudUndo(nudHipRecoilRightL, SpreadRecoilChangedL, isLeft); y += 24;
            nudAdsRecoilUpL = CreateNullableNumericRow(gbRecoil, "ADS Up", 8, y, 100m); BindNudUndo(nudAdsRecoilUpL, SpreadRecoilChangedL, isLeft); y += 24;
            nudAdsRecoilRightL = CreateNullableNumericRow(gbRecoil, "ADS Rt", 8, y, 100m); BindNudUndo(nudAdsRecoilRightL, SpreadRecoilChangedL, isLeft);
        }
        else
        {
            nudHipRecoilUpR = CreateNullableNumericRow(gbRecoil, "Hip Up", 8, y, 100m); BindNudUndo(nudHipRecoilUpR, SpreadRecoilChangedR, isLeft); y += 24;
            nudHipRecoilRightR = CreateNullableNumericRow(gbRecoil, "Hip Rt", 8, y, 100m); BindNudUndo(nudHipRecoilRightR, SpreadRecoilChangedR, isLeft); y += 24;
            nudAdsRecoilUpR = CreateNullableNumericRow(gbRecoil, "ADS Up", 8, y, 100m); BindNudUndo(nudAdsRecoilUpR, SpreadRecoilChangedR, isLeft); y += 24;
            nudAdsRecoilRightR = CreateNullableNumericRow(gbRecoil, "ADS Rt", 8, y, 100m); BindNudUndo(nudAdsRecoilRightR, SpreadRecoilChangedR, isLeft);
        }
        this.Controls.Add(gbRecoil);

        var gbProp = new GroupBox { Text = "Stats", Location = new Point(x + 360, 318), Size = new Size(160, 130) };
        y = 20;
        if (isLeft)
        {
            txtFireModesL = CreateTextBoxRow(gbProp, "Fire Mode", 8, y);
            txtFireModesL.TextChanged += (s, e) => { if (!updatingControls) { ScheduleUndo(); UpdateAllDamage(); } };
            y += 24;
            nudFireRateL = CreateNullableIntNumericRow(gbProp, "ROF", 8, y, 10000m);
            BindNudUndo(nudFireRateL, (s, e) => UpdateAllDamage(), isLeft);
            y += 24;
            nudRangeModifierL = CreateNullableNumericRow(gbProp, "Range Mod", 8, y, 10m);
            nudRangeModifierL.DecimalPlaces = 3; nudRangeModifierL.Increment = 0.001m;
            BindNudUndo(nudRangeModifierL, (s, e) => { RangeModifierChangedL(s, e); }, isLeft);
            y += 24;
            txtCapacityL = CreateTextBoxRow(gbProp, "Capacity", 8, y);
            txtCapacityL.TextChanged += (s, e) => { if (!updatingControls) ScheduleUndo(); };
        }
        else
        {
            txtFireModesR = CreateTextBoxRow(gbProp, "Fire Mode", 8, y);
            txtFireModesR.TextChanged += (s, e) => { if (!updatingControls) { ScheduleUndo(); UpdateAllDamage(); } };
            y += 24;
            nudFireRateR = CreateNullableIntNumericRow(gbProp, "ROF", 8, y, 10000m);
            BindNudUndo(nudFireRateR, (s, e) => UpdateAllDamage(), isLeft);
            y += 24;
            nudRangeModifierR = CreateNullableNumericRow(gbProp, "Range Mod", 8, y, 10m);
            nudRangeModifierR.DecimalPlaces = 3; nudRangeModifierR.Increment = 0.001m;
            BindNudUndo(nudRangeModifierR, (s, e) => { RangeModifierChangedR(s, e); }, isLeft);
            y += 24;
            txtCapacityR = CreateTextBoxRow(gbProp, "Capacity", 8, y);
            txtCapacityR.TextChanged += (s, e) => { if (!updatingControls) ScheduleUndo(); };
        }
        this.Controls.Add(gbProp);
    }

    #endregion
    #region 散布倍率

    private void CreateSpreadMultiplierGroup(int x, bool isLeft)
    {
        var gb = new GroupBox { Text = "Spread Multiplier", Location = new Point(x, 453), Size = new Size(520, 75) };
        int y = 20;
        if (isLeft)
        {
            nudCrouchSpreadL = CreateNullableNumericRow(gb, "Duck", 8, y, 100m); BindNudUndo(nudCrouchSpreadL, null, isLeft);
            nudProneSpreadL = CreateNullableNumericRow(gb, "Prone", 188, y, 100m); BindNudUndo(nudProneSpreadL, null, isLeft);
            nudStandMoveSpreadL = CreateNullableNumericRow(gb, "Move", 368, y, 100m); BindNudUndo(nudStandMoveSpreadL, null, isLeft);
            y += 26;
            nudSneakMoveSpreadL = CreateNullableNumericRow(gb, "SneakMov", 8, y, 100m); BindNudUndo(nudSneakMoveSpreadL, null, isLeft);
            nudCrouchMoveSpreadL = CreateNullableNumericRow(gb, "DuckMov", 188, y, 100m); BindNudUndo(nudCrouchMoveSpreadL, null, isLeft);
            nudJumpSpreadL = CreateNullableNumericRow(gb, "Jump", 368, y, 100m); BindNudUndo(nudJumpSpreadL, null, isLeft);
        }
        else
        {
            nudCrouchSpreadR = CreateNullableNumericRow(gb, "Duck", 8, y, 100m); BindNudUndo(nudCrouchSpreadR, null, isLeft);
            nudProneSpreadR = CreateNullableNumericRow(gb, "Prone", 188, y, 100m); BindNudUndo(nudProneSpreadR, null, isLeft);
            nudStandMoveSpreadR = CreateNullableNumericRow(gb, "Move", 368, y, 100m); BindNudUndo(nudStandMoveSpreadR, null, isLeft);
            y += 26;
            nudSneakMoveSpreadR = CreateNullableNumericRow(gb, "SneakMov", 8, y, 100m); BindNudUndo(nudSneakMoveSpreadR, null, isLeft);
            nudCrouchMoveSpreadR = CreateNullableNumericRow(gb, "DuckMov", 188, y, 100m); BindNudUndo(nudCrouchMoveSpreadR, null, isLeft);
            nudJumpSpreadR = CreateNullableNumericRow(gb, "Jump", 368, y, 100m); BindNudUndo(nudJumpSpreadR, null, isLeft);
        }
        this.Controls.Add(gb);
    }

    #endregion
    #region 其它属性

    private void CreateOtherStatsGroup(int x, bool isLeft)
    {
        var gb = new GroupBox { Text = "Other Stats", Location = new Point(x, 533), Size = new Size(520, 180) };
        int y = 20;
        if (isLeft)
        {
            nudExtraBulletChamberL = CreateNullableIntNumericRow(gb, "Chamber", 8, y, 1000m); BindNudUndo(nudExtraBulletChamberL, null, isLeft);
            nudBulletsPerShotL = CreateNullableIntNumericRow(gb, "Pellets", 188, y, 100m);
            nudBulletsPerShotL.ValueChanged += (s, e) => { ScheduleUndo(); UpdateAllDamage(); };
            nudBulletsPerShotL.MouseUp += (_, _) => PushUndoNow();
            nudIronsightSpeedScaleL = CreateNullableNumericRow(gb, "ADS Spd", 368, y, 10m); BindNudUndo(nudIronsightSpeedScaleL, null, isLeft);
            y += 26;
            nudWeightL = CreateNullableNumericRow(gb, "Weight", 8, y, 100m); BindNudUndo(nudWeightL, null, isLeft);
            nudZMBuyPriceL = CreateNullableIntNumericRow(gb, "ZM Price", 188, y, 1000000m); BindNudUndo(nudZMBuyPriceL, null, isLeft);
            nudZMWeightL = CreateNullableIntNumericRow(gb, "ZM Block", 368, y, 100m); BindNudUndo(nudZMWeightL, null, isLeft);
            y += 26;
            nudMetalPenL = CreateNullableNumericRow(gb, "MetalPen", 8, y, 10000m); BindNudUndo(nudMetalPenL, null, isLeft);
            nudGlassPenL = CreateNullableNumericRow(gb, "GlassPen", 188, y, 10000m); BindNudUndo(nudGlassPenL, null, isLeft);
            nudConcretePenL = CreateNullableNumericRow(gb, "ConcrPen", 368, y, 10000m); BindNudUndo(nudConcretePenL, null, isLeft);
            y += 26;
            nudWoodPenL = CreateNullableNumericRow(gb, "WoodPen", 8, y, 10000m); BindNudUndo(nudWoodPenL, null, isLeft);
            nudOtherPenL = CreateNullableNumericRow(gb, "OtherPen", 188, y, 10000m); BindNudUndo(nudOtherPenL, null, isLeft);
            nudConcreteDmgModL = CreateNullableNumericRow(gb, "ConcrMod", 368, y, 100m); BindNudUndo(nudConcreteDmgModL, null, isLeft);
            y += 26;
            nudMetalDmgModL = CreateNullableNumericRow(gb, "MetalMod", 8, y, 100m); BindNudUndo(nudMetalDmgModL, null, isLeft);
            nudGlassDmgModL = CreateNullableNumericRow(gb, "GlassMod", 188, y, 100m); BindNudUndo(nudGlassDmgModL, null, isLeft);
            nudWoodDmgModL = CreateNullableNumericRow(gb, "WoodMod", 368, y, 100m); BindNudUndo(nudWoodDmgModL, null, isLeft);
            y += 26;
            nudOtherDmgModL = CreateNullableNumericRow(gb, "OtherMod", 8, y, 100m); BindNudUndo(nudOtherDmgModL, null, isLeft);
            nudSecondaryFireRateL = CreateNullableIntNumericRow(gb, "2ndROF", 188, y, 10000m);
            nudSecondaryFireRateL.Minimum = -1m;
            nudSecondaryFireRateL.ValueChanged += (s, e) => { ScheduleUndo(); UpdateAllDamage(); };
            nudSecondaryFireRateL.MouseUp += (_, _) => PushUndoNow();
            nudSecondaryFireRateL.Enter += (s, e) => { UpdateAllDamage(); };
            nudSecondaryFireRateL.Leave += (s, e) => { UpdateAllDamage(); };
            nudIronSightL = CreateNullableIntNumericRow(gb, "IronSight", 368, y, 1m);
            nudIronSightL.ValueChanged += (s, e) =>
            {
                ScheduleUndo();
                bool noIronsight = nudIronSightL.Value == 0;
                nudAdsSpreadL.Enabled = !noIronsight;
                nudAdsRecoilUpL.Enabled = !noIronsight;
                nudAdsRecoilRightL.Enabled = !noIronsight;
                nudIronsightSpeedScaleL.Enabled = !noIronsight;
                pnlSpread.Invalidate();
                pnlRecoil.Invalidate();
            };
            nudIronSightL.MouseUp += (_, _) => PushUndoNow();
        }
        else
        {
            nudExtraBulletChamberR = CreateNullableIntNumericRow(gb, "Chamber", 8, y, 1000m); BindNudUndo(nudExtraBulletChamberR, null, isLeft);
            nudBulletsPerShotR = CreateNullableIntNumericRow(gb, "Pellets", 188, y, 100m);
            nudBulletsPerShotR.ValueChanged += (s, e) => { ScheduleUndo(); UpdateAllDamage(); };
            nudBulletsPerShotR.MouseUp += (_, _) => PushUndoNow();
            nudIronsightSpeedScaleR = CreateNullableNumericRow(gb, "ADS Spd", 368, y, 10m); BindNudUndo(nudIronsightSpeedScaleR, null, isLeft);
            y += 26;
            nudWeightR = CreateNullableNumericRow(gb, "Weight", 8, y, 100m); BindNudUndo(nudWeightR, null, isLeft);
            nudZMBuyPriceR = CreateNullableIntNumericRow(gb, "ZM Price", 188, y, 1000000m); BindNudUndo(nudZMBuyPriceR, null, isLeft);
            nudZMWeightR = CreateNullableIntNumericRow(gb, "ZM Block", 368, y, 100m); BindNudUndo(nudZMWeightR, null, isLeft);
            y += 26;
            nudMetalPenR = CreateNullableNumericRow(gb, "MetalPen", 8, y, 10000m); BindNudUndo(nudMetalPenR, null, isLeft);
            nudGlassPenR = CreateNullableNumericRow(gb, "GlassPen", 188, y, 10000m); BindNudUndo(nudGlassPenR, null, isLeft);
            nudConcretePenR = CreateNullableNumericRow(gb, "ConcrPen", 368, y, 10000m); BindNudUndo(nudConcretePenR, null, isLeft);
            y += 26;
            nudWoodPenR = CreateNullableNumericRow(gb, "WoodPen", 8, y, 10000m); BindNudUndo(nudWoodPenR, null, isLeft);
            nudOtherPenR = CreateNullableNumericRow(gb, "OtherPen", 188, y, 10000m); BindNudUndo(nudOtherPenR, null, isLeft);
            nudConcreteDmgModR = CreateNullableNumericRow(gb, "ConcrMod", 368, y, 100m); BindNudUndo(nudConcreteDmgModR, null, isLeft);
            y += 26;
            nudMetalDmgModR = CreateNullableNumericRow(gb, "MetalMod", 8, y, 100m); BindNudUndo(nudMetalDmgModR, null, isLeft);
            nudGlassDmgModR = CreateNullableNumericRow(gb, "GlassMod", 188, y, 100m); BindNudUndo(nudGlassDmgModR, null, isLeft);
            nudWoodDmgModR = CreateNullableNumericRow(gb, "WoodMod", 368, y, 100m); BindNudUndo(nudWoodDmgModR, null, isLeft);
            y += 26;
            nudOtherDmgModR = CreateNullableNumericRow(gb, "OtherMod", 8, y, 100m); BindNudUndo(nudOtherDmgModR, null, isLeft);
            nudSecondaryFireRateR = CreateNullableIntNumericRow(gb, "2ndROF", 188, y, 10000m);
            nudSecondaryFireRateR.Minimum = -1m;
            nudSecondaryFireRateR.ValueChanged += (s, e) => { ScheduleUndo(); UpdateAllDamage(); };
            nudSecondaryFireRateR.MouseUp += (_, _) => PushUndoNow();
            nudSecondaryFireRateR.Enter += (s, e) => { UpdateAllDamage(); };
            nudSecondaryFireRateR.Leave += (s, e) => { UpdateAllDamage(); };
            nudIronSightR = CreateNullableIntNumericRow(gb, "IronSight", 368, y, 1m);
            nudIronSightR.ValueChanged += (s, e) =>
            {
                ScheduleUndo();
                bool noIronsight = nudIronSightR.Value == 0;
                nudAdsSpreadR.Enabled = !noIronsight;
                nudAdsRecoilUpR.Enabled = !noIronsight;
                nudAdsRecoilRightR.Enabled = !noIronsight;
                nudIronsightSpeedScaleR.Enabled = !noIronsight;
                pnlSpread.Invalidate();
                pnlRecoil.Invalidate();
            };
            nudIronSightR.MouseUp += (_, _) => PushUndoNow();
        }
        this.Controls.Add(gb);
    }

    #endregion
    #region 控件工厂

    private (TrackBar, NumericUpDown, Label) CreateSliderRow(Control parent, string text, ref int y, bool isLeft)
    {
        parent.Controls.Add(new Label { Text = text, Location = new Point(8, y + 8), Size = new Size(35, 18), TextAlign = ContentAlignment.MiddleLeft });
        var tb = new TrackBar { Location = new Point(45, y + 2), Size = new Size(270, 34), Minimum = (int)(SliderMin / SliderStep), Maximum = (int)(SliderMax / SliderStep), TickFrequency = (int)(0.5 / SliderStep), Value = (int)(1.0 / SliderStep) };
        var nud = new NumericUpDown { Location = new Point(320, y + 7), Size = new Size(55, 22), DecimalPlaces = 2, Increment = 0.01m, Minimum = (decimal)SliderMin, Maximum = 7.5m, Value = 1.00m };
        var lbl = new Label { Text = "= 0.0 | ∞shots | ∞ms", Location = new Point(380, y + 9), AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.DarkRed, Font = new Font("Arial", 8, FontStyle.Bold) };
        parent.Controls.Add(tb);
        parent.Controls.Add(nud);
        parent.Controls.Add(lbl);

        tb.Tag = nud;
        nud.Tag = tb;
        if (isLeft)
        {
            tb.ValueChanged += (s, e) => { SliderChangedL(s, e); ScheduleUndo(); };
            tb.MouseUp += (_, _) => PushUndoNow();
            nud.ValueChanged += (s, e) => { NumericChangedL(s, e); ScheduleUndo(); };
            nud.MouseUp += (_, _) => PushUndoNow();
        }
        else
        {
            tb.ValueChanged += (s, e) => { SliderChangedR(s, e); ScheduleUndo(); };
            tb.MouseUp += (_, _) => PushUndoNow();
            nud.ValueChanged += (s, e) => { NumericChangedR(s, e); ScheduleUndo(); };
            nud.MouseUp += (_, _) => PushUndoNow();
        }

        y += 37;
        return (tb, nud, lbl);
    }

    //给nud绑定ScheduleUndo和PushUndoNow统一处理
    private void BindNudUndo(NumericUpDown nud, EventHandler? extraHandler, bool isLeft)
    {
        nud.ValueChanged += (s, e) =>
        {
            ScheduleUndo();
            extraHandler?.Invoke(s, e);
        };
        nud.MouseUp += (_, _) => PushUndoNow();
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
    #endregion
}