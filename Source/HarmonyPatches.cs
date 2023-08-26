using HarmonyLib;
using System.Collections.Generic;
using RimWorld;
using Verse;
using System;
using System.Reflection;

namespace XenobionicPatcher {
    [StaticConstructorOnStartup]
    [HarmonyPatch]
    internal class HarmonyPatches {
        /* Fix AllRecipes to remove dupes.
         * 
         * See bug report: https://ludeon.com/forums/index.php?topic=49779.0
         */

        // AllRecipes builds a permanent cache, so running this more than once is wasteful, especially for
        // a getter.
        internal static readonly HashSet<int> hasRemovedDupesFromRecipeCache = new HashSet<int> {};

        [HarmonyPatch(typeof(ThingDef), nameof(ThingDef.AllRecipes), MethodType.Getter)]
        [HarmonyPostfix]
        private static void AllRecipes_Postfix(ThingDef __instance, List<RecipeDef> __result) {
            // already ran; bounce
            if ( hasRemovedDupesFromRecipeCache.Contains(__instance.GetHashCode()) ) return;

            __result.RemoveDuplicates();
            hasRemovedDupesFromRecipeCache.Add(__instance.GetHashCode());
        }

        // NOTE: Sadly, the StatRequest object is basically useless here, because Def stats don't get additional context, like Pawn objects.

        // Override the RecipeDef.SpecialDisplayStats method to display our own surgery stats.
        [HarmonyPatch(typeof(RecipeDef), nameof(RecipeDef.SpecialDisplayStats))]
        [HarmonyPostfix]
        private static IEnumerable<StatDrawEntry> RecipeDef_SpecialDisplayStats_Postfix(IEnumerable<StatDrawEntry> values, RecipeDef __instance, StatRequest req) {
            // Cycle through the entries
            foreach (StatDrawEntry value in values) yield return value;
                
            // Add our own
            if (__instance.IsSurgery) {
                foreach (StatDrawEntry value in ExtraSurgeryStats.SpecialDisplayStats(__instance, req)) yield return value;
            }
        }

        // Ditto for HediffDef
        [HarmonyPatch(typeof(HediffDef), nameof(HediffDef.SpecialDisplayStats))]
        [HarmonyPostfix]
        private static IEnumerable<StatDrawEntry> HediffDef_SpecialDisplayStats_Postfix(IEnumerable<StatDrawEntry> values, HediffDef __instance, StatRequest req) {
            // Cycle through the entries
            foreach (StatDrawEntry value in values) yield return value;
                
            // Add our own
            foreach (StatDrawEntry value in ExtraHediffStats.SpecialDisplayStats(__instance, req)) yield return value;
        }

        // BodyPartDef doesn't have a SpecialDisplayStats, so we'll have to tie this to the base method, unfortunately
        [HarmonyPatch(typeof(Def), nameof(Def.SpecialDisplayStats))]
        [HarmonyPostfix]
        private static IEnumerable<StatDrawEntry> Def_SpecialDisplayStats_Postfix(IEnumerable<StatDrawEntry> values, Def __instance, StatRequest req) {
            // Cycle through the entries (probably nothing here)
            foreach (StatDrawEntry value in values) yield return value;

            if (!(__instance is BodyPartDef)) yield break;
                
            // Add our own
            foreach (StatDrawEntry value in ExtraBodyPartStats.SpecialDisplayStats((BodyPartDef)__instance, req)) yield return value;
        }

        /* NOTE: VREA's code has a particularly hostile HarmonyPriority(-2147483648) in its Postfix against *all* AvailableOnNow methods,
         * ensuring that Team Oskar's code always gets a chance to override whatever was set.  Fortunately, it uses a separate
         * RecipeIsAvailableOnAndroid method for all of its logic, which we can postfix ourselves, and does not mess with non-Android checks.
         * 
         * I'm still going to take issue with the HarmonyPriority abuse.  One should use something like Priority.Last, instead of escaling into
         * a priority arms race.  Or to recite a Syndrome/CSS cross-over joke: "When everything is !important, nothing is."
         */

        // Harmony patches specifically for Vanilla Races Expanded: Androids
        [HarmonyPatch]
        internal class VREA {
            internal static MethodInfo isAndroidMethod = Helpers.SafeTypeByName("VREAndroids.Utils")?.GetMethod("IsAndroid", new Type[] { typeof(Pawn) });

            [HarmonyReversePatch(HarmonyReversePatchType.Snapshot)]
            [HarmonyPatch(typeof(Recipe_Surgery), "AvailableOnNow")]
            private static bool BaseAONMethod(object instance, Thing thing, BodyPartRecord part = null) {
                throw new NotImplementedException("It's a stub");
            }

            // Allow androids to use surgery types from non-VREA sources
            [HarmonyPatch]
            internal class RecipeIsAvailableOnAndroid_HarmonyPatch {
                [HarmonyTargetMethod]
                private static MethodBase TargetMethod() {
                    // If the mod isn't loaded, don't bother with this whole class
                    Type VREAndroids_PatchType = Helpers.SafeTypeByName("VREAndroids.RecipeWorker_AvailableOnNow_Patch");
                    if (VREAndroids_PatchType == null) return null;
                    return VREAndroids_PatchType.GetMethod("RecipeIsAvailableOnAndroid", AccessTools.all);
                }

