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

                // The bond-penalty target type is internal to CHHusbandry; resolve it by
                // full name and patch via AccessTools (works on private/internal members).
                Type penaltyTargetType = FindType("CrystalHellHusbandry.Patches.EntityAlive_ProcessDamageResponseLocal_Patches");
                if (penaltyTargetType != null)
                {
                    System.Reflection.MethodInfo penaltyMethod = AccessTools.Method(penaltyTargetType, "TryApplyOwnerDamageBondPenalty");
                    if (penaltyMethod != null)
                    {
                        System.Reflection.MethodInfo prefix = AccessTools.Method(typeof(OwnerDamagePenaltyPatch), "Prefix");
                        harmony.Patch(penaltyMethod, prefix: new HarmonyMethod(prefix));
                        Log.Out("[CompanionFriendlyFireFix] Bond-penalty suppression patch applied.");
                    }
                    else
                    {
                        Log.Warning("[CompanionFriendlyFireFix] Could not find TryApplyOwnerDamageBondPenalty; owner-hit warning remains.");
                    }
                }
                else
                {
                    Log.Warning("[CompanionFriendlyFireFix] Could not resolve CHH penalty type; owner-hit warning remains.");
                }

                Log.Out("[CompanionFriendlyFireFix] Harmony patches applied.");
            }
            catch (Exception ex)
            {
                Log.Error("[CompanionFriendlyFireFix] Failed to apply patches: " + ex);
            }
        }

        private static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = asm.GetType(fullName, false);
                if (t != null) return t;
            }
            return null;
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

                if (IsOwnerOrFriend(player, ownerId, world))
                    return false;                               // owner/friend -> skip original (no damage)

                return true;                                    // not owner, not friend -> let damage run
            }
            catch (Exception ex)
            {
                Log.Error("[CompanionFriendlyFireFix] Prefix exception (fail-open): " + ex);
                return true;                                    // fail-open: never silently disable damage
            }
        }

        internal static bool IsOwnerOrFriend(EntityPlayer player, int ownerId, World world)
        {
            if (player == null) return false;
            if (player.entityId == ownerId) return true;
            Entity owner = world.GetEntity(ownerId);
            EntityPlayer ownerPlayer = owner as EntityPlayer;
            if (ownerPlayer != null && player.IsFriendsWith(ownerPlayer)) return true;
            return false;
        }
    }

    // Converted to a plain helper class patched manually in InitMod (target type is internal).
    public static class OwnerDamagePenaltyPatch
    {
        // CHH emits the "You hurt {pet}!" warning and applies a bond penalty for
        // owner damage. Since we now block owner damage entirely, that warning and
        // penalty are misleading — no-op when the attacker is the owner or a friend.
        public static bool Prefix(EntityAlive targetEntity, int sourceId)
        {
            try
            {
                if (targetEntity == null) return true;
                if (EntityInfoManager.Instance == null) return true;
                EntityInfo info;
                if (!EntityInfoManager.Instance.TryGetValue(targetEntity.entityId, out info)) return true;
                if (info == null || !info.IsTamed) return true;
                int ownerId = info.OwnerId;
                if (ownerId <= 0) return true;

                World world = GameManager.Instance != null ? GameManager.Instance.World : null;
                if (world == null) return true;
                Entity source = world.GetEntity(sourceId);
                EntityPlayer player = source as EntityPlayer;
                if (player == null) return true;

                if (FriendlyFirePatch.IsOwnerOrFriend(player, ownerId, world))
                    return false;                               // suppress warning + bond penalty

                return true;
            }
            catch (Exception ex)
            {
                Log.Error("[CompanionFriendlyFireFix] BondPenalty prefix exception (fail-open): " + ex);
                return true;
            }
        }
    }

    // DoT buffs like bleeding are applied through EntityBuffs.AddBuff, not through
    // the damage method we already patch. It consults FriendlyFireCheck(instigator),
    // which in v3.2.0 is a stub returning true — so owner/friend hits still apply
    // bleeding and other buffs to the pet. No-op this for owner/friend instigators.
    [HarmonyPatch(typeof(EntityBuffs), "AddBuff",
        new Type[] { typeof(string), typeof(Vector3i), typeof(int), typeof(bool), typeof(bool), typeof(float) })]
    public static class OwnerBuffPatch
    {
        private static bool Prefix(EntityBuffs __instance, int __2)
        {
            try
            {
                if (__instance == null) return true;
                EntityAlive target = __instance.parent;
                if (target == null) return true;
                if (EntityInfoManager.Instance == null) return true;
                EntityInfo info;
                if (!EntityInfoManager.Instance.TryGetValue(target.entityId, out info)) return true;
                if (info == null || !info.IsTamed) return true;
                int ownerId = info.OwnerId;
                if (ownerId <= 0) return true;

                World world = GameManager.Instance != null ? GameManager.Instance.World : null;
                if (world == null) return true;
                Entity source = world.GetEntity(__2);
                EntityPlayer player = source as EntityPlayer;
                if (player == null) return true;

                if (FriendlyFirePatch.IsOwnerOrFriend(player, ownerId, world))
                    return false;                               // owner/friend buff on own pet -> skip

                return true;
            }
            catch (Exception ex)
            {
                Log.Error("[CompanionFriendlyFireFix] Buff prefix exception (fail-open): " + ex);
                return true;
            }
        }
    }
}
