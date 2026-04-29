using System.Windows.Forms;
using WeaponDamageCalc.Models;

namespace WeaponDamageCalc;

public partial class Form1
{
    private void UpdateAllDamage()
    {
        UpdateLeftDamage();
        UpdateRightDamage();
    }

    private void UpdateLeftDamage()
    {
        if (currentWeaponLeft == null) return;
        double hm = trkHeadL.Value * SliderStep, cm = trkChestL.Value * SliderStep, sm = trkStomachL.Value * SliderStep;
        double lm = trkLegL.Value * SliderStep, am = trkArmL.Value * SliderStep;
        double dist = trkDistanceL.Value, dg = currentWeaponLeft.DamageGeneric ?? 0, rm = (double)nudRangeModifierL.Value;
        double bd = dg * Math.Pow(rm, dist / DistanceDivisor);
        double vest = chkVestL.Checked ? ((currentWeaponLeft.BulletsPerShot ?? 1) > 1 ? 0.8 : 0.9) : 1.0;
        int rpm = currentWeaponLeft.FireRate ?? 600;
        int pellets = currentWeaponLeft.BulletsPerShot ?? 1;
        UpdateDamageLabel(lblHeadDmgL, bd * hm * pellets, 100, rpm);
        UpdateDamageLabel(lblChestDmgL, bd * cm * vest * pellets, 100, rpm);
        UpdateDamageLabel(lblStomachDmgL, bd * sm * vest * pellets, 100, rpm);
        UpdateDamageLabel(lblLegDmgL, bd * lm * pellets, 100, rpm);
        UpdateDamageLabel(lblArmDmgL, bd * am * pellets, 100, rpm);
    }

    private void UpdateRightDamage()
    {
        if (currentWeaponRight == null) return;
        double hm = trkHeadR.Value * SliderStep, cm = trkChestR.Value * SliderStep, sm = trkStomachR.Value * SliderStep;
        double lm = trkLegR.Value * SliderStep, am = trkArmR.Value * SliderStep;
        double dist = trkDistanceR.Value, dg = currentWeaponRight.DamageGeneric ?? 0, rm = (double)nudRangeModifierR.Value;
        double bd = dg * Math.Pow(rm, dist / DistanceDivisor);
        double vest = chkVestR.Checked ? ((currentWeaponRight.BulletsPerShot ?? 1) > 1 ? 0.8 : 0.9) : 1.0;
        int rpm = currentWeaponRight.FireRate ?? 600;
        int pellets = currentWeaponRight.BulletsPerShot ?? 1;
        UpdateDamageLabel(lblHeadDmgR, bd * hm * pellets, 100, rpm);
        UpdateDamageLabel(lblChestDmgR, bd * cm * vest * pellets, 100, rpm);
        UpdateDamageLabel(lblStomachDmgR, bd * sm * vest * pellets, 100, rpm);
        UpdateDamageLabel(lblLegDmgR, bd * lm * pellets, 100, rpm);
        UpdateDamageLabel(lblArmDmgR, bd * am * pellets, 100, rpm);
    }

    private void UpdateDamageLabel(Label lbl, double damage, double hp, int rpm)
    {
        if (damage <= 0 || rpm <= 0) { lbl.Text = "= 0.0 | ∞shots | ∞ms"; return; }
        int shots = (int)Math.Ceiling(hp / damage);
        double ttkMs = (shots - 1) * 60000.0 / rpm;
        lbl.Text = $"= {damage:F1} | {shots}shots | {ttkMs:F0}ms";
    }

