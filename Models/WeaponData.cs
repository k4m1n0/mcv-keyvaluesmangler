using WeaponDamageCalc.Tools;

namespace WeaponDamageCalc.Models;

public class WeaponData
{
    [CsvColumn("ScriptName")]
    public string ScriptName { get; set; } = string.Empty;

    [CsvColumn("printname")]
    public string PrintName { get; set; } = string.Empty;

    [CsvColumn("SupportedFireModes")]
    public string FireModes { get; set; } = string.Empty;
    [CsvColumn("dov_SupportedFireModes")]
    public string DovFireModes { get; set; } = string.Empty;
    [CsvColumn("zombie_SupportedFireModes")]
    public string ZombieFireModes { get; set; } = string.Empty;

    [CsvColumn("default_clip")]
    public int? DefaultClip { get; set; }
    [CsvColumn("dov_default_clip")]
    public int? DovDefaultClip { get; set; }
    [CsvColumn("zombie_default_clip")]
    public int? ZombieDefaultClip { get; set; }

    [CsvColumn("SecondaryFireRate")]
    public int? SecondaryFireRate { get; set; }
    [CsvColumn("dov_SecondaryFireRate")]
    public int? DovSecondaryFireRate { get; set; }
    [CsvColumn("zombie_SecondaryFireRate")]
    public int? ZombieSecondaryFireRate { get; set; }

    [CsvColumn("ExtraBulletChamber")]
    public int? ExtraBulletChamber { get; set; }
    [CsvColumn("dov_ExtraBulletChamber")]
    public int? DovExtraBulletChamber { get; set; }
    [CsvColumn("zombie_ExtraBulletChamber")]
    public int? ZombieExtraBulletChamber { get; set; }

    [CsvColumn("bullets_per_shot")]
    public int? BulletsPerShot { get; set; }
    [CsvColumn("dov_bullets_per_shot")]
    public int? DovBulletsPerShot { get; set; }
    [CsvColumn("zombie_bullets_per_shot")]
    public int? ZombieBulletsPerShot { get; set; }

    [CsvColumn("FireRate")]
    public int? FireRate { get; set; }
    [CsvColumn("dov_FireRate")]
    public int? DovFireRate { get; set; }
    [CsvColumn("zombie_FireRate")]
    public int? ZombieFireRate { get; set; }

    [CsvColumn("BulletSpreadDegrees")]
    public double? BulletSpread { get; set; }
    [CsvColumn("dov_BulletSpreadDegrees")]
    public double? DovBulletSpread { get; set; }
    [CsvColumn("zombie_BulletSpreadDegrees")]
    public double? ZombieBulletSpread { get; set; }

    [CsvColumn("BulletSpreadDegreesIronsighted")]
    public double? BulletSpreadDegreesIronsighted { get; set; }
    [CsvColumn("dov_BulletSpreadDegreesIronsighted")]
    public double? DovBulletSpreadDegreesIronsighted { get; set; }
    [CsvColumn("zombie_BulletSpreadDegreesIronsighted")]
    public double? ZombieBulletSpreadDegreesIronsighted { get; set; }

    [CsvColumn("BulletSpreadDegreesBipod")]
    public double? BulletSpreadDegreesBipod { get; set; }
    [CsvColumn("dov_BulletSpreadDegreesBipod")]
    public double? DovBulletSpreadDegreesBipod { get; set; }
    [CsvColumn("zombie_BulletSpreadDegreesBipod")]
    public double? ZombieBulletSpreadDegreesBipod { get; set; }

    [CsvColumn("BulletSpreadDegreesBipodIronsighted")]
    public double? BulletSpreadDegreesBipodIronsighted { get; set; }
    [CsvColumn("dov_BulletSpreadDegreesBipodIronsighted")]
    public double? DovBulletSpreadDegreesBipodIronsighted { get; set; }
    [CsvColumn("zombie_BulletSpreadDegreesBipodIronsighted")]
    public double? ZombieBulletSpreadDegreesBipodIronsighted { get; set; }

    [CsvColumn("rangemodifier")]
    public double? RangeModifier { get; set; }
    [CsvColumn("dov_rangemodifier")]
    public double? DovRangeModifier { get; set; }
    [CsvColumn("zombie_rangemodifier")]
    public double? ZombieRangeModifier { get; set; }

    [CsvColumn("IronsightSpeedScale")]
    public double? IronsightSpeedScale { get; set; }
    [CsvColumn("dov_IronsightSpeedScale")]
    public double? DovIronsightSpeedScale { get; set; }
    [CsvColumn("zombie_IronsightSpeedScale")]
    public double? ZombieIronsightSpeedScale { get; set; }

