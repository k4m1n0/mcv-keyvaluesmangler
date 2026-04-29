using System.Collections.Generic;
using System.Windows.Forms;
using WeaponDamageCalc.Models;

namespace WeaponDamageCalc;

public partial class Form1
{
    private ComboBox cmbWeaponsL = null!;
    private TrackBar trkHeadL, trkChestL, trkStomachL, trkLegL, trkArmL, trkDistanceL;
    private NumericUpDown nudHeadL, nudChestL, nudStomachL, nudLegL, nudArmL;
    private NumericUpDown nudHipSpreadL, nudAdsSpreadL, nudBipodHipSpreadL, nudBipodAdsSpreadL;
    private NumericUpDown nudHipRecoilUpL, nudHipRecoilRightL, nudAdsRecoilUpL, nudAdsRecoilRightL;
    private NumericUpDown nudFireRateL, nudRangeModifierL, nudDamageGenericL, nudDistanceL;
    private NumericUpDown nudExtraBulletChamberL, nudBulletsPerShotL, nudIronsightSpeedScaleL;
    private NumericUpDown nudWeightL, nudZMBuyPriceL, nudZMWeightL;
    private NumericUpDown nudMetalPenL, nudGlassPenL, nudConcretePenL, nudWoodPenL, nudOtherPenL;
    private NumericUpDown nudMetalDmgModL, nudGlassDmgModL, nudConcreteDmgModL, nudWoodDmgModL, nudOtherDmgModL;
    private NumericUpDown nudCrouchSpreadL, nudProneSpreadL, nudStandMoveSpreadL, nudSneakMoveSpreadL, nudCrouchMoveSpreadL, nudJumpSpreadL;
    private TextBox txtFireModesL, txtCapacityL;
    private Label lblHeadDmgL, lblChestDmgL, lblStomachDmgL, lblLegDmgL, lblArmDmgL;
    private CheckBox chkVestL;

    private void InitLeftPanel(List<WeaponData> weapons)
    {
        int x = 5;
        cmbWeaponsL = new ComboBox { Location = new Point(340, 6), Size = new Size(180, 23), AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.ListItems, DisplayMember = "PrintName" };
        cmbWeaponsL.SelectedIndexChanged += WeaponSelectedL;
        this.Controls.Add(cmbWeaponsL);
        this.Controls.Add(new Label { Text = "WeaponL", Location = new Point(275, 8), Size = new Size(65, 20) });
        CreateDamageMultiplierGroup(x, true);
        CreateRangeGroup(x, true);
        CreateSpreadRecoilAndPropertiesGroups(x, true);
        CreateSpreadMultiplierGroup(x, true);
        CreateOtherStatsGroup(x, true);

        if (weapons.Count > 0)
        {
            cmbWeaponsL.DataSource = new List<WeaponData>(weapons);
            cmbWeaponsL.SelectedIndex = 0;
        }
    }
}