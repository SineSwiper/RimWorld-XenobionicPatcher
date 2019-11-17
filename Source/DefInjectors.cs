using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Harmony;
using Verse;

namespace XenobionicPatcher {
    public class DefInjectors {
        public void InjectSurgeryRecipes (IEnumerable<RecipeDef> surgeryList, IEnumerable<ThingDef> pawnList) {
            Base XP = Base.Instance;

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
            var partToPartMapper = new Dictionary<string, List<BodyPartDef>> {};

            // This list is used a few times.  Best to compose it outside the loops.  Distinct is important
            // because there's a lot of dupes.
            List<BodyPartRecord> raceBodyParts =
                pawnList.
                Select    (p  => p.race.body).Distinct().
                SelectMany(bd => bd.AllParts).Distinct().
                ToList()
            ;

            // Both of these are useful in surgery->pawn body part matches
            var doesPawnHaveSurgery  = new HashSet<string> {};
            var doesPawnHaveBodyPart = new HashSet<string> {};
            foreach (ThingDef pawn in pawnList) {
                if (pawn.recipes != null) doesPawnHaveSurgery.AddRange(
                    pawn.recipes.Select(
                        s => pawn.defName + "|" + s.label.ToLower()
                    ).ToList()
                );
                doesPawnHaveBodyPart.AddRange(
                    pawn.race.body.AllParts.Distinct().Select(
                        bpr => pawn.defName + "|" + bpr.def.defName
                    ).ToList()
                );
            }

            // Start with a hard-coded list, just in case any of these don't match.  This is especially helpful for
            // animals, since they don't always have obvious humanlike analogues.
            var staticPartGroups = new Dictionary<string, string[]> {
                { "Hand",    new[] { "hand", "hoof", "claw", "grasper", "pincer" } },
                { "Eye",     new[] { "eye", "sight", "seeing"                    } },
                { "Ear",     new[] { "ear", "hear", "hearing"                    } },
                { "Nose",    new[] { "nose", "nostril", "smell", "smelling"      } },
                { "Jaw",     new[] { "jaw", "beak", "mouth", "maw", "teeth"      } },
                // Doubtful anybody has any surgeries like these...
                { "Ribcage", new[] { "ribcage", "thorax" } },
                { "Neck",    new[] { "neck", "pronotum"  } },
            };

            // Static part loop
            foreach (var partDefName in staticPartGroups.Keys) {
                BodyPartDef vanillaPart = DefDatabase<BodyPartDef>.GetNamed(partDefName);
                if (!partToPartMapper.ContainsKey(partDefName)) partToPartMapper[partDefName] = new List<BodyPartDef> {};

                var partGroup  = staticPartGroups[partDefName];
                var groupParts = new List<BodyPartDef> { vanillaPart };
                for (int i = 0; i < partGroup.Count(); i++) {
                    string fuzzyPartName = partGroup[i];
                    foreach (BodyPartDef raceBodyPart in
                        raceBodyParts.Where(bpr => Helpers.DoesBodyPartMatch(bpr, fuzzyPartName)).Select(bpr => bpr.def)
                    ) {
                        string rbpDefName = raceBodyPart.defName;
                        if (!partToPartMapper.ContainsKey(rbpDefName)) partToPartMapper[rbpDefName] = new List<BodyPartDef> {};

                        groupParts.Add(raceBodyPart);
                    }
                }

                // New list construction should already be covered by the above "if (!ContainsKey)" checks
                groupParts.ForEach( bpd => partToPartMapper[bpd.defName].AddRange(groupParts) );
            }

            // Main surgery loop
            foreach (RecipeDef surgery in surgeryList.Where(s => s.targetsBodyPart)) {
                string surgeryBioType    = Helpers.GetSurgeryBioType(surgery);
                string surgeryLabelLower = surgery.label.ToLower();

                foreach (BodyPartDef surgeryBodyPart in surgery.appliedOnFixedBodyParts) {
                    string sbpDefName = surgeryBodyPart.defName;
                    if (!partToPartMapper.ContainsKey(sbpDefName)) partToPartMapper[sbpDefName] = new List<BodyPartDef> {};

                    // Look for matching surgery labels, and map them to similar body parts
                    partToPartMapper[sbpDefName].AddRange(
                        pawnList.
                        // We can't cross the animal/humanlike boundary with these checks because animal surgery recipes tend to be a lot
                        // looser with limbs (ie: power claws on animal legs)
                        Where     (p  => Helpers.GetPawnBioType(p) == surgeryBioType && p.recipes != null).
                        SelectMany(p  => p.recipes).Distinct().
                        Where     (s  => s.targetsBodyPart && s != surgery && s.defName != surgery.defName && s.label.ToLower() == surgeryLabelLower).
                        SelectMany(s  => s.appliedOnFixedBodyParts).Distinct().
                        Where     (bp => bp != surgeryBodyPart && bp.defName != sbpDefName)
                    );

                    // Looks for matching (or near-matching) body part labels
                    partToPartMapper[sbpDefName].AddRange(
                        raceBodyParts.
                        Where (bpr => surgeryBodyPart != bpr.def && sbpDefName != bpr.def.defName && Helpers.DoesBodyPartMatch(bpr, surgeryBodyPart)).
                        Select(bpr => bpr.def)
                    );
                }
            }

            // Clear out empty lists and dupes
            foreach (string part in partToPartMapper.Keys.ToArray()) {
                if (partToPartMapper[part].Count < 1) partToPartMapper.Remove(part);
                else                                  partToPartMapper[part].RemoveDuplicates();
            }

            // With the parts mapped, add new body parts to existing recipes
            foreach (RecipeDef surgery in surgeryList.Where(s => s.targetsBodyPart)) {
                var newPartList = new List<BodyPartDef> {};
                foreach (BodyPartDef surgeryBodyPart in surgery.appliedOnFixedBodyParts) {
                    if (partToPartMapper.ContainsKey(surgeryBodyPart.defName)) {
                        newPartList.AddRange(partToPartMapper[surgeryBodyPart.defName]);
                        newPartList.RemoveDuplicates();
                    }
                }
                if (newPartList.Count() >= 1) {
                    surgery.appliedOnFixedBodyParts.AddRange(newPartList);
                    surgery.appliedOnFixedBodyParts.RemoveDuplicates();
                }
            }

            // Apply relevant missing surgery options to all pawn Defs
            foreach (RecipeDef surgery in surgeryList) {
                string surgeryLabelLower = surgery.label.ToLower();

                foreach (ThingDef pawnDef in pawnList.Where( p =>
                    // If it already exists, don't add it
                    !doesPawnHaveSurgery.Contains( p.defName + "|" + surgeryLabelLower ) &&
                    // If the pawn never had any recipes, then it doesn't even have the basics, so don't risk adding new ones
                    p.recipes != null && p.recipes.Count() >= 1
                )) {
                    bool shouldAddSurgery = false;

                    // If it's an administer recipe, add it
                    if (!surgery.targetsBodyPart) shouldAddSurgery = true;

                    // If it targets any body parts that exist within the pawn, add it
                    else if (surgery.targetsBodyPart && surgery.appliedOnFixedBodyParts.Count() >= 1 && surgery.appliedOnFixedBodyParts.Any( sbp =>
                        doesPawnHaveBodyPart.Contains( pawnDef.defName + "|" + sbp.defName )
                    )) shouldAddSurgery = true;

                    if (shouldAddSurgery) {
                        pawnDef.recipes.Add(surgery);
                        if (surgery.recipeUsers == null) surgery.recipeUsers = new List<ThingDef> { pawnDef };
                        else                             surgery.recipeUsers.Add(pawnDef);
                    }
                }
            }
        }

