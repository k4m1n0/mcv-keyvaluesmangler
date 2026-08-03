using System.Drawing;
using System.Windows.Forms;

namespace WeaponDamageCalc;

public partial class Form1
{
    private Button btnSave = null!;
    private Button btnCsvToScripts = null!;
    private Button btnScriptsToCsv = null!;
    private Button btnQuickExport = null!;
    private Button btnRefresh = null!;
    private Panel pnlSpread = null!;
    private Panel pnlRecoil = null!;
    private Label lblC64_1 = null!;
    private Label lblC64_2 = null!;
    private Label lblC64_3 = null!;

    private System.Windows.Forms.Timer? tmrC64;
    private System.Windows.Forms.Timer? tmrC64Reset;
    private void StartC64Anim()
    {
        lblC64_2.TextAlign = ContentAlignment.MiddleCenter;
        tmrC64 = new System.Windows.Forms.Timer { Interval = 2000 };
        tmrC64.Tick += (_, _) =>
        {
            long lWs = Environment.WorkingSet / 1024;
            long lGcHeap = GC.GetTotalMemory(false) / 1024;
            long lUnmanaged = lWs > lGcHeap ? lWs - lGcHeap : 0;
            lblC64_2.Text = $"{lWs / 1024}M RAM SYSTEM  {lUnmanaged}K NATIVE BYTES FREE";
        };
        tmrC64.Start();
        long lWs = Environment.WorkingSet / 1024;
        long lGcHeap = GC.GetTotalMemory(false) / 1024;
        long lUnmanaged = lWs > lGcHeap ? lWs - lGcHeap : 0;
        lblC64_2.Text = $"{lWs / 1024}M RAM SYSTEM  {lUnmanaged}K NATIVE BYTES FREE";
    }

