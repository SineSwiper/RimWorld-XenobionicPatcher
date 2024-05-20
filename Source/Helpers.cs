using RimWorld;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Verse;

namespace XenobionicPatcher {
    [Flags]
    public enum XPBioType : ushort {
        None      = 0,
        Animal    = 1 << 0,
        Humanlike = 1 << 1,
        Flesh     = 1 << 2,
        Mech      = 1 << 3,

        Other     = 1 << 6,
        NonPawn   = 1 << 7,

        Critterlike = Animal | Humanlike,
        Fleshlike   = Animal | Humanlike | Flesh,
        SmartPawn   = Humanlike | Mech,
        Pawnlike    = Fleshlike | Mech,

        All = (1 << 8) - 1,
    };

    public static class Helpers {
        // Most of these are very hot (and deterministic), so they have caches built-in

        internal static Dictionary<string, XPBioType> surgeryBioTypeCache = new() {};

        internal static List<string> mechSurgeryClasses = new List<string> {
            "AlphaBehavioursAndEvents.Recipe_ShutDown",
            "AnimalBehaviours.Recipe_ShutDown",
            "Androids.Recipe_Disassemble",
            "Androids.Recipe_RepairKit",
            "MOARANDROIDS.Recipe_AndroidRewireSurgery",
            "MOARANDROIDS.Recipe_RemoveSentience",
            "MOARANDROIDS.Recipe_RerollTraits",
            "MOARANDROIDS.Recipe_InstallImplantAndroid",
            "MOARANDROIDS.Recipe_InstallArtificialBodyPartAndroid",
            "MOARANDROIDS.Recipe_InstallArtificialBrain",
            "MOARANDROIDS.Recipe_ApplyHydraulicNaniteBank",
            "MOARANDROIDS.Recipe_ApplyHealFrameworkSystem",
            "MOARANDROIDS.Recipe_ApplyHealCoolingSystem",
            "MOARANDROIDS.Recipe_ApplyHealCPUSerum",
            "VREAndroids.Recipe_InstallAndroidPart",
            "VREAndroids.Recipe_InstallReactor",
            "VREAndroids.Recipe_RemoveArtificialBodyPart"
        };

        public static XPBioType GetSurgeryBioType (RecipeDef surgery, bool useCache = true) {
            if (useCache && surgeryBioTypeCache.ContainsKey(surgery.defName)) return surgeryBioTypeCache[surgery.defName];

            // Special short-circuit
            foreach (string mechSurgeryClass in mechSurgeryClasses) {
                if (IsSupertypeOf(mechSurgeryClass, surgery.workerClass)) {
                    surgeryBioTypeCache[surgery.defName] = XPBioType.Mech;
                    return XPBioType.Mech;
                }
            }

            var users = surgery.AllRecipeUsers.Where(p => IsSupertypeOf(typeof(Pawn), p.thingClass)).ToList();  // pawns only
            users.RemoveDuplicates();

            XPBioType result = XPBioType.None;  // start with nothing
            users.ForEach(p => result |= GetPawnBioType(p) );
            surgeryBioTypeCache[surgery.defName] = result;
            return result;
        }

        internal static Dictionary<string, XPBioType> pawnBioTypeCache = new() {};

        public static XPBioType GetPawnBioType (ThingDef pawn, bool useCache = true) {
            if (useCache && pawnBioTypeCache.ContainsKey(pawn.defName)) return pawnBioTypeCache[pawn.defName];

            // certain surgeries work against non-pawns
            if (pawn.race == null) {
                pawnBioTypeCache[pawn.defName] = XPBioType.NonPawn;
                return XPBioType.NonPawn;
            }

            XPBioType result = XPBioType.Other;  // default; must be a toolUser?

            // Fungi like Wildpods also don't have meat, so this takes priority
            if (pawn.race.Animal) result = XPBioType.Animal;

            // This catches mechanoids and droids, but not meat-containing Androids
            else if (pawn.race.IsMechanoid || !pawn.race.IsFlesh || pawn.GetStatValueAbstract(StatDefOf.MeatAmount) <= 0) result = XPBioType.Mech;

            else if (pawn.race.Humanlike) result = XPBioType.Humanlike;

            // Fleshlike ToolUsers, like Anomaly entities
            else if (pawn.race.IsFlesh) result = XPBioType.Flesh;

            pawnBioTypeCache[pawn.defName] = result;
            return result;
        }

        internal static Dictionary<string, Type> typeByNameCache = new Dictionary<string, Type>() {};
        public static Type SafeTypeByName (string typeName) {
            if (typeByNameCache.ContainsKey(typeName)) return typeByNameCache[typeName];

            Type type = null;
            try { type = AccessTools.TypeByName(typeName); }
            catch (TypeLoadException) {};

            typeByNameCache[typeName] = type;
            return type;
        }

