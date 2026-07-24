using CsvHelper.Configuration.Attributes;

namespace WeaponDamageCalc.Models;

public class WeaponData
{
    [Name("ScriptName")]
    public string ScriptName { get; set; } = string.Empty;

    [Name("printname")]
    public string PrintName { get; set; } = string.Empty;

    [Name("SupportedFireModes")]
    public string FireModes { get; set; } = string.Empty;
    [Name("dov_SupportedFireModes")]
    public string DovFireModes { get; set; } = string.Empty;
    [Name("zombie_SupportedFireModes")]
    public string ZombieFireModes { get; set; } = string.Empty;

    [Name("default_clip")]
    public int? DefaultClip { get; set; }

    [Name("SecondaryFireRate")]
    public int? SecondaryFireRate { get; set; }
    [Name("dov_SecondaryFireRate")]
    public int? DovSecondaryFireRate { get; set; }
    [Name("zombie_SecondaryFireRate")]
    public int? ZombieSecondaryFireRate { get; set; }

    [Name("ExtraBulletChamber")]
    public int? ExtraBulletChamber { get; set; }
    [Name("dov_ExtraBulletChamber")]
    public int? DovExtraBulletChamber { get; set; }
    [Name("zombie_ExtraBulletChamber")]
    public int? ZombieExtraBulletChamber { get; set; }

    [Name("bullets_per_shot")]
    public int? BulletsPerShot { get; set; }

    [Name("FireRate")]
    public int? FireRate { get; set; }
    [Name("dov_FireRate")]
    public int? DovFireRate { get; set; }
    [Name("zombie_FireRate")]
    public int? ZombieFireRate { get; set; }

    [Name("BulletSpreadDegrees")]
    public double? BulletSpread { get; set; }
    [Name("dov_BulletSpreadDegrees")]
    public double? DovBulletSpread { get; set; }
    [Name("zombie_BulletSpreadDegrees")]
    public double? ZombieBulletSpread { get; set; }

    [Name("BulletSpreadDegreesIronsighted")]
    public double? BulletSpreadDegreesIronsighted { get; set; }
    [Name("dov_BulletSpreadDegreesIronsighted")]
    public double? DovBulletSpreadDegreesIronsighted { get; set; }
    [Name("zombie_BulletSpreadDegreesIronsighted")]
    public double? ZombieBulletSpreadDegreesIronsighted { get; set; }

    [Name("BulletSpreadDegreesBipod")]
    public double? BulletSpreadDegreesBipod { get; set; }
    [Name("dov_BulletSpreadDegreesBipod")]
    public double? DovBulletSpreadDegreesBipod { get; set; }
    [Name("zombie_BulletSpreadDegreesBipod")]
    public double? ZombieBulletSpreadDegreesBipod { get; set; }

    [Name("BulletSpreadDegreesBipodIronsighted")]
    public double? BulletSpreadDegreesBipodIronsighted { get; set; }
    [Name("dov_BulletSpreadDegreesBipodIronsighted")]
    public double? DovBulletSpreadDegreesBipodIronsighted { get; set; }
    [Name("zombie_BulletSpreadDegreesBipodIronsighted")]
    public double? ZombieBulletSpreadDegreesBipodIronsighted { get; set; }

    [Name("rangemodifier")]
    public double? RangeModifier { get; set; }
    [Name("dov_rangemodifier")]
    public double? DovRangeModifier { get; set; }
    [Name("zombie_rangemodifier")]
    public double? ZombieRangeModifier { get; set; }

    [Name("IronsightSpeedScale")]
    public double? IronsightSpeedScale { get; set; }
    [Name("dov_IronsightSpeedScale")]
    public double? DovIronsightSpeedScale { get; set; }
    [Name("zombie_IronsightSpeedScale")]
    public double? ZombieIronsightSpeedScale { get; set; }

    [Name("IronSight")]
    public int? IronSight { get; set; }
    [Name("dov_IronSight")]
    public int? DovIronSight { get; set; }
    [Name("zombie_IronSight")]
    public int? ZombieIronSight { get; set; }