        public void CleanupSurgeryRecipes (IEnumerable<RecipeDef> surgeryList, IEnumerable<ThingDef> pawnList) {
            Base XP = Base.Instance;

            // Just in case we have some easy dupes to clean
            foreach (ThingDef pawnDef in pawnList) {
                pawnDef.recipes?.RemoveDuplicates();
            }

            // XXX: Given that Core and other "important" modules are loaded first, we'll assume the first recipe
            // (surgery) is the destination that should receive the expanded parts lists from otherSurgery.

            // Merge surgeries that do the same thing on different body parts
            var partsSurgeryList = surgeryList.Where(s => s.targetsBodyPart).ToList();
            for (int ps = 0; ps < partsSurgeryList.Count(); ps++) {
                RecipeDef surgery = partsSurgeryList[ps];
                
                // The other side of the "easy dupe" cleaning
                surgery.recipeUsers?.RemoveDuplicates();

                var toDelete = new List<RecipeDef> {};
                foreach (RecipeDef otherSurgery in partsSurgeryList.Where(s => 
                    s != surgery && s.defName != surgery.defName &&
                    s.label.ToLower()    == surgery.label.ToLower()    &&
                    s.workerClass        == surgery.workerClass        &&
                    s.workerCounterClass == surgery.workerCounterClass &&
                    s.addsHediff         == surgery.addsHediff         &&

                    s.fixedIngredientFilter?.Summary == surgery.fixedIngredientFilter?.Summary
                )) {
                    /*
                    XP.ModLogger.Message(
                        "    [" + surgery.defName + "] Merging " + otherSurgery.defName
                    );
                    */

                    surgery.appliedOnFixedBodyParts.AddRange(otherSurgery.appliedOnFixedBodyParts);
                    surgery.appliedOnFixedBodyParts.RemoveDuplicates();

                    surgery.recipeUsers.AddRange(otherSurgery.recipeUsers);
                    surgery.recipeUsers.RemoveDuplicates();

                    // This is like pawnDef.AllRecipes, without actually initializing the permanent cache.  We aren't going
                    // to trust that every def is actually injected in both sides (pawn.recipes + surgery.recipeUsers).
                    List<ThingDef> otherSurgeryPawns = otherSurgery.recipeUsers;
                    otherSurgeryPawns.AddRange( pawnList.Where(p => p.recipes.Contains(otherSurgery)) );
                    otherSurgeryPawns.RemoveDuplicates();
                    
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

                    /*
                    XP.ModLogger.Message(
                        "    [" + surgery.defName + "] Results: " + string.Join(", ", surgery.appliedOnFixedBodyParts?.Select(bp => bp.defName).ToArray())
                    );
                    */
                }

                // Second loop is still an enumerator, so delete here
                toDelete.ForEach( s => partsSurgeryList.Remove(s) );
            }
        }
    }
}
