using HarmonyLib;
using System.Collections.Generic;
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
    }
}