    [Name("CrouchSpreadMultiplier")]
    public double? CrouchSpreadMultiplier { get; set; }
    [Name("dov_CrouchSpreadMultiplier")]
    public double? DovCrouchSpreadMultiplier { get; set; }
    [Name("zombie_CrouchSpreadMultiplier")]
    public double? ZombieCrouchSpreadMultiplier { get; set; }

    [Name("ProneSpreadMultiplier")]
    public double? ProneSpreadMultiplier { get; set; }
    [Name("dov_ProneSpreadMultiplier")]
    public double? DovProneSpreadMultiplier { get; set; }
    [Name("zombie_ProneSpreadMultiplier")]
    public double? ZombieProneSpreadMultiplier { get; set; }

    [Name("StandMoveSpreadMultiplier")]
    public double? StandMoveSpreadMultiplier { get; set; }
    [Name("dov_StandMoveSpreadMultiplier")]
    public double? DovStandMoveSpreadMultiplier { get; set; }
    [Name("zombie_StandMoveSpreadMultiplier")]
    public double? ZombieStandMoveSpreadMultiplier { get; set; }

    [Name("SneakMoveSpreadMultiplier")]
    public double? SneakMoveSpreadMultiplier { get; set; }
    [Name("dov_SneakMoveSpreadMultiplier")]
    public double? DovSneakMoveSpreadMultiplier { get; set; }
    [Name("zombie_SneakMoveSpreadMultiplier")]
    public double? ZombieSneakMoveSpreadMultiplier { get; set; }

    [Name("CrouchMoveSpreadMultiplier")]
    public double? CrouchMoveSpreadMultiplier { get; set; }
    [Name("dov_CrouchMoveSpreadMultiplier")]
    public double? DovCrouchMoveSpreadMultiplier { get; set; }
    [Name("zombie_CrouchMoveSpreadMultiplier")]
    public double? ZombieCrouchMoveSpreadMultiplier { get; set; }

    [Name("JumpSpreadMultiplier")]
    public double? JumpSpreadMultiplier { get; set; }
    [Name("dov_JumpSpreadMultiplier")]
    public double? DovJumpSpreadMultiplier { get; set; }
    [Name("zombie_JumpSpreadMultiplier")]
    public double? ZombieJumpSpreadMultiplier { get; set; }

    [Name("ViewSlideRecoil.Up")]
    public double? ViewSlideRecoilUp { get; set; }
    [Name("dov_ViewSlideRecoil.Up")]
    public double? DovViewSlideRecoilUp { get; set; }
    [Name("zombie_ViewSlideRecoil.Up")]
    public double? ZombieViewSlideRecoilUp { get; set; }

    [Name("ViewSlideRecoil.Right")]
    public double? ViewSlideRecoilRight { get; set; }
    [Name("dov_ViewSlideRecoil.Right")]
    public double? DovViewSlideRecoilRight { get; set; }
    [Name("zombie_ViewSlideRecoil.Right")]
    public double? ZombieViewSlideRecoilRight { get; set; }

    [Name("ViewSlideRecoilIronsight.Up")]
    public double? ViewSlideRecoilIronsightUp { get; set; }
    [Name("dov_ViewSlideRecoilIronsight.Up")]
    public double? DovViewSlideRecoilIronsightUp { get; set; }
    [Name("zombie_ViewSlideRecoilIronsight.Up")]
    public double? ZombieViewSlideRecoilIronsightUp { get; set; }

    [Name("ViewSlideRecoilIronsight.Right")]
    public double? ViewSlideRecoilIronsightRight { get; set; }
    [Name("dov_ViewSlideRecoilIronsight.Right")]
    public double? DovViewSlideRecoilIronsightRight { get; set; }
    [Name("zombie_ViewSlideRecoilIronsight.Right")]
    public double? ZombieViewSlideRecoilIronsightRight { get; set; }

    [Name("DamageHeadMultiplier")]
    public double? DamageHeadMultiplier { get; set; }
    [Name("dov_DamageHeadMultiplier")]
    public double? DovDamageHeadMultiplier { get; set; }
    [Name("zombie_DamageHeadMultiplier")]
    public double? ZombieDamageHeadMultiplier { get; set; }

    [Name("DamageChestMultiplier")]
    public double? DamageChestMultiplier { get; set; }
    [Name("dov_DamageChestMultiplier")]
    public double? DovDamageChestMultiplier { get; set; }
    [Name("zombie_DamageChestMultiplier")]
    public double? ZombieDamageChestMultiplier { get; set; }

