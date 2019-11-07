using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using HugsLib;
using HugsLib.Settings;
using Verse;

namespace XenobionicPatcher {
    [StaticConstructorOnStartup]
    public class Base : ModBase {
        public override string ModIdentifier {
            get { return "XenobionicPatcher"; }
        }
        public static Base Instance { get; private set; }

        internal HugsLib.Utils.ModLogger ModLogger { get; private set; }

        public Base() {
            Instance = this;
        }

        internal List<Type> relevantSurgeryWorkerClasses = new List<Type> {
            typeof(Recipe_AdministerIngestible),
            typeof(Recipe_AdministerUsableItem),
            typeof(Recipe_InstallNaturalBodyPart),
            typeof(Recipe_InstallArtificialBodyPart),
            typeof(Recipe_InstallImplant),
        };

        public override void DefsLoaded() {
            // FIXME: Config booleans

            // Start with a more global list
            List<ThingDef> allPawnDefs = DefDatabase<ThingDef>.AllDefs.Where(
                thing => typeof(Pawn).IsAssignableFrom(thing.thingClass)
            ).ToList();
            List<RecipeDef> allSurgeryDefs = DefDatabase<RecipeDef>.AllDefs.Where(
                recipe => recipe.IsSurgery && relevantSurgeryWorkerClasses.Any( t => t.IsAssignableFrom(recipe.workerClass) )
            ).ToList();
            allSurgeryDefs.Add( DefDatabase<RecipeDef>.GetNamed("RemoveBodyPart") ); // WorkerClass typeof is internal, so we have to use GetNamed

            // Administer
            // InstallNaturalBodyPart
            // InstallArtificialBodyPart
            // InstallImplant
            // RemoveBodyPart

            // Animal/Animal
            // Humanlike/Humanlike
            // Animal/Humanlike
            // Humanlike/Animal

            // DEBUG
            foreach (ThingDef pawnDef in allPawnDefs.Where(p => p.race.Humanlike)) {
                Logger.Message(
                    "Full "+pawnDef.defName+" recipes (pre): " + 
                    string.Join(", ", pawnDef.recipes.Select(s => s.defName).ToArray())
                );
            }

            Logger.Message("Injecting surgical recipes into animals/aliens");
            InjectSurgeryRecipes(allPawnDefs, allSurgeryDefs);
        }

        // DEBUG
        public override void WorldLoaded() {
            List<ThingDef> allPawnDefs = DefDatabase<ThingDef>.AllDefs.Where(
                thing => typeof(Pawn).IsAssignableFrom(thing.thingClass)
            ).ToList();

            // DEBUG
            foreach (ThingDef pawnDef in allPawnDefs.Where(p => p.race.Humanlike)) {
                Logger.Message(
                    "Full "+pawnDef.defName+" recipes (world): " + 
                    string.Join(", ", pawnDef.recipes.Select(s => s.defName).ToArray())
                );
            }
        }

        public static void InjectSurgeryRecipes (List<ThingDef> pawnList, List<RecipeDef> surgeryList) {
            /* Some mods (like *cough*elderthings*cough*) like to use different def names for basic body parts.
             * This makes it harder to add the surgery recipe to the alien.  We'll need to add in the similar
             * body part to the appliedOnFixedBodyParts list first.
             * 
             * First, look through the list of common surgery recipes to infer the part type.  In other words, if
             * there's a surgery called "Install bionic arm" then that part is an arm that can accept other kinds of
             * arms.  Then, also look for body part matches by looking at the body part labels directly (basically
             * duck typing).
             */
            var partToPartMapper = new Dictionary<string, List<BodyPartDef>> {};

            foreach (RecipeDef surgery in surgeryList.Where(s => s.targetsBodyPart)) {
                // Look for matching surgery labels, and map them to similar body parts
                foreach (RecipeDef otherSurgery in surgeryList.Where(s =>
                    s.targetsBodyPart && s != surgery && s.defName != surgery.defName && s.label.ToLower() == surgery.label.ToLower() &&
                    // We can't cross the animal/humanlike boundary with these checks because animal surgery recipes tend to be a lot
                    // looser with limbs (ie: power claws on animal legs)
                    GetSurgeryBioType(s) == GetSurgeryBioType(surgery)
                )) {
                    foreach (BodyPartDef surgeryBodyPart in surgery.appliedOnFixedBodyParts) {
                        foreach (BodyPartDef otherSurgeryBodyPart in otherSurgery.appliedOnFixedBodyParts.Where(
                            bp => bp != surgeryBodyPart && bp.defName != surgeryBodyPart.defName && bp.label.ToLower() != surgeryBodyPart.label.ToLower()
                        )) {
                            if (!partToPartMapper.ContainsKey(surgeryBodyPart.defName)) partToPartMapper[surgeryBodyPart.defName] = new List<BodyPartDef> {};
                            partToPartMapper[surgeryBodyPart.defName].AddDistinct(otherSurgeryBodyPart);
                        }
                    }
                }

                // Looks for matching body part labels
                foreach (BodyPartDef surgeryBodyPart in surgery.appliedOnFixedBodyParts) {
                    foreach (
                        BodyPartDef raceBodyPart in
                        DefDatabase<BodyDef>.AllDefs.SelectMany(body => body.AllParts).Select(bpr => bpr.def).Where(rbp =>
                            surgeryBodyPart != rbp && surgeryBodyPart.defName != rbp.defName && surgeryBodyPart.label.ToLower() == rbp.label.ToLower()
                        )
                    ) {
                        if (!partToPartMapper.ContainsKey(surgeryBodyPart.defName)) partToPartMapper[surgeryBodyPart.defName] = new List<BodyPartDef> {};
                        partToPartMapper[surgeryBodyPart.defName].AddDistinct(raceBodyPart);
                    }
                }
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
                foreach (ThingDef pawnDef in pawnList) {
                    bool shouldAddSurgery = false;

                    // If the pawn never had any recipes, then it doesn't even have the basics, so don't risk adding new ones
                    if (pawnDef.recipes == null || pawnDef.recipes.Count() < 1) continue;

                    // If it already exists, don't add it
                    else if (pawnDef.recipes.Any( s =>
                        s == surgery || s.defName == surgery.defName || s.label.ToLower() == surgery.label.ToLower()
                    )) continue;

                    // If it's an administer recipe, add it
                    else if (!surgery.targetsBodyPart) shouldAddSurgery = true;

                    // If it targets any body parts that exist within the alien, add it
                    else if (surgery.targetsBodyPart && surgery.appliedOnFixedBodyParts.Count() >= 1 && surgery.appliedOnFixedBodyParts.Any( sbp =>
                        pawnDef.race.body.AllParts.Any( rbpr => sbp.defName == rbpr.def.defName )
                    )) shouldAddSurgery = true;

                    if (shouldAddSurgery) {
                        pawnDef.recipes.Add(surgery);
                        if (surgery.recipeUsers == null) surgery.recipeUsers = new List<ThingDef> { pawnDef };
                        else                             surgery.recipeUsers.Add(pawnDef);
                    }
                }
            }

            // Just in case other mods added dupes...
            foreach (ThingDef pawnDef in pawnList) {
                pawnDef.recipes?.RemoveDuplicates();
            }
        }

        public static string GetSurgeryBioType (RecipeDef surgery) {
            var users = surgery.AllRecipeUsers;
            if (users.All(p => p.race.IsMechanoid)) return "mechanoid";
            if (users.All(p => p.race.Animal))      return "animal";
            if (users.All(p => p.race.Humanlike))   return "humanlike";
            if (users.Any(p => p.race.Animal))      return "mixed";
            return "humanlike";
        }
    }
}