    [CsvColumn("IronSight")]
    public int? IronSight { get; set; }
    [CsvColumn("dov_IronSight")]
    public int? DovIronSight { get; set; }
    [CsvColumn("zombie_IronSight")]
    public int? ZombieIronSight { get; set; }

    [CsvColumn("CrouchSpreadMultiplier")]
    public double? CrouchSpreadMultiplier { get; set; }
    [CsvColumn("dov_CrouchSpreadMultiplier")]
    public double? DovCrouchSpreadMultiplier { get; set; }
    [CsvColumn("zombie_CrouchSpreadMultiplier")]
    public double? ZombieCrouchSpreadMultiplier { get; set; }

    [CsvColumn("ProneSpreadMultiplier")]
    public double? ProneSpreadMultiplier { get; set; }
    [CsvColumn("dov_ProneSpreadMultiplier")]
    public double? DovProneSpreadMultiplier { get; set; }
    [CsvColumn("zombie_ProneSpreadMultiplier")]
    public double? ZombieProneSpreadMultiplier { get; set; }

    [CsvColumn("StandMoveSpreadMultiplier")]
    public double? StandMoveSpreadMultiplier { get; set; }
    [CsvColumn("dov_StandMoveSpreadMultiplier")]
    public double? DovStandMoveSpreadMultiplier { get; set; }
    [CsvColumn("zombie_StandMoveSpreadMultiplier")]
    public double? ZombieStandMoveSpreadMultiplier { get; set; }

    [CsvColumn("SneakMoveSpreadMultiplier")]
    public double? SneakMoveSpreadMultiplier { get; set; }
    [CsvColumn("dov_SneakMoveSpreadMultiplier")]
    public double? DovSneakMoveSpreadMultiplier { get; set; }
    [CsvColumn("zombie_SneakMoveSpreadMultiplier")]
    public double? ZombieSneakMoveSpreadMultiplier { get; set; }

    [CsvColumn("CrouchMoveSpreadMultiplier")]
    public double? CrouchMoveSpreadMultiplier { get; set; }
    [CsvColumn("dov_CrouchMoveSpreadMultiplier")]
    public double? DovCrouchMoveSpreadMultiplier { get; set; }
    [CsvColumn("zombie_CrouchMoveSpreadMultiplier")]
    public double? ZombieCrouchMoveSpreadMultiplier { get; set; }

    [CsvColumn("JumpSpreadMultiplier")]
    public double? JumpSpreadMultiplier { get; set; }
    [CsvColumn("dov_JumpSpreadMultiplier")]
    public double? DovJumpSpreadMultiplier { get; set; }
    [CsvColumn("zombie_JumpSpreadMultiplier")]
    public double? ZombieJumpSpreadMultiplier { get; set; }

    [CsvColumn("ViewSlideRecoil.Up")]
    public double? ViewSlideRecoilUp { get; set; }
    [CsvColumn("dov_ViewSlideRecoil.Up")]
    public double? DovViewSlideRecoilUp { get; set; }
    [CsvColumn("zombie_ViewSlideRecoil.Up")]
    public double? ZombieViewSlideRecoilUp { get; set; }

    [CsvColumn("ViewSlideRecoil.Right")]
    public double? ViewSlideRecoilRight { get; set; }
    [CsvColumn("dov_ViewSlideRecoil.Right")]
    public double? DovViewSlideRecoilRight { get; set; }
    [CsvColumn("zombie_ViewSlideRecoil.Right")]
    public double? ZombieViewSlideRecoilRight { get; set; }

    [CsvColumn("ViewSlideRecoilIronsight.Up")]
    public double? ViewSlideRecoilIronsightUp { get; set; }
    [CsvColumn("dov_ViewSlideRecoilIronsight.Up")]
    public double? DovViewSlideRecoilIronsightUp { get; set; }
    [CsvColumn("zombie_ViewSlideRecoilIronsight.Up")]
    public double? ZombieViewSlideRecoilIronsightUp { get; set; }

    [CsvColumn("ViewSlideRecoilIronsight.Right")]
    public double? ViewSlideRecoilIronsightRight { get; set; }
    [CsvColumn("dov_ViewSlideRecoilIronsight.Right")]
    public double? DovViewSlideRecoilIronsightRight { get; set; }
    [CsvColumn("zombie_ViewSlideRecoilIronsight.Right")]
    public double? ZombieViewSlideRecoilIronsightRight { get; set; }

    [CsvColumn("DamageHeadMultiplier")]
    public double? DamageHeadMultiplier { get; set; }
    [CsvColumn("dov_DamageHeadMultiplier")]
    public double? DovDamageHeadMultiplier { get; set; }
    [CsvColumn("zombie_DamageHeadMultiplier")]
    public double? ZombieDamageHeadMultiplier { get; set; }

