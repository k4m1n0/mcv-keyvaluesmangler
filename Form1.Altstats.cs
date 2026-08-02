using System;
using System.Drawing;
using System.Windows.Forms;
using WeaponDamageCalc.Models;
using WeaponDamageCalc.Services;

namespace WeaponDamageCalc;

public partial class Form1
{
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
                if (wCurrentLeft != null) { LoadWeaponToControls(wCurrentLeft, true); }
                if (wCurrentRight != null) { LoadWeaponToControls(wCurrentRight, false); }
                RestoreAllNudEnabled(true); RestoreAllNudEnabled(false);
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
                else { LoadWeaponToControls(wCurrentLeft!, true); SetAltStatReadonly(true, amMode); }
                if (bRightHas) { LoadAltStatsToControls(false, amMode); SetAltStatReadonly(false, amMode); }
                else if (!ReferenceEquals(wCurrentLeft, wCurrentRight)) { LoadWeaponToControls(wCurrentRight!, false); SetAltStatReadonly(false, amMode); }
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
        WeaponScriptService.AltStatMode.Dov => wWeapon?.DovDamageGeneric != null || wWeapon?.DovFireRate != null,
        WeaponScriptService.AltStatMode.Zombie => wWeapon?.ZombieClipSize != null || wWeapon?.ZombieDamageGeneric != null || wWeapon?.ZombieFireRate != null || wWeapon?.ZombieWeight != null,
        _ => false
    };

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
        wTemp.ExtraBulletChamber = (bIsDov ? wWeapon.DovExtraBulletChamber : wWeapon.ZombieExtraBulletChamber) ?? wWeapon.ExtraBulletChamber;
        wTemp.FireRate = (bIsDov ? wWeapon.DovFireRate : wWeapon.ZombieFireRate) ?? wWeapon.FireRate;
        wTemp.BulletSpread = (bIsDov ? wWeapon.DovBulletSpread : wWeapon.ZombieBulletSpread) ?? wWeapon.BulletSpread;
        wTemp.BulletSpreadDegreesIronsighted = (bIsDov ? wWeapon.DovBulletSpreadDegreesIronsighted : wWeapon.ZombieBulletSpreadDegreesIronsighted) ?? wWeapon.BulletSpreadDegreesIronsighted;
        wTemp.BulletSpreadDegreesBipod = (bIsDov ? wWeapon.DovBulletSpreadDegreesBipod : wWeapon.ZombieBulletSpreadDegreesBipod) ?? wWeapon.BulletSpreadDegreesBipod;
        wTemp.BulletSpreadDegreesBipodIronsighted = (bIsDov ? wWeapon.DovBulletSpreadDegreesBipodIronsighted : wWeapon.ZombieBulletSpreadDegreesBipodIronsighted) ?? wWeapon.BulletSpreadDegreesBipodIronsighted;
        wTemp.RangeModifier = (bIsDov ? wWeapon.DovRangeModifier : wWeapon.ZombieRangeModifier) ?? wWeapon.RangeModifier;
        wTemp.IronsightSpeedScale = (bIsDov ? wWeapon.DovIronsightSpeedScale : wWeapon.ZombieIronsightSpeedScale) ?? wWeapon.IronsightSpeedScale;
        wTemp.CrouchSpreadMultiplier = (bIsDov ? wWeapon.DovCrouchSpreadMultiplier : wWeapon.ZombieCrouchSpreadMultiplier) ?? wWeapon.CrouchSpreadMultiplier;
        wTemp.ProneSpreadMultiplier = (bIsDov ? wWeapon.DovProneSpreadMultiplier : wWeapon.ZombieProneSpreadMultiplier) ?? wWeapon.ProneSpreadMultiplier;
        wTemp.StandMoveSpreadMultiplier = (bIsDov ? wWeapon.DovStandMoveSpreadMultiplier : wWeapon.ZombieStandMoveSpreadMultiplier) ?? wWeapon.StandMoveSpreadMultiplier;
        wTemp.SneakMoveSpreadMultiplier = (bIsDov ? wWeapon.DovSneakMoveSpreadMultiplier : wWeapon.ZombieSneakMoveSpreadMultiplier) ?? wWeapon.SneakMoveSpreadMultiplier;
        wTemp.CrouchMoveSpreadMultiplier = (bIsDov ? wWeapon.DovCrouchMoveSpreadMultiplier : wWeapon.ZombieCrouchMoveSpreadMultiplier) ?? wWeapon.CrouchMoveSpreadMultiplier;
        wTemp.JumpSpreadMultiplier = (bIsDov ? wWeapon.DovJumpSpreadMultiplier : wWeapon.ZombieJumpSpreadMultiplier) ?? wWeapon.JumpSpreadMultiplier;
        wTemp.ViewSlideRecoilUp = (bIsDov ? wWeapon.DovViewSlideRecoilUp : wWeapon.ZombieViewSlideRecoilUp) ?? wWeapon.ViewSlideRecoilUp;
        wTemp.ViewSlideRecoilRight = (bIsDov ? wWeapon.DovViewSlideRecoilRight : wWeapon.ZombieViewSlideRecoilRight) ?? wWeapon.ViewSlideRecoilRight;
        wTemp.ViewSlideRecoilIronsightUp = (bIsDov ? wWeapon.DovViewSlideRecoilIronsightUp : wWeapon.ZombieViewSlideRecoilIronsightUp) ?? wWeapon.ViewSlideRecoilIronsightUp;
        wTemp.ViewSlideRecoilIronsightRight = (bIsDov ? wWeapon.DovViewSlideRecoilIronsightRight : wWeapon.ZombieViewSlideRecoilIronsightRight) ?? wWeapon.ViewSlideRecoilIronsightRight;
        wTemp.DamageHeadMultiplier = (bIsDov ? wWeapon.DovDamageHeadMultiplier : wWeapon.ZombieDamageHeadMultiplier) ?? wWeapon.DamageHeadMultiplier;
        wTemp.DamageChestMultiplier = (bIsDov ? wWeapon.DovDamageChestMultiplier : wWeapon.ZombieDamageChestMultiplier) ?? wWeapon.DamageChestMultiplier;
        wTemp.DamageStomachMultiplier = (bIsDov ? wWeapon.DovDamageStomachMultiplier : wWeapon.ZombieDamageStomachMultiplier) ?? wWeapon.DamageStomachMultiplier;
        wTemp.DamageLegMultiplier = (bIsDov ? wWeapon.DovDamageLegMultiplier : wWeapon.ZombieDamageLegMultiplier) ?? wWeapon.DamageLegMultiplier;
        wTemp.DamageArmMultiplier = (bIsDov ? wWeapon.DovDamageArmMultiplier : wWeapon.ZombieDamageArmMultiplier) ?? wWeapon.DamageArmMultiplier;
        wTemp.DamageGeneric = (bIsDov ? wWeapon.DovDamageGeneric : wWeapon.ZombieDamageGeneric) ?? wWeapon.DamageGeneric;
        wTemp.ShakeScale = (bIsDov ? wWeapon.DovShakeScale : wWeapon.ZombieShakeScale) ?? wWeapon.ShakeScale;
        wTemp.ShakeFreq = (bIsDov ? wWeapon.DovShakeFreq : wWeapon.ZombieShakeFreq) ?? wWeapon.ShakeFreq;
        wTemp.ShakeDuration = (bIsDov ? wWeapon.DovShakeDuration : wWeapon.ZombieShakeDuration) ?? wWeapon.ShakeDuration;
        wTemp.CrosshairMinDistance = (bIsDov ? wWeapon.DovCrosshairMinDistance : wWeapon.ZombieCrosshairMinDistance) ?? wWeapon.CrosshairMinDistance;
        wTemp.CrosshairDeltaDistance = (bIsDov ? wWeapon.DovCrosshairDeltaDistance : wWeapon.ZombieCrosshairDeltaDistance) ?? wWeapon.CrosshairDeltaDistance;
        wTemp.Weight = (bIsDov ? wWeapon.DovWeight : wWeapon.ZombieWeight) ?? wWeapon.Weight;
        wTemp.ZMBuyPrice = (bIsDov ? wWeapon.DovZMBuyPrice : wWeapon.ZombieZMBuyPrice) ?? wWeapon.ZMBuyPrice;
        wTemp.ZMWeight = (bIsDov ? wWeapon.DovZMWeight : wWeapon.ZombieZMWeight) ?? wWeapon.ZMWeight;
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
        wTemp.ClipSize = (bIsDov ? wWeapon.DovClipSize : wWeapon.ZombieClipSize) ?? wWeapon.ClipSize;
        wTemp.SecondaryFireRate = (bIsDov ? wWeapon.DovSecondaryFireRate : wWeapon.ZombieSecondaryFireRate) ?? wWeapon.SecondaryFireRate;
        wTemp.IronSight = (bIsDov ? wWeapon.DovIronSight : wWeapon.ZombieIronSight) ?? wWeapon.IronSight;

        LoadWeaponToControls(wTemp, bIsLeft);

        string? sAltFireModes = bIsDov ? wWeapon.DovFireModes : wWeapon.ZombieFireModes;
        if (!string.IsNullOrEmpty(sAltFireModes))
        { if (bIsLeft) txtFireModesL.Text = sAltFireModes; else txtFireModesR.Text = sAltFireModes; }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, $"LoadAltStatsToControls: isLeft={bIsLeft}, mode={amMode}");
        }
    }

    private void RestoreAllNudEnabled(bool bIsLeft)
    {
        var rgNuds = bIsLeft
            ? new[] { nudExtraBulletChamberL, nudBulletsPerShotL, nudIronsightSpeedScaleL, nudWeightL, nudZMBuyPriceL, nudZMWeightL,
                      nudMetalPenL, nudGlassPenL, nudConcretePenL, nudWoodPenL, nudOtherPenL,
                      nudMetalDmgModL, nudGlassDmgModL, nudConcreteDmgModL, nudWoodDmgModL, nudOtherDmgModL,
                      nudCrouchSpreadL, nudProneSpreadL, nudStandMoveSpreadL, nudSneakMoveSpreadL, nudCrouchMoveSpreadL, nudJumpSpreadL,
                      nudSecondaryFireRateL, nudIronSightL, nudAdsSpreadL, nudAdsRecoilUpL, nudAdsRecoilRightL, nudIronsightSpeedScaleL }
            : new[] { nudExtraBulletChamberR, nudBulletsPerShotR, nudIronsightSpeedScaleR, nudWeightR, nudZMBuyPriceR, nudZMWeightR,
                      nudMetalPenR, nudGlassPenR, nudConcretePenR, nudWoodPenR, nudOtherPenR,
                      nudMetalDmgModR, nudGlassDmgModR, nudConcreteDmgModR, nudWoodDmgModR, nudOtherDmgModR,
                      nudCrouchSpreadR, nudProneSpreadR, nudStandMoveSpreadR, nudSneakMoveSpreadR, nudCrouchMoveSpreadR, nudJumpSpreadR,
                      nudSecondaryFireRateR, nudIronSightR, nudAdsSpreadR, nudAdsRecoilUpR, nudAdsRecoilRightR, nudIronsightSpeedScaleR };
        foreach (var nud in rgNuds) nud.Enabled = true;
    }

    //将控件当前值同步回备选数值字段
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
}