    private void InitC64Labels()
    {
        int iCx = 525;
        lblC64_1 = new Label { Location = new Point(iCx, 675), Size = new Size(300, 13), Font = new Font("Consolas", 8, FontStyle.Bold), ForeColor = Color.FromArgb(200, 200, 255), BackColor = Color.FromArgb(60, 60, 160), TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0), Padding = new Padding(0) };
        lblC64_2 = new Label { Location = new Point(iCx, 686), Size = new Size(300, 13), Font = new Font("Consolas", 8, FontStyle.Bold), ForeColor = Color.FromArgb(200, 200, 255), BackColor = Color.FromArgb(60, 60, 160), TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0), Padding = new Padding(0) };
        lblC64_3 = new Label { Location = new Point(iCx, 697), Size = new Size(300, 13), Font = new Font("Consolas", 8, FontStyle.Bold), ForeColor = Color.FromArgb(200, 200, 255), BackColor = Color.FromArgb(60, 60, 160), TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0), Padding = new Padding(0) };

        var sTadaPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "tada.wav");
        System.Media.SoundPlayer? spTada = File.Exists(sTadaPath) ? new System.Media.SoundPlayer(sTadaPath) : null;
        void PlayTada() { try { spTada?.Play(); } catch { } }
        lblC64_1.Click += (_, _) => PlayTada();
        lblC64_2.Click += (_, _) => PlayTada();
        lblC64_3.Click += (_, _) => PlayTada();

        this.Controls.Add(lblC64_1);
        this.Controls.Add(lblC64_2);
        this.Controls.Add(lblC64_3);
        UpdateC64Labels(rgWeapons.Count > 0);
    }

    private void UpdateC64Labels(bool bHasData)
    {
        lblC64_1.Text = bHasData ? "         **** COMMODORE 64 BASIC V2 ****" : "";
        lblC64_3.Text = bHasData ? "READY." : "";
    }

    private void SetC64Status(string sStatus, bool bAutoReset = true)
    {
        tmrC64Reset?.Stop();
        tmrC64Reset?.Dispose();
        tmrC64Reset = null;
        lblC64_3.Text = sStatus;
        if (bAutoReset && (sStatus == "SAVED." || sStatus == "EXPORTED." || sStatus == "UNDONE." || sStatus == "REDONE."))
        {
            tmrC64Reset = new System.Windows.Forms.Timer { Interval = 1145 };
            tmrC64Reset.Tick += (_, _) => { lblC64_3.Text = "READY."; tmrC64Reset.Stop(); tmrC64Reset.Dispose(); tmrC64Reset = null; };
            tmrC64Reset.Start();
        }
    }

    #region 伤害倍率和衰减

    private void CreateDamageMultiplierGroup(int iX, bool bIsLeft)
    {
        var gb = new GroupBox { Text = "Damage Multiplier", Location = new Point(iX, 38), Size = new Size(520, 215) };
        int iY = 18;
        if (bIsLeft)
        {
            (trkHeadL, nudHeadL, lblHeadDmgL) = CreateSliderRow(gb, "Head", ref iY, true);
            (trkChestL, nudChestL, lblChestDmgL) = CreateSliderRow(gb, "Chest", ref iY, true);
            (trkStomachL, nudStomachL, lblStomachDmgL) = CreateSliderRow(gb, "Stomach", ref iY, true);
            (trkLegL, nudLegL, lblLegDmgL) = CreateSliderRow(gb, "Leg", ref iY, true);
            (trkArmL, nudArmL, lblArmDmgL) = CreateSliderRow(gb, "Arm", ref iY, true);
        }
        else
        {
            (trkHeadR, nudHeadR, lblHeadDmgR) = CreateSliderRow(gb, "Head", ref iY, false);
            (trkChestR, nudChestR, lblChestDmgR) = CreateSliderRow(gb, "Chest", ref iY, false);
            (trkStomachR, nudStomachR, lblStomachDmgR) = CreateSliderRow(gb, "Stomach", ref iY, false);
            (trkLegR, nudLegR, lblLegDmgR) = CreateSliderRow(gb, "Leg", ref iY, false);
            (trkArmR, nudArmR, lblArmDmgR) = CreateSliderRow(gb, "Arm", ref iY, false);
        }
        this.Controls.Add(gb);
    }

    private void CreateRangeGroup(int iX, bool bIsLeft)
    {
        var gb = new GroupBox { Text = "Range", Location = new Point(iX, 258), Size = new Size(520, 55) };
        gb.Controls.Add(new Label { Text = "0", Location = new Point(8, 20), Size = new Size(20, 18) });
        if (bIsLeft)
        {
            trkDistanceL = new TrackBar { Location = new Point(30, 16), Size = new Size(380, 35), Minimum = 0, Maximum = 100 };
            trkDistanceL.ValueChanged += (s, e) => { ScheduleUndo(); nudDistanceL.Value = trkDistanceL.Value; UpdateAllDamage(); LogService.DebugDebounce("trk_Distance_L", $"Distance L: {trkDistanceL.Value}", 500); };
            trkDistanceL.MouseUp += (_, _) => PushUndoNow();
            gb.Controls.Add(trkDistanceL);
            nudDistanceL = new NumericUpDown { Location = new Point(415, 16), Size = new Size(45, 22), DecimalPlaces = 0, Increment = 1, Minimum = 0, Maximum = 100 };
            nudDistanceL.ValueChanged += (s, e) => { ScheduleUndo(); trkDistanceL.Value = Math.Max(0, Math.Min(100, (int)nudDistanceL.Value)); UpdateAllDamage(); LogService.DebugDebounce("nud_Distance_L", $"Distance L: {nudDistanceL.Value}", 500); };
            nudDistanceL.MouseUp += (_, _) => PushUndoNow();
            gb.Controls.Add(nudDistanceL);
            chkVestL = new CheckBox { Text = "Vest", Location = new Point(465, 18), Size = new Size(55, 22) };
            chkVestL.CheckedChanged += (s, e) => { ScheduleUndo(); UpdateAllDamage(); LogService.DebugDebounce("chkVest_L", $"Vest L: {chkVestL.Checked}", 500); };
            gb.Controls.Add(chkVestL);
        }
        else
        {
            trkDistanceR = new TrackBar { Location = new Point(30, 16), Size = new Size(380, 35), Minimum = 0, Maximum = 100 };
            trkDistanceR.ValueChanged += (s, e) => { ScheduleUndo(); nudDistanceR.Value = trkDistanceR.Value; UpdateAllDamage(); LogService.DebugDebounce("trk_Distance_R", $"Distance R: {trkDistanceR.Value}", 500); };
            trkDistanceR.MouseUp += (_, _) => PushUndoNow();
            gb.Controls.Add(trkDistanceR);
            nudDistanceR = new NumericUpDown { Location = new Point(415, 16), Size = new Size(45, 22), DecimalPlaces = 0, Increment = 1, Minimum = 0, Maximum = 100 };
            nudDistanceR.ValueChanged += (s, e) => { ScheduleUndo(); trkDistanceR.Value = Math.Max(0, Math.Min(100, (int)nudDistanceR.Value)); UpdateAllDamage(); LogService.DebugDebounce("nud_Distance_R", $"Distance R: {nudDistanceR.Value}", 500); };
            nudDistanceR.MouseUp += (_, _) => PushUndoNow();
            gb.Controls.Add(nudDistanceR);
            chkVestR = new CheckBox { Text = "Vest", Location = new Point(465, 18), Size = new Size(55, 22) };
            chkVestR.CheckedChanged += (s, e) => { ScheduleUndo(); UpdateAllDamage(); LogService.DebugDebounce("chkVest_R", $"Vest R: {chkVestR.Checked}", 500); };
            gb.Controls.Add(chkVestR);
        }
        this.Controls.Add(gb);
    }

    #endregion
    #region 散布后座主属性

    private void CreateSpreadRecoilAndPropertiesGroups(int iX, bool bIsLeft)
    {
        var gbSpread = new GroupBox { Text = "Spread (°)", Location = new Point(iX, 318), Size = new Size(175, 130) };
        int iY = 20;
        if (bIsLeft)
        {
            nudHipSpreadL = CreateNullableNumericRow(gbSpread, "Hip", 8, iY, 100m); BindNudUndo(nudHipSpreadL, SpreadRecoilChangedL, bIsLeft); iY += 24;
            nudAdsSpreadL = CreateNullableNumericRow(gbSpread, "ADS", 8, iY, 100m); BindNudUndo(nudAdsSpreadL, SpreadRecoilChangedL, bIsLeft); iY += 24;
            nudBipodHipSpreadL = CreateNullableNumericRow(gbSpread, "Bipod Hip", 8, iY, 100m); BindNudUndo(nudBipodHipSpreadL, SpreadRecoilChangedL, bIsLeft); iY += 24;
            nudBipodAdsSpreadL = CreateNullableNumericRow(gbSpread, "Bipod ADS", 8, iY, 100m); BindNudUndo(nudBipodAdsSpreadL, SpreadRecoilChangedL, bIsLeft);
        }
        else
        {
            nudHipSpreadR = CreateNullableNumericRow(gbSpread, "Hip", 8, iY, 100m); BindNudUndo(nudHipSpreadR, SpreadRecoilChangedR, bIsLeft); iY += 24;
            nudAdsSpreadR = CreateNullableNumericRow(gbSpread, "ADS", 8, iY, 100m); BindNudUndo(nudAdsSpreadR, SpreadRecoilChangedR, bIsLeft); iY += 24;
            nudBipodHipSpreadR = CreateNullableNumericRow(gbSpread, "Bipod Hip", 8, iY, 100m); BindNudUndo(nudBipodHipSpreadR, SpreadRecoilChangedR, bIsLeft); iY += 24;
            nudBipodAdsSpreadR = CreateNullableNumericRow(gbSpread, "Bipod ADS", 8, iY, 100m); BindNudUndo(nudBipodAdsSpreadR, SpreadRecoilChangedR, bIsLeft);
        }
        this.Controls.Add(gbSpread);

        var gbRecoil = new GroupBox { Text = "Recoil (°)", Location = new Point(iX + 180, 318), Size = new Size(175, 130) };
        iY = 20;
        if (bIsLeft)
        {
            nudHipRecoilUpL = CreateNullableNumericRow(gbRecoil, "Hip Up", 8, iY, 100m); BindNudUndo(nudHipRecoilUpL, SpreadRecoilChangedL, bIsLeft); iY += 24;
            nudHipRecoilRightL = CreateNullableNumericRow(gbRecoil, "Hip Rt", 8, iY, 100m); BindNudUndo(nudHipRecoilRightL, SpreadRecoilChangedL, bIsLeft); iY += 24;
            nudAdsRecoilUpL = CreateNullableNumericRow(gbRecoil, "ADS Up", 8, iY, 100m); BindNudUndo(nudAdsRecoilUpL, SpreadRecoilChangedL, bIsLeft); iY += 24;
            nudAdsRecoilRightL = CreateNullableNumericRow(gbRecoil, "ADS Rt", 8, iY, 100m); BindNudUndo(nudAdsRecoilRightL, SpreadRecoilChangedL, bIsLeft);
        }
        else
        {
            nudHipRecoilUpR = CreateNullableNumericRow(gbRecoil, "Hip Up", 8, iY, 100m); BindNudUndo(nudHipRecoilUpR, SpreadRecoilChangedR, bIsLeft); iY += 24;
            nudHipRecoilRightR = CreateNullableNumericRow(gbRecoil, "Hip Rt", 8, iY, 100m); BindNudUndo(nudHipRecoilRightR, SpreadRecoilChangedR, bIsLeft); iY += 24;
            nudAdsRecoilUpR = CreateNullableNumericRow(gbRecoil, "ADS Up", 8, iY, 100m); BindNudUndo(nudAdsRecoilUpR, SpreadRecoilChangedR, bIsLeft); iY += 24;
            nudAdsRecoilRightR = CreateNullableNumericRow(gbRecoil, "ADS Rt", 8, iY, 100m); BindNudUndo(nudAdsRecoilRightR, SpreadRecoilChangedR, bIsLeft);
        }
        this.Controls.Add(gbRecoil);

        var gbProp = new GroupBox { Text = "Stats", Location = new Point(iX + 360, 318), Size = new Size(160, 130) };
        iY = 20;
        if (bIsLeft)
        {
            txtFireModesL = CreateTextBoxRow(gbProp, "Fire Mode", 8, iY);
            txtFireModesL.TextChanged += (s, e) => { if (!bUpdatingControls) { ScheduleUndo(); UpdateAllDamage(); LogService.DebugDebounce("txt_FireModes_L", $"FireModes L: {txtFireModesL.Text}", 500); } };
            iY += 24;
            nudFireRateL = CreateNullableIntNumericRow(gbProp, "ROF", 8, iY, 10000m);
            BindNudUndo(nudFireRateL, (s, e) => UpdateAllDamage(), bIsLeft);
            iY += 24;
            nudRangeModifierL = CreateNullableNumericRow(gbProp, "Range Mod", 8, iY, 10m);
            nudRangeModifierL.DecimalPlaces = 3; nudRangeModifierL.Increment = 0.001m;
            BindNudUndo(nudRangeModifierL, (s, e) => { RangeModifierChangedL(s, e); }, bIsLeft);
            iY += 24;
            txtCapacityL = CreateTextBoxRow(gbProp, "Capacity", 8, iY);
            txtCapacityL.TextChanged += (s, e) => { if (!bUpdatingControls) { ScheduleUndo(); LogService.DebugDebounce("txt_Capacity_L", $"Capacity L: {txtCapacityL.Text}", 500); } };
        }
        else
        {
            txtFireModesR = CreateTextBoxRow(gbProp, "Fire Mode", 8, iY);
            txtFireModesR.TextChanged += (s, e) => { if (!bUpdatingControls) { ScheduleUndo(); UpdateAllDamage(); LogService.DebugDebounce("txt_FireModes_R", $"FireModes R: {txtFireModesR.Text}", 500); } };
            iY += 24;
            nudFireRateR = CreateNullableIntNumericRow(gbProp, "ROF", 8, iY, 10000m);
            BindNudUndo(nudFireRateR, (s, e) => UpdateAllDamage(), bIsLeft);
            iY += 24;
            nudRangeModifierR = CreateNullableNumericRow(gbProp, "Range Mod", 8, iY, 10m);
            nudRangeModifierR.DecimalPlaces = 3; nudRangeModifierR.Increment = 0.001m;
            BindNudUndo(nudRangeModifierR, (s, e) => { RangeModifierChangedR(s, e); }, bIsLeft);
            iY += 24;
            txtCapacityR = CreateTextBoxRow(gbProp, "Capacity", 8, iY);
            txtCapacityR.TextChanged += (s, e) => { if (!bUpdatingControls) { ScheduleUndo(); LogService.DebugDebounce("txt_Capacity_R", $"Capacity R: {txtCapacityR.Text}", 500); } };
        }
        this.Controls.Add(gbProp);
    }

    #endregion
    #region 散布倍率

    private void CreateSpreadMultiplierGroup(int iX, bool bIsLeft)
    {
        var gb = new GroupBox { Text = "Spread Multiplier", Location = new Point(iX, 453), Size = new Size(520, 75) };
        int iY = 20;
        if (bIsLeft)
        {
            nudCrouchSpreadL = CreateNullableNumericRow(gb, "Duck", 8, iY, 100m); BindNudUndo(nudCrouchSpreadL, null, bIsLeft);
            nudProneSpreadL = CreateNullableNumericRow(gb, "Prone", 188, iY, 100m); BindNudUndo(nudProneSpreadL, null, bIsLeft);
            nudStandMoveSpreadL = CreateNullableNumericRow(gb, "Move", 368, iY, 100m); BindNudUndo(nudStandMoveSpreadL, null, bIsLeft);
            iY += 26;
            nudSneakMoveSpreadL = CreateNullableNumericRow(gb, "SneakMov", 8, iY, 100m); BindNudUndo(nudSneakMoveSpreadL, null, bIsLeft);
            nudCrouchMoveSpreadL = CreateNullableNumericRow(gb, "DuckMov", 188, iY, 100m); BindNudUndo(nudCrouchMoveSpreadL, null, bIsLeft);
            nudJumpSpreadL = CreateNullableNumericRow(gb, "Jump", 368, iY, 100m); BindNudUndo(nudJumpSpreadL, null, bIsLeft);
        }
        else
        {
            nudCrouchSpreadR = CreateNullableNumericRow(gb, "Duck", 8, iY, 100m); BindNudUndo(nudCrouchSpreadR, null, bIsLeft);
            nudProneSpreadR = CreateNullableNumericRow(gb, "Prone", 188, iY, 100m); BindNudUndo(nudProneSpreadR, null, bIsLeft);
            nudStandMoveSpreadR = CreateNullableNumericRow(gb, "Move", 368, iY, 100m); BindNudUndo(nudStandMoveSpreadR, null, bIsLeft);
            iY += 26;
            nudSneakMoveSpreadR = CreateNullableNumericRow(gb, "SneakMov", 8, iY, 100m); BindNudUndo(nudSneakMoveSpreadR, null, bIsLeft);
            nudCrouchMoveSpreadR = CreateNullableNumericRow(gb, "DuckMov", 188, iY, 100m); BindNudUndo(nudCrouchMoveSpreadR, null, bIsLeft);
            nudJumpSpreadR = CreateNullableNumericRow(gb, "Jump", 368, iY, 100m); BindNudUndo(nudJumpSpreadR, null, bIsLeft);
        }
        this.Controls.Add(gb);
    }

    #endregion
    #region 其它属性

    private void CreateOtherStatsGroup(int iX, bool bIsLeft)
    {
        var gb = new GroupBox { Text = "Other Stats", Location = new Point(iX, 533), Size = new Size(520, 180) };
        int iY = 20;
        if (bIsLeft)
        {
            nudExtraBulletChamberL = CreateNullableIntNumericRow(gb, "Chamber", 8, iY, 1000m); BindNudUndo(nudExtraBulletChamberL, null, bIsLeft);
            nudBulletsPerShotL = CreateNullableIntNumericRow(gb, "Pellets", 188, iY, 100m);
            nudBulletsPerShotL.ValueChanged += (s, e) => { ScheduleUndo(); UpdateAllDamage(); LogService.DebugDebounce("nud_Pellets_L", $"Pellets L: {nudBulletsPerShotL.Value}", 500); };
            nudBulletsPerShotL.MouseUp += (_, _) => PushUndoNow();
            nudIronsightSpeedScaleL = CreateNullableNumericRow(gb, "ADS Spd", 368, iY, 10m); BindNudUndo(nudIronsightSpeedScaleL, null, bIsLeft);
            iY += 26;
            nudWeightL = CreateNullableNumericRow(gb, "Weight", 8, iY, 100m); BindNudUndo(nudWeightL, null, bIsLeft);
            nudZMBuyPriceL = CreateNullableIntNumericRow(gb, "ZM Price", 188, iY, 1000000m); BindNudUndo(nudZMBuyPriceL, null, bIsLeft);
            nudZMWeightL = CreateNullableIntNumericRow(gb, "ZM Block", 368, iY, 100m); BindNudUndo(nudZMWeightL, null, bIsLeft);
            iY += 26;
            nudMetalPenL = CreateNullableNumericRow(gb, "MetalPen", 8, iY, 10000m); BindNudUndo(nudMetalPenL, null, bIsLeft);
            nudGlassPenL = CreateNullableNumericRow(gb, "GlassPen", 188, iY, 10000m); BindNudUndo(nudGlassPenL, null, bIsLeft);
            nudConcretePenL = CreateNullableNumericRow(gb, "ConcrPen", 368, iY, 10000m); BindNudUndo(nudConcretePenL, null, bIsLeft);
            iY += 26;
            nudWoodPenL = CreateNullableNumericRow(gb, "WoodPen", 8, iY, 10000m); BindNudUndo(nudWoodPenL, null, bIsLeft);
            nudOtherPenL = CreateNullableNumericRow(gb, "OtherPen", 188, iY, 10000m); BindNudUndo(nudOtherPenL, null, bIsLeft);
            nudConcreteDmgModL = CreateNullableNumericRow(gb, "ConcrMod", 368, iY, 100m); BindNudUndo(nudConcreteDmgModL, null, bIsLeft);
            iY += 26;
            nudMetalDmgModL = CreateNullableNumericRow(gb, "MetalMod", 8, iY, 100m); BindNudUndo(nudMetalDmgModL, null, bIsLeft);
            nudGlassDmgModL = CreateNullableNumericRow(gb, "GlassMod", 188, iY, 100m); BindNudUndo(nudGlassDmgModL, null, bIsLeft);
            nudWoodDmgModL = CreateNullableNumericRow(gb, "WoodMod", 368, iY, 100m); BindNudUndo(nudWoodDmgModL, null, bIsLeft);
            iY += 26;
            nudOtherDmgModL = CreateNullableNumericRow(gb, "OtherMod", 8, iY, 100m); BindNudUndo(nudOtherDmgModL, null, bIsLeft);
            nudSecondaryFireRateL = CreateNullableIntNumericRow(gb, "2ndROF", 188, iY, 10000m);
            nudSecondaryFireRateL.Minimum = -1m;
            nudSecondaryFireRateL.ValueChanged += (s, e) => { ScheduleUndo(); UpdateAllDamage(); LogService.DebugDebounce("nud_2ndROF_L", $"2ndROF L: {nudSecondaryFireRateL.Value}", 500); };
            nudSecondaryFireRateL.MouseUp += (_, _) => PushUndoNow();
            nudSecondaryFireRateL.Enter += (s, e) => { UpdateAllDamage(); };
            nudSecondaryFireRateL.Leave += (s, e) => { UpdateAllDamage(); };
            nudIronSightL = CreateNullableIntNumericRow(gb, "IronSight", 368, iY, 1m);
            nudIronSightL.ValueChanged += (s, e) =>
            {
                ScheduleUndo();
                bool bNoIronsight = nudIronSightL.Value == 0;
                nudAdsSpreadL.Enabled = !bNoIronsight;
                nudAdsRecoilUpL.Enabled = !bNoIronsight;
                nudAdsRecoilRightL.Enabled = !bNoIronsight;
                nudIronsightSpeedScaleL.Enabled = !bNoIronsight;
                pnlSpread.Invalidate();
                pnlRecoil.Invalidate();
                LogService.DebugDebounce("nud_IronSight_L", $"IronSight L: {nudIronSightL.Value}", 500);
            };
            nudIronSightL.MouseUp += (_, _) => PushUndoNow();
        }
        else
        {
            nudExtraBulletChamberR = CreateNullableIntNumericRow(gb, "Chamber", 8, iY, 1000m); BindNudUndo(nudExtraBulletChamberR, null, bIsLeft);
            nudBulletsPerShotR = CreateNullableIntNumericRow(gb, "Pellets", 188, iY, 100m);
            nudBulletsPerShotR.ValueChanged += (s, e) => { ScheduleUndo(); UpdateAllDamage(); LogService.DebugDebounce("nud_Pellets_R", $"Pellets R: {nudBulletsPerShotR.Value}", 500); };
            nudBulletsPerShotR.MouseUp += (_, _) => PushUndoNow();
            nudIronsightSpeedScaleR = CreateNullableNumericRow(gb, "ADS Spd", 368, iY, 10m); BindNudUndo(nudIronsightSpeedScaleR, null, bIsLeft);
            iY += 26;
            nudWeightR = CreateNullableNumericRow(gb, "Weight", 8, iY, 100m); BindNudUndo(nudWeightR, null, bIsLeft);
            nudZMBuyPriceR = CreateNullableIntNumericRow(gb, "ZM Price", 188, iY, 1000000m); BindNudUndo(nudZMBuyPriceR, null, bIsLeft);
            nudZMWeightR = CreateNullableIntNumericRow(gb, "ZM Block", 368, iY, 100m); BindNudUndo(nudZMWeightR, null, bIsLeft);
            iY += 26;
            nudMetalPenR = CreateNullableNumericRow(gb, "MetalPen", 8, iY, 10000m); BindNudUndo(nudMetalPenR, null, bIsLeft);
            nudGlassPenR = CreateNullableNumericRow(gb, "GlassPen", 188, iY, 10000m); BindNudUndo(nudGlassPenR, null, bIsLeft);
            nudConcretePenR = CreateNullableNumericRow(gb, "ConcrPen", 368, iY, 10000m); BindNudUndo(nudConcretePenR, null, bIsLeft);
            iY += 26;
            nudWoodPenR = CreateNullableNumericRow(gb, "WoodPen", 8, iY, 10000m); BindNudUndo(nudWoodPenR, null, bIsLeft);
            nudOtherPenR = CreateNullableNumericRow(gb, "OtherPen", 188, iY, 10000m); BindNudUndo(nudOtherPenR, null, bIsLeft);
            nudConcreteDmgModR = CreateNullableNumericRow(gb, "ConcrMod", 368, iY, 100m); BindNudUndo(nudConcreteDmgModR, null, bIsLeft);
            iY += 26;
            nudMetalDmgModR = CreateNullableNumericRow(gb, "MetalMod", 8, iY, 100m); BindNudUndo(nudMetalDmgModR, null, bIsLeft);
            nudGlassDmgModR = CreateNullableNumericRow(gb, "GlassMod", 188, iY, 100m); BindNudUndo(nudGlassDmgModR, null, bIsLeft);
            nudWoodDmgModR = CreateNullableNumericRow(gb, "WoodMod", 368, iY, 100m); BindNudUndo(nudWoodDmgModR, null, bIsLeft);
            iY += 26;
            nudOtherDmgModR = CreateNullableNumericRow(gb, "OtherMod", 8, iY, 100m); BindNudUndo(nudOtherDmgModR, null, bIsLeft);
            nudSecondaryFireRateR = CreateNullableIntNumericRow(gb, "2ndROF", 188, iY, 10000m);
            nudSecondaryFireRateR.Minimum = -1m;
            nudSecondaryFireRateR.ValueChanged += (s, e) => { ScheduleUndo(); UpdateAllDamage(); LogService.DebugDebounce("nud_2ndROF_R", $"2ndROF R: {nudSecondaryFireRateR.Value}", 500); };
            nudSecondaryFireRateR.MouseUp += (_, _) => PushUndoNow();
            nudSecondaryFireRateR.Enter += (s, e) => { UpdateAllDamage(); };
            nudSecondaryFireRateR.Leave += (s, e) => { UpdateAllDamage(); };
            nudIronSightR = CreateNullableIntNumericRow(gb, "IronSight", 368, iY, 1m);
            nudIronSightR.ValueChanged += (s, e) =>
            {
                ScheduleUndo();
                bool bNoIronsight = nudIronSightR.Value == 0;
                nudAdsSpreadR.Enabled = !bNoIronsight;
                nudAdsRecoilUpR.Enabled = !bNoIronsight;
                nudAdsRecoilRightR.Enabled = !bNoIronsight;
                nudIronsightSpeedScaleR.Enabled = !bNoIronsight;
                pnlSpread.Invalidate();
                pnlRecoil.Invalidate();
                LogService.DebugDebounce("nud_IronSight_R", $"IronSight R: {nudIronSightR.Value}", 500);
            };
            nudIronSightR.MouseUp += (_, _) => PushUndoNow();
        }
        this.Controls.Add(gb);
    }

    #endregion
    #region 控件工厂

    private (TrackBar, NumericUpDown, Label) CreateSliderRow(Control ctrlParent, string sText, ref int iY, bool bIsLeft)
    {
        ctrlParent.Controls.Add(new Label { Text = sText, Location = new Point(8, iY + 8), Size = new Size(35, 18), TextAlign = ContentAlignment.MiddleLeft });
        var tb = new TrackBar { Location = new Point(45, iY + 2), Size = new Size(270, 34), Minimum = (int)(dSliderMin / dSliderStep), Maximum = (int)(dSliderMax / dSliderStep), TickFrequency = (int)(0.5 / dSliderStep), Value = (int)(1.0 / dSliderStep) };
        var nud = new NumericUpDown { Location = new Point(320, iY + 7), Size = new Size(55, 22), DecimalPlaces = 2, Increment = 0.01m, Minimum = (decimal)dSliderMin, Maximum = 7.5m, Value = 1.00m };
        var lbl = new Label { Text = "= 0.0 | ∞shots | ∞ms", Location = new Point(380, iY + 9), AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.DarkRed, Font = new Font("Arial", 8, FontStyle.Bold) };
        ctrlParent.Controls.Add(tb);
        ctrlParent.Controls.Add(nud);
        ctrlParent.Controls.Add(lbl);

        tb.Tag = nud;
        nud.Tag = tb;
        string sSideTag = bIsLeft ? "L" : "R";
        string sKeyPrefix = $"slider_{sText}_{sSideTag}";
        if (bIsLeft)
        {
            tb.ValueChanged += (s, e) => { SliderChangedL(s, e); ScheduleUndo(); LogService.DebugDebounce($"{sKeyPrefix}_tb", $"Slider {sText} L: {tb.Value}", 300); };
            tb.MouseUp += (_, _) => PushUndoNow();
            nud.ValueChanged += (s, e) => { NumericChangedL(s, e); ScheduleUndo(); LogService.DebugDebounce($"{sKeyPrefix}_nud", $"NUD {sText} L: {nud.Value}", 300); };
            nud.MouseUp += (_, _) => PushUndoNow();
        }
        else
        {
            tb.ValueChanged += (s, e) => { SliderChangedR(s, e); ScheduleUndo(); LogService.DebugDebounce($"{sKeyPrefix}_tb", $"Slider {sText} R: {tb.Value}", 300); };
            tb.MouseUp += (_, _) => PushUndoNow();
            nud.ValueChanged += (s, e) => { NumericChangedR(s, e); ScheduleUndo(); LogService.DebugDebounce($"{sKeyPrefix}_nud", $"NUD {sText} R: {nud.Value}", 300); };
            nud.MouseUp += (_, _) => PushUndoNow();
        }

        iY += 37;
        return (tb, nud, lbl);
    }

    //给nud绑定ScheduleUndo和PushUndoNow统一处理
    private void BindNudUndo(NumericUpDown nud, EventHandler? ehExtra, bool bIsLeft)
    {
        nud.ValueChanged += (s, e) =>
        {
            ScheduleUndo();
            LogService.DebugDebounce($"nud_{nud.Name ?? "?"}", $"NUD changed: {nud.Name} = {nud.Text} ({(bIsLeft ? "L" : "R")})", 500);
            ehExtra?.Invoke(s, e);
        };
        nud.MouseUp += (_, _) => PushUndoNow();
    }

    private NumericUpDown CreateNullableNumericRow(Control ctrlParent, string sText, int iX, int iY, decimal decMax)
    {
        ctrlParent.Controls.Add(new Label { Text = sText, Location = new Point(iX, iY + 3), Size = new Size(70, 18), TextAlign = ContentAlignment.MiddleLeft });
        var nud = new NumericUpDown { Location = new Point(iX + 72, iY + 1), Size = new Size(65, 22), DecimalPlaces = 2, Increment = 0.01m, Minimum = 0m, Maximum = decMax };
        ctrlParent.Controls.Add(nud);
        return nud;
    }

    private NumericUpDown CreateNullableIntNumericRow(Control ctrlParent, string sText, int iX, int iY, decimal decMax)
    {
        ctrlParent.Controls.Add(new Label { Text = sText, Location = new Point(iX, iY + 3), Size = new Size(70, 18), TextAlign = ContentAlignment.MiddleLeft });
        var nud = new NumericUpDown { Location = new Point(iX + 72, iY + 1), Size = new Size(65, 22), DecimalPlaces = 0, Increment = 1, Minimum = 0m, Maximum = decMax };
        ctrlParent.Controls.Add(nud);
        return nud;
    }

    private TextBox CreateTextBoxRow(Control ctrlParent, string sText, int iX, int iY)
    {
        ctrlParent.Controls.Add(new Label { Text = sText, Location = new Point(iX, iY + 3), Size = new Size(70, 18), TextAlign = ContentAlignment.MiddleLeft });
        var tb = new TextBox { Location = new Point(iX + 72, iY + 1), Size = new Size(65, 22) };
        ctrlParent.Controls.Add(tb);
        return tb;
    }
    #endregion
}