    private void LoadWeaponToControls(WeaponData w, bool isLeft)
    {
        if (isLeft)
        {
            SetControlsValue(trkHeadL, nudHeadL, w.DamageHeadMultiplier ?? 1.0);
            SetControlsValue(trkChestL, nudChestL, w.DamageChestMultiplier ?? 1.0);
            SetControlsValue(trkStomachL, nudStomachL, w.DamageStomachMultiplier ?? 1.0);
            SetControlsValue(trkLegL, nudLegL, w.DamageLegMultiplier ?? 1.0);
            SetControlsValue(trkArmL, nudArmL, w.DamageArmMultiplier ?? 1.0);
            nudHipSpreadL.Value = (decimal)(w.BulletSpread ?? 1.0);
            nudAdsSpreadL.Value = (decimal)(w.BulletSpreadDegreesIronsighted ?? 1.0);
            nudBipodHipSpreadL.Value = (decimal)(w.BulletSpreadDegreesBipod ?? 0);
            nudBipodAdsSpreadL.Value = (decimal)(w.BulletSpreadDegreesBipodIronsighted ?? 0);
            nudHipRecoilUpL.Value = (decimal)(w.ViewSlideRecoilUp ?? 0);
            nudHipRecoilRightL.Value = (decimal)(w.ViewSlideRecoilRight ?? 0);
            nudAdsRecoilUpL.Value = (decimal)(w.ViewSlideRecoilIronsightUp ?? 0);
            nudAdsRecoilRightL.Value = (decimal)(w.ViewSlideRecoilIronsightRight ?? 0);
            txtFireModesL.Text = w.FireModes ?? "";
            nudFireRateL.Value = w.FireRate ?? 0;
            nudRangeModifierL.Value = (decimal)(w.RangeModifier ?? 1.0);
            txtCapacityL.Text = w.ClipSize ?? w.DefaultClip?.ToString() ?? "";
            nudExtraBulletChamberL.Value = w.ExtraBulletChamber ?? 0;
            nudBulletsPerShotL.Value = w.BulletsPerShot ?? 1;
            nudIronsightSpeedScaleL.Value = (decimal)(w.IronsightSpeedScale ?? 1.0);
            nudWeightL.Value = (decimal)(w.Weight ?? 0);
            nudZMBuyPriceL.Value = w.ZMBuyPrice ?? 0;
            nudZMWeightL.Value = w.ZMWeight ?? 0;
            nudMetalPenL.Value = (decimal)(w.MetalPenetrationDepth ?? 0);
            nudGlassPenL.Value = (decimal)(w.GlassPenetrationDepth ?? 0);
            nudConcretePenL.Value = (decimal)(w.ConcretePenetrationDepth ?? 0);
            nudWoodPenL.Value = (decimal)(w.WoodPenetrationDepth ?? 0);
            nudOtherPenL.Value = (decimal)(w.OtherPenetrationDepth ?? 0);
            nudMetalDmgModL.Value = (decimal)(w.MetalDamageModifier ?? 1.0);
            nudGlassDmgModL.Value = (decimal)(w.GlassDamageModifier ?? 1.0);
            nudConcreteDmgModL.Value = (decimal)(w.ConcreteDamageModifier ?? 1.0);
            nudWoodDmgModL.Value = (decimal)(w.WoodDamageModifier ?? 1.0);
            nudOtherDmgModL.Value = (decimal)(w.OtherDamageModifier ?? 1.0);
            nudCrouchSpreadL.Value = (decimal)(w.CrouchSpreadMultiplier ?? 0);
            nudProneSpreadL.Value = (decimal)(w.ProneSpreadMultiplier ?? 0);
            nudStandMoveSpreadL.Value = (decimal)(w.StandMoveSpreadMultiplier ?? 0);
            nudSneakMoveSpreadL.Value = (decimal)(w.SneakMoveSpreadMultiplier ?? 0);
            nudCrouchMoveSpreadL.Value = (decimal)(w.CrouchMoveSpreadMultiplier ?? 0);
            nudJumpSpreadL.Value = (decimal)(w.JumpSpreadMultiplier ?? 0);
            nudDamageGenericL.Value = (decimal)(w.DamageGeneric ?? 0);
        }
        else
        {
            SetControlsValue(trkHeadR, nudHeadR, w.DamageHeadMultiplier ?? 1.0);
            SetControlsValue(trkChestR, nudChestR, w.DamageChestMultiplier ?? 1.0);
            SetControlsValue(trkStomachR, nudStomachR, w.DamageStomachMultiplier ?? 1.0);
            SetControlsValue(trkLegR, nudLegR, w.DamageLegMultiplier ?? 1.0);
            SetControlsValue(trkArmR, nudArmR, w.DamageArmMultiplier ?? 1.0);
            nudHipSpreadR.Value = (decimal)(w.BulletSpread ?? 1.0);
            nudAdsSpreadR.Value = (decimal)(w.BulletSpreadDegreesIronsighted ?? 1.0);
            nudBipodHipSpreadR.Value = (decimal)(w.BulletSpreadDegreesBipod ?? 0);
            nudBipodAdsSpreadR.Value = (decimal)(w.BulletSpreadDegreesBipodIronsighted ?? 0);
            nudHipRecoilUpR.Value = (decimal)(w.ViewSlideRecoilUp ?? 0);
            nudHipRecoilRightR.Value = (decimal)(w.ViewSlideRecoilRight ?? 0);
            nudAdsRecoilUpR.Value = (decimal)(w.ViewSlideRecoilIronsightUp ?? 0);
            nudAdsRecoilRightR.Value = (decimal)(w.ViewSlideRecoilIronsightRight ?? 0);
            txtFireModesR.Text = w.FireModes ?? "";
            nudFireRateR.Value = w.FireRate ?? 0;
            nudRangeModifierR.Value = (decimal)(w.RangeModifier ?? 1.0);
            txtCapacityR.Text = w.ClipSize ?? w.DefaultClip?.ToString() ?? "";
            nudExtraBulletChamberR.Value = w.ExtraBulletChamber ?? 0;
            nudBulletsPerShotR.Value = w.BulletsPerShot ?? 1;
            nudIronsightSpeedScaleR.Value = (decimal)(w.IronsightSpeedScale ?? 1.0);
            nudWeightR.Value = (decimal)(w.Weight ?? 0);
            nudZMBuyPriceR.Value = w.ZMBuyPrice ?? 0;
            nudZMWeightR.Value = w.ZMWeight ?? 0;
            nudMetalPenR.Value = (decimal)(w.MetalPenetrationDepth ?? 0);
            nudGlassPenR.Value = (decimal)(w.GlassPenetrationDepth ?? 0);
            nudConcretePenR.Value = (decimal)(w.ConcretePenetrationDepth ?? 0);
            nudWoodPenR.Value = (decimal)(w.WoodPenetrationDepth ?? 0);
            nudOtherPenR.Value = (decimal)(w.OtherPenetrationDepth ?? 0);
            nudMetalDmgModR.Value = (decimal)(w.MetalDamageModifier ?? 1.0);
            nudGlassDmgModR.Value = (decimal)(w.GlassDamageModifier ?? 1.0);
            nudConcreteDmgModR.Value = (decimal)(w.ConcreteDamageModifier ?? 1.0);
            nudWoodDmgModR.Value = (decimal)(w.WoodDamageModifier ?? 1.0);
            nudOtherDmgModR.Value = (decimal)(w.OtherDamageModifier ?? 1.0);
            nudCrouchSpreadR.Value = (decimal)(w.CrouchSpreadMultiplier ?? 0);
            nudProneSpreadR.Value = (decimal)(w.ProneSpreadMultiplier ?? 0);
            nudStandMoveSpreadR.Value = (decimal)(w.StandMoveSpreadMultiplier ?? 0);
            nudSneakMoveSpreadR.Value = (decimal)(w.SneakMoveSpreadMultiplier ?? 0);
            nudCrouchMoveSpreadR.Value = (decimal)(w.CrouchMoveSpreadMultiplier ?? 0);
            nudJumpSpreadR.Value = (decimal)(w.JumpSpreadMultiplier ?? 0);
            nudDamageGenericR.Value = (decimal)(w.DamageGeneric ?? 0);
        }
    }

