using HarmonyLib;
using MCM.Abstractions.Settings.Base.Global;
using System.Collections.Generic;
using System.Reflection.Emit;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using MCM.Abstractions.Attributes.v2;
using MCM.Abstractions.Attributes;

namespace PartialParry
{

    internal sealed class Settings : AttributeGlobalSettings<Settings>
    {
        private int _baseMagnitudeParried = 100;
        private int _perfectParryBonus = 150;
        private bool _showLog = false;
        private int _shieldBreakWeaponDefendMalus = 90;
        private int _twoHandedWeaponParryBonus = 150;
        private int _littleWeaponParryMalus = 75;

        public override string Id => "PartialParryMeleeHit";
        public override string DisplayName => $"Parry doesn't always block everything";
        public override string FolderName => "Parry Setting";
        public override string FormatType => "json2";

        [SettingPropertyInteger("parry base magnitude", 0, 1000, RequireRestart = false, HintText = "how much base damage you can block before it starts crushing through parry")]
        [SettingPropertyGroup("General")]
        public int baseMagnitudeParried
        {
            get => _baseMagnitudeParried;
            set
            {
                if (_baseMagnitudeParried != value)
                {
                    _baseMagnitudeParried = value;
                    OnPropertyChanged();
                }
            }
        }

        [SettingPropertyInteger("perfect parry bonus in %", 0, 1000, RequireRestart = false, HintText = "multiply the \"parry base magnitude\" by this percentage if you get a perfect parry (blocking right before you get hit)")]
        [SettingPropertyGroup("General")]
        public int perfectParryBonus
        {
            get => _perfectParryBonus;
            set
            {
                if (_perfectParryBonus != value)
                {
                    _perfectParryBonus = value;
                    OnPropertyChanged();
                }
            }
        }

        [SettingPropertyBool("should show log", Order = 1, RequireRestart = false, HintText = "show the magnitude before and after parry in log")]
        [SettingPropertyGroup("General")]
        public bool showLog
        {
            get => _showLog;
            set
            {
                if (_showLog != value)
                {
                    _showLog = value;
                    OnPropertyChanged();
                }
            }
        }

        [SettingPropertyInteger("malus against shield break weapon %", 0, 100, RequireRestart = false, HintText = "multiply the \"parry base magnitude\" by this percentage if you parry a hit from a weapon with shield damage bonus on shield")]
        [SettingPropertyGroup("General")]
        public int shieldBreakWeaponDefendMalus
        {
            get => _shieldBreakWeaponDefendMalus;
            set
            {
                if (_shieldBreakWeaponDefendMalus != value)
                {
                    _shieldBreakWeaponDefendMalus = value;
                    OnPropertyChanged();
                }
            }
        }

        [SettingPropertyInteger("malus when parrying with a dagger in %", 0, 100, RequireRestart = false, HintText = "multiply the \"parry base magnitude\" by this percentage if you parry with a dagger")]
        [SettingPropertyGroup("General")]
        public int littleWeaponParryMalus
        {
            get => _littleWeaponParryMalus;
            set
            {
                if (_littleWeaponParryMalus != value)
                {
                    _littleWeaponParryMalus = value;
                    OnPropertyChanged();
                }
            }
        }

        [SettingPropertyInteger("two hand weapon bonus in %", 0, 1000, RequireRestart = false, HintText = " multiply the \"parry base magnitude\" by this percentage if you're using a two handed weapon")]
        [SettingPropertyGroup("General")]
        public int twoHandedWeaponParryBonus
        {
            get => _twoHandedWeaponParryBonus;
            set
            {
                if (_twoHandedWeaponParryBonus != value)
                {
                    _twoHandedWeaponParryBonus = value;
                    OnPropertyChanged();
                }
            }
        }
    }

    public class SubModule : MBSubModuleBase
    {
        protected override void OnSubModuleLoad()
        {
            Harmony.DEBUG = true;
            Harmony harmony = new Harmony ("PartialParry");
            harmony.PatchAll();
        }

