using System;
using System.Drawing;
using System.Windows.Forms;
using WeaponDamageCalc.Models;
using WeaponDamageCalc.Services;

namespace WeaponDamageCalc;

public partial class Form1
{
    #region 模式切换
    private void ToggleAltStats(WeaponScriptService.AltStatMode amMode)
    {
        try
        {
            bool bLeftHas = WeaponHasAltStats(wCurrentLeft, amMode);
            bool bRightHas = WeaponHasAltStats(wCurrentRight, amMode);

            if (bShowingAltStats && amCurrentAltStat == amMode)
            {
                LogService.Info($"ToggleAltStats: exiting {amMode} mode");
                if ((wCurrentLeft != null && HasUnsavedChanges(true, bCheckBothSides: true))
                    || (wCurrentRight != null && HasUnsavedChanges(false, bCheckBothSides: true)))
                {
                    var drResult = MessageBox.Show("Unsaved alt stat changes will be lost. Discard?",
                        "Unsaved Changes", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                    if (drResult != DialogResult.Yes) return;
                }
                bShowingAltStats = false;
                bUpdatingControls = true;
                if (wCurrentLeft != null) { LoadWeaponToControls(wCurrentLeft, true); }
                if (wCurrentRight != null) { LoadWeaponToControls(wCurrentRight, false); }
                RestoreAllNudEnabled(true); RestoreAllNudEnabled(false);
                bUpdatingControls = false;
                ResetAltStatButtons();
                StoreSnapshot();
            }
            else
            {
                LogService.Info($"ToggleAltStats: entering {amMode} mode");
                if ((wCurrentLeft != null && HasUnsavedChanges(true, bCheckBothSides: true))
                    || (wCurrentRight != null && HasUnsavedChanges(false, bCheckBothSides: true)))
                {
                    var drResult = MessageBox.Show("Unsaved changes will be lost. Switch stats mode?",
                        "Unsaved Changes", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                    if (drResult != DialogResult.Yes) return;
                }
                PushUndo();
                bShowingAltStats = true; amCurrentAltStat = amMode;
                HighlightAltStatButton(amMode);
                bUpdatingControls = true;
                if (bLeftHas) { LoadAltStatsToControls(true, amMode); SetAltStatReadonly(true, amMode); }
                else { LoadWeaponToControls(wCurrentLeft!, true); RestoreAllNudEnabled(true); SetAltStatReadonly(true, amMode); }
                if (bRightHas) { LoadAltStatsToControls(false, amMode); SetAltStatReadonly(false, amMode); }
                else if (!ReferenceEquals(wCurrentLeft, wCurrentRight)) { LoadWeaponToControls(wCurrentRight!, false); RestoreAllNudEnabled(false); SetAltStatReadonly(false, amMode); }
                bUpdatingControls = false;
                StoreSnapshot();
            }
            UpdateAllDamage(); pnlSpread.Invalidate(); pnlRecoil.Invalidate();
        }
        catch (Exception ex)
        {
            LogService.Error(ex, $"ToggleAltStats: mode={amMode}");
        }
    }

    //高亮当前模式的按钮 另一个恢复默认
    private void HighlightAltStatButton(WeaponScriptService.AltStatMode amMode)
    {
        Color cInactive = bDarkMode ? Color.FromArgb(60, 60, 60) : SystemColors.Control;
        Color cActiveGreen = bDarkMode ? Color.FromArgb(40, 100, 40) : Color.LightGreen;
        Color cActiveYellow = bDarkMode ? Color.FromArgb(120, 100, 0) : Color.Yellow;

        foreach (Control ctrl in this.Controls)
        {
            if (ctrl is Button btn)
            {
                if (btn.Text == "DoV")
                {
                    bool bActive = amMode == WeaponScriptService.AltStatMode.Dov;
                    if (!bActive) { btn.BackColor = cInactive; continue; }
                    bool bHasAny = WeaponHasAnyAltStat(wCurrentLeft, WeaponScriptService.AltStatMode.Dov)
                        || WeaponHasAnyAltStat(wCurrentRight, WeaponScriptService.AltStatMode.Dov);
                    btn.BackColor = bHasAny ? cActiveGreen : cActiveYellow;
                }
                else if (btn.Text == "Zmb")
                {
                    bool bActive = amMode == WeaponScriptService.AltStatMode.Zombie;
                    if (!bActive) { btn.BackColor = cInactive; continue; }
                    bool bHasAny = WeaponHasAnyAltStat(wCurrentLeft, WeaponScriptService.AltStatMode.Zombie)
                        || WeaponHasAnyAltStat(wCurrentRight, WeaponScriptService.AltStatMode.Zombie);
                    btn.BackColor = bHasAny ? cActiveGreen : cActiveYellow;
                }
            }
        }
    }

    private bool WeaponHasAnyAltStat(WeaponData? wWeapon, WeaponScriptService.AltStatMode amMode)
    {
        if (wWeapon == null) return false;
        return WeaponScriptService.mpCsvToScript.Keys.Any(sK =>
            WeaponScriptService.GetFieldValue(wWeapon, sK, amMode) != null);
    }

    private void ResetAltStatButtons()
    {
        Color cInactive = bDarkMode ? Color.FromArgb(60, 60, 60) : SystemColors.Control;
        foreach (Control ctrl in this.Controls)
            if (ctrl is Button btn && (btn.Text == "DoV" || btn.Text == "Zmb")) btn.BackColor = cInactive;
    }

    #endregion
    #region 控件状态

    private void SetAltStatReadonly(bool bIsLeft, WeaponScriptService.AltStatMode amMode)
    {
        var wWeapon = bIsLeft ? wCurrentLeft : wCurrentRight;
        if (wWeapon != null)
        {
            bool bNoAds = GetAltStatIronSight(wWeapon, amMode) == 0;
            SetNudEnabled(bIsLeft ? nudAdsSpreadL : nudAdsSpreadR, !bNoAds);
            SetNudEnabled(bIsLeft ? nudAdsRecoilUpL : nudAdsRecoilUpR, !bNoAds);
            SetNudEnabled(bIsLeft ? nudAdsRecoilRightL : nudAdsRecoilRightR, !bNoAds);
            SetNudEnabled(bIsLeft ? nudIronsightSpeedScaleL : nudIronsightSpeedScaleR, !bNoAds);
        }
    }

    private static int? GetAltStatIronSight(WeaponData wWeapon, WeaponScriptService.AltStatMode amMode) => amMode switch
    {
        WeaponScriptService.AltStatMode.Dov => wWeapon.DovIronSight ?? wWeapon.IronSight,
        WeaponScriptService.AltStatMode.Zombie => wWeapon.ZombieIronSight ?? wWeapon.IronSight,
        _ => wWeapon.IronSight
    };

    private static void SetNudEnabled(NumericUpDown nud, bool bEnabled) => nud.Enabled = bEnabled;

    private static bool WeaponHasAltStats(WeaponData? wWeapon, WeaponScriptService.AltStatMode amMode) => amMode switch
    {
        WeaponScriptService.AltStatMode.Dov => wWeapon?.DovDamageGeneric != null || wWeapon?.DovFireRate != null || wWeapon?.DovZMBuyPrice != null || wWeapon?.DovZMWeight != null,
        WeaponScriptService.AltStatMode.Zombie => wWeapon?.ZombieClipSize != null || wWeapon?.ZombieDamageGeneric != null || wWeapon?.ZombieFireRate != null || wWeapon?.ZombieWeight != null,
        _ => false
    };

    #endregion
    #region 背景颜色

    private Color GetDefaultBackColor(bool bIsDark)
    {
        return bIsDark ? Color.FromArgb(50, 50, 50) : SystemColors.Window;
    }

    private Color GetAltStatBackColor(bool bIsDark, bool bIsDov)
    {
        return bIsDark
            ? (bIsDov ? Color.FromArgb(40, 50, 80) : Color.FromArgb(80, 50, 30))
            : (bIsDov ? Color.FromArgb(200, 220, 255) : Color.FromArgb(255, 220, 180));
    }

    private void SetAltStatBackColor(NumericUpDown nud, bool bHasAltValue, bool bIsDov)
    {
        nud.BackColor = bHasAltValue ? GetAltStatBackColor(bDarkMode, bIsDov) : GetDefaultBackColor(bDarkMode);
    }

    private void SetAltStatBackColor(TextBox txt, bool bHasAltValue, bool bIsDov)
    {
        txt.BackColor = bHasAltValue ? GetAltStatBackColor(bDarkMode, bIsDov) : GetDefaultBackColor(bDarkMode);
    }

    private void RestoreAltStatBackColors(bool bIsLeft)
    {
        Color cDefault = GetDefaultBackColor(bDarkMode);
        if (bIsLeft)
        {
            txtFireModesL.BackColor = cDefault;
            txtCapacityL.BackColor = cDefault;
        }
        else
        {
            txtFireModesR.BackColor = cDefault;
            txtCapacityR.BackColor = cDefault;
        }
    }

    #endregion
    #region 备选值加载

    private void LoadAltStatsToControls(bool bIsLeft, WeaponScriptService.AltStatMode amMode)
    {
        try
        {
        var wWeapon = bIsLeft ? wCurrentLeft : wCurrentRight;
        if (wWeapon == null) return;
        LogService.Debug($"LoadAltStatsToControls: isLeft={bIsLeft}, mode={amMode}, weapon={wWeapon.ScriptName}");
        var wTemp = new WeaponData();
        CopyWeaponDataFields(wWeapon, wTemp);

        bool bIsDov = amMode == WeaponScriptService.AltStatMode.Dov;

        int? nAltExtraBulletChamber = bIsDov ? wWeapon.DovExtraBulletChamber : wWeapon.ZombieExtraBulletChamber;
        wTemp.ExtraBulletChamber = nAltExtraBulletChamber ?? wWeapon.ExtraBulletChamber;
        SetAltStatBackColor(bIsLeft ? nudExtraBulletChamberL : nudExtraBulletChamberR,
            nAltExtraBulletChamber != null && nAltExtraBulletChamber != wWeapon.ExtraBulletChamber, bIsDov);

        int? nAltFireRate = bIsDov ? wWeapon.DovFireRate : wWeapon.ZombieFireRate;
        wTemp.FireRate = nAltFireRate ?? wWeapon.FireRate;
        SetAltStatBackColor(bIsLeft ? nudFireRateL : nudFireRateR,
            nAltFireRate != null && nAltFireRate != wWeapon.FireRate, bIsDov);

        double? fAltBulletSpread = bIsDov ? wWeapon.DovBulletSpread : wWeapon.ZombieBulletSpread;
        wTemp.BulletSpread = fAltBulletSpread ?? wWeapon.BulletSpread;
        SetAltStatBackColor(bIsLeft ? nudHipSpreadL : nudHipSpreadR,
            fAltBulletSpread != null && Math.Abs(fAltBulletSpread.Value - (wWeapon.BulletSpread ?? 0)) > 0.001, bIsDov);

        double? fAltBulletSpreadAds = bIsDov ? wWeapon.DovBulletSpreadDegreesIronsighted : wWeapon.ZombieBulletSpreadDegreesIronsighted;
        wTemp.BulletSpreadDegreesIronsighted = fAltBulletSpreadAds ?? wWeapon.BulletSpreadDegreesIronsighted;
        SetAltStatBackColor(bIsLeft ? nudAdsSpreadL : nudAdsSpreadR,
            fAltBulletSpreadAds != null && Math.Abs(fAltBulletSpreadAds.Value - (wWeapon.BulletSpreadDegreesIronsighted ?? 0)) > 0.001, bIsDov);

        double? fAltBulletSpreadBipod = bIsDov ? wWeapon.DovBulletSpreadDegreesBipod : wWeapon.ZombieBulletSpreadDegreesBipod;
        wTemp.BulletSpreadDegreesBipod = fAltBulletSpreadBipod ?? wWeapon.BulletSpreadDegreesBipod;
        SetAltStatBackColor(bIsLeft ? nudBipodHipSpreadL : nudBipodHipSpreadR,
            fAltBulletSpreadBipod != null && Math.Abs(fAltBulletSpreadBipod.Value - (wWeapon.BulletSpreadDegreesBipod ?? 0)) > 0.001, bIsDov);

        double? fAltBulletSpreadBipodAds = bIsDov ? wWeapon.DovBulletSpreadDegreesBipodIronsighted : wWeapon.ZombieBulletSpreadDegreesBipodIronsighted;
        wTemp.BulletSpreadDegreesBipodIronsighted = fAltBulletSpreadBipodAds ?? wWeapon.BulletSpreadDegreesBipodIronsighted;
        SetAltStatBackColor(bIsLeft ? nudBipodAdsSpreadL : nudBipodAdsSpreadR,
            fAltBulletSpreadBipodAds != null && Math.Abs(fAltBulletSpreadBipodAds.Value - (wWeapon.BulletSpreadDegreesBipodIronsighted ?? 0)) > 0.001, bIsDov);

        double? fAltRangeModifier = bIsDov ? wWeapon.DovRangeModifier : wWeapon.ZombieRangeModifier;
        wTemp.RangeModifier = fAltRangeModifier ?? wWeapon.RangeModifier;
        SetAltStatBackColor(bIsLeft ? nudRangeModifierL : nudRangeModifierR,
            fAltRangeModifier != null && Math.Abs(fAltRangeModifier.Value - (wWeapon.RangeModifier ?? 0)) > 0.001, bIsDov);

        double? fAltIronsightSpeedScale = bIsDov ? wWeapon.DovIronsightSpeedScale : wWeapon.ZombieIronsightSpeedScale;
        wTemp.IronsightSpeedScale = fAltIronsightSpeedScale ?? wWeapon.IronsightSpeedScale;
        SetAltStatBackColor(bIsLeft ? nudIronsightSpeedScaleL : nudIronsightSpeedScaleR,
            fAltIronsightSpeedScale != null && Math.Abs(fAltIronsightSpeedScale.Value - (wWeapon.IronsightSpeedScale ?? 0)) > 0.001, bIsDov);

        double? fAltCrouchSpread = bIsDov ? wWeapon.DovCrouchSpreadMultiplier : wWeapon.ZombieCrouchSpreadMultiplier;
        wTemp.CrouchSpreadMultiplier = fAltCrouchSpread ?? wWeapon.CrouchSpreadMultiplier;
        SetAltStatBackColor(bIsLeft ? nudCrouchSpreadL : nudCrouchSpreadR,
            fAltCrouchSpread != null && Math.Abs(fAltCrouchSpread.Value - (wWeapon.CrouchSpreadMultiplier ?? 0)) > 0.001, bIsDov);

        double? fAltProneSpread = bIsDov ? wWeapon.DovProneSpreadMultiplier : wWeapon.ZombieProneSpreadMultiplier;
        wTemp.ProneSpreadMultiplier = fAltProneSpread ?? wWeapon.ProneSpreadMultiplier;
        SetAltStatBackColor(bIsLeft ? nudProneSpreadL : nudProneSpreadR,
            fAltProneSpread != null && Math.Abs(fAltProneSpread.Value - (wWeapon.ProneSpreadMultiplier ?? 0)) > 0.001, bIsDov);

        double? fAltStandMoveSpread = bIsDov ? wWeapon.DovStandMoveSpreadMultiplier : wWeapon.ZombieStandMoveSpreadMultiplier;
        wTemp.StandMoveSpreadMultiplier = fAltStandMoveSpread ?? wWeapon.StandMoveSpreadMultiplier;
        SetAltStatBackColor(bIsLeft ? nudStandMoveSpreadL : nudStandMoveSpreadR,
            fAltStandMoveSpread != null && Math.Abs(fAltStandMoveSpread.Value - (wWeapon.StandMoveSpreadMultiplier ?? 0)) > 0.001, bIsDov);

        double? fAltSneakMoveSpread = bIsDov ? wWeapon.DovSneakMoveSpreadMultiplier : wWeapon.ZombieSneakMoveSpreadMultiplier;
        wTemp.SneakMoveSpreadMultiplier = fAltSneakMoveSpread ?? wWeapon.SneakMoveSpreadMultiplier;
        SetAltStatBackColor(bIsLeft ? nudSneakMoveSpreadL : nudSneakMoveSpreadR,
            fAltSneakMoveSpread != null && Math.Abs(fAltSneakMoveSpread.Value - (wWeapon.SneakMoveSpreadMultiplier ?? 0)) > 0.001, bIsDov);

        double? fAltCrouchMoveSpread = bIsDov ? wWeapon.DovCrouchMoveSpreadMultiplier : wWeapon.ZombieCrouchMoveSpreadMultiplier;
        wTemp.CrouchMoveSpreadMultiplier = fAltCrouchMoveSpread ?? wWeapon.CrouchMoveSpreadMultiplier;
        SetAltStatBackColor(bIsLeft ? nudCrouchMoveSpreadL : nudCrouchMoveSpreadR,
            fAltCrouchMoveSpread != null && Math.Abs(fAltCrouchMoveSpread.Value - (wWeapon.CrouchMoveSpreadMultiplier ?? 0)) > 0.001, bIsDov);

        double? fAltJumpSpread = bIsDov ? wWeapon.DovJumpSpreadMultiplier : wWeapon.ZombieJumpSpreadMultiplier;
        wTemp.JumpSpreadMultiplier = fAltJumpSpread ?? wWeapon.JumpSpreadMultiplier;
        SetAltStatBackColor(bIsLeft ? nudJumpSpreadL : nudJumpSpreadR,
            fAltJumpSpread != null && Math.Abs(fAltJumpSpread.Value - (wWeapon.JumpSpreadMultiplier ?? 0)) > 0.001, bIsDov);

        double? fAltRecoilUp = bIsDov ? wWeapon.DovViewSlideRecoilUp : wWeapon.ZombieViewSlideRecoilUp;
        wTemp.ViewSlideRecoilUp = fAltRecoilUp ?? wWeapon.ViewSlideRecoilUp;
        SetAltStatBackColor(bIsLeft ? nudHipRecoilUpL : nudHipRecoilUpR,
            fAltRecoilUp != null && Math.Abs(fAltRecoilUp.Value - (wWeapon.ViewSlideRecoilUp ?? 0)) > 0.001, bIsDov);

        double? fAltRecoilRight = bIsDov ? wWeapon.DovViewSlideRecoilRight : wWeapon.ZombieViewSlideRecoilRight;
        wTemp.ViewSlideRecoilRight = fAltRecoilRight ?? wWeapon.ViewSlideRecoilRight;
        SetAltStatBackColor(bIsLeft ? nudHipRecoilRightL : nudHipRecoilRightR,
            fAltRecoilRight != null && Math.Abs(fAltRecoilRight.Value - (wWeapon.ViewSlideRecoilRight ?? 0)) > 0.001, bIsDov);

        double? fAltRecoilAdsUp = bIsDov ? wWeapon.DovViewSlideRecoilIronsightUp : wWeapon.ZombieViewSlideRecoilIronsightUp;
        wTemp.ViewSlideRecoilIronsightUp = fAltRecoilAdsUp ?? wWeapon.ViewSlideRecoilIronsightUp;
        SetAltStatBackColor(bIsLeft ? nudAdsRecoilUpL : nudAdsRecoilUpR,
            fAltRecoilAdsUp != null && Math.Abs(fAltRecoilAdsUp.Value - (wWeapon.ViewSlideRecoilIronsightUp ?? 0)) > 0.001, bIsDov);

        double? fAltRecoilAdsRight = bIsDov ? wWeapon.DovViewSlideRecoilIronsightRight : wWeapon.ZombieViewSlideRecoilIronsightRight;
        wTemp.ViewSlideRecoilIronsightRight = fAltRecoilAdsRight ?? wWeapon.ViewSlideRecoilIronsightRight;
        SetAltStatBackColor(bIsLeft ? nudAdsRecoilRightL : nudAdsRecoilRightR,
            fAltRecoilAdsRight != null && Math.Abs(fAltRecoilAdsRight.Value - (wWeapon.ViewSlideRecoilIronsightRight ?? 0)) > 0.001, bIsDov);

        double? fAltDmgGeneric = bIsDov ? wWeapon.DovDamageGeneric : wWeapon.ZombieDamageGeneric;
        wTemp.DamageGeneric = fAltDmgGeneric ?? wWeapon.DamageGeneric;
        SetAltStatBackColor(bIsLeft ? nudDamageGenericL : nudDamageGenericR,
            fAltDmgGeneric != null && Math.Abs(fAltDmgGeneric.Value - (wWeapon.DamageGeneric ?? 0)) > 0.001, bIsDov);

        double? fAltDmgHead = bIsDov ? wWeapon.DovDamageHeadMultiplier : wWeapon.ZombieDamageHeadMultiplier;
        wTemp.DamageHeadMultiplier = fAltDmgHead ?? wWeapon.DamageHeadMultiplier;
        SetAltStatBackColor(bIsLeft ? nudHeadL : nudHeadR,
            fAltDmgHead != null && Math.Abs(fAltDmgHead.Value - (wWeapon.DamageHeadMultiplier ?? 0)) > 0.001, bIsDov);
        double? fAltDmgChest = bIsDov ? wWeapon.DovDamageChestMultiplier : wWeapon.ZombieDamageChestMultiplier;
        wTemp.DamageChestMultiplier = fAltDmgChest ?? wWeapon.DamageChestMultiplier;
        SetAltStatBackColor(bIsLeft ? nudChestL : nudChestR,
            fAltDmgChest != null && Math.Abs(fAltDmgChest.Value - (wWeapon.DamageChestMultiplier ?? 0)) > 0.001, bIsDov);
        double? fAltDmgStomach = bIsDov ? wWeapon.DovDamageStomachMultiplier : wWeapon.ZombieDamageStomachMultiplier;
        wTemp.DamageStomachMultiplier = fAltDmgStomach ?? wWeapon.DamageStomachMultiplier;
        SetAltStatBackColor(bIsLeft ? nudStomachL : nudStomachR,
            fAltDmgStomach != null && Math.Abs(fAltDmgStomach.Value - (wWeapon.DamageStomachMultiplier ?? 0)) > 0.001, bIsDov);
        double? fAltDmgLeg = bIsDov ? wWeapon.DovDamageLegMultiplier : wWeapon.ZombieDamageLegMultiplier;
        wTemp.DamageLegMultiplier = fAltDmgLeg ?? wWeapon.DamageLegMultiplier;
        SetAltStatBackColor(bIsLeft ? nudLegL : nudLegR,
            fAltDmgLeg != null && Math.Abs(fAltDmgLeg.Value - (wWeapon.DamageLegMultiplier ?? 0)) > 0.001, bIsDov);
        double? fAltDmgArm = bIsDov ? wWeapon.DovDamageArmMultiplier : wWeapon.ZombieDamageArmMultiplier;
        wTemp.DamageArmMultiplier = fAltDmgArm ?? wWeapon.DamageArmMultiplier;
        SetAltStatBackColor(bIsLeft ? nudArmL : nudArmR,
            fAltDmgArm != null && Math.Abs(fAltDmgArm.Value - (wWeapon.DamageArmMultiplier ?? 0)) > 0.001, bIsDov);

        double? fAltWeight = bIsDov ? wWeapon.DovWeight : wWeapon.ZombieWeight;
        wTemp.Weight = fAltWeight ?? wWeapon.Weight;
        SetAltStatBackColor(bIsLeft ? nudWeightL : nudWeightR,
            fAltWeight != null && Math.Abs(fAltWeight.Value - (wWeapon.Weight ?? 0)) > 0.001, bIsDov);

        int? nAltZMBuyPrice = bIsDov ? wWeapon.DovZMBuyPrice : null;
        wTemp.ZMBuyPrice = nAltZMBuyPrice ?? wWeapon.ZMBuyPrice;
        SetAltStatBackColor(bIsLeft ? nudZMBuyPriceL : nudZMBuyPriceR,
            nAltZMBuyPrice != null && nAltZMBuyPrice != wWeapon.ZMBuyPrice, bIsDov);

        int? nAltZMWeight = bIsDov ? wWeapon.DovZMWeight : null;
        wTemp.ZMWeight = nAltZMWeight ?? wWeapon.ZMWeight;
        SetAltStatBackColor(bIsLeft ? nudZMWeightL : nudZMWeightR,
            nAltZMWeight != null && nAltZMWeight != wWeapon.ZMWeight, bIsDov);

        string? sAltClipSize = bIsDov ? wWeapon.DovClipSize : wWeapon.ZombieClipSize;
        wTemp.ClipSize = sAltClipSize ?? wWeapon.ClipSize;
        SetAltStatBackColor(bIsLeft ? txtCapacityL : txtCapacityR,
            sAltClipSize != null && sAltClipSize != wWeapon.ClipSize, bIsDov);

        int? nAltSecondaryFireRate = bIsDov ? wWeapon.DovSecondaryFireRate : wWeapon.ZombieSecondaryFireRate;
        wTemp.SecondaryFireRate = nAltSecondaryFireRate ?? wWeapon.SecondaryFireRate;
        SetAltStatBackColor(bIsLeft ? nudSecondaryFireRateL : nudSecondaryFireRateR,
            nAltSecondaryFireRate != null && nAltSecondaryFireRate != wWeapon.SecondaryFireRate, bIsDov);

        int? nAltIronSight = bIsDov ? wWeapon.DovIronSight : wWeapon.ZombieIronSight;
        wTemp.IronSight = nAltIronSight ?? wWeapon.IronSight;
        SetAltStatBackColor(bIsLeft ? nudIronSightL : nudIronSightR,
            nAltIronSight != null && nAltIronSight != wWeapon.IronSight, bIsDov);

        //无gui控件的备选字段直接赋值 不设背景色
        wTemp.ShakeScale = (bIsDov ? wWeapon.DovShakeScale : wWeapon.ZombieShakeScale) ?? wWeapon.ShakeScale;
        wTemp.ShakeFreq = (bIsDov ? wWeapon.DovShakeFreq : wWeapon.ZombieShakeFreq) ?? wWeapon.ShakeFreq;
        wTemp.ShakeDuration = (bIsDov ? wWeapon.DovShakeDuration : wWeapon.ZombieShakeDuration) ?? wWeapon.ShakeDuration;
        wTemp.CrosshairMinDistance = (bIsDov ? wWeapon.DovCrosshairMinDistance : wWeapon.ZombieCrosshairMinDistance) ?? wWeapon.CrosshairMinDistance;
        wTemp.CrosshairDeltaDistance = (bIsDov ? wWeapon.DovCrosshairDeltaDistance : wWeapon.ZombieCrosshairDeltaDistance) ?? wWeapon.CrosshairDeltaDistance;
        wTemp.RecoilPushbackValue = (bIsDov ? wWeapon.DovRecoilPushbackValue : wWeapon.ZombieRecoilPushbackValue) ?? wWeapon.RecoilPushbackValue;
        wTemp.IronsightWalkBobbingStrength = (bIsDov ? wWeapon.DovIronsightWalkBobbingStrength : wWeapon.ZombieIronsightWalkBobbingStrength) ?? wWeapon.IronsightWalkBobbingStrength;
        wTemp.MetalPenetrationDepth = (bIsDov ? wWeapon.DovMetalPenetrationDepth : wWeapon.ZombieMetalPenetrationDepth) ?? wWeapon.MetalPenetrationDepth;
        wTemp.GlassPenetrationDepth = (bIsDov ? wWeapon.DovGlassPenetrationDepth : wWeapon.ZombieGlassPenetrationDepth) ?? wWeapon.GlassPenetrationDepth;
        wTemp.ConcretePenetrationDepth = (bIsDov ? wWeapon.DovConcretePenetrationDepth : wWeapon.ZombieConcretePenetrationDepth) ?? wWeapon.ConcretePenetrationDepth;
        wTemp.WoodPenetrationDepth = (bIsDov ? wWeapon.DovWoodPenetrationDepth : wWeapon.ZombieWoodPenetrationDepth) ?? wWeapon.WoodPenetrationDepth;
        wTemp.OtherPenetrationDepth = (bIsDov ? wWeapon.DovOtherPenetrationDepth : wWeapon.ZombieOtherPenetrationDepth) ?? wWeapon.OtherPenetrationDepth;
        wTemp.MetalDamageModifier = (bIsDov ? wWeapon.DovMetalDamageModifier : wWeapon.ZombieMetalDamageModifier) ?? wWeapon.MetalDamageModifier;
        wTemp.GlassDamageModifier = (bIsDov ? wWeapon.DovGlassDamageModifier : wWeapon.ZombieGlassDamageModifier) ?? wWeapon.GlassDamageModifier;
        wTemp.ConcreteDamageModifier = (bIsDov ? wWeapon.DovConcreteDamageModifier : wWeapon.ZombieConcreteDamageModifier) ?? wWeapon.ConcreteDamageModifier;
        wTemp.WoodDamageModifier = (bIsDov ? wWeapon.DovWoodDamageModifier : wWeapon.ZombieWoodDamageModifier) ?? wWeapon.WoodDamageModifier;
        wTemp.OtherDamageModifier = (bIsDov ? wWeapon.DovOtherDamageModifier : wWeapon.ZombieOtherDamageModifier) ?? wWeapon.OtherDamageModifier;
        wTemp.NearwallDistance = (bIsDov ? wWeapon.DovNearwallDistance : wWeapon.ZombieNearwallDistance) ?? wWeapon.NearwallDistance;

        LoadWeaponToControls(wTemp, bIsLeft);

        string? sAltFireModes = bIsDov ? wWeapon.DovFireModes : wWeapon.ZombieFireModes;
        if (!string.IsNullOrEmpty(sAltFireModes))
        { if (bIsLeft) txtFireModesL.Text = sAltFireModes; else txtFireModesR.Text = sAltFireModes; }
        SetAltStatBackColor(bIsLeft ? txtFireModesL : txtFireModesR,
            sAltFireModes != null && sAltFireModes != wWeapon.FireModes, bIsDov);

        }
        catch (Exception ex)
        {
            LogService.Error(ex, $"LoadAltStatsToControls: isLeft={bIsLeft}, mode={amMode}");
        }
    }

    private void RestoreAllNudEnabled(bool bIsLeft)
    {
        Color cDefault = GetDefaultBackColor(bDarkMode);
        var rgNuds = bIsLeft
            ? new[] { nudHeadL, nudChestL, nudStomachL, nudLegL, nudArmL,
                      nudExtraBulletChamberL, nudBulletsPerShotL, nudIronsightSpeedScaleL, nudWeightL, nudZMBuyPriceL, nudZMWeightL,
                      nudMetalPenL, nudGlassPenL, nudConcretePenL, nudWoodPenL, nudOtherPenL,
                      nudMetalDmgModL, nudGlassDmgModL, nudConcreteDmgModL, nudWoodDmgModL, nudOtherDmgModL,
                      nudCrouchSpreadL, nudProneSpreadL, nudStandMoveSpreadL, nudSneakMoveSpreadL, nudCrouchMoveSpreadL, nudJumpSpreadL,
                      nudSecondaryFireRateL, nudIronSightL, nudFireRateL, nudDamageGenericL, nudRangeModifierL,
                      nudHipSpreadL, nudAdsSpreadL, nudBipodHipSpreadL, nudBipodAdsSpreadL,
                      nudHipRecoilUpL, nudHipRecoilRightL, nudAdsRecoilUpL, nudAdsRecoilRightL }
            : new[] { nudHeadR, nudChestR, nudStomachR, nudLegR, nudArmR,
                      nudExtraBulletChamberR, nudBulletsPerShotR, nudIronsightSpeedScaleR, nudWeightR, nudZMBuyPriceR, nudZMWeightR,
                      nudMetalPenR, nudGlassPenR, nudConcretePenR, nudWoodPenR, nudOtherPenR,
                      nudMetalDmgModR, nudGlassDmgModR, nudConcreteDmgModR, nudWoodDmgModR, nudOtherDmgModR,
                      nudCrouchSpreadR, nudProneSpreadR, nudStandMoveSpreadR, nudSneakMoveSpreadR, nudCrouchMoveSpreadR, nudJumpSpreadR,
                      nudSecondaryFireRateR, nudIronSightR, nudFireRateR, nudDamageGenericR, nudRangeModifierR,
                      nudHipSpreadR, nudAdsSpreadR, nudBipodHipSpreadR, nudBipodAdsSpreadR,
                      nudHipRecoilUpR, nudHipRecoilRightR, nudAdsRecoilUpR, nudAdsRecoilRightR };
        foreach (var nud in rgNuds) { nud.Enabled = true; nud.BackColor = cDefault; }
        RestoreAltStatBackColors(bIsLeft);
    }

    #endregion
    #region 备选值同步

    private void SyncAltStatFields(WeaponData wWeapon, WeaponScriptService.AltStatMode amMode)
    {
        try
        {
        LogService.Debug($"SyncAltStatFields: mode={amMode}, weapon={wWeapon.ScriptName}");
        //同武器时用焦点侧 不同武器时用ReferenceEquals
        bool bIsLeft = ReferenceEquals(wCurrentLeft, wCurrentRight)
            ? bLastFocusLeft
            : ReferenceEquals(wWeapon, wCurrentLeft);
        bool bIsDov = amMode == WeaponScriptService.AltStatMode.Dov;
        
        //从控件读取当前值写入备选字段
        if (bIsDov)
        {
            wWeapon.DovDamageHeadMultiplier = GetSliderValue(bIsLeft, "Head");
            wWeapon.DovDamageChestMultiplier = GetSliderValue(bIsLeft, "Chest");
            wWeapon.DovDamageStomachMultiplier = GetSliderValue(bIsLeft, "Stomach");
            wWeapon.DovDamageLegMultiplier = GetSliderValue(bIsLeft, "Leg");
            wWeapon.DovDamageArmMultiplier = GetSliderValue(bIsLeft, "Arm");
            wWeapon.DovBulletSpread = (double)GetNud(bIsLeft, nudHipSpreadL, nudHipSpreadR).Value;
            wWeapon.DovBulletSpreadDegreesIronsighted = (double)GetNud(bIsLeft, nudAdsSpreadL, nudAdsSpreadR).Value;
            wWeapon.DovBulletSpreadDegreesBipod = (double)GetNud(bIsLeft, nudBipodHipSpreadL, nudBipodHipSpreadR).Value;
            wWeapon.DovBulletSpreadDegreesBipodIronsighted = (double)GetNud(bIsLeft, nudBipodAdsSpreadL, nudBipodAdsSpreadR).Value;
            wWeapon.DovRangeModifier = (double)GetNud(bIsLeft, nudRangeModifierL, nudRangeModifierR).Value;
            wWeapon.DovIronsightSpeedScale = (double)GetNud(bIsLeft, nudIronsightSpeedScaleL, nudIronsightSpeedScaleR).Value;
            wWeapon.DovCrouchSpreadMultiplier = (double)GetNud(bIsLeft, nudCrouchSpreadL, nudCrouchSpreadR).Value;
            wWeapon.DovProneSpreadMultiplier = (double)GetNud(bIsLeft, nudProneSpreadL, nudProneSpreadR).Value;
            wWeapon.DovStandMoveSpreadMultiplier = (double)GetNud(bIsLeft, nudStandMoveSpreadL, nudStandMoveSpreadR).Value;
            wWeapon.DovSneakMoveSpreadMultiplier = (double)GetNud(bIsLeft, nudSneakMoveSpreadL, nudSneakMoveSpreadR).Value;
            wWeapon.DovCrouchMoveSpreadMultiplier = (double)GetNud(bIsLeft, nudCrouchMoveSpreadL, nudCrouchMoveSpreadR).Value;
            wWeapon.DovJumpSpreadMultiplier = (double)GetNud(bIsLeft, nudJumpSpreadL, nudJumpSpreadR).Value;
            wWeapon.DovViewSlideRecoilUp = (double)GetNud(bIsLeft, nudHipRecoilUpL, nudHipRecoilUpR).Value;
            wWeapon.DovViewSlideRecoilRight = (double)GetNud(bIsLeft, nudHipRecoilRightL, nudHipRecoilRightR).Value;
            wWeapon.DovViewSlideRecoilIronsightUp = (double)GetNud(bIsLeft, nudAdsRecoilUpL, nudAdsRecoilUpR).Value;
            wWeapon.DovViewSlideRecoilIronsightRight = (double)GetNud(bIsLeft, nudAdsRecoilRightL, nudAdsRecoilRightR).Value;
            wWeapon.DovFireRate = (int)GetNud(bIsLeft, nudFireRateL, nudFireRateR).Value;
            wWeapon.DovExtraBulletChamber = (int)GetNud(bIsLeft, nudExtraBulletChamberL, nudExtraBulletChamberR).Value;
            wWeapon.DovDamageGeneric = (double)GetNud(bIsLeft, nudDamageGenericL, nudDamageGenericR).Value;
            wWeapon.DovWeight = (double)GetNud(bIsLeft, nudWeightL, nudWeightR).Value;
            wWeapon.DovClipSize = GetTextBox(bIsLeft, txtCapacityL, txtCapacityR).Text;
            wWeapon.DovFireModes = GetTextBox(bIsLeft, txtFireModesL, txtFireModesR).Text;
            wWeapon.DovShakeScale = wWeapon.ShakeScale; wWeapon.DovShakeFreq = wWeapon.ShakeFreq; wWeapon.DovShakeDuration = wWeapon.ShakeDuration;
            wWeapon.DovCrosshairMinDistance = wWeapon.CrosshairMinDistance; wWeapon.DovCrosshairDeltaDistance = wWeapon.CrosshairDeltaDistance;
            wWeapon.DovZMBuyPrice = (int)GetNud(bIsLeft, nudZMBuyPriceL, nudZMBuyPriceR).Value;
            wWeapon.DovZMWeight = (int)GetNud(bIsLeft, nudZMWeightL, nudZMWeightR).Value;
            wWeapon.DovRecoilPushbackValue = wWeapon.RecoilPushbackValue; wWeapon.DovIronsightWalkBobbingStrength = wWeapon.IronsightWalkBobbingStrength;
            wWeapon.DovMetalPenetrationDepth = wWeapon.MetalPenetrationDepth; wWeapon.DovGlassPenetrationDepth = wWeapon.GlassPenetrationDepth;
            wWeapon.DovConcretePenetrationDepth = wWeapon.ConcretePenetrationDepth; wWeapon.DovWoodPenetrationDepth = wWeapon.WoodPenetrationDepth;
            wWeapon.DovOtherPenetrationDepth = wWeapon.OtherPenetrationDepth;
            wWeapon.DovMetalDamageModifier = wWeapon.MetalDamageModifier; wWeapon.DovGlassDamageModifier = wWeapon.GlassDamageModifier;
            wWeapon.DovConcreteDamageModifier = wWeapon.ConcreteDamageModifier; wWeapon.DovWoodDamageModifier = wWeapon.WoodDamageModifier;
            wWeapon.DovOtherDamageModifier = wWeapon.OtherDamageModifier; wWeapon.DovNearwallDistance = wWeapon.NearwallDistance;
            wWeapon.DovSecondaryFireRate = wWeapon.SecondaryFireRate; wWeapon.DovIronSight = wWeapon.IronSight;
        }
        else
        {
            wWeapon.ZombieDamageHeadMultiplier = GetSliderValue(bIsLeft, "Head");
            wWeapon.ZombieDamageChestMultiplier = GetSliderValue(bIsLeft, "Chest");
            wWeapon.ZombieDamageStomachMultiplier = GetSliderValue(bIsLeft, "Stomach");
            wWeapon.ZombieDamageLegMultiplier = GetSliderValue(bIsLeft, "Leg");
            wWeapon.ZombieDamageArmMultiplier = GetSliderValue(bIsLeft, "Arm");
            wWeapon.ZombieBulletSpread = (double)GetNud(bIsLeft, nudHipSpreadL, nudHipSpreadR).Value;
            wWeapon.ZombieBulletSpreadDegreesIronsighted = (double)GetNud(bIsLeft, nudAdsSpreadL, nudAdsSpreadR).Value;
            wWeapon.ZombieBulletSpreadDegreesBipod = (double)GetNud(bIsLeft, nudBipodHipSpreadL, nudBipodHipSpreadR).Value;
            wWeapon.ZombieBulletSpreadDegreesBipodIronsighted = (double)GetNud(bIsLeft, nudBipodAdsSpreadL, nudBipodAdsSpreadR).Value;
            wWeapon.ZombieRangeModifier = (double)GetNud(bIsLeft, nudRangeModifierL, nudRangeModifierR).Value;
            wWeapon.ZombieIronsightSpeedScale = (double)GetNud(bIsLeft, nudIronsightSpeedScaleL, nudIronsightSpeedScaleR).Value;
            wWeapon.ZombieCrouchSpreadMultiplier = (double)GetNud(bIsLeft, nudCrouchSpreadL, nudCrouchSpreadR).Value;
            wWeapon.ZombieProneSpreadMultiplier = (double)GetNud(bIsLeft, nudProneSpreadL, nudProneSpreadR).Value;
            wWeapon.ZombieStandMoveSpreadMultiplier = (double)GetNud(bIsLeft, nudStandMoveSpreadL, nudStandMoveSpreadR).Value;
            wWeapon.ZombieSneakMoveSpreadMultiplier = (double)GetNud(bIsLeft, nudSneakMoveSpreadL, nudSneakMoveSpreadR).Value;
            wWeapon.ZombieCrouchMoveSpreadMultiplier = (double)GetNud(bIsLeft, nudCrouchMoveSpreadL, nudCrouchMoveSpreadR).Value;
            wWeapon.ZombieJumpSpreadMultiplier = (double)GetNud(bIsLeft, nudJumpSpreadL, nudJumpSpreadR).Value;
            wWeapon.ZombieViewSlideRecoilUp = (double)GetNud(bIsLeft, nudHipRecoilUpL, nudHipRecoilUpR).Value;
            wWeapon.ZombieViewSlideRecoilRight = (double)GetNud(bIsLeft, nudHipRecoilRightL, nudHipRecoilRightR).Value;
            wWeapon.ZombieViewSlideRecoilIronsightUp = (double)GetNud(bIsLeft, nudAdsRecoilUpL, nudAdsRecoilUpR).Value;
            wWeapon.ZombieViewSlideRecoilIronsightRight = (double)GetNud(bIsLeft, nudAdsRecoilRightL, nudAdsRecoilRightR).Value;
            wWeapon.ZombieFireRate = (int)GetNud(bIsLeft, nudFireRateL, nudFireRateR).Value;
            wWeapon.ZombieExtraBulletChamber = (int)GetNud(bIsLeft, nudExtraBulletChamberL, nudExtraBulletChamberR).Value;
            wWeapon.ZombieDamageGeneric = (double)GetNud(bIsLeft, nudDamageGenericL, nudDamageGenericR).Value;
            wWeapon.ZombieWeight = (double)GetNud(bIsLeft, nudWeightL, nudWeightR).Value;
            wWeapon.ZombieClipSize = GetTextBox(bIsLeft, txtCapacityL, txtCapacityR).Text;
            wWeapon.ZombieFireModes = GetTextBox(bIsLeft, txtFireModesL, txtFireModesR).Text;
            wWeapon.ZombieShakeScale = wWeapon.ShakeScale; wWeapon.ZombieShakeFreq = wWeapon.ShakeFreq; wWeapon.ZombieShakeDuration = wWeapon.ShakeDuration;
            wWeapon.ZombieCrosshairMinDistance = wWeapon.CrosshairMinDistance; wWeapon.ZombieCrosshairDeltaDistance = wWeapon.CrosshairDeltaDistance;
            wWeapon.ZMBuyPrice = (int)GetNud(bIsLeft, nudZMBuyPriceL, nudZMBuyPriceR).Value;
            wWeapon.ZMWeight = (int)GetNud(bIsLeft, nudZMWeightL, nudZMWeightR).Value;
            wWeapon.ZombieRecoilPushbackValue = wWeapon.RecoilPushbackValue; wWeapon.ZombieIronsightWalkBobbingStrength = wWeapon.IronsightWalkBobbingStrength;
            wWeapon.ZombieMetalPenetrationDepth = wWeapon.MetalPenetrationDepth; wWeapon.ZombieGlassPenetrationDepth = wWeapon.GlassPenetrationDepth;
            wWeapon.ZombieConcretePenetrationDepth = wWeapon.ConcretePenetrationDepth; wWeapon.ZombieWoodPenetrationDepth = wWeapon.WoodPenetrationDepth;
            wWeapon.ZombieOtherPenetrationDepth = wWeapon.OtherPenetrationDepth;
            wWeapon.ZombieMetalDamageModifier = wWeapon.MetalDamageModifier; wWeapon.ZombieGlassDamageModifier = wWeapon.GlassDamageModifier;
            wWeapon.ZombieConcreteDamageModifier = wWeapon.ConcreteDamageModifier; wWeapon.ZombieWoodDamageModifier = wWeapon.WoodDamageModifier;
            wWeapon.ZombieOtherDamageModifier = wWeapon.OtherDamageModifier; wWeapon.ZombieNearwallDistance = wWeapon.NearwallDistance;
            wWeapon.ZombieSecondaryFireRate = wWeapon.SecondaryFireRate; wWeapon.ZombieIronSight = wWeapon.IronSight;
        }
        }
        catch (Exception ex)
        {
            LogService.Warn($"SyncAltStatFields failed: mode={amMode}, {ex.Message}");
        }
    }

    private double GetSliderValue(bool bIsLeft, string sPart)
    {
        var tb = bIsLeft ? sPart switch
        {
            "Head" => trkHeadL, "Chest" => trkChestL, "Stomach" => trkStomachL,
            "Leg" => trkLegL, "Arm" => trkArmL, _ => trkHeadL
        } : sPart switch
        {
            "Head" => trkHeadR, "Chest" => trkChestR, "Stomach" => trkStomachR,
            "Leg" => trkLegR, "Arm" => trkArmR, _ => trkHeadR
        };
        return tb.Value * dSliderStep;
    }

    private NumericUpDown GetNud(bool bIsLeft, NumericUpDown nudL, NumericUpDown nudR) => bIsLeft ? nudL : nudR;

    private TextBox GetTextBox(bool bIsLeft, TextBox txtL, TextBox txtR) => bIsLeft ? txtL : txtR;
    #endregion
}