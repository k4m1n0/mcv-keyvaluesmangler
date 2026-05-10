using System;
using System.Windows.Forms;
using WeaponDamageCalc.Models;

namespace WeaponDamageCalc;

public partial class Form1
{
    #region 伤害计算
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
        double dist = trkDistanceL.Value, dg = (double)nudDamageGenericL.Value, rm = (double)nudRangeModifierL.Value;
        double bd = dg * Math.Pow(rm, dist / DistanceDivisor);
        int pellets = (int)nudBulletsPerShotL.Value;
        double vest = chkVestL.Checked ? (pellets > 1 ? 0.8 : 0.9) : 1.0;//普通0.9x 霰弹0.8x
        int rpm = (int)nudFireRateL.Value;
        var (burstCount, burstInterval) = ParseBurstInfo(txtFireModesL.Text);
        UpdateDamageLabel(lblHeadDmgL, bd * hm * pellets, 100, rpm, burstCount, burstInterval);
        UpdateDamageLabel(lblChestDmgL, bd * cm * vest * pellets, 100, rpm, burstCount, burstInterval);
        UpdateDamageLabel(lblStomachDmgL, bd * sm * vest * pellets, 100, rpm, burstCount, burstInterval);
        UpdateDamageLabel(lblLegDmgL, bd * lm * pellets, 100, rpm, burstCount, burstInterval);
        UpdateDamageLabel(lblArmDmgL, bd * am * pellets, 100, rpm, burstCount, burstInterval);
    }

    private void UpdateRightDamage()
    {
        if (currentWeaponRight == null) return;
        double hm = trkHeadR.Value * SliderStep, cm = trkChestR.Value * SliderStep, sm = trkStomachR.Value * SliderStep;
        double lm = trkLegR.Value * SliderStep, am = trkArmR.Value * SliderStep;
        double dist = trkDistanceR.Value, dg = (double)nudDamageGenericR.Value, rm = (double)nudRangeModifierR.Value;
        double bd = dg * Math.Pow(rm, dist / DistanceDivisor);//基伤*衰减^(距离/12.7)
        int pellets = (int)nudBulletsPerShotR.Value;
        double vest = chkVestR.Checked ? (pellets > 1 ? 0.8 : 0.9) : 1.0;
        int rpm = (int)nudFireRateR.Value;
        var (burstCount, burstInterval) = ParseBurstInfo(txtFireModesR.Text);
        UpdateDamageLabel(lblHeadDmgR, bd * hm * pellets, 100, rpm, burstCount, burstInterval);
        UpdateDamageLabel(lblChestDmgR, bd * cm * vest * pellets, 100, rpm, burstCount, burstInterval);
        UpdateDamageLabel(lblStomachDmgR, bd * sm * vest * pellets, 100, rpm, burstCount, burstInterval);
        UpdateDamageLabel(lblLegDmgR, bd * lm * pellets, 100, rpm, burstCount, burstInterval);
        UpdateDamageLabel(lblArmDmgR, bd * am * pellets, 100, rpm, burstCount, burstInterval);
    }

    private static (int burstCount, double burstInterval) ParseBurstInfo(string? fireModes)
    {
        if (string.IsNullOrEmpty(fireModes)) return (0, 0);
        if (fireModes.Contains("Burst", StringComparison.OrdinalIgnoreCase))
            return (3, 1.0);
        return (0, 0);
    }

    private void UpdateDamageLabel(Label lbl, double damage, double hp, int rpm, int burstCount, double burstInterval)
    {
        if (damage <= 0 || rpm <= 0) { lbl.Text = "= 0.0 | ∞shots | ∞ms"; return; }
        int shots = (int)Math.Ceiling(hp / damage);
        double ttkMs;
        if (burstCount > 0 && burstInterval > 0)
        {
            int fullBursts = (shots - 1) / burstCount;
            int remainingShots = shots - fullBursts * burstCount;
            double shotInterval = 60000.0 / rpm;
            ttkMs = fullBursts * ((burstCount - 1) * shotInterval + burstInterval * 1000.0);
            ttkMs += (remainingShots - 1) * shotInterval;
        }
        else
        {
            ttkMs = (shots - 1) * 60000.0 / rpm;
        }
        lbl.Text = $"= {damage:F1} | {shots}shots | {ttkMs:F0}ms";
    }

    #endregion
    #region 控件值加载与保存

    private static decimal ClampNud(decimal value, NumericUpDown nud)
    {
        if (value < nud.Minimum) return nud.Minimum;
        if (value > nud.Maximum) return nud.Maximum;
        return value;
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
            nudHipSpreadL.Value = ClampNud((decimal)(w.BulletSpread ?? 1.0), nudHipSpreadL);
            nudAdsSpreadL.Value = ClampNud((decimal)(w.BulletSpreadDegreesIronsighted ?? 1.0), nudAdsSpreadL);
            nudBipodHipSpreadL.Value = ClampNud((decimal)(w.BulletSpreadDegreesBipod ?? 0), nudBipodHipSpreadL);
            nudBipodAdsSpreadL.Value = ClampNud((decimal)(w.BulletSpreadDegreesBipodIronsighted ?? 0), nudBipodAdsSpreadL);
            nudHipRecoilUpL.Value = ClampNud((decimal)(w.ViewSlideRecoilUp ?? 0), nudHipRecoilUpL);
            nudHipRecoilRightL.Value = ClampNud((decimal)(w.ViewSlideRecoilRight ?? 0), nudHipRecoilRightL);
            nudAdsRecoilUpL.Value = ClampNud((decimal)(w.ViewSlideRecoilIronsightUp ?? 0), nudAdsRecoilUpL);
            nudAdsRecoilRightL.Value = ClampNud((decimal)(w.ViewSlideRecoilIronsightRight ?? 0), nudAdsRecoilRightL);
            txtFireModesL.Text = w.FireModes ?? "";
            nudFireRateL.Value = ClampNud(w.FireRate ?? 0, nudFireRateL);
            nudRangeModifierL.Value = ClampNud((decimal)(w.RangeModifier ?? 1.0), nudRangeModifierL);
            txtCapacityL.Text = w.ClipSize ?? w.DefaultClip?.ToString() ?? "";
            nudExtraBulletChamberL.Value = ClampNud(w.ExtraBulletChamber ?? 0, nudExtraBulletChamberL);
            nudBulletsPerShotL.Value = ClampNud(w.BulletsPerShot ?? 1, nudBulletsPerShotL);
            nudIronsightSpeedScaleL.Value = ClampNud((decimal)(w.IronsightSpeedScale ?? 1.0), nudIronsightSpeedScaleL);
            nudWeightL.Value = ClampNud((decimal)(w.Weight ?? 0), nudWeightL);
            nudZMBuyPriceL.Value = ClampNud(w.ZMBuyPrice ?? 0, nudZMBuyPriceL);
            nudZMWeightL.Value = ClampNud(w.ZMWeight ?? 0, nudZMWeightL);
            nudMetalPenL.Value = ClampNud((decimal)(w.MetalPenetrationDepth ?? 0), nudMetalPenL);
            nudGlassPenL.Value = ClampNud((decimal)(w.GlassPenetrationDepth ?? 0), nudGlassPenL);
            nudConcretePenL.Value = ClampNud((decimal)(w.ConcretePenetrationDepth ?? 0), nudConcretePenL);
            nudWoodPenL.Value = ClampNud((decimal)(w.WoodPenetrationDepth ?? 0), nudWoodPenL);
            nudOtherPenL.Value = ClampNud((decimal)(w.OtherPenetrationDepth ?? 0), nudOtherPenL);
            nudMetalDmgModL.Value = ClampNud((decimal)(w.MetalDamageModifier ?? 1.0), nudMetalDmgModL);
            nudGlassDmgModL.Value = ClampNud((decimal)(w.GlassDamageModifier ?? 1.0), nudGlassDmgModL);
            nudConcreteDmgModL.Value = ClampNud((decimal)(w.ConcreteDamageModifier ?? 1.0), nudConcreteDmgModL);
            nudWoodDmgModL.Value = ClampNud((decimal)(w.WoodDamageModifier ?? 1.0), nudWoodDmgModL);
            nudOtherDmgModL.Value = ClampNud((decimal)(w.OtherDamageModifier ?? 1.0), nudOtherDmgModL);
            nudCrouchSpreadL.Value = ClampNud((decimal)(w.CrouchSpreadMultiplier ?? 0), nudCrouchSpreadL);
            nudProneSpreadL.Value = ClampNud((decimal)(w.ProneSpreadMultiplier ?? 0), nudProneSpreadL);
            nudStandMoveSpreadL.Value = ClampNud((decimal)(w.StandMoveSpreadMultiplier ?? 0), nudStandMoveSpreadL);
            nudSneakMoveSpreadL.Value = ClampNud((decimal)(w.SneakMoveSpreadMultiplier ?? 0), nudSneakMoveSpreadL);
            nudCrouchMoveSpreadL.Value = ClampNud((decimal)(w.CrouchMoveSpreadMultiplier ?? 0), nudCrouchMoveSpreadL);
            nudJumpSpreadL.Value = ClampNud((decimal)(w.JumpSpreadMultiplier ?? 0), nudJumpSpreadL);
            nudDamageGenericL.Value = ClampNud((decimal)(w.DamageGeneric ?? 0), nudDamageGenericL);
        }
        else
        {
            SetControlsValue(trkHeadR, nudHeadR, w.DamageHeadMultiplier ?? 1.0);
            SetControlsValue(trkChestR, nudChestR, w.DamageChestMultiplier ?? 1.0);
            SetControlsValue(trkStomachR, nudStomachR, w.DamageStomachMultiplier ?? 1.0);
            SetControlsValue(trkLegR, nudLegR, w.DamageLegMultiplier ?? 1.0);
            SetControlsValue(trkArmR, nudArmR, w.DamageArmMultiplier ?? 1.0);
            nudHipSpreadR.Value = ClampNud((decimal)(w.BulletSpread ?? 1.0), nudHipSpreadR);
            nudAdsSpreadR.Value = ClampNud((decimal)(w.BulletSpreadDegreesIronsighted ?? 1.0), nudAdsSpreadR);
            nudBipodHipSpreadR.Value = ClampNud((decimal)(w.BulletSpreadDegreesBipod ?? 0), nudBipodHipSpreadR);
            nudBipodAdsSpreadR.Value = ClampNud((decimal)(w.BulletSpreadDegreesBipodIronsighted ?? 0), nudBipodAdsSpreadR);
            nudHipRecoilUpR.Value = ClampNud((decimal)(w.ViewSlideRecoilUp ?? 0), nudHipRecoilUpR);
            nudHipRecoilRightR.Value = ClampNud((decimal)(w.ViewSlideRecoilRight ?? 0), nudHipRecoilRightR);
            nudAdsRecoilUpR.Value = ClampNud((decimal)(w.ViewSlideRecoilIronsightUp ?? 0), nudAdsRecoilUpR);
            nudAdsRecoilRightR.Value = ClampNud((decimal)(w.ViewSlideRecoilIronsightRight ?? 0), nudAdsRecoilRightR);
            txtFireModesR.Text = w.FireModes ?? "";
            nudFireRateR.Value = ClampNud(w.FireRate ?? 0, nudFireRateR);
            nudRangeModifierR.Value = ClampNud((decimal)(w.RangeModifier ?? 1.0), nudRangeModifierR);
            txtCapacityR.Text = w.ClipSize ?? w.DefaultClip?.ToString() ?? "";
            nudExtraBulletChamberR.Value = ClampNud(w.ExtraBulletChamber ?? 0, nudExtraBulletChamberR);
            nudBulletsPerShotR.Value = ClampNud(w.BulletsPerShot ?? 1, nudBulletsPerShotR);
            nudIronsightSpeedScaleR.Value = ClampNud((decimal)(w.IronsightSpeedScale ?? 1.0), nudIronsightSpeedScaleR);
            nudWeightR.Value = ClampNud((decimal)(w.Weight ?? 0), nudWeightR);
            nudZMBuyPriceR.Value = ClampNud(w.ZMBuyPrice ?? 0, nudZMBuyPriceR);
            nudZMWeightR.Value = ClampNud(w.ZMWeight ?? 0, nudZMWeightR);
            nudMetalPenR.Value = ClampNud((decimal)(w.MetalPenetrationDepth ?? 0), nudMetalPenR);
            nudGlassPenR.Value = ClampNud((decimal)(w.GlassPenetrationDepth ?? 0), nudGlassPenR);
            nudConcretePenR.Value = ClampNud((decimal)(w.ConcretePenetrationDepth ?? 0), nudConcretePenR);
            nudWoodPenR.Value = ClampNud((decimal)(w.WoodPenetrationDepth ?? 0), nudWoodPenR);
            nudOtherPenR.Value = ClampNud((decimal)(w.OtherPenetrationDepth ?? 0), nudOtherPenR);
            nudMetalDmgModR.Value = ClampNud((decimal)(w.MetalDamageModifier ?? 1.0), nudMetalDmgModR);
            nudGlassDmgModR.Value = ClampNud((decimal)(w.GlassDamageModifier ?? 1.0), nudGlassDmgModR);
            nudConcreteDmgModR.Value = ClampNud((decimal)(w.ConcreteDamageModifier ?? 1.0), nudConcreteDmgModR);
            nudWoodDmgModR.Value = ClampNud((decimal)(w.WoodDamageModifier ?? 1.0), nudWoodDmgModR);
            nudOtherDmgModR.Value = ClampNud((decimal)(w.OtherDamageModifier ?? 1.0), nudOtherDmgModR);
            nudCrouchSpreadR.Value = ClampNud((decimal)(w.CrouchSpreadMultiplier ?? 0), nudCrouchSpreadR);
            nudProneSpreadR.Value = ClampNud((decimal)(w.ProneSpreadMultiplier ?? 0), nudProneSpreadR);
            nudStandMoveSpreadR.Value = ClampNud((decimal)(w.StandMoveSpreadMultiplier ?? 0), nudStandMoveSpreadR);
            nudSneakMoveSpreadR.Value = ClampNud((decimal)(w.SneakMoveSpreadMultiplier ?? 0), nudSneakMoveSpreadR);
            nudCrouchMoveSpreadR.Value = ClampNud((decimal)(w.CrouchMoveSpreadMultiplier ?? 0), nudCrouchMoveSpreadR);
            nudJumpSpreadR.Value = ClampNud((decimal)(w.JumpSpreadMultiplier ?? 0), nudJumpSpreadR);
            nudDamageGenericR.Value = ClampNud((decimal)(w.DamageGeneric ?? 0), nudDamageGenericR);
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
            var clipParts = txtCapacityL.Text.Split('/');//从字段拆分出DefaultClip 如30/120取30
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
    #endregion
}