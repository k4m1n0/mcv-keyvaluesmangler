using System;
using System.Drawing;
using System.Windows.Forms;
using WeaponDamageCalc.Models;
using WeaponDamageCalc.Services;

namespace WeaponDamageCalc;

public partial class Form1
{
    private void ToggleAltStats(WeaponScriptService.AltStatMode mode)
    {
        try
        {
            bool leftHas = WeaponHasAltStats(currentWeaponLeft, mode);
            bool rightHas = WeaponHasAltStats(currentWeaponRight, mode);
            if (!leftHas && !rightHas) return;

            //如果正在显示同一种模式则关闭 否则切换到新模式
            if (showingAltStats && currentAltStatMode == mode)
            {
                LogService.Info($"ToggleAltStats: exiting {mode} mode");
                //退出备选模式前检测是否有未保存修改
                if ((currentWeaponLeft != null && HasUnsavedChanges(true))
                    || (currentWeaponRight != null && HasUnsavedChanges(false)))
                {
                    var result = MessageBox.Show("Unsaved alt stat changes will be lost. Discard?",
                        "Unsaved Changes", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                    if (result != DialogResult.Yes) return;
                }
                showingAltStats = false;
                if (currentWeaponLeft != null) { LoadWeaponToControls(currentWeaponLeft, true); }
                if (currentWeaponRight != null) { LoadWeaponToControls(currentWeaponRight, false); }
                RestoreAllNudEnabled(true); RestoreAllNudEnabled(false);
                ResetAltStatButtons();
                StoreSnapshot();
            }
            else
            {
                LogService.Info($"ToggleAltStats: entering {mode} mode");
                //进入备选模式前检测普通模式下是否有未保存修改
                if ((currentWeaponLeft != null && HasUnsavedChanges(true))
                    || (currentWeaponRight != null && HasUnsavedChanges(false)))
                {
                    var result = MessageBox.Show("Unsaved changes will be lost. Switch stats mode?",
                        "Unsaved Changes", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                    if (result != DialogResult.Yes) return;
                }
                PushUndo();
                showingAltStats = true; currentAltStatMode = mode;
                HighlightAltStatButton(mode);
                updatingControls = true;
                if (leftHas) { LoadAltStatsToControls(true, mode); SetAltStatReadonly(true, mode); }
                if (rightHas) { LoadAltStatsToControls(false, mode); SetAltStatReadonly(false, mode); }
                updatingControls = false;
                _snapshotLeft = new WeaponData();
                _snapshotRight = new WeaponData();
                SaveControlsToWeapon(_snapshotLeft, true);
                SaveControlsToWeapon(_snapshotRight, false);
            }
            UpdateAllDamage(); pnlSpread.Invalidate(); pnlRecoil.Invalidate();
        }
        catch (Exception ex)
        {
            LogService.Error(ex, $"ToggleAltStats: mode={mode}");
        }
    }

    //高亮当前模式的按钮 另一个恢复默认
    private void HighlightAltStatButton(WeaponScriptService.AltStatMode mode)
    {
        foreach (Control c in this.Controls)
        {
            if (c is Button btn)
            {
                if (btn.Text == "DoV") btn.BackColor = mode == WeaponScriptService.AltStatMode.Dov ? Color.LightGreen : SystemColors.Control;
                else if (btn.Text == "Zmb") btn.BackColor = mode == WeaponScriptService.AltStatMode.Zombie ? Color.LightGreen : SystemColors.Control;
            }
        }
    }

    private void ResetAltStatButtons()
    {
        foreach (Control c in this.Controls)
            if (c is Button btn && (btn.Text == "DoV" || btn.Text == "Zmb")) btn.BackColor = SystemColors.Control;
    }

    private void SetAltStatReadonly(bool isLeft, WeaponScriptService.AltStatMode mode)
    {
        var w = isLeft ? currentWeaponLeft : currentWeaponRight;
        if (w != null)
        {
            bool noAds = GetAltStatIronSight(w, mode) == 0;
            SetNudEnabled(isLeft ? nudAdsSpreadL : nudAdsSpreadR, !noAds);
            SetNudEnabled(isLeft ? nudAdsRecoilUpL : nudAdsRecoilUpR, !noAds);
            SetNudEnabled(isLeft ? nudAdsRecoilRightL : nudAdsRecoilRightR, !noAds);
            SetNudEnabled(isLeft ? nudIronsightSpeedScaleL : nudIronsightSpeedScaleR, !noAds);
        }
    }

    private static int? GetAltStatIronSight(WeaponData w, WeaponScriptService.AltStatMode mode) => mode switch
    {
        WeaponScriptService.AltStatMode.Dov => w.DovIronSight ?? w.IronSight,
        WeaponScriptService.AltStatMode.Zombie => w.ZombieIronSight ?? w.IronSight,
        _ => w.IronSight
    };

    private static void SetNudEnabled(NumericUpDown nud, bool enabled) => nud.Enabled = enabled;

    private static bool WeaponHasAltStats(WeaponData? weapon, WeaponScriptService.AltStatMode mode) => mode switch
    {
        WeaponScriptService.AltStatMode.Dov => weapon?.DovDamageGeneric != null || weapon?.DovFireRate != null,
        WeaponScriptService.AltStatMode.Zombie => weapon?.ZombieClipSize != null || weapon?.ZombieDamageGeneric != null || weapon?.ZombieFireRate != null || weapon?.ZombieWeight != null,
        _ => false
    };

    private void ExitAltStatMode()
    {
        if (!WeaponHasAltStats(currentWeaponLeft, currentAltStatMode) && !WeaponHasAltStats(currentWeaponRight, currentAltStatMode))
        { showingAltStats = false; RestoreAllNudEnabled(true); RestoreAllNudEnabled(false); ResetAltStatButtons(); }
    }

    private void LoadAltStatsToControls(bool isLeft, WeaponScriptService.AltStatMode mode)
    {
        var weapon = isLeft ? currentWeaponLeft : currentWeaponRight;
        if (weapon == null) return;
        var temp = new WeaponData();
        CopyWeaponDataFields(weapon, temp);

        bool isDov = mode == WeaponScriptService.AltStatMode.Dov;
        temp.ExtraBulletChamber = (isDov ? weapon.DovExtraBulletChamber : weapon.ZombieExtraBulletChamber) ?? weapon.ExtraBulletChamber;
        temp.FireRate = (isDov ? weapon.DovFireRate : weapon.ZombieFireRate) ?? weapon.FireRate;
        temp.BulletSpread = (isDov ? weapon.DovBulletSpread : weapon.ZombieBulletSpread) ?? weapon.BulletSpread;
        temp.BulletSpreadDegreesIronsighted = (isDov ? weapon.DovBulletSpreadDegreesIronsighted : weapon.ZombieBulletSpreadDegreesIronsighted) ?? weapon.BulletSpreadDegreesIronsighted;
        temp.BulletSpreadDegreesBipod = (isDov ? weapon.DovBulletSpreadDegreesBipod : weapon.ZombieBulletSpreadDegreesBipod) ?? weapon.BulletSpreadDegreesBipod;
        temp.BulletSpreadDegreesBipodIronsighted = (isDov ? weapon.DovBulletSpreadDegreesBipodIronsighted : weapon.ZombieBulletSpreadDegreesBipodIronsighted) ?? weapon.BulletSpreadDegreesBipodIronsighted;
        temp.RangeModifier = (isDov ? weapon.DovRangeModifier : weapon.ZombieRangeModifier) ?? weapon.RangeModifier;
        temp.IronsightSpeedScale = (isDov ? weapon.DovIronsightSpeedScale : weapon.ZombieIronsightSpeedScale) ?? weapon.IronsightSpeedScale;
        temp.CrouchSpreadMultiplier = (isDov ? weapon.DovCrouchSpreadMultiplier : weapon.ZombieCrouchSpreadMultiplier) ?? weapon.CrouchSpreadMultiplier;
        temp.ProneSpreadMultiplier = (isDov ? weapon.DovProneSpreadMultiplier : weapon.ZombieProneSpreadMultiplier) ?? weapon.ProneSpreadMultiplier;
        temp.StandMoveSpreadMultiplier = (isDov ? weapon.DovStandMoveSpreadMultiplier : weapon.ZombieStandMoveSpreadMultiplier) ?? weapon.StandMoveSpreadMultiplier;
        temp.SneakMoveSpreadMultiplier = (isDov ? weapon.DovSneakMoveSpreadMultiplier : weapon.ZombieSneakMoveSpreadMultiplier) ?? weapon.SneakMoveSpreadMultiplier;
        temp.CrouchMoveSpreadMultiplier = (isDov ? weapon.DovCrouchMoveSpreadMultiplier : weapon.ZombieCrouchMoveSpreadMultiplier) ?? weapon.CrouchMoveSpreadMultiplier;
        temp.JumpSpreadMultiplier = (isDov ? weapon.DovJumpSpreadMultiplier : weapon.ZombieJumpSpreadMultiplier) ?? weapon.JumpSpreadMultiplier;
        temp.ViewSlideRecoilUp = (isDov ? weapon.DovViewSlideRecoilUp : weapon.ZombieViewSlideRecoilUp) ?? weapon.ViewSlideRecoilUp;
        temp.ViewSlideRecoilRight = (isDov ? weapon.DovViewSlideRecoilRight : weapon.ZombieViewSlideRecoilRight) ?? weapon.ViewSlideRecoilRight;
        temp.ViewSlideRecoilIronsightUp = (isDov ? weapon.DovViewSlideRecoilIronsightUp : weapon.ZombieViewSlideRecoilIronsightUp) ?? weapon.ViewSlideRecoilIronsightUp;
        temp.ViewSlideRecoilIronsightRight = (isDov ? weapon.DovViewSlideRecoilIronsightRight : weapon.ZombieViewSlideRecoilIronsightRight) ?? weapon.ViewSlideRecoilIronsightRight;
        temp.DamageHeadMultiplier = (isDov ? weapon.DovDamageHeadMultiplier : weapon.ZombieDamageHeadMultiplier) ?? weapon.DamageHeadMultiplier;
        temp.DamageChestMultiplier = (isDov ? weapon.DovDamageChestMultiplier : weapon.ZombieDamageChestMultiplier) ?? weapon.DamageChestMultiplier;
        temp.DamageStomachMultiplier = (isDov ? weapon.DovDamageStomachMultiplier : weapon.ZombieDamageStomachMultiplier) ?? weapon.DamageStomachMultiplier;
        temp.DamageLegMultiplier = (isDov ? weapon.DovDamageLegMultiplier : weapon.ZombieDamageLegMultiplier) ?? weapon.DamageLegMultiplier;
        temp.DamageArmMultiplier = (isDov ? weapon.DovDamageArmMultiplier : weapon.ZombieDamageArmMultiplier) ?? weapon.DamageArmMultiplier;
        temp.DamageGeneric = (isDov ? weapon.DovDamageGeneric : weapon.ZombieDamageGeneric) ?? weapon.DamageGeneric;
        temp.ShakeScale = (isDov ? weapon.DovShakeScale : weapon.ZombieShakeScale) ?? weapon.ShakeScale;
        temp.ShakeFreq = (isDov ? weapon.DovShakeFreq : weapon.ZombieShakeFreq) ?? weapon.ShakeFreq;
        temp.ShakeDuration = (isDov ? weapon.DovShakeDuration : weapon.ZombieShakeDuration) ?? weapon.ShakeDuration;
        temp.CrosshairMinDistance = (isDov ? weapon.DovCrosshairMinDistance : weapon.ZombieCrosshairMinDistance) ?? weapon.CrosshairMinDistance;
        temp.CrosshairDeltaDistance = (isDov ? weapon.DovCrosshairDeltaDistance : weapon.ZombieCrosshairDeltaDistance) ?? weapon.CrosshairDeltaDistance;
        temp.Weight = (isDov ? weapon.DovWeight : weapon.ZombieWeight) ?? weapon.Weight;
        temp.ZMBuyPrice = (isDov ? weapon.DovZMBuyPrice : weapon.ZombieZMBuyPrice) ?? weapon.ZMBuyPrice;
        temp.ZMWeight = (isDov ? weapon.DovZMWeight : weapon.ZombieZMWeight) ?? weapon.ZMWeight;
        temp.RecoilPushbackValue = (isDov ? weapon.DovRecoilPushbackValue : weapon.ZombieRecoilPushbackValue) ?? weapon.RecoilPushbackValue;
        temp.IronsightWalkBobbingStrength = (isDov ? weapon.DovIronsightWalkBobbingStrength : weapon.ZombieIronsightWalkBobbingStrength) ?? weapon.IronsightWalkBobbingStrength;
        temp.MetalPenetrationDepth = (isDov ? weapon.DovMetalPenetrationDepth : weapon.ZombieMetalPenetrationDepth) ?? weapon.MetalPenetrationDepth;
        temp.GlassPenetrationDepth = (isDov ? weapon.DovGlassPenetrationDepth : weapon.ZombieGlassPenetrationDepth) ?? weapon.GlassPenetrationDepth;
        temp.ConcretePenetrationDepth = (isDov ? weapon.DovConcretePenetrationDepth : weapon.ZombieConcretePenetrationDepth) ?? weapon.ConcretePenetrationDepth;
        temp.WoodPenetrationDepth = (isDov ? weapon.DovWoodPenetrationDepth : weapon.ZombieWoodPenetrationDepth) ?? weapon.WoodPenetrationDepth;
        temp.OtherPenetrationDepth = (isDov ? weapon.DovOtherPenetrationDepth : weapon.ZombieOtherPenetrationDepth) ?? weapon.OtherPenetrationDepth;
        temp.MetalDamageModifier = (isDov ? weapon.DovMetalDamageModifier : weapon.ZombieMetalDamageModifier) ?? weapon.MetalDamageModifier;
        temp.GlassDamageModifier = (isDov ? weapon.DovGlassDamageModifier : weapon.ZombieGlassDamageModifier) ?? weapon.GlassDamageModifier;
        temp.ConcreteDamageModifier = (isDov ? weapon.DovConcreteDamageModifier : weapon.ZombieConcreteDamageModifier) ?? weapon.ConcreteDamageModifier;
        temp.WoodDamageModifier = (isDov ? weapon.DovWoodDamageModifier : weapon.ZombieWoodDamageModifier) ?? weapon.WoodDamageModifier;
        temp.OtherDamageModifier = (isDov ? weapon.DovOtherDamageModifier : weapon.ZombieOtherDamageModifier) ?? weapon.OtherDamageModifier;
        temp.NearwallDistance = (isDov ? weapon.DovNearwallDistance : weapon.ZombieNearwallDistance) ?? weapon.NearwallDistance;
        temp.ClipSize = (isDov ? weapon.DovClipSize : weapon.ZombieClipSize) ?? weapon.ClipSize;
        temp.SecondaryFireRate = (isDov ? weapon.DovSecondaryFireRate : weapon.ZombieSecondaryFireRate) ?? weapon.SecondaryFireRate;
        temp.IronSight = (isDov ? weapon.DovIronSight : weapon.ZombieIronSight) ?? weapon.IronSight;

        LoadWeaponToControls(temp, isLeft);

        string? altFireModes = isDov ? weapon.DovFireModes : weapon.ZombieFireModes;
        if (!string.IsNullOrEmpty(altFireModes))
        { if (isLeft) txtFireModesL.Text = altFireModes; else txtFireModesR.Text = altFireModes; }
    }

    private void RestoreAllNudEnabled(bool isLeft)
    {
        var nuds = isLeft
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
        foreach (var nud in nuds) nud.Enabled = true;
    }

    //将顶层值同步回备选数值字段
    private static void SyncAltStatFields(WeaponData w, WeaponScriptService.AltStatMode mode)
    {
        bool isDov = mode == WeaponScriptService.AltStatMode.Dov;
        if (isDov)
        {
            w.DovDamageHeadMultiplier = w.DamageHeadMultiplier; w.DovDamageChestMultiplier = w.DamageChestMultiplier;
            w.DovDamageStomachMultiplier = w.DamageStomachMultiplier; w.DovDamageLegMultiplier = w.DamageLegMultiplier;
            w.DovDamageArmMultiplier = w.DamageArmMultiplier; w.DovDamageGeneric = w.DamageGeneric;
            w.DovBulletSpread = w.BulletSpread; w.DovBulletSpreadDegreesIronsighted = w.BulletSpreadDegreesIronsighted;
            w.DovBulletSpreadDegreesBipod = w.BulletSpreadDegreesBipod; w.DovBulletSpreadDegreesBipodIronsighted = w.BulletSpreadDegreesBipodIronsighted;
            w.DovRangeModifier = w.RangeModifier; w.DovIronsightSpeedScale = w.IronsightSpeedScale;
            w.DovCrouchSpreadMultiplier = w.CrouchSpreadMultiplier; w.DovProneSpreadMultiplier = w.ProneSpreadMultiplier;
            w.DovStandMoveSpreadMultiplier = w.StandMoveSpreadMultiplier; w.DovSneakMoveSpreadMultiplier = w.SneakMoveSpreadMultiplier;
            w.DovCrouchMoveSpreadMultiplier = w.CrouchMoveSpreadMultiplier; w.DovJumpSpreadMultiplier = w.JumpSpreadMultiplier;
            w.DovViewSlideRecoilUp = w.ViewSlideRecoilUp; w.DovViewSlideRecoilRight = w.ViewSlideRecoilRight;
            w.DovViewSlideRecoilIronsightUp = w.ViewSlideRecoilIronsightUp; w.DovViewSlideRecoilIronsightRight = w.ViewSlideRecoilIronsightRight;
            w.DovFireRate = w.FireRate; w.DovExtraBulletChamber = w.ExtraBulletChamber;
            w.DovShakeScale = w.ShakeScale; w.DovShakeFreq = w.ShakeFreq; w.DovShakeDuration = w.ShakeDuration;
            w.DovCrosshairMinDistance = w.CrosshairMinDistance; w.DovCrosshairDeltaDistance = w.CrosshairDeltaDistance;
            w.DovWeight = w.Weight; w.DovZMBuyPrice = w.ZMBuyPrice; w.DovZMWeight = w.ZMWeight;
            w.DovRecoilPushbackValue = w.RecoilPushbackValue; w.DovIronsightWalkBobbingStrength = w.IronsightWalkBobbingStrength;
            w.DovMetalPenetrationDepth = w.MetalPenetrationDepth; w.DovGlassPenetrationDepth = w.GlassPenetrationDepth;
            w.DovConcretePenetrationDepth = w.ConcretePenetrationDepth; w.DovWoodPenetrationDepth = w.WoodPenetrationDepth;
            w.DovOtherPenetrationDepth = w.OtherPenetrationDepth;
            w.DovMetalDamageModifier = w.MetalDamageModifier; w.DovGlassDamageModifier = w.GlassDamageModifier;
            w.DovConcreteDamageModifier = w.ConcreteDamageModifier; w.DovWoodDamageModifier = w.WoodDamageModifier;
            w.DovOtherDamageModifier = w.OtherDamageModifier; w.DovNearwallDistance = w.NearwallDistance;
            w.DovClipSize = w.ClipSize; w.DovFireModes = w.FireModes;
            w.DovSecondaryFireRate = w.SecondaryFireRate; w.DovIronSight = w.IronSight;
        }
        else
        {
            w.ZombieDamageHeadMultiplier = w.DamageHeadMultiplier; w.ZombieDamageChestMultiplier = w.DamageChestMultiplier;
            w.ZombieDamageStomachMultiplier = w.DamageStomachMultiplier; w.ZombieDamageLegMultiplier = w.DamageLegMultiplier;
            w.ZombieDamageArmMultiplier = w.DamageArmMultiplier; w.ZombieDamageGeneric = w.DamageGeneric;
            w.ZombieBulletSpread = w.BulletSpread; w.ZombieBulletSpreadDegreesIronsighted = w.BulletSpreadDegreesIronsighted;
            w.ZombieBulletSpreadDegreesBipod = w.BulletSpreadDegreesBipod; w.ZombieBulletSpreadDegreesBipodIronsighted = w.BulletSpreadDegreesBipodIronsighted;
            w.ZombieRangeModifier = w.RangeModifier; w.ZombieIronsightSpeedScale = w.IronsightSpeedScale;
            w.ZombieCrouchSpreadMultiplier = w.CrouchSpreadMultiplier; w.ZombieProneSpreadMultiplier = w.ProneSpreadMultiplier;
            w.ZombieStandMoveSpreadMultiplier = w.StandMoveSpreadMultiplier; w.ZombieSneakMoveSpreadMultiplier = w.SneakMoveSpreadMultiplier;
            w.ZombieCrouchMoveSpreadMultiplier = w.CrouchMoveSpreadMultiplier; w.ZombieJumpSpreadMultiplier = w.JumpSpreadMultiplier;
            w.ZombieViewSlideRecoilUp = w.ViewSlideRecoilUp; w.ZombieViewSlideRecoilRight = w.ViewSlideRecoilRight;
            w.ZombieViewSlideRecoilIronsightUp = w.ViewSlideRecoilIronsightUp; w.ZombieViewSlideRecoilIronsightRight = w.ViewSlideRecoilIronsightRight;
            w.ZombieFireRate = w.FireRate; w.ZombieExtraBulletChamber = w.ExtraBulletChamber;
            w.ZombieShakeScale = w.ShakeScale; w.ZombieShakeFreq = w.ShakeFreq; w.ZombieShakeDuration = w.ShakeDuration;
            w.ZombieCrosshairMinDistance = w.CrosshairMinDistance; w.ZombieCrosshairDeltaDistance = w.CrosshairDeltaDistance;
            w.ZombieWeight = w.Weight; w.ZombieZMBuyPrice = w.ZMBuyPrice; w.ZombieZMWeight = w.ZMWeight;
            w.ZombieRecoilPushbackValue = w.RecoilPushbackValue; w.ZombieIronsightWalkBobbingStrength = w.IronsightWalkBobbingStrength;
            w.ZombieMetalPenetrationDepth = w.MetalPenetrationDepth; w.ZombieGlassPenetrationDepth = w.GlassPenetrationDepth;
            w.ZombieConcretePenetrationDepth = w.ConcretePenetrationDepth; w.ZombieWoodPenetrationDepth = w.WoodPenetrationDepth;
            w.ZombieOtherPenetrationDepth = w.OtherPenetrationDepth;
            w.ZombieMetalDamageModifier = w.MetalDamageModifier; w.ZombieGlassDamageModifier = w.GlassDamageModifier;
            w.ZombieConcreteDamageModifier = w.ConcreteDamageModifier; w.ZombieWoodDamageModifier = w.WoodDamageModifier;
            w.ZombieOtherDamageModifier = w.OtherDamageModifier; w.ZombieNearwallDistance = w.NearwallDistance;
            w.ZombieClipSize = w.ClipSize; w.ZombieFireModes = w.FireModes;
            w.ZombieSecondaryFireRate = w.SecondaryFireRate; w.ZombieIronSight = w.IronSight;
        }
    }
}