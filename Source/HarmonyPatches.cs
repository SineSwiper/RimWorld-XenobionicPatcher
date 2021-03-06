﻿using HarmonyLib;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace XenobionicPatcher {
    [StaticConstructorOnStartup]
    internal class HarmonyPatches {
        /* Fix AllRecipes to remove dupes.
         * 
         * See bug report: https://ludeon.com/forums/index.php?topic=49779.0
         */

        [HarmonyPatch(typeof (ThingDef), "AllRecipes", MethodType.Getter)]
        public static class AllRecipes_Postfix {
            // AllRecipes builds a permanent cache, so running this more than once is wasteful, especially for
            // a getter.
            internal static readonly HashSet<int> hasRemovedDupesFromRecipeCache = new HashSet<int> {};

            [HarmonyPostfix]
            static void Postfix(ThingDef __instance, List<RecipeDef> __result) {
                // already ran; bounce
                if ( hasRemovedDupesFromRecipeCache.Contains(__instance.GetHashCode()) ) return;

                __result.RemoveDuplicates();
                hasRemovedDupesFromRecipeCache.Add(__instance.GetHashCode());
            }
        }

        // NOTE: Sadly, the StatRequest object is basically useless here, because Def stats don't get additional context, like Pawn objects.

        // Override the RecipeDef.SpecialDisplayStats method to display our own surgery stats.
        [HarmonyPatch(typeof(RecipeDef), "SpecialDisplayStats")]
        private static class RecipeDef_SpecialDisplayStats_Postfix {
            [HarmonyPostfix]
            static IEnumerable<StatDrawEntry> Postfix(IEnumerable<StatDrawEntry> values, RecipeDef __instance, StatRequest req) {
                // Cycle through the entries
                foreach (StatDrawEntry value in values) yield return value;
                
                // Add our own
                if (__instance.IsSurgery) {
                    foreach (StatDrawEntry value in ExtraSurgeryStats.SpecialDisplayStats(__instance, req)) yield return value;
                }
            }
        }

        // Ditto for HediffDef
        [HarmonyPatch(typeof(HediffDef), "SpecialDisplayStats")]
        private static class HediffDef_SpecialDisplayStats_Postfix {
            [HarmonyPostfix]
            static IEnumerable<StatDrawEntry> Postfix(IEnumerable<StatDrawEntry> values, HediffDef __instance, StatRequest req) {
                // Cycle through the entries
                foreach (StatDrawEntry value in values) yield return value;
                
                // Add our own
                foreach (StatDrawEntry value in ExtraHediffStats.SpecialDisplayStats(__instance, req)) yield return value;
            }
        }

        // BodyPartDef doesn't have a SpecialDisplayStats, so we'll have to tie this to the base method, unfortunately
        [HarmonyPatch(typeof(Def), "SpecialDisplayStats")]
        private static class Def_SpecialDisplayStats_Postfix {
            [HarmonyPostfix]
            static IEnumerable<StatDrawEntry> Postfix(IEnumerable<StatDrawEntry> values, Def __instance, StatRequest req) {
                // Cycle through the entries (probably nothing here)
                foreach (StatDrawEntry value in values) yield return value;

                if (!(__instance is BodyPartDef)) yield break;
                
                // Add our own
                foreach (StatDrawEntry value in ExtraBodyPartStats.SpecialDisplayStats((BodyPartDef)__instance, req)) yield return value;
            }
        }
    }
}
