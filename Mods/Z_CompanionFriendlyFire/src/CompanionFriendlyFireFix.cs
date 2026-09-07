using System;
using HarmonyLib;
using UnityEngine;
using CHHusbandry.Data;

namespace CompanionFriendlyFireFix
{
    // Entry point discovered by 7DTD mod loader (IModApi.InitMod).
    public class CompanionFriendlyFireFixMod : IModApi
    {
        public void InitMod(global::Mod _modInstance)
        {
            try
            {
                var harmony = new Harmony("com.hellsjanitor.CompanionFriendlyFireFix");
                harmony.PatchAll(GetType().Assembly);
                Log.Out("[CompanionFriendlyFireFix] Harmony patches applied.");
            }
            catch (Exception ex)
            {
                Log.Error("[CompanionFriendlyFireFix] Failed to apply patches: " + ex);
            }
        }
    }

    [HarmonyPatch(typeof(EntityAlive), "ProcessDamageResponseLocal")]
    public static class FriendlyFirePatch
    {
        private static bool Prefix(EntityAlive __instance, DamageResponse __0)
        {
            try
            {
                int sourceId = __0.Source.getEntityId();
                if (sourceId == 0) return true;                 // no attacker -> let damage run
                if (__instance == null) return true;

                // Not a CHH companion at all -> let damage run (wild animals unaffected).
                if (EntityInfoManager.Instance == null) return true;
                EntityInfo info;
                if (!EntityInfoManager.Instance.TryGetValue(__instance.entityId, out info)) return true;
                if (info == null || !info.IsTamed) return true;
                int ownerId = info.OwnerId;
                if (ownerId <= 0) return true;

                World world = GameManager.Instance != null ? GameManager.Instance.World : null;
                if (world == null) return true;
                Entity source = world.GetEntity(sourceId);
                EntityPlayer player = source as EntityPlayer;
                if (player == null) return true;                // zombie/animal/NPC still damages pet

                if (player.entityId == ownerId)
                    return false;                               // owner -> skip original (no damage)

                Entity owner = world.GetEntity(ownerId);
                EntityPlayer ownerPlayer = owner as EntityPlayer;
                if (ownerPlayer != null && player.IsFriendsWith(ownerPlayer))
                    return false;                               // Steam friend -> skip original (no damage)

                return true;                                    // not owner, not friend -> let damage run
            }
            catch (Exception ex)
            {
                Log.Error("[CompanionFriendlyFireFix] Prefix exception (fail-open): " + ex);
                return true;                                    // fail-open: never silently disable damage
            }
        }
    }
}
