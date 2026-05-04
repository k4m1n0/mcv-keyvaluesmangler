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
        this.Controls.Add(new Label { Text = "Dmg", Location = new Point(4, 8), AutoSize = true });
        nudDamageGenericL = new NumericUpDown { Location = new Point(42, 6), Size = new Size(65, 23), DecimalPlaces = 1, Increment = 1m, Minimum = 0m, Maximum = 999m, Value = 0m };//我用了好几天才发现这个输入框被裁掉了
        nudDamageGenericL.ValueChanged += (s, e) => { currentWeaponLeft!.DamageGeneric = (double)nudDamageGenericL.Value; UpdateAllDamage(); };
        this.Controls.Add(nudDamageGenericL);
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