using System.Collections.Generic;
using System.Windows.Forms;
using WeaponDamageCalc.Models;

namespace WeaponDamageCalc;

public partial class Form1
{
    private ComboBox cmbWeaponsR = null!;
    private TrackBar trkHeadR, trkChestR, trkStomachR, trkLegR, trkArmR, trkDistanceR;
    private NumericUpDown nudHeadR, nudChestR, nudStomachR, nudLegR, nudArmR;
    private NumericUpDown nudHipSpreadR, nudAdsSpreadR, nudBipodHipSpreadR, nudBipodAdsSpreadR;
    private NumericUpDown nudHipRecoilUpR, nudHipRecoilRightR, nudAdsRecoilUpR, nudAdsRecoilRightR;
    private NumericUpDown nudFireRateR, nudRangeModifierR, nudDamageGenericR, nudDistanceR;
    private NumericUpDown nudExtraBulletChamberR, nudBulletsPerShotR, nudIronsightSpeedScaleR;
    private NumericUpDown nudWeightR, nudZMBuyPriceR, nudZMWeightR;
    private NumericUpDown nudMetalPenR, nudGlassPenR, nudConcretePenR, nudWoodPenR, nudOtherPenR;
    private NumericUpDown nudMetalDmgModR, nudGlassDmgModR, nudConcreteDmgModR, nudWoodDmgModR, nudOtherDmgModR;
    private NumericUpDown nudCrouchSpreadR, nudProneSpreadR, nudStandMoveSpreadR, nudSneakMoveSpreadR, nudCrouchMoveSpreadR, nudJumpSpreadR;
    private TextBox txtFireModesR, txtCapacityR;
    private Label lblHeadDmgR, lblChestDmgR, lblStomachDmgR, lblLegDmgR, lblArmDmgR;
    private CheckBox chkVestR;

    private void InitRightPanel(List<WeaponData> weapons)
    {
        int x = 825;
        this.Controls.Add(new Label { Text = "WeaponR", Location = new Point(x + 190, 8), Size = new Size(65, 20) });
        cmbWeaponsR = new ComboBox { Location = new Point(x + 5, 6), Size = new Size(180, 23), AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.ListItems, DisplayMember = "PrintName" };
        cmbWeaponsR.SelectedIndexChanged += WeaponSelectedR;
        this.Controls.Add(cmbWeaponsR);

        CreateDamageMultiplierGroup(x, false);
        CreateRangeGroup(x, false);
        CreateSpreadRecoilAndPropertiesGroups(x, false);
        CreateSpreadMultiplierGroup(x, false);
        CreateOtherStatsGroup(x, false);

        if (weapons.Count > 0)
        {
            cmbWeaponsR.DataSource = new List<WeaponData>(weapons);
            cmbWeaponsR.SelectedIndex = 0;
        }
    }
}