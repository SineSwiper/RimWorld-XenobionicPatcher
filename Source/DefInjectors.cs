using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace XenobionicPatcher {
    public class DefInjectors {
        enum partMatchType {
            BodyPartRecord,
            BodyPartDef,
            DefName,
            LabelShort,
            Label,
        };

        /* WARNING: Because of the sheer amount of combinations and loops we're dealing with, there is a LOT
         * of caching (both here and within Helpers), HashSets (for duplicate checks), and stopwatch timing.
         * Everything needs be optimized to the Nth degree to reduce as much overhead as possible.
         */

        public void InjectSurgeryRecipes (List<RecipeDef> surgeryList, List<ThingDef> pawnList) {
            Base XP = Base.Instance;

            Stopwatch stopwatch = Stopwatch.StartNew();

            /* Many mods like to use different def names for basic body parts.  This makes it harder to add the
             * surgery recipe to the alien.  We'll need to add in the similar body part to the
             * appliedOnFixedBodyParts list first.
             * 
             * First, look through the list of common surgery recipes to infer the part type.  In other words, if
             * there's a surgery called "Install bionic arm" then that part is an arm that can accept other kinds of
             * arms.  Then, also look for body part matches by looking at the body part labels directly (basically
             * duck typing).
             * 
             * These's all go into the part mapper for later injection.
             */
            var partToPartMapper = new Dictionary<string, HashSet<BodyPartDef>> {};

            // There are only a few pawn bio-types, so compile all of the pawn surgery lists outside of the
            // main surgery double-loop.
            if (Base.IsDebug) stopwatch.Start();

            var pawnSurgeriesByBioType = new Dictionary<string, HashSet<RecipeDef>> {};
            foreach (ThingDef pawn in pawnList.Where(p => p.recipes != null)) {
                string pawnBioType = Helpers.GetPawnBioType(pawn);
                pawnSurgeriesByBioType.SetOrAddNestedRange(pawnBioType, pawn.recipes);
            }

            if (Base.IsDebug) {
                stopwatch.Stop();
                XP.ModLogger.Message(
                    "    PawnSurgeriesByBioType cache: took {0:F4}s; {1:N0}/{2:N0} keys/recipes",
                    stopwatch.ElapsedMilliseconds / 1000f,
                    pawnSurgeriesByBioType.Keys.Count(), pawnSurgeriesByBioType.Values.Sum(h => h.Count())
                );
                stopwatch.Reset();
            }

            // This list is used a few times.  Best to compose it outside the loops.  Distinct is important
            // because there's a lot of dupes.
            if (Base.IsDebug) stopwatch.Start();

            List<BodyPartRecord> raceBodyParts =
                pawnList.
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
                doesPawnHaveBodyPart.AddRange(
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

            /* Start with a hard-coded list, just in case any of these don't match.  This is especially helpful for
             * animals, since they don't always have obvious humanlike analogues.  This also works as a part group
             * separator to ensure parts don't get mixed into the wrong groups.
             */
            var staticPartGroups = new Dictionary<string, string[]> {
                { "Arm",      new[] { "arm", "flipper"                           } },
                { "Shoulder", new[] { "shoulder"                                 } },
                { "Hand",     new[] { "hand", "claw", "grasper", "pincer"        } },
                { "Finger",   new[] { "finger", "thumb", "pinky"                 } },
                { "Leg",      new[] { "leg"                                      } },
                { "Foot",     new[] { "foot", "hoof", "paw"                      } },
                { "Toe",      new[] { "toe"                                      } },
                { "Eye",      new[] { "eye", "sight", "seeing", "visual"         } },
                { "Ear",      new[] { "ear", "antenna", "hear", "hearing", "sound" } },
                { "Nose",     new[] { "nose", "nostril", "smell", "smelling"     } },
                { "Jaw",      new[] { "jaw", "beak", "mouth", "maw", "teeth", "mandible" } },
                { "Brain",    new[] { "brain"                       } },
                { "Torso",    new[] { "torso", "thorax", "body", "shell" } },
                { "Ribcage",  new[] { "ribcage"                     } },
                { "Heart",    new[] { "heart", "reactor"            } },
                { "Lung",     new[] { "lung"                        } },
                { "Kidney",   new[] { "kidney"                      } },
                { "Liver",    new[] { "liver"                       } },
                { "Stomach",  new[] { "stomach"                     } },
                { "Spine",    new[] { "spine"                       } },
                { "Neck",     new[] { "neck", "pronotum"            } },
                // Wing should really be the base name, but there is no vanilla Wing part (even for birds!)
                { "Elytra",   new[] { "elytra", "wing"              } },
            };

            /* It's futile to try to separate the hand/foot connection, as animals have "hands" which also
             * sometimes double as feet.  We can try to clean this up later in CleanupHandFootSurgeryRecipes.
             *
             * We're still going to keep the bio-boundary below to keep out leg->hand connections.  That's still a
             * bit off.  And mechs, of course.
             */
            staticPartGroups["Hand"].AddRangeToArray(staticPartGroups["Foot"]);
            staticPartGroups["Foot"] = staticPartGroups["Hand"];

            // Initialize part mapper with the vanilla part
            foreach (string vanillaPartName in staticPartGroups.Keys) {
                partToPartMapper.Add(
                    vanillaPartName,
                    new HashSet<BodyPartDef> { DefDatabase<BodyPartDef>.GetNamed(vanillaPartName) }
                );
            }

            // Static part loop
            if (Base.IsDebug) stopwatch.Start();
            foreach (BodyPartRecord raceBodyPart in raceBodyParts) {
                // Try really hard to only match one vanilla part group
                foreach (partMatchType matchType in Enum.GetValues(typeof(partMatchType))) {
                    var partGroupMatched = new Dictionary<string, bool> {};
                    foreach (string vanillaPartName in staticPartGroups.Keys) {
                        partGroupMatched.Add(
                            vanillaPartName,
                            staticPartGroups[vanillaPartName].Any( fuzzyPartName =>
                                matchType == partMatchType.BodyPartRecord ? Helpers.DoesBodyPartMatch(raceBodyPart,             fuzzyPartName) :
                                matchType == partMatchType.BodyPartDef    ? Helpers.DoesBodyPartMatch(raceBodyPart.def,         fuzzyPartName) :
                                matchType == partMatchType.DefName        ? Helpers.DoesBodyPartMatch(raceBodyPart.def.defName, fuzzyPartName) :
                                matchType == partMatchType.LabelShort     ? Helpers.DoesBodyPartMatch(raceBodyPart.LabelShort,  fuzzyPartName) :
                                matchType == partMatchType.Label          ? Helpers.DoesBodyPartMatch(raceBodyPart.Label,       fuzzyPartName) :
                                false  // ??? Forgot to add a partMatchType?
                            )
                        );
                    }

                    // Only stop to add if there's a conclusive singular part matched
                    int partGroupMatches = staticPartGroups.Keys.Sum(k => partGroupMatched[k] ? 1 : 0);
                    if (partGroupMatches == 1) {
                        string vanillaPartName  = partGroupMatched.Keys.First(k => partGroupMatched[k]);
                        BodyPartDef racePartDef = raceBodyPart.def;

                        // Add to both sides
                        partToPartMapper[vanillaPartName].Add(racePartDef);
                        partToPartMapper.SetOrAddNested(
                            racePartDef.defName,
                            partToPartMapper[vanillaPartName].First(bpd => bpd.defName == vanillaPartName)
                        );
                        break;
                    }
                    else if (partGroupMatches == 0) {
                        // It's never going to match on other loops, so just stop here
                        break;
                    }
                }
            }
            if (Base.IsDebug) {
                stopwatch.Stop();
                XP.ModLogger.Message(
                    "    Static part loop: took {0:F4}s; {1:N0}/{2:N0} PartToPartMapper keys/BPDs",
                    stopwatch.ElapsedMilliseconds / 1000f,
                    partToPartMapper.Keys.Count(), partToPartMapper.Values.Sum(h => h.Count())
                );
                stopwatch.Reset();
            }

            // Part-to-part mapping
            // (This is actually fewer combinations than all of the duplicates within
            // surgeryList -> appliedOnFixedBodyParts.)
            if (Base.IsDebug) stopwatch.Start();
            for (int i = 0; i < raceBodyParts.Count(); i++) {
                BodyPartRecord firstBodyPart = raceBodyParts[i];
                string fbpDefName = firstBodyPart.def.defName;
                partToPartMapper.NewIfNoKey(fbpDefName);

                // Looks for matching (or near-matching) body part labels
                for (int j = i + 1; j < raceBodyParts.Count(); j++) {  // don't repeat previous checks
                    BodyPartRecord secondBodyPart = raceBodyParts[j];
                    string sbpDefName = secondBodyPart.def.defName;

                    if (firstBodyPart.def != secondBodyPart.def && Helpers.DoesBodyPartMatch(firstBodyPart, secondBodyPart)) {
                        partToPartMapper[fbpDefName].Add(secondBodyPart.def);
                        partToPartMapper.SetOrAddNested(sbpDefName, firstBodyPart.def);
                    }
                }
            }
            if (Base.IsDebug) {
                stopwatch.Stop();
                XP.ModLogger.Message(
                    "    Part-to-part mapping: took {0:F4}s; {1:N0}/{2:N0} PartToPartMapper keys/BPDs",
                    stopwatch.ElapsedMilliseconds / 1000f,
                    partToPartMapper.Keys.Count(), partToPartMapper.Values.Sum(h => h.Count())
                );
                stopwatch.Reset();
            }

            // Surgery-to-part mapping
            if (Base.IsDebug) stopwatch.Start();

            foreach (RecipeDef surgery in surgeryList.Where(s => s.targetsBodyPart)) {
                string surgeryBioType    = Helpers.GetSurgeryBioType(surgery);
                string surgeryLabelLower = surgery.label.ToLower();

                if (!pawnSurgeriesByBioType.ContainsKey(surgeryBioType)) continue;

                // Compose this list outside of the surgeryBodyPart loop
                HashSet<BodyPartDef> pawnSurgeryBodyParts =
                    // We can't cross the animal/humanlike boundary with these checks because animal surgery recipes tend to be a lot
                    // looser with limbs (ie: power claws on animal legs)
                    pawnSurgeriesByBioType[surgeryBioType].
                    Where     (s  => s.targetsBodyPart && s != surgery && s.defName != surgery.defName && s.label.ToLower() == surgeryLabelLower).
                    SelectMany(s  => s.appliedOnFixedBodyParts).Distinct().
                    ToHashSet()
                ;
                if (pawnSurgeryBodyParts.Count == 0) continue;

                /* If this list is crossing a bunch of our static part group boundaries, we should skip it.
                 * RoM's Druid Regrowth recipe is one such example that tends to pollute the whole bunch.
                 */
                int partGroupMatches = staticPartGroups.Keys.Sum(k =>
                    partToPartMapper[k].Overlaps(pawnSurgeryBodyParts) || partToPartMapper[k].Overlaps(surgery.appliedOnFixedBodyParts) ? 1 : 0
                );
                if (partGroupMatches >= 2) continue;

                // Look for matching surgery labels, and map them to similar body parts
                bool warnedAboutLargeSet = false;
                foreach (BodyPartDef surgeryBodyPart in surgery.appliedOnFixedBodyParts) {
                    string sbpDefName = surgeryBodyPart.defName;
                    partToPartMapper.NewIfNoKey(sbpDefName);

                    // Useful to warn when it's about to add a bunch of parts into a recipe at one time
                    HashSet<BodyPartDef> diff = pawnSurgeryBodyParts.Except(partToPartMapper[sbpDefName]).ToHashSet();
                    if (diff.Count() > 10 && !warnedAboutLargeSet) {
                        XP.ModLogger.Warning(
                            "Mapping a large set of body parts from \"{0}\":\nSurgery parts: {1}\nCurrent mapper parts: {2} ==> {3}\nNew mapper parts: {4}",
                            surgery.LabelCap,
                            string.Join(", ", surgery.appliedOnFixedBodyParts.Select(bpd => bpd.defName)),
                            sbpDefName, string.Join(", ", partToPartMapper[sbpDefName].Select(bpd => bpd.defName)),
                            string.Join(", ", diff.Select(bpd => bpd.defName))
                        );
                        warnedAboutLargeSet = true;
                    }

                    partToPartMapper[sbpDefName].AddRange(
                        pawnSurgeryBodyParts.Where(bp => bp != surgeryBodyPart && bp.defName != sbpDefName)
                    );
                }
            }
            if (Base.IsDebug) {
                stopwatch.Stop();
                XP.ModLogger.Message(
                    "    Surgery-to-part mapping: took {0:F4}s; {1:N0}/{2:N0} PartToPartMapper keys/BPDs",
                    stopwatch.ElapsedMilliseconds / 1000f,
                    partToPartMapper.Keys.Count(), partToPartMapper.Values.Sum(h => h.Count())
                );
                stopwatch.Reset();
            }

            // Clear out empty lists
            if (Base.IsDebug) stopwatch.Start();

            foreach (string part in partToPartMapper.Keys.ToArray()) {
                if (partToPartMapper[part].Count < 1) partToPartMapper.Remove(part);
            }

            if (Base.IsDebug) {
                stopwatch.Stop();
                XP.ModLogger.Message(
                    "    Empty list cleanup: took {0:F4}s; {1:N0}/{2:N0} PartToPartMapper keys/BPDs",
                    stopwatch.ElapsedMilliseconds / 1000f,
                    partToPartMapper.Keys.Count(), partToPartMapper.Values.Sum(h => h.Count())
                );
                stopwatch.Reset();
            }

            // With the parts mapped, add new body parts to existing recipes
            if (Base.IsDebug) stopwatch.Start();

            int newPartsAdded = 0;
            foreach (RecipeDef surgery in surgeryList.Where(s => s.targetsBodyPart)) {
                var newPartSet = new HashSet<BodyPartDef> {};
                foreach (BodyPartDef surgeryBodyPart in surgery.appliedOnFixedBodyParts) {
                    if (partToPartMapper.ContainsKey(surgeryBodyPart.defName)) {
                        newPartSet.AddRange(partToPartMapper[surgeryBodyPart.defName]);
                    }
                }

                List<BodyPartDef> AOFBP = surgery.appliedOnFixedBodyParts;
                if (newPartSet.Count() >= 1 && !newPartSet.IsSubsetOf(AOFBP)) {
                    newPartSet.ExceptWith(AOFBP);
                    AOFBP.AddRange(newPartSet);
                    newPartsAdded += newPartSet.Count();
                }
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
            foreach (RecipeDef surgery in surgeryList) {
                string surgeryLabelLower = surgery.label.ToLower();

                foreach (ThingDef pawnDef in pawnList.Where( p =>
                    // If it already exists, don't add it
                    !doesPawnHaveSurgery.Contains( p.defName + "|" + surgeryLabelLower )
                )) {
                    bool shouldAddSurgery = false;

                    // If it's an administer recipe, add it
                    if (!surgery.targetsBodyPart) shouldAddSurgery = true;

                    // If it targets a body part, but nothing specific, add it
                    else if (surgery.targetsBodyPart && surgery.appliedOnFixedBodyParts.Count() == 0 && surgery.appliedOnFixedBodyPartGroups.Count() == 0) {
                        shouldAddSurgery = true;
                    }

                    // XXX: Despite my best efforts, this step is still mapping hand/foot surgeries together...

                    // If it targets any body parts that exist within the pawn, add it
                    else if (surgery.targetsBodyPart && surgery.appliedOnFixedBodyParts.Count() >= 1 && surgery.appliedOnFixedBodyParts.Any( sbp =>
                        doesPawnHaveBodyPart.Contains( pawnDef.defName + "|" + sbp.defName )
                    )) shouldAddSurgery = true;

                    if (shouldAddSurgery) {
                        newSurgeriesAdded++;
                        if (pawnDef.recipes     == null) pawnDef.recipes     = new List<RecipeDef> { surgery }; else pawnDef.recipes    .Add(surgery);
                        if (surgery.recipeUsers == null) surgery.recipeUsers = new List<ThingDef>  { pawnDef }; else surgery.recipeUsers.Add(pawnDef);
                    }
                }
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

        public void CleanupHandFootSurgeryRecipes (List<RecipeDef> surgeryList) {
            Base XP = Base.Instance;

            // Try to clean up the more obvious hand/foot cross-connections on humanlikes
            foreach (RecipeDef surgery in surgeryList.Where(s => s.targetsBodyPart)) {
                string surgeryLabelLower = surgery.label.ToLower();

                if      (surgeryLabelLower.Contains(" foot ") || surgeryLabelLower.EndsWith(" foot")) {
                    surgery.appliedOnFixedBodyParts.RemoveAll(sbp => Helpers.DoesBodyPartMatch(sbp, "hand"));
                }
                else if (surgeryLabelLower.Contains(" hand ") || surgeryLabelLower.EndsWith(" hand")) {
                    surgery.appliedOnFixedBodyParts.RemoveAll(sbp => Helpers.DoesBodyPartMatch(sbp, "foot"));
                }

                // This shouldn't happen
                if (surgery.appliedOnFixedBodyParts.Count == 0) {
                    XP.ModLogger.Error("Cleaning up hand/foot surgeries for {0}, but ended up removing all the body parts!", surgery.LabelCap);
                }
            }
        }

        public void CleanupSurgeryRecipes (List<RecipeDef> surgeryList, List<ThingDef> pawnList) {
            Base XP = Base.Instance;

            // Just in case we have some easy dupes to clean
            foreach (ThingDef pawnDef in pawnList) {
                pawnDef.recipes?.RemoveDuplicates();
            }

            /* XXX: Given that Core and other "important" modules are loaded first, we'll assume the first recipe
             * (surgery) is the destination that should receive the expanded parts lists from otherSurgery.
             * 
             * Of course, I say that right before we resort the surgery list...
             */

            ThingCategoryDef EPOERedundancy = DefDatabase<ThingCategoryDef>.GetNamed("EPOE_Redundancy", false);

            // Merge surgeries that do the same thing on different body parts
            var partsSurgeryList = surgeryList.Where(s => s.targetsBodyPart).ToList();
            for (int ps = 0; ps < partsSurgeryList.Count(); ps++) {
                RecipeDef surgery = partsSurgeryList[ps];

                if (surgery.recipeUsers == null) surgery.recipeUsers = new List<ThingDef> {};

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
                    surgery.appliedOnFixedBodyParts.AddRange(otherSurgery.appliedOnFixedBodyParts);
                    surgery.appliedOnFixedBodyParts.RemoveDuplicates();

                    List<ThingDef> otherSurgeryPawns = new List<ThingDef> {};
                    if (otherSurgery.recipeUsers != null && otherSurgery.recipeUsers.Count > 0) {
                        surgery.recipeUsers.AddRange(otherSurgery.recipeUsers);
                        surgery.recipeUsers.RemoveDuplicates();

                        // This is like pawnDef.AllRecipes, without actually initializing the permanent cache.  We aren't going
                        // to trust that every def is actually injected in both sides (pawn.recipes + surgery.recipeUsers).
                        otherSurgeryPawns.AddRange(otherSurgery.recipeUsers);
                    }
                    pawnList.Where(p => p.recipes != null && p.recipes.Contains(otherSurgery)).Do( p => otherSurgeryPawns.AddDistinct(p) );
                    
                    foreach (ThingDef pawnDef in otherSurgeryPawns) {
                        surgery.recipeUsers.AddDistinct(pawnDef);
                        
                        // Try to keep the same index in the replacement
                        var recipes = pawnDef.recipes;
                        int i = recipes.IndexOf(otherSurgery);
                        if (i != -1) {
                            recipes[i] = surgery;
                        }
                        else {
                            // XXX: How would we even get here???
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

                // Second loop is still an enumerator, so delete here
                toDelete.ForEach( s => partsSurgeryList.Remove(s) );
            }

            // Add hyperlinks to surgeries, if they don't exist
            foreach (RecipeDef surgery in surgeryList.Where(s => s.descriptionHyperlinks.NullOrEmpty())) {
                List<DefHyperlink> hyperlinks = Helpers.SurgeryToHyperlinks(surgery);
                if (hyperlinks.NullOrEmpty()) continue;
                surgery.descriptionHyperlinks = hyperlinks;
            }

            foreach (ThingDef pawnDef in pawnList.Where(p => p.recipes != null)) {
                // Sort all of the recipes on the pawn side
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

        }
    }
}
