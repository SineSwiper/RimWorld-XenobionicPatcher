using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Verse;
using Unity.Burst.Intrinsics;
using Verse.Noise;

namespace XenobionicPatcher {
    public class DefInjectors {
        /* WARNING: Because of the sheer amount of combinations and loops we're dealing with, there is a LOT
         * of caching (both here and within Helpers), HashSets (for duplicate checks), and stopwatch timing.
         * Everything needs be optimized to the Nth degree to reduce as much overhead as possible.
         */

        public void InjectSurgeryRecipes (List<RecipeDef> surgeryList, List<ThingDef> pawnList) {
            Base XP = Base.Instance;

            Stopwatch stopwatch = Stopwatch.StartNew();

            // This list is used a few times.  Best to compose it outside the loops.  Distinct is important
            // because there's a lot of dupes.
            if (Base.IsDebug) stopwatch.Start();

            List<BodyPartRecord> raceBodyParts =
                pawnList.
                Where     (p  => p.race?.body != null).  // no idea; NRE bulletproofing because of PawnMorpher?
                Select    (p  => p.race.body).Distinct().
                SelectMany(bd => bd.AllParts).Distinct().
                ToList()
            ;

            if (Base.IsDebug) {
                stopwatch.Stop();
                XP.ModLogger.Message(
                    "    RaceBodyParts cache: took {0:F4}s; {1:N0} BPRs",
                    stopwatch.ElapsedMilliseconds / 1000f,
                    raceBodyParts.Count()
                );
                stopwatch.Reset();
            }

            // Both of these are useful in surgery->pawn body part matches
            if (Base.IsDebug) stopwatch.Start();

            var doesPawnHaveSurgery  = new HashSet<string> {};
            var doesPawnHaveBodyPart = new HashSet<string> {};
            foreach (ThingDef pawn in pawnList) {
                if (pawn.recipes != null) doesPawnHaveSurgery.AddRange(
                    pawn.recipes.Select(
                        s => pawn.defName + "|" + s.label.ToLower()
                    )
                );
                if (pawn.race?.body != null) doesPawnHaveBodyPart.AddRange(
                    pawn.race.body.AllParts.Distinct().Select(
                        bpr => pawn.defName + "|" + bpr.def.defName
                    )
                );
            }

            if (Base.IsDebug) {
                stopwatch.Stop();
                XP.ModLogger.Message(
                    "    DoesPawnHaveSurgery + BodyPart caches: took {0:F4}s; {1:N0} + {2:N0} strings",
                    stopwatch.ElapsedMilliseconds / 1000f,
                    doesPawnHaveSurgery.Count(), doesPawnHaveBodyPart.Count()
                );
                stopwatch.Reset();
            }

            // NOTE: Most of the part matching is in BodyPartMatcher now

            // Part-to-part matching
            if (Base.IsDebug) stopwatch.Start();

            int filteredCount = BodyPartMatcher.MatchPartsToParts(raceBodyParts);

            if (Base.IsDebug) {
                stopwatch.Stop();
                XP.ModLogger.Message(
                    "    Part-to-part mapping: took {0:F4}s; {1:N0}/{2:N0} filtered BPDs",
                    stopwatch.ElapsedMilliseconds / 1000f,
                    filteredCount, raceBodyParts.Count
                );
                stopwatch.Reset();
            }

            // Surgery-to-part mapping
            if (Base.IsDebug) stopwatch.Start();

            BodyPartMatcher.MatchSurgeriesToParts(surgeryList, pawnList);

            if (Base.IsDebug) {
                stopwatch.Stop();
                XP.ModLogger.Message(
                    "    Surgery-to-part mapping: took {0:F4}s; {1:N0} surgeries",
                    stopwatch.ElapsedMilliseconds / 1000f,
                    surgeryList.Count
                );
                stopwatch.Reset();
            }

            var racePartMapper = BodyPartMatcher.GetFilteredPartMapper(raceBodyParts);

            // With the parts mapped, add new body parts to existing recipes
            if (Base.IsDebug) stopwatch.Start();

            int newPartsAdded = 0;
            foreach (RecipeDef surgery in surgeryList.Where( s => s.targetsBodyPart && !s.appliedOnFixedBodyParts.NullOrEmpty() )) {
                HashSet<BodyPartDef> newPartSet =
                    surgery.appliedOnFixedBodyParts.
                    Where     ( sbpd => racePartMapper.ContainsKey(sbpd.defName) ).
                    SelectMany( sbpd => racePartMapper[sbpd.defName] ).
                    ToHashSet()
                ;

                List<BodyPartDef> AOFBP = surgery.appliedOnFixedBodyParts;
                if (newPartSet.Count() >= 1 && !newPartSet.IsSubsetOf(AOFBP)) {
                    newPartSet.ExceptWith(AOFBP);
                    AOFBP.AddRange(newPartSet);
                    newPartsAdded += newPartSet.Count();
                }

                if (Base.IsDebug && BodyPartMatcher.debugSurgeryDefName != null && surgery.defName == BodyPartMatcher.debugSurgeryDefName) XP.ModLogger.Message(
                    "      {0}: new AOFBP = {1}",
                    BodyPartMatcher.debugSurgeryDefName, string.Join(", ", surgery.appliedOnFixedBodyParts)
                );
            }

            if (Base.IsDebug) {
                stopwatch.Stop();
                XP.ModLogger.Message(
                    "    Add new body parts to surgeries: took {0:F4}s; {1:N0} additions",
                    stopwatch.ElapsedMilliseconds / 1000f,
                    newPartsAdded
                );
                stopwatch.Reset();
            }

            // Apply relevant missing surgery options to all pawn Defs
            if (Base.IsDebug) stopwatch.Start();

            int newSurgeriesAdded = 0;
            foreach (ThingDef pawnDef in pawnList) {
                List<RecipeDef> newSurgeries =
                    surgeryList.
                    Where( s =>
                        // If it already exists, don't add it
                        !doesPawnHaveSurgery.Contains( pawnDef.defName + "|" + s.label.ToLower() ) &&
                        (
                            // If it's an administer recipe, add it
                            !s.targetsBodyPart ||
                            // If it targets a body part, but nothing specific, add it
                            (s.appliedOnFixedBodyParts.Count() == 0 && s.appliedOnFixedBodyPartGroups.Count() == 0) ||
                            // If it targets any body parts that exist within the pawn, add it
                            s.appliedOnFixedBodyParts.Any( sbp =>
                                doesPawnHaveBodyPart.Contains( pawnDef.defName + "|" + sbp.defName )
                            )
                        )
                    ).
                    ToList()
                ;
                newSurgeriesAdded += newSurgeries.Count;

                if (pawnDef.recipes == null) pawnDef.recipes = newSurgeries; else pawnDef.recipes.AddRange(newSurgeries);
                newSurgeries.DoIf(
                    condition: s => s.recipeUsers == null,
                    action:    s => s.recipeUsers = new()
                );
                newSurgeries.Do( s => s.recipeUsers.Add(pawnDef) );

                if (
                    Base.IsDebug &&
                    BodyPartMatcher.debugPawnKindDefName != null &&
                    pawnDef.defName == BodyPartMatcher.debugPawnKindDefName
                ) XP.ModLogger.Message(
                    "      Pawn {0}: newSurgeries = {2}",
                    BodyPartMatcher.debugPawnKindDefName, string.Join(", ", newSurgeries.Select( s => s.defName ))
                );
            }
            if (Base.IsDebug) {
                stopwatch.Stop();
                XP.ModLogger.Message(
                    "    Add new surgeries to pawns: took {0:F4}s; {1:N0} additions",
                    stopwatch.ElapsedMilliseconds / 1000f,
                    newSurgeriesAdded
                );
                stopwatch.Reset();
            }
        }

