using System.Windows.Forms;
using WeaponDamageCalc.Models;
using WeaponDamageCalc.Services;

namespace WeaponDamageCalc;

public partial class Form1
{
    private ComboBox cmbWeaponsR = null!;
    private TrackBar trkHeadR = null!, trkChestR = null!, trkStomachR = null!, trkLegR = null!, trkArmR = null!, trkDistanceR = null!;
    private NumericUpDown nudHeadR = null!, nudChestR = null!, nudStomachR = null!, nudLegR = null!, nudArmR = null!;
    private NumericUpDown nudHipSpreadR = null!, nudAdsSpreadR = null!, nudBipodHipSpreadR = null!, nudBipodAdsSpreadR = null!;
    private NumericUpDown nudHipRecoilUpR = null!, nudHipRecoilRightR = null!, nudAdsRecoilUpR = null!, nudAdsRecoilRightR = null!;
    private NumericUpDown nudFireRateR = null!, nudRangeModifierR = null!, nudDamageGenericR = null!, nudDistanceR = null!;
    private NumericUpDown nudExtraBulletChamberR = null!, nudBulletsPerShotR = null!, nudIronsightSpeedScaleR = null!;
    private NumericUpDown nudWeightR = null!, nudZMBuyPriceR = null!, nudZMWeightR = null!;
    private NumericUpDown nudMetalPenR = null!, nudGlassPenR = null!, nudConcretePenR = null!, nudWoodPenR = null!, nudOtherPenR = null!;
    private NumericUpDown nudMetalDmgModR = null!, nudGlassDmgModR = null!, nudConcreteDmgModR = null!, nudWoodDmgModR = null!, nudOtherDmgModR = null!;
    private NumericUpDown nudCrouchSpreadR = null!, nudProneSpreadR = null!, nudStandMoveSpreadR = null!, nudSneakMoveSpreadR = null!, nudCrouchMoveSpreadR = null!, nudJumpSpreadR = null!;
    private NumericUpDown nudSecondaryFireRateR = null!, nudIronSightR = null!;
    private TextBox txtFireModesR = null!, txtCapacityR = null!;
    private Label lblHeadDmgR = null!, lblChestDmgR = null!, lblStomachDmgR = null!, lblLegDmgR = null!, lblArmDmgR = null!;
    private CheckBox chkVestR = null!;

    private void InitRightPanel()
    {
        int iX = 825;
        this.Controls.Add(new Label { Text = "WeaponR", Location = new Point(iX + 190, 8), Size = new Size(70, 20) });
        cmbWeaponsR = new ComboBox { Location = new Point(iX + 5, 6), Size = new Size(180, 23), AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.ListItems, DisplayMember = "PrintName" };
        cmbWeaponsR.SelectedIndexChanged += (s, ev) => WeaponSelected(false, s, ev);
        this.Controls.Add(cmbWeaponsR);
        var btnOpenScriptR = new Button { Text = "EditInFile", Location = new Point(iX + 260, 6), Size = new Size(75, 26) };
        btnOpenScriptR.Click += (s, e) => OpenScriptForCurrent(false);
        this.Controls.Add(btnOpenScriptR);
        new ToolTip().SetToolTip(btnOpenScriptR, "Open weapon script in default editor");
        nudDamageGenericR = new NumericUpDown { Location = new Point(iX + 415, 6), Size = new Size(65, 23), DecimalPlaces = 1, Increment = 1m, Minimum = 0m, Maximum = 999m, Value = 0m };
        nudDamageGenericR.ValueChanged += (s, e) => { ScheduleUndo(); UpdateAllDamage(); LogService.DebugDebounce("nud_Dmg_R", $"Dmg R: {nudDamageGenericR.Value}", 500); };
        nudDamageGenericR.MouseUp += (_, _) => PushUndoNow();
        this.Controls.Add(nudDamageGenericR);
        this.Controls.Add(new Label { Text = "Dmg", Location = new Point(iX + 485, 8), AutoSize = true });

        CreateDamageMultiplierGroup(iX, false);
        CreateRangeGroup(iX, false);
        CreateSpreadRecoilAndPropertiesGroups(iX, false);
        CreateSpreadMultiplierGroup(iX, false);
        CreateOtherStatsGroup(iX, false);
    }
}