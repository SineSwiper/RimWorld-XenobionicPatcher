using RimWorld;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
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
            // Anomaly
            typeof(Recipe_SurgicalInspection),
            typeof(Recipe_GhoulInfusion),

            // Mech/droid, emergency, weirder stuff
            SafeTypeByName("Androids.Recipe_Disassemble"),
            SafeTypeByName("Androids.Recipe_RepairKit"),
            SafeTypeByName("RRYautja.Recipe_RemoveHugger"),
            SafeTypeByName("WhatTheHack.Recipes.Recipe_ExtractBrainData"),
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
            SafeTypeByName("TorannMagic.Recipe_RuneCarving"),

            // Install artificial body parts
            typeof(Recipe_InstallArtificialBodyPart),
            SafeTypeByName("OrenoMSE.Recipe_InstallBodyPartModule"),
            SafeTypeByName("MOARANDROIDS.Recipe_InstallArtificialBodyPartAndroid"),
            SafeTypeByName("MSE2.Recipe_InstallArtificialBodyPartWithChildren"),
            SafeTypeByName("CONN.Recipe_InstallArtificialBodyPartAndClearPawnFromCache"),
            SafeTypeByName("SyrHarpy.Recipe_InstallPart"),
            SafeTypeByName("SurgeryCF_Simple"),
            SafeTypeByName("SurgeryCF_Bionic"),
            SafeTypeByName("SurgeryCF_Archo"),
            SafeTypeByName("SurgeryCF_Battle"),
            SafeTypeByName("Immortals.Recipe_InstallFakeEye"),

            // Install implants
            typeof(Recipe_InstallImplant),
            SafeTypeByName("OrenoMSE.Recipe_InstallImplantSystem"),
            SafeTypeByName("MSE2.Recipe_InstallModule"),
            SafeTypeByName("MOARANDROIDS.Recipe_InstallImplantAndroid"),
            SafeTypeByName("VREAndroids.Recipe_InstallAndroidPart"),
            SafeTypeByName("VREAndroids.Recipe_InstallReactor"),
            SafeTypeByName("MOARANDROIDS.Recipe_InstallArtificialBrain"),
            SafeTypeByName("Polarisbloc.Recipe_MakeCartridgeSurgery"),
            SafeTypeByName("Polarisbloc.Recipe_InstallCombatChip"),
            SafeTypeByName("CyberNet.Recipe_InstallCyberNetBrainImplant"),

            // Install natural body part
            typeof(Recipe_InstallNaturalBodyPart),
            SafeTypeByName("QEthics.RecipeWorker_InstallNaturalBodyPart"),
            SafeTypeByName("MSE2.Recipe_InstallNaturalBodyPartWithChildren"),
            SafeTypeByName("GeneticRim.Recipe_InstallGeneticBodyPart"),

            // Part restoration
            SafeTypeByName("SyrScarRemoval.Recipe_BodyPartRegrowth"),
            SafeTypeByName("Polarisbloc.Recipe_RestoreMissingPart"),
            SafeTypeByName("TorannMagic.Recipe_RegrowBodyPart"),
            SafeTypeByName("TorannMagic.Recipe_RegrowUniversalBodyPart"),

            // "Change" surgeries
            typeof(Recipe_ChangeImplantLevel),
            SafeTypeByName("Polarisbloc.Recipe_TransgenderSurgery"),
            SafeTypeByName("Polarisbloc.Recipe_SurgeryChangeBioAge"),
            SafeTypeByName("Polarisbloc.Recipe_ExtractAbility"),
            SafeTypeByName("SyrHarpy.Recipe_ChangeLightningAmplifier"),

            // Simple Hediff additions
            typeof(Recipe_AddHediff),
            SafeTypeByName("VFEI.Other.Recipe_AddMutationHediff"),

            // Xenogerm/Pregnancy
            typeof(Recipe_ImplantXenogerm),
            typeof(Recipe_ImplantEmbryo),
            typeof(Recipe_ImplantIUD),
            typeof(Recipe_ExtractOvum),
            typeof(Recipe_TerminatePregnancy),

            // Lobotomy
            SafeTypeByName("QEthics.RecipeWorker_NerveStapling"),
            SafeTypeByName("Diseases.RecipeWorker_Lobotomy"),

            // Removals
            typeof(Recipe_RemoveHediff),
            SafeTypeByName("SyrScarRemoval.Recipe_ScarRemoval"),
            SafeTypeByName("SyrScarRemoval.Recipe_ScarRemovalBrain"),
            SafeTypeByName("ScarRemoving.Recipe_RemoveHediff_noBrain"),
            SafeTypeByName("EPIA.Recipe_RemoveScarHediff"),
            SafeTypeByName("EPIA.Recipe_RemoveBrainScarHediff"),
            SafeTypeByName("OrenoMSE.Recipe_RemoveImplantSystem"),
            SafeTypeByName("MSE2.Recipe_RemoveModules"),
            SafeTypeByName("EPIA.Recipe_RemoveImplant"),
            SafeTypeByName("Polarisbloc.Recipe_RemoveImplant"),
            SafeTypeByName("Polarisbloc.Recipe_RemoveHediffIsOld"),
            SafeTypeByName("VREAndroids.Recipe_RemoveArtificialBodyPart"),
            SafeTypeByName("OrenoMSE.Recipe_RemoveBodyPartSystem"),
            SafeTypeByName("RRYautja.Recipe_Remove_Gauntlet"),
            SafeTypeByName("VFEPirates.RecipeWorker_WarcasketRemoval"),

            // Blood work
            typeof(Recipe_ExtractHemogen),
            typeof(Recipe_BloodTransfusion),
            SafeTypeByName("Vampire.Recipe_ExtractBloodVial"),
            SafeTypeByName("Vampire.Recipe_ExtractBloodPack"),
            SafeTypeByName("Vampire.Recipe_ExtractBloodWine"),
            SafeTypeByName("Vampire.Recipe_TransferBlood"),

            // Administer items
            typeof(Recipe_AdministerUsableItem),
            typeof(Recipe_AdministerIngestible),
            SafeTypeByName("DeathRattle.Recipe_AdministerComaDrug"),

            // Vanilla removals
            typeof(Recipe_RemoveImplant),
            typeof(Recipe_RemoveBodyPart),

            // Vanilla execute
            typeof(Recipe_ExecuteByCut),

            // Final fallback
            typeof(Recipe_Surgery),
        };

        internal static Dictionary<BodyPartDef, int> partSortOrderLookupCache = new();

        public static int SurgerySort (RecipeDef surgery) {
            // Initialize cache
            if (partSortOrderLookupCache.Count == 0) {
                // Start with a human body part list
                List<BodyPartDef> partList = DefDatabase<BodyDef>.GetNamed("Human").AllParts.Select(bpr => bpr.def).ToList();
                partList.RemoveDuplicates();

                // Create an ordered list that combines all of the bodies in as close to its original order as possible (in relation
                // to the original human body set)
                foreach (BodyDef bodyDef in DefDatabase<BodyDef>.AllDefs) {
                    List<BodyPartDef> bodyPartList = bodyDef.AllParts.Select(bpr => bpr.def).ToList();

                    int lastKnownPartIndex = 0;
                    foreach (var bpd in bodyPartList) {
                        int index = partList.IndexOf(bpd);
                        if (index > -1) {
                            lastKnownPartIndex = index;
                            continue;
                        }

                        /* XXX: This matching still isn't perfect, since parts with different base names aren't going to get
                         * tied together here.
                         *
                         * What we really need is the partToPartMapper used in DefInjectors, but caching that data in
                         * BodyPartMatcher may start polluting pawn species types for the different separated patching loops.
                         * It can be done (by filtering the returned HashSet<BodyPartDef> on each loop), but it would require
                         * moving a large part of InjectSurgeryRecipes into the BodyPartMatcher, and quite a bit more
                         * testing/timing.
                         * 
                         * I'm not against doing so, as it's probably additional time savings in the loops, but just not right
                         * now.  It feels too much like bikeshedding currently, and this v1.5 version of XP is "good enough".
                         * I might revisit this next DLC cycle...
                         */ 

                        // try harder with the BodyPartMatcher
                        string simplePartLabel = BodyPartMatcher.SimplifyBodyPartLabel(bpd);
                        index = partList.FirstIndexOf( obpd => BodyPartMatcher.SimplifyBodyPartLabel(obpd) == simplePartLabel );

                        if (index > -1) {
                            // still need to add in the part, but at least we know where it goes
                            partList.Insert(index + 1, bpd);
                            lastKnownPartIndex = index + 2;
                            continue;
                        }

                        partList.Insert(lastKnownPartIndex + 1, bpd);
                        lastKnownPartIndex++;
                    }
                }

                partSortOrderLookupCache = partList.ToDictionary( bpd => bpd, partList.IndexOf );
            }

            // First sort
            var worker    = surgery.workerClass;
            int typeOrder = surgeryTypeOrder.IndexOf(worker);

            if (typeOrder == -1) typeOrder = surgery.targetsBodyPart ? 150 : 155;  // XXX: Recipe_Surgery should take care of almost all of these, anyway

            // Second sort
            int partOrder = 9999;
            if (surgery.targetsBodyPart) {
                // XXX: We looked through every BodyPartDef, so this First shouldn't ever fail (and should always
                // return index 0).  But, I can't argue against a little NPE protection...
                BodyPartDef foundPart = surgery.appliedOnFixedBodyParts.FirstOrDefault( bpd => partSortOrderLookupCache.ContainsKey(bpd) );
                if (foundPart != null) partOrder = partSortOrderLookupCache[foundPart];
            }

            return typeOrder * 10000 + partOrder;
        }

        public static void ClearCaches () {
            surgeryBioTypeCache.Clear();
            pawnBioTypeCache   .Clear();
            typeByNameCache    .Clear();
            partSortOrderLookupCache.Clear();
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