    private void SetControlsValue(TrackBar tb, NumericUpDown nud, double v)
    {
        int iv = (int)Math.Round(v / SliderStep);
        iv = Math.Max(tb.Minimum, Math.Min(tb.Maximum, iv));
        tb.Value = iv;
        nud.Value = Math.Round((decimal)v, 2);
    }

    private void SaveControlsToWeapon(WeaponData w, bool isLeft)
    {
        if (isLeft)
        {
            w.DamageHeadMultiplier = trkHeadL.Value * SliderStep;
            w.DamageChestMultiplier = trkChestL.Value * SliderStep;
            w.DamageStomachMultiplier = trkStomachL.Value * SliderStep;
            w.DamageLegMultiplier = trkLegL.Value * SliderStep;
            w.DamageArmMultiplier = trkArmL.Value * SliderStep;
            w.BulletSpread = (double)nudHipSpreadL.Value;
            w.BulletSpreadDegreesIronsighted = (double)nudAdsSpreadL.Value;
            w.BulletSpreadDegreesBipod = (double)nudBipodHipSpreadL.Value;
            w.BulletSpreadDegreesBipodIronsighted = (double)nudBipodAdsSpreadL.Value;
            w.ViewSlideRecoilUp = (double)nudHipRecoilUpL.Value;
            w.ViewSlideRecoilRight = (double)nudHipRecoilRightL.Value;
            w.ViewSlideRecoilIronsightUp = (double)nudAdsRecoilUpL.Value;
            w.ViewSlideRecoilIronsightRight = (double)nudAdsRecoilRightL.Value;
            w.FireModes = txtFireModesL.Text;
            w.FireRate = (int)nudFireRateL.Value;
            w.RangeModifier = (double)nudRangeModifierL.Value;
            w.ClipSize = txtCapacityL.Text;
            var clipParts = txtCapacityL.Text.Split('/');
            if (clipParts.Length > 0 && int.TryParse(clipParts[0], out int firstNum)) w.DefaultClip = firstNum;
            w.ExtraBulletChamber = (int)nudExtraBulletChamberL.Value;
            w.BulletsPerShot = (int)nudBulletsPerShotL.Value;
            w.IronsightSpeedScale = (double)nudIronsightSpeedScaleL.Value;
            w.Weight = (double)nudWeightL.Value;
            w.ZMBuyPrice = (int)nudZMBuyPriceL.Value;
            w.ZMWeight = (int)nudZMWeightL.Value;
            w.MetalPenetrationDepth = (double)nudMetalPenL.Value;
            w.GlassPenetrationDepth = (double)nudGlassPenL.Value;
            w.ConcretePenetrationDepth = (double)nudConcretePenL.Value;
            w.WoodPenetrationDepth = (double)nudWoodPenL.Value;
            w.OtherPenetrationDepth = (double)nudOtherPenL.Value;
            w.MetalDamageModifier = (double)nudMetalDmgModL.Value;
            w.GlassDamageModifier = (double)nudGlassDmgModL.Value;
            w.ConcreteDamageModifier = (double)nudConcreteDmgModL.Value;
            w.WoodDamageModifier = (double)nudWoodDmgModL.Value;
            w.OtherDamageModifier = (double)nudOtherDmgModL.Value;
            w.CrouchSpreadMultiplier = (double)nudCrouchSpreadL.Value;
            w.ProneSpreadMultiplier = (double)nudProneSpreadL.Value;
            w.StandMoveSpreadMultiplier = (double)nudStandMoveSpreadL.Value;
            w.SneakMoveSpreadMultiplier = (double)nudSneakMoveSpreadL.Value;
            w.CrouchMoveSpreadMultiplier = (double)nudCrouchMoveSpreadL.Value;
            w.JumpSpreadMultiplier = (double)nudJumpSpreadL.Value;
            w.DamageGeneric = (double)nudDamageGenericL.Value;
        }
        else
        {
            w.DamageHeadMultiplier = trkHeadR.Value * SliderStep;
            w.DamageChestMultiplier = trkChestR.Value * SliderStep;
            w.DamageStomachMultiplier = trkStomachR.Value * SliderStep;
            w.DamageLegMultiplier = trkLegR.Value * SliderStep;
            w.DamageArmMultiplier = trkArmR.Value * SliderStep;
            w.BulletSpread = (double)nudHipSpreadR.Value;
            w.BulletSpreadDegreesIronsighted = (double)nudAdsSpreadR.Value;
            w.BulletSpreadDegreesBipod = (double)nudBipodHipSpreadR.Value;
            w.BulletSpreadDegreesBipodIronsighted = (double)nudBipodAdsSpreadR.Value;
            w.ViewSlideRecoilUp = (double)nudHipRecoilUpR.Value;
            w.ViewSlideRecoilRight = (double)nudHipRecoilRightR.Value;
            w.ViewSlideRecoilIronsightUp = (double)nudAdsRecoilUpR.Value;
            w.ViewSlideRecoilIronsightRight = (double)nudAdsRecoilRightR.Value;
            w.FireModes = txtFireModesR.Text;
            w.FireRate = (int)nudFireRateR.Value;
            w.RangeModifier = (double)nudRangeModifierR.Value;
            w.ClipSize = txtCapacityR.Text;
            var clipParts = txtCapacityR.Text.Split('/');
            if (clipParts.Length > 0 && int.TryParse(clipParts[0], out int firstNum)) w.DefaultClip = firstNum;
            w.ExtraBulletChamber = (int)nudExtraBulletChamberR.Value;
            w.BulletsPerShot = (int)nudBulletsPerShotR.Value;
            w.IronsightSpeedScale = (double)nudIronsightSpeedScaleR.Value;
            w.Weight = (double)nudWeightR.Value;
            w.ZMBuyPrice = (int)nudZMBuyPriceR.Value;
            w.ZMWeight = (int)nudZMWeightR.Value;
            w.MetalPenetrationDepth = (double)nudMetalPenR.Value;
            w.GlassPenetrationDepth = (double)nudGlassPenR.Value;
            w.ConcretePenetrationDepth = (double)nudConcretePenR.Value;
            w.WoodPenetrationDepth = (double)nudWoodPenR.Value;
            w.OtherPenetrationDepth = (double)nudOtherPenR.Value;
            w.MetalDamageModifier = (double)nudMetalDmgModR.Value;
            w.GlassDamageModifier = (double)nudGlassDmgModR.Value;
            w.ConcreteDamageModifier = (double)nudConcreteDmgModR.Value;
            w.WoodDamageModifier = (double)nudWoodDmgModR.Value;
            w.OtherDamageModifier = (double)nudOtherDmgModR.Value;
            w.CrouchSpreadMultiplier = (double)nudCrouchSpreadR.Value;
            w.ProneSpreadMultiplier = (double)nudProneSpreadR.Value;
            w.StandMoveSpreadMultiplier = (double)nudStandMoveSpreadR.Value;
            w.SneakMoveSpreadMultiplier = (double)nudSneakMoveSpreadR.Value;
            w.CrouchMoveSpreadMultiplier = (double)nudCrouchMoveSpreadR.Value;
            w.JumpSpreadMultiplier = (double)nudJumpSpreadR.Value;
            w.DamageGeneric = (double)nudDamageGenericR.Value;
        }
    }
}