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

    [Name("default_clip")]
    public int? DefaultClip { get; set; }

    [Name("ExtraBulletChamber")]
    public int? ExtraBulletChamber { get; set; }
    [Name("dov_ExtraBulletChamber")]
    public int? DovExtraBulletChamber { get; set; }

    [Name("bullets_per_shot")]
    public int? BulletsPerShot { get; set; }

    [Name("FireRate")]
    public int? FireRate { get; set; }
    [Name("dov_FireRate")]
    public int? DovFireRate { get; set; }

    [Name("BulletSpreadDegrees")]
    public double? BulletSpread { get; set; }
    [Name("dov_BulletSpreadDegrees")]
    public double? DovBulletSpread { get; set; }

    [Name("BulletSpreadDegreesIronsighted")]
    public double? BulletSpreadDegreesIronsighted { get; set; }
    [Name("dov_BulletSpreadDegreesIronsighted")]
    public double? DovBulletSpreadDegreesIronsighted { get; set; }

    [Name("BulletSpreadDegreesBipod")]
    public double? BulletSpreadDegreesBipod { get; set; }
    [Name("dov_BulletSpreadDegreesBipod")]
    public double? DovBulletSpreadDegreesBipod { get; set; }

    [Name("BulletSpreadDegreesBipodIronsighted")]
    public double? BulletSpreadDegreesBipodIronsighted { get; set; }
    [Name("dov_BulletSpreadDegreesBipodIronsighted")]
    public double? DovBulletSpreadDegreesBipodIronsighted { get; set; }

    [Name("rangemodifier")]
    public double? RangeModifier { get; set; }
    [Name("dov_rangemodifier")]
    public double? DovRangeModifier { get; set; }

    [Name("IronsightSpeedScale")]
    public double? IronsightSpeedScale { get; set; }
    [Name("dov_IronsightSpeedScale")]
    public double? DovIronsightSpeedScale { get; set; }

    [Name("CrouchSpreadMultiplier")]
    public double? CrouchSpreadMultiplier { get; set; }
    [Name("dov_CrouchSpreadMultiplier")]
    public double? DovCrouchSpreadMultiplier { get; set; }

    [Name("ProneSpreadMultiplier")]
    public double? ProneSpreadMultiplier { get; set; }
    [Name("dov_ProneSpreadMultiplier")]
    public double? DovProneSpreadMultiplier { get; set; }

    [Name("StandMoveSpreadMultiplier")]
    public double? StandMoveSpreadMultiplier { get; set; }
    [Name("dov_StandMoveSpreadMultiplier")]
    public double? DovStandMoveSpreadMultiplier { get; set; }

    [Name("SneakMoveSpreadMultiplier")]
    public double? SneakMoveSpreadMultiplier { get; set; }
    [Name("dov_SneakMoveSpreadMultiplier")]
    public double? DovSneakMoveSpreadMultiplier { get; set; }

    [Name("CrouchMoveSpreadMultiplier")]
    public double? CrouchMoveSpreadMultiplier { get; set; }
    [Name("dov_CrouchMoveSpreadMultiplier")]
    public double? DovCrouchMoveSpreadMultiplier { get; set; }

    [Name("JumpSpreadMultiplier")]
    public double? JumpSpreadMultiplier { get; set; }
    [Name("dov_JumpSpreadMultiplier")]
    public double? DovJumpSpreadMultiplier { get; set; }

    [Name("ViewSlideRecoil.Up")]
    public double? ViewSlideRecoilUp { get; set; }
    [Name("dov_ViewSlideRecoil.Up")]
    public double? DovViewSlideRecoilUp { get; set; }

    [Name("ViewSlideRecoil.Right")]
    public double? ViewSlideRecoilRight { get; set; }
    [Name("dov_ViewSlideRecoil.Right")]
    public double? DovViewSlideRecoilRight { get; set; }

    [Name("ViewSlideRecoilIronsight.Up")]
    public double? ViewSlideRecoilIronsightUp { get; set; }
    [Name("dov_ViewSlideRecoilIronsight.Up")]
    public double? DovViewSlideRecoilIronsightUp { get; set; }

    [Name("ViewSlideRecoilIronsight.Right")]
    public double? ViewSlideRecoilIronsightRight { get; set; }
    [Name("dov_ViewSlideRecoilIronsight.Right")]
    public double? DovViewSlideRecoilIronsightRight { get; set; }

    [Name("DamageHeadMultiplier")]
    public double? DamageHeadMultiplier { get; set; }
    [Name("dov_DamageHeadMultiplier")]
    public double? DovDamageHeadMultiplier { get; set; }

    [Name("DamageChestMultiplier")]
    public double? DamageChestMultiplier { get; set; }
    [Name("dov_DamageChestMultiplier")]
    public double? DovDamageChestMultiplier { get; set; }

    [Name("DamageStomachMultiplier")]
    public double? DamageStomachMultiplier { get; set; }
    [Name("dov_DamageStomachMultiplier")]
    public double? DovDamageStomachMultiplier { get; set; }

    [Name("DamageLegMultiplier")]
    public double? DamageLegMultiplier { get; set; }
    [Name("dov_DamageLegMultiplier")]
    public double? DovDamageLegMultiplier { get; set; }

    [Name("DamageArmMultiplier")]
    public double? DamageArmMultiplier { get; set; }
    [Name("dov_DamageArmMultiplier")]
    public double? DovDamageArmMultiplier { get; set; }

    [Name("DamageGeneric")]
    public double? DamageGeneric { get; set; }
    [Name("dov_DamageGeneric")]
    public double? DovDamageGeneric { get; set; }

    [Name("ShakeScale")]
    public double? ShakeScale { get; set; }

    [Name("ShakeFreq")]
    public double? ShakeFreq { get; set; }

    [Name("ShakeDuration")]
    public double? ShakeDuration { get; set; }

    [Name("CrosshairMinDistance")]
    public int? CrosshairMinDistance { get; set; }
    [Name("dov_CrosshairMinDistance")]
    public int? DovCrosshairMinDistance { get; set; }

    [Name("CrosshairDeltaDistance")]
    public int? CrosshairDeltaDistance { get; set; }
    [Name("dov_CrosshairDeltaDistance")]
    public int? DovCrosshairDeltaDistance { get; set; }

    [Name("weight")]
    public double? Weight { get; set; }
    [Name("dov_weight")]
    public double? DovWeight { get; set; }

    [Name("ZMBuyPrice")]
    public int? ZMBuyPrice { get; set; }
    [Name("dov_ZMBuyPrice")]
    public int? DovZMBuyPrice { get; set; }

    [Name("ZMWeight")]
    public int? ZMWeight { get; set; }
    [Name("dov_ZMWeight")]
    public int? DovZMWeight { get; set; }

    [Name("recoilpushbackvalue")]
    public double? RecoilPushbackValue { get; set; }

    [Name("ironsightwalkbobbingstrength")]
    public double? IronsightWalkBobbingStrength { get; set; }

    [Name("MetalPenetrationDepth")]
    public double? MetalPenetrationDepth { get; set; }

    [Name("GlassPenetrationDepth")]
    public double? GlassPenetrationDepth { get; set; }

    [Name("ConcretePenetrationDepth")]
    public double? ConcretePenetrationDepth { get; set; }

    [Name("WoodPenetrationDepth")]
    public double? WoodPenetrationDepth { get; set; }

    [Name("OtherPenetrationDepth")]
    public double? OtherPenetrationDepth { get; set; }

    [Name("MetalDamageModifier")]
    public double? MetalDamageModifier { get; set; }

    [Name("GlassDamageModifier")]
    public double? GlassDamageModifier { get; set; }

    [Name("ConcreteDamageModifier")]
    public double? ConcreteDamageModifier { get; set; }

    [Name("WoodDamageModifier")]
    public double? WoodDamageModifier { get; set; }

    [Name("OtherDamageModifier")]
    public double? OtherDamageModifier { get; set; }

    [Name("NearwallDistance")]
    public int? NearwallDistance { get; set; }

    [Name("primary_ammo")]
    public string PrimaryAmmo { get; set; } = string.Empty;

    [Name("clip_size")]
    public string ClipSize { get; set; } = string.Empty;
    [Name("dov_clip_size")]
    public string DovClipSize { get; set; } = string.Empty;
}