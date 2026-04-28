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

    [Name("default_clip")]
    public int? DefaultClip { get; set; }

    [Name("Surplus Ammo")]
    public int? SurplusAmmo { get; set; }

    [Name("ExtraBulletChamber")]
    public int? ExtraBulletChamber { get; set; }

    [Name("bullets_per_shot")]
    public int? BulletsPerShot { get; set; }

    [Name("FireRate")]
    public int? FireRate { get; set; }

    [Name("BulletSpreadDegrees")]
    public double? BulletSpread { get; set; }

    [Name("BulletSpreadDegreesIronsighted")]
    public double? BulletSpreadDegreesIronsighted { get; set; }

    [Name("BulletSpreadDegreesBipod")]
    public double? BulletSpreadDegreesBipod { get; set; }

    [Name("BulletSpreadDegreesBipodIronsighted")]
    public double? BulletSpreadDegreesBipodIronsighted { get; set; }

    [Name("rangemodifier")]
    public double? RangeModifier { get; set; }

    [Name("IronsightSpeedScale")]
    public double? IronsightSpeedScale { get; set; }

    [Name("CrouchSpreadMultiplier")]
    public double? CrouchSpreadMultiplier { get; set; }

    [Name("ProneSpreadMultiplier")]
    public double? ProneSpreadMultiplier { get; set; }

    [Name("StandMoveSpreadMultiplier")]
    public double? StandMoveSpreadMultiplier { get; set; }

    [Name("SneakMoveSpreadMultiplier")]
    public double? SneakMoveSpreadMultiplier { get; set; }

    [Name("CrouchMoveSpreadMultiplier")]
    public double? CrouchMoveSpreadMultiplier { get; set; }

    [Name("JumpSpreadMultiplier")]
    public double? JumpSpreadMultiplier { get; set; }

    [Name("ViewSlideRecoil.Up")]
    public double? ViewSlideRecoilUp { get; set; }

    [Name("ViewSlideRecoil.Right")]
    public double? ViewSlideRecoilRight { get; set; }

    [Name("ViewSlideRecoilIronsight.Up")]
    public double? ViewSlideRecoilIronsightUp { get; set; }

    [Name("ViewSlideRecoilIronsight.Right")]
    public double? ViewSlideRecoilIronsightRight { get; set; }

    [Name("DamageHeadMultiplier")]
    public double? DamageHeadMultiplier { get; set; }

    [Name("DamageChestMultiplier")]
    public double? DamageChestMultiplier { get; set; }

    [Name("DamageStomachMultiplier")]
    public double? DamageStomachMultiplier { get; set; }

    [Name("DamageLegMultiplier")]
    public double? DamageLegMultiplier { get; set; }

    [Name("DamageArmMultiplier")]
    public double? DamageArmMultiplier { get; set; }

    [Name("DamageGeneric")]
    public double? DamageGeneric { get; set; }

    [Name("ShakeScale")]
    public double? ShakeScale { get; set; }

    [Name("ShakeFreq")]
    public double? ShakeFreq { get; set; }

    [Name("ShakeDuration")]
    public double? ShakeDuration { get; set; }

    [Name("CrosshairMinDistance")]
    public int? CrosshairMinDistance { get; set; }

    [Name("CrosshairDeltaDistance")]
    public int? CrosshairDeltaDistance { get; set; }

    [Name("weight")]
    public double? Weight { get; set; }

    [Name("ZMBuyPrice")]
    public int? ZMBuyPrice { get; set; }

    [Name("ZMWeight")]
    public int? ZMWeight { get; set; }

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
}