        public void CleanupNamedPartSurgeryRecipes (List<RecipeDef> surgeryList) {
            Base XP = Base.Instance;

            // Try to clean up the more obvious named part cross-connections on humanlikes
            foreach (RecipeDef surgery in surgeryList.Where(
                s => s.targetsBodyPart && s.appliedOnFixedBodyParts.Count > 0
            )) {
                string surgeryLabelLower = surgery.label.ToLower();

                if      (surgeryLabelLower.Contains(" foot ") || surgeryLabelLower.EndsWith(" foot")) {
                    surgery.appliedOnFixedBodyParts.RemoveAll(sbp => BodyPartMatcher.SimplifyBodyPartLabel(sbp) == "hand");
                }
                else if (surgeryLabelLower.Contains(" hand ") || surgeryLabelLower.EndsWith(" hand")) {
                    surgery.appliedOnFixedBodyParts.RemoveAll(sbp => BodyPartMatcher.SimplifyBodyPartLabel(sbp) == "foot");
                }
                else if (surgeryLabelLower.Contains(" leg ") || surgeryLabelLower.EndsWith(" leg")) {
                    surgery.appliedOnFixedBodyParts.RemoveAll(sbp => 
                        BodyPartMatcher.SimplifyBodyPartLabel(sbp) is string sbpl && (sbpl == "arm" || sbpl == "shoulder")
                    );
                }
                else if (surgeryLabelLower.Contains(" arm ") || surgeryLabelLower.EndsWith(" arm")) {
                    surgery.appliedOnFixedBodyParts.RemoveAll(sbp => BodyPartMatcher.SimplifyBodyPartLabel(sbp) == "leg");
                }

                // This shouldn't happen
                if (surgery.appliedOnFixedBodyParts.Count == 0) {
                    XP.ModLogger.Error("Cleaning up named part surgeries for {0}, but ended up removing all the body parts!", surgery.LabelCap);
                }
            }
        }