        public static bool IsSupertypeOf (Type baseType, Type currentType) {
            return baseType.IsAssignableFrom(currentType);
        }
        public static bool IsSupertypeOf (Type baseType, string currentTypeName) {
            Type currentType = SafeTypeByName(currentTypeName);
            if (baseType == null || currentType == null) return false;

            return baseType.IsAssignableFrom(currentType);
        }
        public static bool IsSupertypeOf (string baseTypeName, Type currentType) {
            Type baseType = SafeTypeByName(baseTypeName);
            if (baseType == null || currentType == null) return false;

            return baseType.IsAssignableFrom(currentType);
        }
        public static bool IsSupertypeOf (string baseTypeName, string currentTypeName) {
            Type baseType    = SafeTypeByName(   baseTypeName);
            Type currentType = SafeTypeByName(currentTypeName);
            if (baseType == null || currentType == null) return false;

            return baseType.IsAssignableFrom(currentType);
        }

        public static List<DefHyperlink> SurgeryToHyperlinks (RecipeDef surgery) {
            var defs = new HashSet<Def> {};

            Predicate<string> checkWorkerType = delegate(string t) {
                 return SafeTypeByName(t) is Type type && type != null && IsSupertypeOf(type, surgery.workerClass);
            };

            foreach (IngredientCount ingredientCount in surgery.ingredients.Where(i => i.IsFixedIngredient)) {
                ThingDef part = ingredientCount.FixedIngredient;
                if (
                    // Exempt from this check
                    !(checkWorkerType("Recipe_AdministerIngestible") || checkWorkerType("Recipe_AdministerUsableItem")) && (
                        part.IsMedicine || part.IsStuff || part.defName.StartsWith("RepairKit")
                    )
                ) continue;

                defs.Add(part);
            }

            var hediffChangers = new List<HediffDef> { surgery.addsHediff, surgery.removesHediff, surgery.changesHediffLevel };
            foreach (HediffDef hediff in hediffChangers.Where(
                hd => hd != null && defs.All(d => d.defName != hd.defName)
            )) {
                if      (!hediff.descriptionHyperlinks.NullOrEmpty()) defs.AddRange( hediff.descriptionHyperlinks.Select(dhl => dhl.def) );
                else if (hediff.spawnThingOnRemoved != null)          defs.Add(hediff.spawnThingOnRemoved);
                else                                                  defs.Add(hediff);
            }

            return defs.Select(d => new DefHyperlink(d)).ToList();
        }

        internal static List<Type> surgeryTypeOrder = new List<Type> {
            // Mech/droid, emergency, weirder stuff
            SafeTypeByName("Androids.Recipe_Disassemble"),
            SafeTypeByName("Androids.Recipe_RepairKit"),
            SafeTypeByName("RRYautja.Recipe_RemoveHugger"),
            SafeTypeByName("MSE2.Surgey_MakeShiftRepair"),
            SafeTypeByName("MSE2.Surgery_MakeShiftRepair"),
            SafeTypeByName("QEthics.RecipeWorker_CreateBrainScan"),
            SafeTypeByName("QEthics.RecipeWorker_GenomeSequencing"),
            SafeTypeByName("MOARANDROIDS.Recipe_AndroidRewireSurgery"),
            SafeTypeByName("MOARANDROIDS.Recipe_RemoveSentience"),
            SafeTypeByName("MOARANDROIDS.Recipe_RerollTraits"),
            SafeTypeByName("MOARANDROIDS.Recipe_ApplyHydraulicNaniteBank"),
            SafeTypeByName("MOARANDROIDS.Recipe_ApplyHealFrameworkSystem"),
            SafeTypeByName("MOARANDROIDS.Recipe_ApplyHealCoolingSystem"),
            SafeTypeByName("MOARANDROIDS.Recipe_ApplyHealCPUSerum"),
            SafeTypeByName("SyrHarpy.Recipe_ChangeLightningAmplifier"),
            SafeTypeByName("SyrScarRemoval.Recipe_BodyPartRegrowth"),
            SafeTypeByName("SyrScarRemoval.Recipe_ScarRemoval"),
            SafeTypeByName("SyrScarRemoval.Recipe_ScarRemovalBrain"),
            SafeTypeByName("TorannMagic.Recipe_RuneCarving"),

            // Install artificial body parts
            typeof(Recipe_InstallArtificialBodyPart),
            SafeTypeByName("OrenoMSE.Recipe_InstallBodyPartModule"),
            SafeTypeByName("MOARANDROIDS.Recipe_InstallArtificialBodyPartAndroid"),
            SafeTypeByName("MSE2.Recipe_InstallArtificialBodyPartWithChildren"),
            SafeTypeByName("Immortals.Recipe_InstallFakeEye"),

            // Install implants
            typeof(Recipe_InstallImplant),
            SafeTypeByName("OrenoMSE.Recipe_InstallImplantSystem"),
            SafeTypeByName("MSE2.Recipe_InstallModule"),
            SafeTypeByName("MOARANDROIDS.Recipe_InstallImplantAndroid"),
            SafeTypeByName("VREAndroids.Recipe_InstallAndroidPart"),
            SafeTypeByName("VREAndroids.Recipe_InstallReactor"),
            SafeTypeByName("MOARANDROIDS.Recipe_InstallArtificialBrain"),
            SafeTypeByName("Recipe_ChangeImplantLevel"),
            SafeTypeByName("QEthics.RecipeWorker_NerveStapling"),

            // Install natural body part
            typeof(Recipe_InstallNaturalBodyPart),
            SafeTypeByName("QEthics.RecipeWorker_InstallNaturalBodyPart"),
            SafeTypeByName("MSE2.Recipe_InstallNaturalBodyPartWithChildren"),
            SafeTypeByName("GeneticRim.Recipe_InstallGeneticBodyPart"),

            // Removals
            typeof(Recipe_RemoveHediff),
            SafeTypeByName("ScarRemoving.Recipe_RemoveHediff_noBrain"),
            SafeTypeByName("EPIA.Recipe_RemoveScarHediff"),
            SafeTypeByName("OrenoMSE.Recipe_RemoveImplantSystem"),
            SafeTypeByName("VREAndroids.Recipe_RemoveArtificialBodyPart"),
            SafeTypeByName("MSE2.Recipe_RemoveModules"),
            SafeTypeByName("EPIA.Recipe_RemoveImplant"),
            SafeTypeByName("RRYautja.Recipe_Remove_Gauntlet"),
            SafeTypeByName("VFEPirates.RecipeWorker_WarcasketRemoval"),

            // Administer items
            typeof(Recipe_AdministerUsableItem),
            typeof(Recipe_AdministerIngestible),

            // Vanilla removals
            AccessTools.TypeByName("RimWorld.Recipe_RemoveBodyPart"),

            // Vanilla execute
            typeof(Recipe_ExecuteByCut),
            SafeTypeByName("Diseases.RecipeWorker_Lobotomy"),

            // Final fallback
            typeof(Recipe_Surgery),
        };