        protected override void OnSubModuleUnloaded()
        {
            base.OnSubModuleUnloaded();

        }
    }

    [HarmonyPatch(typeof (Mission))]
    [HarmonyPatch ("MeleeHitCallback")]
    public class PartialParryMeleeHitCallback
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);

            for (int i = 1; i < code.Count - 1; i++)
            {
                // we search for the first local set local variable (which is a "flag" who he used to define if the attack as been blocked)
                if (code[i].opcode == OpCodes.Stloc_0)
                {
                    // we make sure that it will be set at false by changing the instruction where the code jump when true
                    // so that the registerBlow will always be called since the blow is not parried
                    if (code[i - 1].opcode == OpCodes.Ldc_I4_1)
                    {
                        code[i - 1].opcode = OpCodes.Ldc_I4_0;
                        return code;
                    }
                    else
                    {
                        break;
                    }
                }
            }
            Debug.Assert(false, "code not found, taleword update as changed MeleeHitCallback?");
            return code;
        }
    }

    [HarmonyPatch(typeof(Mission), "ComputeBlowDamage")]
    public class PartialParryComputeBlowDamage
    {
        static private bool IsWeaponTwoHanded(ref MissionWeapon weapon)
        {
            var weaponClass = weapon.CurrentUsageItem.WeaponClass;
            return weaponClass == WeaponClass.TwoHandedAxe
                || weaponClass == WeaponClass.TwoHandedSword
                || weaponClass == WeaponClass.TwoHandedMace
                || weaponClass == WeaponClass.TwoHandedPolearm;
        }

        // here we reduce the magnitude which his the damage before armor so that parry will not always parry everything
        static bool Prefix(ref AttackInformation attackInformation, ref AttackCollisionData attackCollisionData, ref float magnitude, ref MissionWeapon attackerWeapon)
        {
            if (!attackCollisionData.AttackBlockedWithShield
                && (attackCollisionData.CollisionResult == CombatCollisionResult.Parried 
                    || attackCollisionData.CollisionResult == CombatCollisionResult.Blocked))
            {
                if (GlobalSettings<Settings>.Instance == null)
                {
                    Debug.Assert(false, "setting not generated?");
                    return true;
                }
                float baseParryMagnitude = GlobalSettings<Settings>.Instance.baseMagnitudeParried;

                if (attackCollisionData.CollisionResult == CombatCollisionResult.Parried)
                    baseParryMagnitude *= (float)GlobalSettings<Settings>.Instance.perfectParryBonus / 100f;
                if (attackerWeapon.CurrentUsageItem.WeaponFlags.HasAnyFlag(WeaponFlags.BonusAgainstShield))
                    baseParryMagnitude *= (float)GlobalSettings<Settings>.Instance.shieldBreakWeaponDefendMalus / 100f;
                if (IsWeaponTwoHanded(ref attackInformation.VictimMainHandWeapon))
                    baseParryMagnitude *= (float)GlobalSettings<Settings>.Instance.twoHandedWeaponParryBonus / 100f;
                if (attackInformation.VictimMainHandWeapon.CurrentUsageItem.WeaponClass == WeaponClass.Dagger)
                    baseParryMagnitude *= (float)GlobalSettings<Settings>.Instance.littleWeaponParryMalus / 100f;

                float oldMagnitude = magnitude;
                magnitude -= baseParryMagnitude;
                magnitude = MathF.Max(0f, magnitude);

                if (GlobalSettings<Settings>.Instance.showLog && (attackInformation.IsAttackerPlayer || attackInformation.IsVictimPlayer))
                {
                    InformationManager.DisplayMessage(new InformationMessage(string.Format("magnitude before parry:{0} after:{1} ", oldMagnitude, magnitude)));
                }
            }
            return true;
        }
    }
}