        public void CleanupSurgeryRecipes (List<RecipeDef> surgeryList, List<ThingDef> pawnList) {
            Base XP = Base.Instance;

            Stopwatch stopwatch = Stopwatch.StartNew();

            // Just in case we have some easy dupes to clean
            if (Base.IsDebug) stopwatch.Start();
            int recipesCount = 0;

            foreach (ThingDef pawnDef in pawnList) {
                if (pawnDef.recipes == null) continue;
                recipesCount += pawnDef.recipes.Count();
                pawnDef.recipes.RemoveDuplicates();
            }

            if (Base.IsDebug) {
                stopwatch.Stop();
                XP.ModLogger.Message(
                    "    Remove dupes from PawnDefs: took {0:F4}s; {1:N0} surgeries total",
                    stopwatch.ElapsedMilliseconds / 1000f, recipesCount
                );
                stopwatch.Reset();
            }

            /* XXX: Given that Core and other "important" modules are loaded first, we'll assume the first recipe
             * (surgery) is the destination that should receive the expanded parts lists from otherSurgery.
             * 
             * Of course, I say that right before we resort the surgery list...
             */

            int surgeryDeletions = 0;
            if (Base.IsDebug) stopwatch.Start();

            ThingCategoryDef EPOERedundancy = DefDatabase<ThingCategoryDef>.GetNamed("EPOE_Redundancy", false);

            // Merge surgeries that do the same thing on different body parts
            var partsSurgeryList = surgeryList.Where(s => s.targetsBodyPart).ToList();
            for (int ps = 0; ps < partsSurgeryList.Count(); ps++) {
                RecipeDef surgery = partsSurgeryList[ps];

                if (surgery.recipeUsers             == null) surgery.recipeUsers             = new List<ThingDef>    {};
                if (surgery.appliedOnFixedBodyParts == null) surgery.appliedOnFixedBodyParts = new List<BodyPartDef> {};

                // The other side of the "easy dupe" cleaning
                surgery.recipeUsers.RemoveDuplicates();

                // EPOE Forked has these obsolete parts that still have surgery options, and they are called
                // the same thing for some reason.  We have to detect and ignore these...
                if (EPOERedundancy != null && surgery.fixedIngredientFilter?.AllowedThingDefs != null && surgery.fixedIngredientFilter.AllowedThingDefs.Any(
                    t => t.thingCategories != null && t.thingCategories.Contains(EPOERedundancy)
                )) continue;

                var toDelete = new List<RecipeDef> {};
                foreach (RecipeDef otherSurgery in partsSurgeryList.Where(s => 
                    s != surgery && s.defName != surgery.defName &&
                    s.label.ToLower()    == surgery.label.ToLower()    &&
                    s.workerClass        == surgery.workerClass        &&
                    s.workerCounterClass == surgery.workerCounterClass &&
                    s.addsHediff         == surgery.addsHediff         &&
                    s.removesHediff      == surgery.removesHediff      &&
                    s.changesHediffLevel == surgery.changesHediffLevel &&

                    s.fixedIngredientFilter?.Summary == surgery.fixedIngredientFilter?.Summary
                )) {
                    try {
                        if (!otherSurgery.appliedOnFixedBodyParts.NullOrEmpty()) {
                            surgery.appliedOnFixedBodyParts.AddRange(otherSurgery.appliedOnFixedBodyParts);
                            surgery.appliedOnFixedBodyParts.RemoveDuplicates();
                        }

                        List<ThingDef> otherSurgeryPawns = new List<ThingDef> {};
                        if (!otherSurgery.recipeUsers.NullOrEmpty()) {
                            surgery.recipeUsers.AddRange(otherSurgery.recipeUsers);
                            surgery.recipeUsers.RemoveDuplicates();

                            // This is like pawnDef.AllRecipes, without actually initializing the permanent cache.  We aren't going
                            // to trust that every def is actually injected in both sides (pawn.recipes + surgery.recipeUsers).
                            otherSurgeryPawns.AddRange(otherSurgery.recipeUsers);
                        }
                        pawnList.Where( p => p.recipes != null && p.recipes.Contains(otherSurgery) ).Do( p => otherSurgeryPawns.AddDistinct(p) );
                    
                        foreach (ThingDef pawnDef in otherSurgeryPawns) {
                            surgery.recipeUsers.AddDistinct(pawnDef);
                        
                            if (pawnDef.recipes == null) pawnDef.recipes = new List<RecipeDef> {};

                            // Try to keep the same index in the replacement
                            var recipes = pawnDef.recipes;
                            int i = recipes.IndexOf(otherSurgery);
                            if (i != -1) {
                                recipes[i] = surgery;
                            }
                            else {
                                // XXX: How would we even get here???  It didn't exist and was only found in surgery.recipeUsers?
                                recipes.Add(surgery);
                                recipes.Remove(otherSurgery);
                            }
                        }

                        // XXX: Well, of course it's a private method.  Guess we reflect now...

                        // Time to die!
                        MethodInfo removeMethod = AccessTools.Method(typeof(DefDatabase<RecipeDef>), "Remove");
                        removeMethod.Invoke(null, new object[] { otherSurgery });  // static method: first arg is null

                        toDelete.Add(otherSurgery);  // don't re-merge in the other direction on our main loop
                    }
                    catch (Exception ex) {
                        throw new Exception(
                            string.Format("Triggered exception while merging surgeries {0} and {1}", surgery.defName, otherSurgery.defName),
                            ex
                        );
                    }
                }

                // Second loop is still an enumerator, so delete here
                surgeryDeletions += toDelete.Count();
                toDelete.ForEach( s => partsSurgeryList.Remove(s) );
            }

            if (Base.IsDebug) {
                stopwatch.Stop();
                XP.ModLogger.Message(
                    "    Merge surgeries: took {0:F4}s; {1:N0} surgeries merged; {2:N0} surgeries total",
                    stopwatch.ElapsedMilliseconds / 1000f, surgeryDeletions, partsSurgeryList.Count()
                );
                stopwatch.Reset();
            }

            // Add hyperlinks to surgeries, if they don't exist
            if (Base.IsDebug) stopwatch.Start();
            int surgeriesNeedingHyperlinks = 0;

            foreach (RecipeDef surgery in surgeryList.Where(s => s.descriptionHyperlinks.NullOrEmpty())) {
                surgeriesNeedingHyperlinks++;
                try {
                    List<DefHyperlink> hyperlinks = Helpers.SurgeryToHyperlinks(surgery);
                    if (hyperlinks.NullOrEmpty()) continue;
                    surgery.descriptionHyperlinks = hyperlinks;
                }
                catch (Exception ex) {
                    throw new Exception(
                        string.Format("Triggered exception while adding hyperlinks to {0}", surgery.defName),
                        ex
                    );
                }
            }

            if (Base.IsDebug) {
                stopwatch.Stop();
                XP.ModLogger.Message(
                    "    Add hyperlinks to surgeries: took {0:F4}s; {1:N0} surgeries with new hyperlinks",
                    stopwatch.ElapsedMilliseconds / 1000f, surgeriesNeedingHyperlinks
                );
                stopwatch.Reset();
            }

            // Sort all of the recipes on the pawn side
            if (Base.IsDebug) stopwatch.Start();

            foreach (ThingDef pawnDef in pawnList.Where(p => p.recipes != null)) {
                try {
                    pawnDef.recipes = pawnDef.recipes.
                        OrderBy(s => Helpers.SurgerySort(s)).
                        ThenBy (s => s.label.ToLower()).
                        ToList()
                    ;

                    /* One of the mods before us may have called AllRecipes, which sets up a permanent cache.  This
                     * means that our new additions might never show up.  Force clear the cache, punching through
                     * the private field via reflection.
                     */
                    Traverse.Create(pawnDef).Field("allRecipesCached").SetValue(null);
                }
                catch (Exception ex) {
                    throw new Exception(
                        string.Format("Triggered exception while sorting recipes for {0}", pawnDef.defName),
                        ex
                    );
                }
            }

            if (Base.IsDebug) {
                stopwatch.Stop();
                XP.ModLogger.Message(
                    "    Sort surgeries: took {0:F4}s; {1:N0} surgeries total",
                    stopwatch.ElapsedMilliseconds / 1000f, pawnList.Where( p => p.recipes != null ).Sum( p => p.recipes.Count() )
                );
                stopwatch.Reset();
            }

        }
    }
}