    [CsvColumn("DamageChestMultiplier")]
    public double? DamageChestMultiplier { get; set; }
    [CsvColumn("dov_DamageChestMultiplier")]
    public double? DovDamageChestMultiplier { get; set; }
    [CsvColumn("zombie_DamageChestMultiplier")]
    public double? ZombieDamageChestMultiplier { get; set; }

    [CsvColumn("DamageStomachMultiplier")]
    public double? DamageStomachMultiplier { get; set; }
    [CsvColumn("dov_DamageStomachMultiplier")]
    public double? DovDamageStomachMultiplier { get; set; }
    [CsvColumn("zombie_DamageStomachMultiplier")]
    public double? ZombieDamageStomachMultiplier { get; set; }

    [CsvColumn("DamageLegMultiplier")]
    public double? DamageLegMultiplier { get; set; }
    [CsvColumn("dov_DamageLegMultiplier")]
    public double? DovDamageLegMultiplier { get; set; }
    [CsvColumn("zombie_DamageLegMultiplier")]
    public double? ZombieDamageLegMultiplier { get; set; }

    [CsvColumn("DamageArmMultiplier")]
    public double? DamageArmMultiplier { get; set; }
    [CsvColumn("dov_DamageArmMultiplier")]
    public double? DovDamageArmMultiplier { get; set; }
    [CsvColumn("zombie_DamageArmMultiplier")]
    public double? ZombieDamageArmMultiplier { get; set; }

    [CsvColumn("DamageGeneric")]
    public double? DamageGeneric { get; set; }
    [CsvColumn("dov_DamageGeneric")]
    public double? DovDamageGeneric { get; set; }
    [CsvColumn("zombie_DamageGeneric")]
    public double? ZombieDamageGeneric { get; set; }

    [CsvColumn("ShakeScale")]
    public double? ShakeScale { get; set; }
    [CsvColumn("dov_ShakeScale")]
    public double? DovShakeScale { get; set; }
    [CsvColumn("zombie_ShakeScale")]
    public double? ZombieShakeScale { get; set; }

    [CsvColumn("ShakeFreq")]
    public double? ShakeFreq { get; set; }
    [CsvColumn("dov_ShakeFreq")]
    public double? DovShakeFreq { get; set; }
    [CsvColumn("zombie_ShakeFreq")]
    public double? ZombieShakeFreq { get; set; }

    [CsvColumn("ShakeDuration")]
    public double? ShakeDuration { get; set; }
    [CsvColumn("dov_ShakeDuration")]
    public double? DovShakeDuration { get; set; }
    [CsvColumn("zombie_ShakeDuration")]
    public double? ZombieShakeDuration { get; set; }

    [CsvColumn("CrosshairMinDistance")]
    public int? CrosshairMinDistance { get; set; }
    [CsvColumn("dov_CrosshairMinDistance")]
    public int? DovCrosshairMinDistance { get; set; }
    [CsvColumn("zombie_CrosshairMinDistance")]
    public int? ZombieCrosshairMinDistance { get; set; }

    [CsvColumn("CrosshairDeltaDistance")]
    public int? CrosshairDeltaDistance { get; set; }
    [CsvColumn("dov_CrosshairDeltaDistance")]
    public int? DovCrosshairDeltaDistance { get; set; }
    [CsvColumn("zombie_CrosshairDeltaDistance")]
    public int? ZombieCrosshairDeltaDistance { get; set; }

    [CsvColumn("weight")]
    public double? Weight { get; set; }
    [CsvColumn("dov_weight")]
    public double? DovWeight { get; set; }
    [CsvColumn("zombie_weight")]
    public double? ZombieWeight { get; set; }

    [CsvColumn("ZMBuyPrice")]
    public int? ZMBuyPrice { get; set; }
    [CsvColumn("dov_ZMBuyPrice")]
    public int? DovZMBuyPrice { get; set; }

    [CsvColumn("ZMWeight")]
    public int? ZMWeight { get; set; }
    [CsvColumn("dov_ZMWeight")]
    public int? DovZMWeight { get; set; }

    [CsvColumn("recoilpushbackvalue")]
    public double? RecoilPushbackValue { get; set; }
    [CsvColumn("dov_recoilpushbackvalue")]
    public double? DovRecoilPushbackValue { get; set; }
    [CsvColumn("zombie_recoilpushbackvalue")]
    public double? ZombieRecoilPushbackValue { get; set; }

    [CsvColumn("ironsightwalkbobbingstrength")]
    public double? IronsightWalkBobbingStrength { get; set; }
    [CsvColumn("dov_ironsightwalkbobbingstrength")]
    public double? DovIronsightWalkBobbingStrength { get; set; }
    [CsvColumn("zombie_ironsightwalkbobbingstrength")]
    public double? ZombieIronsightWalkBobbingStrength { get; set; }

