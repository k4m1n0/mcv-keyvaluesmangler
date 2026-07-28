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
            if (!leftHas && !rightHas)
            {
                LogService.Debug($"ToggleAltStats: {mode} - no weapon has alt stats, abort");
                return;
            }

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
                StoreSnapshot();
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

    private void LoadAltStatsToControls(bool isLeft, WeaponScriptService.AltStatMode mode)
    {
        try
        {
        var weapon = isLeft ? currentWeaponLeft : currentWeaponRight;
        if (weapon == null) return;
        LogService.Debug($"LoadAltStatsToControls: isLeft={isLeft}, mode={mode}, weapon={weapon.ScriptName}");
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
        catch (Exception ex)
        {
            LogService.Error(ex, $"LoadAltStatsToControls: isLeft={isLeft}, mode={mode}");
        }
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

    //将控件当前值同步回备选数值字段
    private void SyncAltStatFields(WeaponData w, WeaponScriptService.AltStatMode mode)
    {
        try
        {
        LogService.Debug($"SyncAltStatFields: mode={mode}, weapon={w.ScriptName}");
        //同武器时用焦点侧 不同武器时用ReferenceEquals
        bool isLeft = ReferenceEquals(currentWeaponLeft, currentWeaponRight)
            ? lastFocusLeft
            : ReferenceEquals(w, currentWeaponLeft);
        bool isDov = mode == WeaponScriptService.AltStatMode.Dov;
        
        //从控件读取当前值写入备选字段
        if (isDov)
        {
            w.DovDamageHeadMultiplier = GetSliderValue(isLeft, "Head");
            w.DovDamageChestMultiplier = GetSliderValue(isLeft, "Chest");
            w.DovDamageStomachMultiplier = GetSliderValue(isLeft, "Stomach");
            w.DovDamageLegMultiplier = GetSliderValue(isLeft, "Leg");
            w.DovDamageArmMultiplier = GetSliderValue(isLeft, "Arm");
            w.DovBulletSpread = (double)GetNud(isLeft, nudHipSpreadL, nudHipSpreadR).Value;
            w.DovBulletSpreadDegreesIronsighted = (double)GetNud(isLeft, nudAdsSpreadL, nudAdsSpreadR).Value;
            w.DovBulletSpreadDegreesBipod = (double)GetNud(isLeft, nudBipodHipSpreadL, nudBipodHipSpreadR).Value;
            w.DovBulletSpreadDegreesBipodIronsighted = (double)GetNud(isLeft, nudBipodAdsSpreadL, nudBipodAdsSpreadR).Value;
            w.DovRangeModifier = (double)GetNud(isLeft, nudRangeModifierL, nudRangeModifierR).Value;
            w.DovIronsightSpeedScale = (double)GetNud(isLeft, nudIronsightSpeedScaleL, nudIronsightSpeedScaleR).Value;
            w.DovCrouchSpreadMultiplier = (double)GetNud(isLeft, nudCrouchSpreadL, nudCrouchSpreadR).Value;
            w.DovProneSpreadMultiplier = (double)GetNud(isLeft, nudProneSpreadL, nudProneSpreadR).Value;
            w.DovStandMoveSpreadMultiplier = (double)GetNud(isLeft, nudStandMoveSpreadL, nudStandMoveSpreadR).Value;
            w.DovSneakMoveSpreadMultiplier = (double)GetNud(isLeft, nudSneakMoveSpreadL, nudSneakMoveSpreadR).Value;
            w.DovCrouchMoveSpreadMultiplier = (double)GetNud(isLeft, nudCrouchMoveSpreadL, nudCrouchMoveSpreadR).Value;
            w.DovJumpSpreadMultiplier = (double)GetNud(isLeft, nudJumpSpreadL, nudJumpSpreadR).Value;
            w.DovViewSlideRecoilUp = (double)GetNud(isLeft, nudHipRecoilUpL, nudHipRecoilUpR).Value;
            w.DovViewSlideRecoilRight = (double)GetNud(isLeft, nudHipRecoilRightL, nudHipRecoilRightR).Value;
            w.DovViewSlideRecoilIronsightUp = (double)GetNud(isLeft, nudAdsRecoilUpL, nudAdsRecoilUpR).Value;
            w.DovViewSlideRecoilIronsightRight = (double)GetNud(isLeft, nudAdsRecoilRightL, nudAdsRecoilRightR).Value;
            w.DovFireRate = (int)GetNud(isLeft, nudFireRateL, nudFireRateR).Value;
            w.DovExtraBulletChamber = (int)GetNud(isLeft, nudExtraBulletChamberL, nudExtraBulletChamberR).Value;
            w.DovDamageGeneric = (double)GetNud(isLeft, nudDamageGenericL, nudDamageGenericR).Value;
            w.DovWeight = (double)GetNud(isLeft, nudWeightL, nudWeightR).Value;
            w.DovClipSize = GetTextBox(isLeft, txtCapacityL, txtCapacityR).Text;
            w.DovFireModes = GetTextBox(isLeft, txtFireModesL, txtFireModesR).Text;
            w.DovShakeScale = w.ShakeScale; w.DovShakeFreq = w.ShakeFreq; w.DovShakeDuration = w.ShakeDuration;
            w.DovCrosshairMinDistance = w.CrosshairMinDistance; w.DovCrosshairDeltaDistance = w.CrosshairDeltaDistance;
            w.DovZMBuyPrice = w.ZMBuyPrice; w.DovZMWeight = w.ZMWeight;
            w.DovRecoilPushbackValue = w.RecoilPushbackValue; w.DovIronsightWalkBobbingStrength = w.IronsightWalkBobbingStrength;
            w.DovMetalPenetrationDepth = w.MetalPenetrationDepth; w.DovGlassPenetrationDepth = w.GlassPenetrationDepth;
            w.DovConcretePenetrationDepth = w.ConcretePenetrationDepth; w.DovWoodPenetrationDepth = w.WoodPenetrationDepth;
            w.DovOtherPenetrationDepth = w.OtherPenetrationDepth;
            w.DovMetalDamageModifier = w.MetalDamageModifier; w.DovGlassDamageModifier = w.GlassDamageModifier;
            w.DovConcreteDamageModifier = w.ConcreteDamageModifier; w.DovWoodDamageModifier = w.WoodDamageModifier;
            w.DovOtherDamageModifier = w.OtherDamageModifier; w.DovNearwallDistance = w.NearwallDistance;
            w.DovSecondaryFireRate = w.SecondaryFireRate; w.DovIronSight = w.IronSight;
        }
        else
        {
            w.ZombieDamageHeadMultiplier = GetSliderValue(isLeft, "Head");
            w.ZombieDamageChestMultiplier = GetSliderValue(isLeft, "Chest");
            w.ZombieDamageStomachMultiplier = GetSliderValue(isLeft, "Stomach");
            w.ZombieDamageLegMultiplier = GetSliderValue(isLeft, "Leg");
            w.ZombieDamageArmMultiplier = GetSliderValue(isLeft, "Arm");
            w.ZombieBulletSpread = (double)GetNud(isLeft, nudHipSpreadL, nudHipSpreadR).Value;
            w.ZombieBulletSpreadDegreesIronsighted = (double)GetNud(isLeft, nudAdsSpreadL, nudAdsSpreadR).Value;
            w.ZombieBulletSpreadDegreesBipod = (double)GetNud(isLeft, nudBipodHipSpreadL, nudBipodHipSpreadR).Value;
            w.ZombieBulletSpreadDegreesBipodIronsighted = (double)GetNud(isLeft, nudBipodAdsSpreadL, nudBipodAdsSpreadR).Value;
            w.ZombieRangeModifier = (double)GetNud(isLeft, nudRangeModifierL, nudRangeModifierR).Value;
            w.ZombieIronsightSpeedScale = (double)GetNud(isLeft, nudIronsightSpeedScaleL, nudIronsightSpeedScaleR).Value;
            w.ZombieCrouchSpreadMultiplier = (double)GetNud(isLeft, nudCrouchSpreadL, nudCrouchSpreadR).Value;
            w.ZombieProneSpreadMultiplier = (double)GetNud(isLeft, nudProneSpreadL, nudProneSpreadR).Value;
            w.ZombieStandMoveSpreadMultiplier = (double)GetNud(isLeft, nudStandMoveSpreadL, nudStandMoveSpreadR).Value;
            w.ZombieSneakMoveSpreadMultiplier = (double)GetNud(isLeft, nudSneakMoveSpreadL, nudSneakMoveSpreadR).Value;
            w.ZombieCrouchMoveSpreadMultiplier = (double)GetNud(isLeft, nudCrouchMoveSpreadL, nudCrouchMoveSpreadR).Value;
            w.ZombieJumpSpreadMultiplier = (double)GetNud(isLeft, nudJumpSpreadL, nudJumpSpreadR).Value;
            w.ZombieViewSlideRecoilUp = (double)GetNud(isLeft, nudHipRecoilUpL, nudHipRecoilUpR).Value;
            w.ZombieViewSlideRecoilRight = (double)GetNud(isLeft, nudHipRecoilRightL, nudHipRecoilRightR).Value;
            w.ZombieViewSlideRecoilIronsightUp = (double)GetNud(isLeft, nudAdsRecoilUpL, nudAdsRecoilUpR).Value;
            w.ZombieViewSlideRecoilIronsightRight = (double)GetNud(isLeft, nudAdsRecoilRightL, nudAdsRecoilRightR).Value;
            w.ZombieFireRate = (int)GetNud(isLeft, nudFireRateL, nudFireRateR).Value;
            w.ZombieExtraBulletChamber = (int)GetNud(isLeft, nudExtraBulletChamberL, nudExtraBulletChamberR).Value;
            w.ZombieDamageGeneric = (double)GetNud(isLeft, nudDamageGenericL, nudDamageGenericR).Value;
            w.ZombieWeight = (double)GetNud(isLeft, nudWeightL, nudWeightR).Value;
            w.ZombieClipSize = GetTextBox(isLeft, txtCapacityL, txtCapacityR).Text;
            w.ZombieFireModes = GetTextBox(isLeft, txtFireModesL, txtFireModesR).Text;
            w.ZombieShakeScale = w.ShakeScale; w.ZombieShakeFreq = w.ShakeFreq; w.ZombieShakeDuration = w.ShakeDuration;
            w.ZombieCrosshairMinDistance = w.CrosshairMinDistance; w.ZombieCrosshairDeltaDistance = w.CrosshairDeltaDistance;
            w.ZombieZMBuyPrice = w.ZMBuyPrice; w.ZombieZMWeight = w.ZMWeight;
            w.ZombieRecoilPushbackValue = w.RecoilPushbackValue; w.ZombieIronsightWalkBobbingStrength = w.IronsightWalkBobbingStrength;
            w.ZombieMetalPenetrationDepth = w.MetalPenetrationDepth; w.ZombieGlassPenetrationDepth = w.GlassPenetrationDepth;
            w.ZombieConcretePenetrationDepth = w.ConcretePenetrationDepth; w.ZombieWoodPenetrationDepth = w.WoodPenetrationDepth;
            w.ZombieOtherPenetrationDepth = w.OtherPenetrationDepth;
            w.ZombieMetalDamageModifier = w.MetalDamageModifier; w.ZombieGlassDamageModifier = w.GlassDamageModifier;
            w.ZombieConcreteDamageModifier = w.ConcreteDamageModifier; w.ZombieWoodDamageModifier = w.WoodDamageModifier;
            w.ZombieOtherDamageModifier = w.OtherDamageModifier; w.ZombieNearwallDistance = w.NearwallDistance;
            w.ZombieSecondaryFireRate = w.SecondaryFireRate; w.ZombieIronSight = w.IronSight;
        }
        }
        catch (Exception ex)
        {
            LogService.Warn($"SyncAltStatFields failed: mode={mode}, {ex.Message}");
        }
    }

    private double GetSliderValue(bool isLeft, string part)
    {
        var tb = isLeft ? part switch
        {
            "Head" => trkHeadL, "Chest" => trkChestL, "Stomach" => trkStomachL,
            "Leg" => trkLegL, "Arm" => trkArmL, _ => trkHeadL
        } : part switch
        {
            "Head" => trkHeadR, "Chest" => trkChestR, "Stomach" => trkStomachR,
            "Leg" => trkLegR, "Arm" => trkArmR, _ => trkHeadR
        };
        return tb.Value * SliderStep;
    }

    private NumericUpDown GetNud(bool isLeft, NumericUpDown l, NumericUpDown r) => isLeft ? l : r;

    private TextBox GetTextBox(bool isLeft, TextBox l, TextBox r) => isLeft ? l : r;
}