        public static int SurgerySort (RecipeDef surgery) {
            // First sort
            var worker    = surgery.workerClass;
            int typeOrder = surgeryTypeOrder.IndexOf(worker);

            if (typeOrder == -1) typeOrder = surgery.targetsBodyPart ? 50 : 55;

            // Second sort
            List<BodyPartDef> humanPartList = DefDatabase<BodyDef>.GetNamed("Human").AllParts.Select(bpr => bpr.def).ToList();
            int partOrder = -1;
            if (surgery.targetsBodyPart) {
                foreach (var part in surgery.appliedOnFixedBodyParts) {
                    partOrder = humanPartList.IndexOf(part);
                    if (partOrder > -1) break;
                }
            }

            if (partOrder == -1) partOrder = 999;

            return typeOrder * 1000 + partOrder;
        }

        public static void ClearCaches () {
            surgeryBioTypeCache.Clear();
            pawnBioTypeCache   .Clear();
            typeByNameCache    .Clear();
            BodyPartMatcher.simplifyCache.Clear();
        }

        // Dictionary helpers
        internal static void NewIfNoKey<K, V> (this Dictionary<K, V> dict, K key) where V : new() {
            if (!dict.ContainsKey(key)) dict.Add(key, new V());
        }
        internal static void NewIfNoKey<K, V> (this Dictionary<K, V[]> dict, K key) where V : new() {
            if (!dict.ContainsKey(key)) dict.Add(key, new V[] {});
        }

        internal static void SetOrAddNested<K, V> (this Dictionary<K, V[]> dict, K key, V value) {
            if (!dict.ContainsKey(key)) dict.Add(key, new V[] { value });
            else                        dict[key].AddItem(value);
        }

        internal static void SetOrAddNested<K, V> (this Dictionary<K, List<V>> dict, K key, V value) {
            dict.NewIfNoKey(key);
            dict[key].Add(value);
        }

        internal static void SetOrAddNested<K, V> (this Dictionary<K, HashSet<V>> dict, K key, V value) {
            dict.NewIfNoKey(key);
            dict[key].Add(value);
        }

        internal static void SetOrAddNestedRange<K, V> (this Dictionary<K, V[]> dict, K key, IEnumerable<V> value) {
            if (dict.ContainsKey(key))
                dict[key] = dict[key].Concat(value).ToArray();
            else
                dict.Add(key, value.ToArray());
        }

        internal static void SetOrAddNestedRange<K, V> (this Dictionary<K, List<V>> dict, K key, IEnumerable<V> value) {
            if (dict.ContainsKey(key))
                dict[key].AddRange(value);
            else
                dict.Add(key, value.ToList());
        }

        internal static void SetOrAddNestedRange<K, V> (this Dictionary<K, HashSet<V>> dict, K key, IEnumerable<V> value) {
            if (dict.ContainsKey(key))
                dict[key].AddRange(value);
            else
                dict.Add(key, value.ToHashSet());
        }
    }
}