    [Name("DamageStomachMultiplier")]
    public double? DamageStomachMultiplier { get; set; }
    [Name("dov_DamageStomachMultiplier")]
    public double? DovDamageStomachMultiplier { get; set; }
    [Name("zombie_DamageStomachMultiplier")]
    public double? ZombieDamageStomachMultiplier { get; set; }

    [Name("DamageLegMultiplier")]
    public double? DamageLegMultiplier { get; set; }
    [Name("dov_DamageLegMultiplier")]
    public double? DovDamageLegMultiplier { get; set; }
    [Name("zombie_DamageLegMultiplier")]
    public double? ZombieDamageLegMultiplier { get; set; }

    [Name("DamageArmMultiplier")]
    public double? DamageArmMultiplier { get; set; }
    [Name("dov_DamageArmMultiplier")]
    public double? DovDamageArmMultiplier { get; set; }
    [Name("zombie_DamageArmMultiplier")]
    public double? ZombieDamageArmMultiplier { get; set; }

    [Name("DamageGeneric")]
    public double? DamageGeneric { get; set; }
    [Name("dov_DamageGeneric")]
    public double? DovDamageGeneric { get; set; }
    [Name("zombie_DamageGeneric")]
    public double? ZombieDamageGeneric { get; set; }

    [Name("ShakeScale")]
    public double? ShakeScale { get; set; }
    [Name("dov_ShakeScale")]
    public double? DovShakeScale { get; set; }
    [Name("zombie_ShakeScale")]
    public double? ZombieShakeScale { get; set; }

    [Name("ShakeFreq")]
    public double? ShakeFreq { get; set; }
    [Name("dov_ShakeFreq")]
    public double? DovShakeFreq { get; set; }
    [Name("zombie_ShakeFreq")]
    public double? ZombieShakeFreq { get; set; }

    [Name("ShakeDuration")]
    public double? ShakeDuration { get; set; }
    [Name("dov_ShakeDuration")]
    public double? DovShakeDuration { get; set; }
    [Name("zombie_ShakeDuration")]
    public double? ZombieShakeDuration { get; set; }

    [Name("CrosshairMinDistance")]
    public int? CrosshairMinDistance { get; set; }
    [Name("dov_CrosshairMinDistance")]
    public int? DovCrosshairMinDistance { get; set; }
    [Name("zombie_CrosshairMinDistance")]
    public int? ZombieCrosshairMinDistance { get; set; }

    [Name("CrosshairDeltaDistance")]
    public int? CrosshairDeltaDistance { get; set; }
    [Name("dov_CrosshairDeltaDistance")]
    public int? DovCrosshairDeltaDistance { get; set; }
    [Name("zombie_CrosshairDeltaDistance")]
    public int? ZombieCrosshairDeltaDistance { get; set; }

    [Name("weight")]
    public double? Weight { get; set; }
    [Name("dov_weight")]
    public double? DovWeight { get; set; }
    [Name("zombie_weight")]
    public double? ZombieWeight { get; set; }

    [Name("ZMBuyPrice")]
    public int? ZMBuyPrice { get; set; }
    [Name("dov_ZMBuyPrice")]
    public int? DovZMBuyPrice { get; set; }
    [Name("zombie_ZMBuyPrice")]
    public int? ZombieZMBuyPrice { get; set; }

    [Name("ZMWeight")]
    public int? ZMWeight { get; set; }
    [Name("dov_ZMWeight")]
    public int? DovZMWeight { get; set; }
    [Name("zombie_ZMWeight")]
    public int? ZombieZMWeight { get; set; }

    [Name("recoilpushbackvalue")]
    public double? RecoilPushbackValue { get; set; }
    [Name("dov_recoilpushbackvalue")]
    public double? DovRecoilPushbackValue { get; set; }
    [Name("zombie_recoilpushbackvalue")]
    public double? ZombieRecoilPushbackValue { get; set; }

