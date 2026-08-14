using System.Collections.Generic;
using System.Windows.Forms;
using WeaponDamageCalc.Models;
using WeaponDamageCalc.Services;

namespace WeaponDamageCalc;

public partial class Form1
{
    private ComboBox cmbWeaponsL = null!;
    private TrackBar trkHeadL = null!, trkChestL = null!, trkStomachL = null!, trkLegL = null!, trkArmL = null!, trkDistanceL = null!;
    private NumericUpDown nudHeadL = null!, nudChestL = null!, nudStomachL = null!, nudLegL = null!, nudArmL = null!;
    private NumericUpDown nudHipSpreadL = null!, nudAdsSpreadL = null!, nudBipodHipSpreadL = null!, nudBipodAdsSpreadL = null!;
    private NumericUpDown nudHipRecoilUpL = null!, nudHipRecoilRightL = null!, nudAdsRecoilUpL = null!, nudAdsRecoilRightL = null!;
    private NumericUpDown nudFireRateL = null!, nudRangeModifierL = null!, nudDamageGenericL = null!, nudDistanceL = null!;
    private NumericUpDown nudExtraBulletChamberL = null!, nudBulletsPerShotL = null!, nudIronsightSpeedScaleL = null!;
    private NumericUpDown nudWeightL = null!, nudZMBuyPriceL = null!, nudZMWeightL = null!;
    private NumericUpDown nudMetalPenL = null!, nudGlassPenL = null!, nudConcretePenL = null!, nudWoodPenL = null!, nudOtherPenL = null!;
    private NumericUpDown nudMetalDmgModL = null!, nudGlassDmgModL = null!, nudConcreteDmgModL = null!, nudWoodDmgModL = null!, nudOtherDmgModL = null!;
    private NumericUpDown nudCrouchSpreadL = null!, nudProneSpreadL = null!, nudStandMoveSpreadL = null!, nudSneakMoveSpreadL = null!, nudCrouchMoveSpreadL = null!, nudJumpSpreadL = null!;
    private NumericUpDown nudSecondaryFireRateL = null!, nudIronSightL = null!;
    private TextBox txtFireModesL = null!, txtCapacityL = null!;
    private Label lblHeadDmgL = null!, lblChestDmgL = null!, lblStomachDmgL = null!, lblLegDmgL = null!, lblArmDmgL = null!;
    private CheckBox chkVestL = null!;

    private void InitLeftPanel(List<WeaponData> rgWeapons)
    {
        int iX = 5;
        cmbWeaponsL = new ComboBox { Location = new Point(340, 6), Size = new Size(180, 23), AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.ListItems, DisplayMember = "PrintName" };
        cmbWeaponsL.SelectedIndexChanged += WeaponSelectedL;
        this.Controls.Add(cmbWeaponsL);
        var btnOpenScriptL = new Button { Text = "EditInFile", Location = new Point(190, 6), Size = new Size(75, 26) };
        btnOpenScriptL.Click += (s, e) => OpenScriptForCurrent(true);
        this.Controls.Add(btnOpenScriptL);
        new ToolTip().SetToolTip(btnOpenScriptL, "Open weapon script in default editor");
        this.Controls.Add(new Label { Text = "WeaponL", Location = new Point(270, 8), Size = new Size(70, 20) });
        this.Controls.Add(new Label { Text = "Dmg", Location = new Point(4, 8), AutoSize = true });
        nudDamageGenericL = new NumericUpDown { Location = new Point(45, 6), Size = new Size(65, 23), DecimalPlaces = 1, Increment = 1m, Minimum = 0m, Maximum = 999m, Value = 0m };//我用了好几天才发现这个输入框被裁掉了
        nudDamageGenericL.ValueChanged += (s, e) => { ScheduleUndo(); UpdateAllDamage(); LogService.DebugDebounce("nud_Dmg_L", $"Dmg L: {nudDamageGenericL.Value}", 500); };
        nudDamageGenericL.MouseUp += (_, _) => PushUndoNow();
        this.Controls.Add(nudDamageGenericL);
        CreateDamageMultiplierGroup(iX, true);
        CreateRangeGroup(iX, true);
        CreateSpreadRecoilAndPropertiesGroups(iX, true);
        CreateSpreadMultiplierGroup(iX, true);
        CreateOtherStatsGroup(iX, true);
    }
}