    [CsvColumn("MetalPenetrationDepth")]
    public double? MetalPenetrationDepth { get; set; }
    [CsvColumn("dov_MetalPenetrationDepth")]
    public double? DovMetalPenetrationDepth { get; set; }
    [CsvColumn("zombie_MetalPenetrationDepth")]
    public double? ZombieMetalPenetrationDepth { get; set; }

    [CsvColumn("GlassPenetrationDepth")]
    public double? GlassPenetrationDepth { get; set; }
    [CsvColumn("dov_GlassPenetrationDepth")]
    public double? DovGlassPenetrationDepth { get; set; }
    [CsvColumn("zombie_GlassPenetrationDepth")]
    public double? ZombieGlassPenetrationDepth { get; set; }

    [CsvColumn("ConcretePenetrationDepth")]
    public double? ConcretePenetrationDepth { get; set; }
    [CsvColumn("dov_ConcretePenetrationDepth")]
    public double? DovConcretePenetrationDepth { get; set; }
    [CsvColumn("zombie_ConcretePenetrationDepth")]
    public double? ZombieConcretePenetrationDepth { get; set; }

    [CsvColumn("WoodPenetrationDepth")]
    public double? WoodPenetrationDepth { get; set; }
    [CsvColumn("dov_WoodPenetrationDepth")]
    public double? DovWoodPenetrationDepth { get; set; }
    [CsvColumn("zombie_WoodPenetrationDepth")]
    public double? ZombieWoodPenetrationDepth { get; set; }

    [CsvColumn("OtherPenetrationDepth")]
    public double? OtherPenetrationDepth { get; set; }
    [CsvColumn("dov_OtherPenetrationDepth")]
    public double? DovOtherPenetrationDepth { get; set; }
    [CsvColumn("zombie_OtherPenetrationDepth")]
    public double? ZombieOtherPenetrationDepth { get; set; }

    [CsvColumn("MetalDamageModifier")]
    public double? MetalDamageModifier { get; set; }
    [CsvColumn("dov_MetalDamageModifier")]
    public double? DovMetalDamageModifier { get; set; }
    [CsvColumn("zombie_MetalDamageModifier")]
    public double? ZombieMetalDamageModifier { get; set; }

    [CsvColumn("GlassDamageModifier")]
    public double? GlassDamageModifier { get; set; }
    [CsvColumn("dov_GlassDamageModifier")]
    public double? DovGlassDamageModifier { get; set; }
    [CsvColumn("zombie_GlassDamageModifier")]
    public double? ZombieGlassDamageModifier { get; set; }

    [CsvColumn("ConcreteDamageModifier")]
    public double? ConcreteDamageModifier { get; set; }
    [CsvColumn("dov_ConcreteDamageModifier")]
    public double? DovConcreteDamageModifier { get; set; }
    [CsvColumn("zombie_ConcreteDamageModifier")]
    public double? ZombieConcreteDamageModifier { get; set; }

    [CsvColumn("WoodDamageModifier")]
    public double? WoodDamageModifier { get; set; }
    [CsvColumn("dov_WoodDamageModifier")]
    public double? DovWoodDamageModifier { get; set; }
    [CsvColumn("zombie_WoodDamageModifier")]
    public double? ZombieWoodDamageModifier { get; set; }

    [CsvColumn("OtherDamageModifier")]
    public double? OtherDamageModifier { get; set; }
    [CsvColumn("dov_OtherDamageModifier")]
    public double? DovOtherDamageModifier { get; set; }
    [CsvColumn("zombie_OtherDamageModifier")]
    public double? ZombieOtherDamageModifier { get; set; }

    [CsvColumn("NearwallDistance")]
    public int? NearwallDistance { get; set; }
    [CsvColumn("dov_NearwallDistance")]
    public int? DovNearwallDistance { get; set; }
    [CsvColumn("zombie_NearwallDistance")]
    public int? ZombieNearwallDistance { get; set; }

    [CsvColumn("primary_ammo")]
    public string PrimaryAmmo { get; set; } = string.Empty;

    [CsvColumn("clip_size")]
    public string ClipSize { get; set; } = string.Empty;
    [CsvColumn("dov_clip_size")]
    public string DovClipSize { get; set; } = string.Empty;
    [CsvColumn("zombie_clip_size")]
    public string ZombieClipSize { get; set; } = string.Empty;

    //浅拷贝 仅在字段为值类型/不可变类型(string)时安全 新增可变引用类型字段(List/数组等)时需改为深拷贝
    public WeaponData ShallowClone() => (WeaponData)MemberwiseClone();
}