    [Name("ironsightwalkbobbingstrength")]
    public double? IronsightWalkBobbingStrength { get; set; }
    [Name("dov_ironsightwalkbobbingstrength")]
    public double? DovIronsightWalkBobbingStrength { get; set; }
    [Name("zombie_ironsightwalkbobbingstrength")]
    public double? ZombieIronsightWalkBobbingStrength { get; set; }

    [Name("MetalPenetrationDepth")]
    public double? MetalPenetrationDepth { get; set; }
    [Name("dov_MetalPenetrationDepth")]
    public double? DovMetalPenetrationDepth { get; set; }
    [Name("zombie_MetalPenetrationDepth")]
    public double? ZombieMetalPenetrationDepth { get; set; }

    [Name("GlassPenetrationDepth")]
    public double? GlassPenetrationDepth { get; set; }
    [Name("dov_GlassPenetrationDepth")]
    public double? DovGlassPenetrationDepth { get; set; }
    [Name("zombie_GlassPenetrationDepth")]
    public double? ZombieGlassPenetrationDepth { get; set; }

    [Name("ConcretePenetrationDepth")]
    public double? ConcretePenetrationDepth { get; set; }
    [Name("dov_ConcretePenetrationDepth")]
    public double? DovConcretePenetrationDepth { get; set; }
    [Name("zombie_ConcretePenetrationDepth")]
    public double? ZombieConcretePenetrationDepth { get; set; }

    [Name("WoodPenetrationDepth")]
    public double? WoodPenetrationDepth { get; set; }
    [Name("dov_WoodPenetrationDepth")]
    public double? DovWoodPenetrationDepth { get; set; }
    [Name("zombie_WoodPenetrationDepth")]
    public double? ZombieWoodPenetrationDepth { get; set; }

    [Name("OtherPenetrationDepth")]
    public double? OtherPenetrationDepth { get; set; }
    [Name("dov_OtherPenetrationDepth")]
    public double? DovOtherPenetrationDepth { get; set; }
    [Name("zombie_OtherPenetrationDepth")]
    public double? ZombieOtherPenetrationDepth { get; set; }

    [Name("MetalDamageModifier")]
    public double? MetalDamageModifier { get; set; }
    [Name("dov_MetalDamageModifier")]
    public double? DovMetalDamageModifier { get; set; }
    [Name("zombie_MetalDamageModifier")]
    public double? ZombieMetalDamageModifier { get; set; }

    [Name("GlassDamageModifier")]
    public double? GlassDamageModifier { get; set; }
    [Name("dov_GlassDamageModifier")]
    public double? DovGlassDamageModifier { get; set; }
    [Name("zombie_GlassDamageModifier")]
    public double? ZombieGlassDamageModifier { get; set; }

    [Name("ConcreteDamageModifier")]
    public double? ConcreteDamageModifier { get; set; }
    [Name("dov_ConcreteDamageModifier")]
    public double? DovConcreteDamageModifier { get; set; }
    [Name("zombie_ConcreteDamageModifier")]
    public double? ZombieConcreteDamageModifier { get; set; }

    [Name("WoodDamageModifier")]
    public double? WoodDamageModifier { get; set; }
    [Name("dov_WoodDamageModifier")]
    public double? DovWoodDamageModifier { get; set; }
    [Name("zombie_WoodDamageModifier")]
    public double? ZombieWoodDamageModifier { get; set; }

    [Name("OtherDamageModifier")]
    public double? OtherDamageModifier { get; set; }
    [Name("dov_OtherDamageModifier")]
    public double? DovOtherDamageModifier { get; set; }
    [Name("zombie_OtherDamageModifier")]
    public double? ZombieOtherDamageModifier { get; set; }

    [Name("NearwallDistance")]
    public int? NearwallDistance { get; set; }
    [Name("dov_NearwallDistance")]
    public int? DovNearwallDistance { get; set; }
    [Name("zombie_NearwallDistance")]
    public int? ZombieNearwallDistance { get; set; }

    [Name("primary_ammo")]
    public string PrimaryAmmo { get; set; } = string.Empty;

    [Name("clip_size")]
    public string ClipSize { get; set; } = string.Empty;
    [Name("dov_clip_size")]
    public string DovClipSize { get; set; } = string.Empty;
    [Name("zombie_clip_size")]
    public string ZombieClipSize { get; set; } = string.Empty;
}