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

                // Needs system is internal to CHH. No-op the hunger/thirst drain loop so
                // tamed companions never need food or water (and never starve/dehydrate).
                Type needsServiceType = FindType("CHHusbandry.Services.CompanionNeedsService");
                if (needsServiceType != null)
                {
                    System.Reflection.MethodInfo needsMethod = AccessTools.Method(needsServiceType, "TryUpdateNeeds");
                    if (needsMethod != null)
                    {
                        System.Reflection.MethodInfo prefix = AccessTools.Method(typeof(NeedsPatch), "Prefix");
                        harmony.Patch(needsMethod, prefix: new HarmonyMethod(prefix));
                        Log.Out("[CompanionFriendlyFireFix] Needs-suppression patch applied.");
                    }
                    else
                    {
                        Log.Warning("[CompanionFriendlyFireFix] Could not find TryUpdateNeeds; companion needs remain.");
                    }
                }
                else
                {
                    Log.Warning("[CompanionFriendlyFireFix] Could not resolve CHH needs service; companion needs remain.");
                }

                CorpseMarkerPatch.Register();

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

    // Fall damage for non-player entities is applied in EntityAlive.fallHitGround,
    // which calls DamageEntity(DamageSource.fall, ...) directly — it does NOT go
    // through the _fallSpeed / FallDamageReduction passive path (that is player-only).
    // So tamed companions still took fall damage when clipping into terrain while
    // following. Zero the damage when the source is a fall and the target is tamed.
    [HarmonyPatch(typeof(EntityAlive), "DamageEntity")]
    public static class FallDamagePatch
    {
        private static bool Prefix(EntityAlive __instance, DamageSource __0, ref int __result)
        {
            try
            {
                if (__instance == null) return true;
                if (__0.damageType != EnumDamageTypes.Falling) return true;   // only fall damage
                if (EntityInfoManager.Instance == null) return true;
                EntityInfo info;
                if (!EntityInfoManager.Instance.TryGetValue(__instance.entityId, out info)) return true;
                if (info == null || !info.IsTamed) return true;
                __result = 0;                                   // tamed companion: no fall damage
                return false;
            }
            catch (Exception ex)
            {
                Log.Error("[CompanionFriendlyFireFix] FallDamage prefix exception (fail-open): " + ex);
                return true;
            }
        }
    }

    // Applied manually in InitMod to the internal CHH CompanionNeedsService.TryUpdateNeeds.
    // No-op so tamed companions never drain hunger/thirst and never take
    // starvation/dehydration damage.
    public static class NeedsPatch
    {
        public static bool Prefix(EntityInfo info)
        {
            try
            {
                if (info == null) return false;                 // nothing to update anyway
                return false;                                   // skip the needs update entirely
            }
            catch (Exception ex)
            {
                Log.Error("[CompanionFriendlyFireFix] Needs prefix exception (fail-open): " + ex);
                return true;
            }
        }
    }

    // NavObjectManager public API used for the corpse marker.
    // Handler registered via ModEvents.EntityKilled.RegisterHandler(...) — same pattern
    // CHH uses (CHHusbandry.ModAPI.OnEntityKilled). Handler signature: void(SEntityKilledData&).
    public static class CorpseMarkerPatch
    {
        private static readonly System.Collections.Generic.Dictionary<int, NavObject> _markers =
            new System.Collections.Generic.Dictionary<int, NavObject>();

        private static bool IsTamedCompanion(EntityAlive e, out int ownerId)
        {
            ownerId = 0;
            if (e == null) return false;
            if (EntityInfoManager.Instance == null) return false;
            EntityInfo info;
            if (!EntityInfoManager.Instance.TryGetValue(e.entityId, out info)) return false;
            if (info == null || !info.IsTamed) return false;
            ownerId = info.OwnerId;
            return ownerId > 0;
        }

        // Registered handler (matches CHH's own OnEntityKilled signature).
        // ModEventHandlerDelegate<SEntityKilledData> is void(SEntityKilledData&) — by ref.
        public static void OnEntityKilled(ref ModEvents.SEntityKilledData args)
        {
            try
            {
                EntityAlive dead = args.KilledEntitiy as EntityAlive;
                int ownerId;
                if (!IsTamedCompanion(dead, out ownerId)) return;
                if (NavObjectManager.Instance == null) return;

                int id = dead.entityId;
                NavObject old;
                if (_markers.TryGetValue(id, out old) && old != null)
                {
                    NavObjectManager.Instance.UnRegisterNavObject(old);
                    _markers.Remove(id);
                }
                NavObject marker = NavObjectManager.Instance.RegisterNavObject(
                    "animaltracking_wolf", dead.position, dead.entityName, false, id, dead);
                if (marker != null) { _markers[id] = marker; }
            }
            catch (Exception ex)
            {
                Log.Error("[CompanionFriendlyFireFix] Corpse marker exception (fail-open): " + ex);
            }
        }

        public static void Register()
        {
            // Same registration CHH uses: ModEvents.EntityKilled.RegisterHandler(handler)
            // where handler is a ModEventHandlerDelegate<SEntityKilledData> (void, by-ref arg).
            ModEvents.EntityKilled.RegisterHandler(OnEntityKilled);
        }

        public static void ClearAll()
        {
            if (NavObjectManager.Instance == null) return;
            foreach (var kv in _markers) { if (kv.Value != null) NavObjectManager.Instance.UnRegisterNavObject(kv.Value); }
            _markers.Clear();
        }
    }
}
