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
        if (wCurrentLeft == null) return;
        double dHm = trkHeadL.Value * dSliderStep, dCm = trkChestL.Value * dSliderStep, dSm = trkStomachL.Value * dSliderStep;
        double dLm = trkLegL.Value * dSliderStep, dAm = trkArmL.Value * dSliderStep;
        double dDist = trkDistanceL.Value, dDg = (double)nudDamageGenericL.Value, dRm = (double)nudRangeModifierL.Value;
        double dBd = dDg * Math.Pow(dRm, dDist / dDistanceDivisor);
        int iPellets = (int)nudBulletsPerShotL.Value;
        double dVest = chkVestL.Checked ? (iPellets > 1 ? 0.8 : 0.9) : 1.0;//普通0.9x 霰弹0.8x
        int iRpm = (int)nudFireRateL.Value;
        if (nudSecondaryFireRateL.Focused && nudSecondaryFireRateL.Value > 0)
            iRpm = (int)nudSecondaryFireRateL.Value;
        var (iBurstCount, dBurstInterval) = ParseBurstInfo(txtFireModesL.Text);
        UpdateDamageLabel(lblHeadDmgL, dBd * dHm * iPellets, 100, iRpm, iBurstCount, dBurstInterval);
        UpdateDamageLabel(lblChestDmgL, dBd * dCm * dVest * iPellets, 100, iRpm, iBurstCount, dBurstInterval);
        UpdateDamageLabel(lblStomachDmgL, dBd * dSm * dVest * iPellets, 100, iRpm, iBurstCount, dBurstInterval);
        UpdateDamageLabel(lblLegDmgL, dBd * dLm * iPellets, 100, iRpm, iBurstCount, dBurstInterval);
        UpdateDamageLabel(lblArmDmgL, dBd * dAm * iPellets, 100, iRpm, iBurstCount, dBurstInterval);
    }

    private void UpdateRightDamage()
    {
        if (wCurrentRight == null) return;
        double dHm = trkHeadR.Value * dSliderStep, dCm = trkChestR.Value * dSliderStep, dSm = trkStomachR.Value * dSliderStep;
        double dLm = trkLegR.Value * dSliderStep, dAm = trkArmR.Value * dSliderStep;
        double dDist = trkDistanceR.Value, dDg = (double)nudDamageGenericR.Value, dRm = (double)nudRangeModifierR.Value;
        double dBd = dDg * Math.Pow(dRm, dDist / dDistanceDivisor);//基伤*衰减^(距离/12.7)
        int iPellets = (int)nudBulletsPerShotR.Value;
        double dVest = chkVestR.Checked ? (iPellets > 1 ? 0.8 : 0.9) : 1.0;
        int iRpm = (int)nudFireRateR.Value;
        // 如果SecondaryFireRate的nud有焦点且值为正整数 使用它
        if (nudSecondaryFireRateR.Focused && nudSecondaryFireRateR.Value > 0)
            iRpm = (int)nudSecondaryFireRateR.Value;
        var (iBurstCount, dBurstInterval) = ParseBurstInfo(txtFireModesR.Text);
        UpdateDamageLabel(lblHeadDmgR, dBd * dHm * iPellets, 100, iRpm, iBurstCount, dBurstInterval);
        UpdateDamageLabel(lblChestDmgR, dBd * dCm * dVest * iPellets, 100, iRpm, iBurstCount, dBurstInterval);
        UpdateDamageLabel(lblStomachDmgR, dBd * dSm * dVest * iPellets, 100, iRpm, iBurstCount, dBurstInterval);
        UpdateDamageLabel(lblLegDmgR, dBd * dLm * iPellets, 100, iRpm, iBurstCount, dBurstInterval);
        UpdateDamageLabel(lblArmDmgR, dBd * dAm * iPellets, 100, iRpm, iBurstCount, dBurstInterval);
    }

    private static (int iBurstCount, double dBurstInterval) ParseBurstInfo(string? sFireModes)
    {
        if (string.IsNullOrEmpty(sFireModes)) return (0, 0);
        if (sFireModes.Contains("Burst", StringComparison.OrdinalIgnoreCase))
            return (3, 1.0);
        return (0, 0);
    }

    private void UpdateDamageLabel(Label lbl, double dDamage, double dHp, int iRpm, int iBurstCount, double dBurstInterval)
    {
        if (dDamage <= 0 || iRpm <= 0) { lbl.Text = "= 0.0 | ∞shots | ∞ms"; return; }
        int iShots = (int)Math.Ceiling(dHp / dDamage);
        double dTtkMs;
        if (iBurstCount > 0 && dBurstInterval > 0)
        {
            int iFullBursts = (iShots - 1) / iBurstCount;
            int iRemainingShots = iShots - iFullBursts * iBurstCount;
            double dShotInterval = 60000.0 / iRpm;
            dTtkMs = iFullBursts * ((iBurstCount - 1) * dShotInterval + dBurstInterval * 1000.0);
            dTtkMs += (iRemainingShots - 1) * dShotInterval;
        }
        else
        {
            dTtkMs = (iShots - 1) * 60000.0 / iRpm;
        }
        lbl.Text = $"= {dDamage:F1} | {iShots}shots | {dTtkMs:F0}ms";
    }

    #endregion
    #region 控件值加载与保存

    private static decimal ClampNud(decimal decValue, NumericUpDown nud)
    {
        if (decValue < nud.Minimum) return nud.Minimum;
        if (decValue > nud.Maximum) return nud.Maximum;
        return decValue;
    }

    private void LoadWeaponToControls(WeaponData wWeapon, bool bIsLeft)
    {
        if (bIsLeft)
        {
            SetControlsValue(trkHeadL, nudHeadL, wWeapon.DamageHeadMultiplier ?? 1.0);
            SetControlsValue(trkChestL, nudChestL, wWeapon.DamageChestMultiplier ?? 1.0);
            SetControlsValue(trkStomachL, nudStomachL, wWeapon.DamageStomachMultiplier ?? 1.0);
            SetControlsValue(trkLegL, nudLegL, wWeapon.DamageLegMultiplier ?? 1.0);
            SetControlsValue(trkArmL, nudArmL, wWeapon.DamageArmMultiplier ?? 1.0);
            nudHipSpreadL.Value = ClampNud((decimal)(wWeapon.BulletSpread ?? 1.0), nudHipSpreadL);
            nudAdsSpreadL.Value = ClampNud((decimal)(wWeapon.BulletSpreadDegreesIronsighted ?? 1.0), nudAdsSpreadL);
            nudBipodHipSpreadL.Value = ClampNud((decimal)(wWeapon.BulletSpreadDegreesBipod ?? 0), nudBipodHipSpreadL);
            nudBipodAdsSpreadL.Value = ClampNud((decimal)(wWeapon.BulletSpreadDegreesBipodIronsighted ?? 0), nudBipodAdsSpreadL);
            nudHipRecoilUpL.Value = ClampNud((decimal)(wWeapon.ViewSlideRecoilUp ?? 0), nudHipRecoilUpL);
            nudHipRecoilRightL.Value = ClampNud((decimal)(wWeapon.ViewSlideRecoilRight ?? 0), nudHipRecoilRightL);
            nudAdsRecoilUpL.Value = ClampNud((decimal)(wWeapon.ViewSlideRecoilIronsightUp ?? 0), nudAdsRecoilUpL);
            nudAdsRecoilRightL.Value = ClampNud((decimal)(wWeapon.ViewSlideRecoilIronsightRight ?? 0), nudAdsRecoilRightL);
            txtFireModesL.Text = wWeapon.FireModes ?? "";
            nudFireRateL.Value = ClampNud(wWeapon.FireRate ?? 0, nudFireRateL);
            nudSecondaryFireRateL.Value = ClampNud(wWeapon.SecondaryFireRate ?? -1, nudSecondaryFireRateL);
            nudRangeModifierL.Value = ClampNud((decimal)(wWeapon.RangeModifier ?? 1.0), nudRangeModifierL);
            txtCapacityL.Text = wWeapon.ClipSize ?? wWeapon.DefaultClip?.ToString() ?? "";
            nudExtraBulletChamberL.Value = ClampNud(wWeapon.ExtraBulletChamber ?? 0, nudExtraBulletChamberL);
            nudBulletsPerShotL.Value = ClampNud(wWeapon.BulletsPerShot ?? 1, nudBulletsPerShotL);
            nudIronsightSpeedScaleL.Value = ClampNud((decimal)(wWeapon.IronsightSpeedScale ?? 1.0), nudIronsightSpeedScaleL);
            nudIronSightL.Value = ClampNud(wWeapon.IronSight ?? 1, nudIronSightL);
            nudWeightL.Value = ClampNud((decimal)(wWeapon.Weight ?? 0), nudWeightL);
            nudZMBuyPriceL.Value = ClampNud(wWeapon.ZMBuyPrice ?? 0, nudZMBuyPriceL);
            nudZMWeightL.Value = ClampNud(wWeapon.ZMWeight ?? 0, nudZMWeightL);
            nudMetalPenL.Value = ClampNud((decimal)(wWeapon.MetalPenetrationDepth ?? 0), nudMetalPenL);
            nudGlassPenL.Value = ClampNud((decimal)(wWeapon.GlassPenetrationDepth ?? 0), nudGlassPenL);
            nudConcretePenL.Value = ClampNud((decimal)(wWeapon.ConcretePenetrationDepth ?? 0), nudConcretePenL);
            nudWoodPenL.Value = ClampNud((decimal)(wWeapon.WoodPenetrationDepth ?? 0), nudWoodPenL);
            nudOtherPenL.Value = ClampNud((decimal)(wWeapon.OtherPenetrationDepth ?? 0), nudOtherPenL);
            nudMetalDmgModL.Value = ClampNud((decimal)(wWeapon.MetalDamageModifier ?? 1.0), nudMetalDmgModL);
            nudGlassDmgModL.Value = ClampNud((decimal)(wWeapon.GlassDamageModifier ?? 1.0), nudGlassDmgModL);
            nudConcreteDmgModL.Value = ClampNud((decimal)(wWeapon.ConcreteDamageModifier ?? 1.0), nudConcreteDmgModL);
            nudWoodDmgModL.Value = ClampNud((decimal)(wWeapon.WoodDamageModifier ?? 1.0), nudWoodDmgModL);
            nudOtherDmgModL.Value = ClampNud((decimal)(wWeapon.OtherDamageModifier ?? 1.0), nudOtherDmgModL);
            nudCrouchSpreadL.Value = ClampNud((decimal)(wWeapon.CrouchSpreadMultiplier ?? 0), nudCrouchSpreadL);
            nudProneSpreadL.Value = ClampNud((decimal)(wWeapon.ProneSpreadMultiplier ?? 0), nudProneSpreadL);
            nudStandMoveSpreadL.Value = ClampNud((decimal)(wWeapon.StandMoveSpreadMultiplier ?? 0), nudStandMoveSpreadL);
            nudSneakMoveSpreadL.Value = ClampNud((decimal)(wWeapon.SneakMoveSpreadMultiplier ?? 0), nudSneakMoveSpreadL);
            nudCrouchMoveSpreadL.Value = ClampNud((decimal)(wWeapon.CrouchMoveSpreadMultiplier ?? 0), nudCrouchMoveSpreadL);
            nudJumpSpreadL.Value = ClampNud((decimal)(wWeapon.JumpSpreadMultiplier ?? 0), nudJumpSpreadL);
            nudDamageGenericL.Value = ClampNud((decimal)(wWeapon.DamageGeneric ?? 0), nudDamageGenericL);

            if (wWeapon.IronSight == 0)
            {
                nudAdsSpreadL.Enabled = false;
                nudAdsRecoilUpL.Enabled = false;
                nudAdsRecoilRightL.Enabled = false;
                nudIronsightSpeedScaleL.Enabled = false;
            }
        }
        else
        {
            SetControlsValue(trkHeadR, nudHeadR, wWeapon.DamageHeadMultiplier ?? 1.0);
            SetControlsValue(trkChestR, nudChestR, wWeapon.DamageChestMultiplier ?? 1.0);
            SetControlsValue(trkStomachR, nudStomachR, wWeapon.DamageStomachMultiplier ?? 1.0);
            SetControlsValue(trkLegR, nudLegR, wWeapon.DamageLegMultiplier ?? 1.0);
            SetControlsValue(trkArmR, nudArmR, wWeapon.DamageArmMultiplier ?? 1.0);
            nudHipSpreadR.Value = ClampNud((decimal)(wWeapon.BulletSpread ?? 1.0), nudHipSpreadR);
            nudAdsSpreadR.Value = ClampNud((decimal)(wWeapon.BulletSpreadDegreesIronsighted ?? 1.0), nudAdsSpreadR);
            nudBipodHipSpreadR.Value = ClampNud((decimal)(wWeapon.BulletSpreadDegreesBipod ?? 0), nudBipodHipSpreadR);
            nudBipodAdsSpreadR.Value = ClampNud((decimal)(wWeapon.BulletSpreadDegreesBipodIronsighted ?? 0), nudBipodAdsSpreadR);
            nudHipRecoilUpR.Value = ClampNud((decimal)(wWeapon.ViewSlideRecoilUp ?? 0), nudHipRecoilUpR);
            nudHipRecoilRightR.Value = ClampNud((decimal)(wWeapon.ViewSlideRecoilRight ?? 0), nudHipRecoilRightR);
            nudAdsRecoilUpR.Value = ClampNud((decimal)(wWeapon.ViewSlideRecoilIronsightUp ?? 0), nudAdsRecoilUpR);
            nudAdsRecoilRightR.Value = ClampNud((decimal)(wWeapon.ViewSlideRecoilIronsightRight ?? 0), nudAdsRecoilRightR);
            txtFireModesR.Text = wWeapon.FireModes ?? "";
            nudFireRateR.Value = ClampNud(wWeapon.FireRate ?? 0, nudFireRateR);
            nudSecondaryFireRateR.Value = ClampNud(wWeapon.SecondaryFireRate ?? -1, nudSecondaryFireRateR);
            nudRangeModifierR.Value = ClampNud((decimal)(wWeapon.RangeModifier ?? 1.0), nudRangeModifierR);
            txtCapacityR.Text = wWeapon.ClipSize ?? wWeapon.DefaultClip?.ToString() ?? "";
            nudExtraBulletChamberR.Value = ClampNud(wWeapon.ExtraBulletChamber ?? 0, nudExtraBulletChamberR);
            nudBulletsPerShotR.Value = ClampNud(wWeapon.BulletsPerShot ?? 1, nudBulletsPerShotR);
            nudIronsightSpeedScaleR.Value = ClampNud((decimal)(wWeapon.IronsightSpeedScale ?? 1.0), nudIronsightSpeedScaleR);
            nudIronSightR.Value = ClampNud(wWeapon.IronSight ?? 1, nudIronSightR);
            nudWeightR.Value = ClampNud((decimal)(wWeapon.Weight ?? 0), nudWeightR);
            nudZMBuyPriceR.Value = ClampNud(wWeapon.ZMBuyPrice ?? 0, nudZMBuyPriceR);
            nudZMWeightR.Value = ClampNud(wWeapon.ZMWeight ?? 0, nudZMWeightR);
            nudMetalPenR.Value = ClampNud((decimal)(wWeapon.MetalPenetrationDepth ?? 0), nudMetalPenR);
            nudGlassPenR.Value = ClampNud((decimal)(wWeapon.GlassPenetrationDepth ?? 0), nudGlassPenR);
            nudConcretePenR.Value = ClampNud((decimal)(wWeapon.ConcretePenetrationDepth ?? 0), nudConcretePenR);
            nudWoodPenR.Value = ClampNud((decimal)(wWeapon.WoodPenetrationDepth ?? 0), nudWoodPenR);
            nudOtherPenR.Value = ClampNud((decimal)(wWeapon.OtherPenetrationDepth ?? 0), nudOtherPenR);
            nudMetalDmgModR.Value = ClampNud((decimal)(wWeapon.MetalDamageModifier ?? 1.0), nudMetalDmgModR);
            nudGlassDmgModR.Value = ClampNud((decimal)(wWeapon.GlassDamageModifier ?? 1.0), nudGlassDmgModR);
            nudConcreteDmgModR.Value = ClampNud((decimal)(wWeapon.ConcreteDamageModifier ?? 1.0), nudConcreteDmgModR);
            nudWoodDmgModR.Value = ClampNud((decimal)(wWeapon.WoodDamageModifier ?? 1.0), nudWoodDmgModR);
            nudOtherDmgModR.Value = ClampNud((decimal)(wWeapon.OtherDamageModifier ?? 1.0), nudOtherDmgModR);
            nudCrouchSpreadR.Value = ClampNud((decimal)(wWeapon.CrouchSpreadMultiplier ?? 0), nudCrouchSpreadR);
            nudProneSpreadR.Value = ClampNud((decimal)(wWeapon.ProneSpreadMultiplier ?? 0), nudProneSpreadR);
            nudStandMoveSpreadR.Value = ClampNud((decimal)(wWeapon.StandMoveSpreadMultiplier ?? 0), nudStandMoveSpreadR);
            nudSneakMoveSpreadR.Value = ClampNud((decimal)(wWeapon.SneakMoveSpreadMultiplier ?? 0), nudSneakMoveSpreadR);
            nudCrouchMoveSpreadR.Value = ClampNud((decimal)(wWeapon.CrouchMoveSpreadMultiplier ?? 0), nudCrouchMoveSpreadR);
            nudJumpSpreadR.Value = ClampNud((decimal)(wWeapon.JumpSpreadMultiplier ?? 0), nudJumpSpreadR);
            nudDamageGenericR.Value = ClampNud((decimal)(wWeapon.DamageGeneric ?? 0), nudDamageGenericR);

            if (wWeapon.IronSight == 0)
            {
                nudAdsSpreadR.Enabled = false;
                nudAdsRecoilUpR.Enabled = false;
                nudAdsRecoilRightR.Enabled = false;
                nudIronsightSpeedScaleR.Enabled = false;
            }
        }
    }

    private void SetControlsValue(TrackBar tb, NumericUpDown nud, double dV)
    {
        int iIv = (int)Math.Round(dV / dSliderStep);
        iIv = Math.Max(tb.Minimum, Math.Min(tb.Maximum, iIv));
        tb.Value = iIv;
        nud.Value = Math.Round((decimal)dV, 2);
    }

    private void SaveControlsToWeapon(WeaponData wWeapon, bool bIsLeft)
    {
        if (bIsLeft)
        {
            wWeapon.DamageHeadMultiplier = trkHeadL.Value * dSliderStep;
            wWeapon.DamageChestMultiplier = trkChestL.Value * dSliderStep;
            wWeapon.DamageStomachMultiplier = trkStomachL.Value * dSliderStep;
            wWeapon.DamageLegMultiplier = trkLegL.Value * dSliderStep;
            wWeapon.DamageArmMultiplier = trkArmL.Value * dSliderStep;
            wWeapon.BulletSpread = (double)nudHipSpreadL.Value;
            wWeapon.BulletSpreadDegreesIronsighted = (double)nudAdsSpreadL.Value;
            wWeapon.BulletSpreadDegreesBipod = (double)nudBipodHipSpreadL.Value;
            wWeapon.BulletSpreadDegreesBipodIronsighted = (double)nudBipodAdsSpreadL.Value;
            wWeapon.ViewSlideRecoilUp = (double)nudHipRecoilUpL.Value;
            wWeapon.ViewSlideRecoilRight = (double)nudHipRecoilRightL.Value;
            wWeapon.ViewSlideRecoilIronsightUp = (double)nudAdsRecoilUpL.Value;
            wWeapon.ViewSlideRecoilIronsightRight = (double)nudAdsRecoilRightL.Value;
            wWeapon.FireModes = txtFireModesL.Text;
            wWeapon.FireRate = (int)nudFireRateL.Value;
            wWeapon.SecondaryFireRate = (int)nudSecondaryFireRateL.Value;
            wWeapon.RangeModifier = (double)nudRangeModifierL.Value;
            wWeapon.ClipSize = txtCapacityL.Text;
            var rgClipParts = txtCapacityL.Text.Split('/');//从字段拆分出DefaultClip 如30/120取30
            if (rgClipParts.Length > 0 && int.TryParse(rgClipParts[0], out int iFirstNum)) wWeapon.DefaultClip = iFirstNum;
            wWeapon.ExtraBulletChamber = (int)nudExtraBulletChamberL.Value;
            wWeapon.BulletsPerShot = (int)nudBulletsPerShotL.Value;
            wWeapon.IronsightSpeedScale = (double)nudIronsightSpeedScaleL.Value;
            wWeapon.IronSight = (int)nudIronSightL.Value;
            wWeapon.Weight = (double)nudWeightL.Value;
            wWeapon.ZMBuyPrice = (int)nudZMBuyPriceL.Value;
            wWeapon.ZMWeight = (int)nudZMWeightL.Value;
            wWeapon.MetalPenetrationDepth = (double)nudMetalPenL.Value;
            wWeapon.GlassPenetrationDepth = (double)nudGlassPenL.Value;
            wWeapon.ConcretePenetrationDepth = (double)nudConcretePenL.Value;
            wWeapon.WoodPenetrationDepth = (double)nudWoodPenL.Value;
            wWeapon.OtherPenetrationDepth = (double)nudOtherPenL.Value;
            wWeapon.MetalDamageModifier = (double)nudMetalDmgModL.Value;
            wWeapon.GlassDamageModifier = (double)nudGlassDmgModL.Value;
            wWeapon.ConcreteDamageModifier = (double)nudConcreteDmgModL.Value;
            wWeapon.WoodDamageModifier = (double)nudWoodDmgModL.Value;
            wWeapon.OtherDamageModifier = (double)nudOtherDmgModL.Value;
            wWeapon.CrouchSpreadMultiplier = (double)nudCrouchSpreadL.Value;
            wWeapon.ProneSpreadMultiplier = (double)nudProneSpreadL.Value;
            wWeapon.StandMoveSpreadMultiplier = (double)nudStandMoveSpreadL.Value;
            wWeapon.SneakMoveSpreadMultiplier = (double)nudSneakMoveSpreadL.Value;
            wWeapon.CrouchMoveSpreadMultiplier = (double)nudCrouchMoveSpreadL.Value;
            wWeapon.JumpSpreadMultiplier = (double)nudJumpSpreadL.Value;
            wWeapon.DamageGeneric = (double)nudDamageGenericL.Value;
        }
        else
        {
            wWeapon.DamageHeadMultiplier = trkHeadR.Value * dSliderStep;
            wWeapon.DamageChestMultiplier = trkChestR.Value * dSliderStep;
            wWeapon.DamageStomachMultiplier = trkStomachR.Value * dSliderStep;
            wWeapon.DamageLegMultiplier = trkLegR.Value * dSliderStep;
            wWeapon.DamageArmMultiplier = trkArmR.Value * dSliderStep;
            wWeapon.BulletSpread = (double)nudHipSpreadR.Value;
            wWeapon.BulletSpreadDegreesIronsighted = (double)nudAdsSpreadR.Value;
            wWeapon.BulletSpreadDegreesBipod = (double)nudBipodHipSpreadR.Value;
            wWeapon.BulletSpreadDegreesBipodIronsighted = (double)nudBipodAdsSpreadR.Value;
            wWeapon.ViewSlideRecoilUp = (double)nudHipRecoilUpR.Value;
            wWeapon.ViewSlideRecoilRight = (double)nudHipRecoilRightR.Value;
            wWeapon.ViewSlideRecoilIronsightUp = (double)nudAdsRecoilUpR.Value;
            wWeapon.ViewSlideRecoilIronsightRight = (double)nudAdsRecoilRightR.Value;
            wWeapon.FireModes = txtFireModesR.Text;
            wWeapon.FireRate = (int)nudFireRateR.Value;
            wWeapon.SecondaryFireRate = (int)nudSecondaryFireRateR.Value;
            wWeapon.RangeModifier = (double)nudRangeModifierR.Value;
            wWeapon.ClipSize = txtCapacityR.Text;
            var rgClipParts = txtCapacityR.Text.Split('/');
            if (rgClipParts.Length > 0 && int.TryParse(rgClipParts[0], out int iFirstNum)) wWeapon.DefaultClip = iFirstNum;
            wWeapon.ExtraBulletChamber = (int)nudExtraBulletChamberR.Value;
            wWeapon.BulletsPerShot = (int)nudBulletsPerShotR.Value;
            wWeapon.IronsightSpeedScale = (double)nudIronsightSpeedScaleR.Value;
            wWeapon.IronSight = (int)nudIronSightR.Value;
            wWeapon.Weight = (double)nudWeightR.Value;
            wWeapon.ZMBuyPrice = (int)nudZMBuyPriceR.Value;
            wWeapon.ZMWeight = (int)nudZMWeightR.Value;
            wWeapon.MetalPenetrationDepth = (double)nudMetalPenR.Value;
            wWeapon.GlassPenetrationDepth = (double)nudGlassPenR.Value;
            wWeapon.ConcretePenetrationDepth = (double)nudConcretePenR.Value;
            wWeapon.WoodPenetrationDepth = (double)nudWoodPenR.Value;
            wWeapon.OtherPenetrationDepth = (double)nudOtherPenR.Value;
            wWeapon.MetalDamageModifier = (double)nudMetalDmgModR.Value;
            wWeapon.GlassDamageModifier = (double)nudGlassDmgModR.Value;
            wWeapon.ConcreteDamageModifier = (double)nudConcreteDmgModR.Value;
            wWeapon.WoodDamageModifier = (double)nudWoodDmgModR.Value;
            wWeapon.OtherDamageModifier = (double)nudOtherDmgModR.Value;
            wWeapon.CrouchSpreadMultiplier = (double)nudCrouchSpreadR.Value;
            wWeapon.ProneSpreadMultiplier = (double)nudProneSpreadR.Value;
            wWeapon.StandMoveSpreadMultiplier = (double)nudStandMoveSpreadR.Value;
            wWeapon.SneakMoveSpreadMultiplier = (double)nudSneakMoveSpreadR.Value;
            wWeapon.CrouchMoveSpreadMultiplier = (double)nudCrouchMoveSpreadR.Value;
            wWeapon.JumpSpreadMultiplier = (double)nudJumpSpreadR.Value;
            wWeapon.DamageGeneric = (double)nudDamageGenericR.Value;
        }
    }
    #endregion
}