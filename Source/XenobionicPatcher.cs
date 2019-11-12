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
        public static Base         Instance    { get; private set; }
        public static DefInjectors DefInjector { get; private set; }

        internal HugsLib.Utils.ModLogger ModLogger { get; private set; }

        public Base() {
            Instance    = this;
            DefInjector = new XenobionicPatcher.DefInjectors();
            ModLogger   = this.Logger;
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

            // Clean up
            Logger.Message("Merging duplicate surgical recipes");
            DefInjector.CleanupSurgeryRecipes(allSurgeryDefs, allPawnDefs);
        }

        public string GetSurgeryBioType (RecipeDef surgery) {
            var users = surgery.AllRecipeUsers;
            if (users.All(p => GetPawnBioType(p) == "mech"))      return "mech";
            if (users.All(p => GetPawnBioType(p) == "animal"))    return "animal";
            if (users.All(p => GetPawnBioType(p) == "humanlike")) return "humanlike";
            if (users.All(p => GetPawnBioType(p) == "other"))     return "other";

            if (users.All(p => Regex.IsMatch( GetPawnBioType(p), "animal|humanlike" ))) return "fleshlike";
            return "mixed";
        }

        public string GetPawnBioType (ThingDef pawn) {
            // This catches mechanoids and droids, but not meat-containing Androids
            if (pawn.race.IsMechanoid || pawn.GetStatValueAbstract(StatDefOf.MeatAmount) <= 0) return "mech";

            if (pawn.race.Animal)    return "animal";
            if (pawn.race.Humanlike) return "humanlike";

            // Must be a toolUser?
            return "other";
        }

    }
}