                [HarmonyPostfix]
                private static void Postfix(ref bool __result, RecipeWorker recipeWorker, Pawn pawn) {
                    /* NOTE: This is the original check:
                     * 
                     * // What about Awakened Androids with drug addictions?
                     * !(recipeWorker is Recipe_AdministerIngestible) &&
                     * 
                     * // Why?  If it has the body part installed, just allow it and let it follow the base code.
                     * (!(recipeWorker is Recipe_RemoveBodyPart) || recipeWorker is Recipe_RemoveArtificialBodyPart) &&
                     * 
                     * // Sure, fine, but XP wants it anyway, because most of my audience wants to slap on biological parts anywhere they want
                     * !(recipeWorker is Recipe_InstallNaturalBodyPart) &&
                     * 
                     * // WTF is it doing blocking recipes.Contains?  This is why it just shows a bunch of "Remove" recipes and nothing else!
                     * (!pawn.def.recipes.Contains(recipeWorker.recipe) || recipeWorker.recipe == VREA_DefOf.VREA_RemoveArtificialPart) &&
                     * 
                     * // Sure, seems fine
                     * recipeWorker.recipe.addsHediff != HediffDefOf.Sterilized;
                     * 
                     * Because of the above mess, I'm falling back to the base Recipe_Surgery check, with some modifications.
                     */

                    __result =
                        // Already did the Pawn check when we entered this function

                        // Androids aren't fertile, so skip the fertility surgeries
                        !(
                            pawn.Sterile() && (
                                recipeWorker.recipe.mustBeFertile || recipeWorker.recipe.genderPrerequisite != null ||
                                recipeWorker is Recipe_TerminatePregnancy || recipeWorker is Recipe_ExtractOvum ||
                                recipeWorker is Recipe_ImplantEmbryo      || recipeWorker is Recipe_ImplantIUD ||
                                recipeWorker.recipe.addsHediff          == HediffDefOf.Sterilized ||
                                recipeWorker.recipe.addsHediffOnFailure == HediffDefOf.Sterilized
                            )
                        ) &&
                        // Also, blood stuff
                        !(recipeWorker is Recipe_ExtractHemogen || recipeWorker is Recipe_BloodTransfusion) &&

                        (recipeWorker.recipe.allowedForQuestLodgers || !pawn.IsQuestLodger()) &&
                        // (No age restrictions for Androids; removing that piece)
                        (!recipeWorker.recipe.developmentalStageFilter.HasValue || recipeWorker.recipe.developmentalStageFilter.Value.Has(pawn.DevelopmentalStage))
                    ;
                }
            }

            // Allow non-androids to use VREA surgery types
            [HarmonyPatch]
            internal class AvailableOnNow_HarmonyPatch {
                [HarmonyTargetMethods]
                private static IEnumerable<MethodBase> TargetMethods() {
                    List<Type> moddedWorkerClasses = new List<Type> {
                        Helpers.SafeTypeByName("VREAndroids.Recipe_InstallAndroidPart"),
                        Helpers.SafeTypeByName("VREAndroids.Recipe_InstallReactor"),
                    };
                    foreach (Type vreaClass in moddedWorkerClasses) {
                        // If the mod isn't loaded, don't bother with this whole class
                        if (vreaClass == null) yield break;

                        MethodInfo method = AccessTools.Method(vreaClass, "AvailableOnNow");
                        if (method != null) yield return method;
                    }
                }

                // See, I'm being nice and leaving it at the default Harmony priority...
                [HarmonyPrefix]
                private static bool Prefix(ref bool __result, Recipe_Surgery __instance, Thing thing, BodyPartRecord part) {
                    if (thing == null || !(thing is Pawn pawn)) {
                        __result = false;
                        return false;
                    }

                    bool isAndroid = (bool)isAndroidMethod.Invoke(null, new object[] { pawn });
                    if (isAndroid) return true;  // let it fall all the way to our Postfix above

                    // Otherwise, use the Recipe_Surgery.AvailableOnNow method verbatim
                    __result = BaseAONMethod(__instance, thing, part);
                    return false;
                }
            }

            // Don't hide android parts on non-androids
            [HarmonyPatch]
            internal class Hediff_AndroidPart_HarmonyPatch {
                [HarmonyTargetMethod]
                private static MethodBase TargetMethod() {
                    // If the mod isn't loaded, don't bother with this whole class
                    Type VREAndroids_PatchType = Helpers.SafeTypeByName("VREAndroids.Hediff_AndroidPart");
                    if (VREAndroids_PatchType == null) return null;
                    return AccessTools.PropertyGetter(VREAndroids_PatchType, "Visible");
                }

                [HarmonyPrefix]
                private static bool Prefix(ref bool __result, Hediff_AddedPart __instance) {
                    bool isAndroid = (bool)isAndroidMethod.Invoke(null, new object[] { __instance.pawn });
                    __result = !isAndroid;
                    return false;
                }
            }

            // See also large comment about VREA's RecipeDef.AvailableNow patch on XenobionicPatcher.Base.DefsLoaded
        }
    }
}
