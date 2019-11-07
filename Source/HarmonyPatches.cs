using Harmony;
using System.Collections.Generic;
using Verse;

namespace XenobionicPatcher {
    [StaticConstructorOnStartup]
    internal class HarmonyPatches {
        /* Fix AllRecipes to remove dupes.
         * 
         * See bug report: https://ludeon.com/forums/index.php?topic=49779.0
         */

        // FIXME: Not very efficient...
        [HarmonyPatch(typeof (ThingDef), "AllRecipes", MethodType.Getter)]
        public static class AllRecipes_Postfix {
            [HarmonyPostfix]
            static void Postfix(List<RecipeDef> __result) {
                __result.RemoveDuplicates();
            }
        }
    }
}
