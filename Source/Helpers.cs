﻿using RimWorld;
using Harmony;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Verse;

namespace XenobionicPatcher {
    public static class Helpers {
        // Most of these are very hot (and deterministic), so they have caches built-in

        internal static Dictionary<string, string> surgeryBioTypeCache = new Dictionary<string, string>() {};

        public static string GetSurgeryBioType (RecipeDef surgery) {
            if (surgeryBioTypeCache.ContainsKey(surgery.defName)) return surgeryBioTypeCache[surgery.defName];

            // Special short-circuit
            if      (
                IsSupertypeOf("Androids.Recipe_Disassemble",                          surgery.workerClass) ||
                IsSupertypeOf("Androids.Recipe_RepairKit",                            surgery.workerClass) ||
                IsSupertypeOf("MOARANDROIDS.Recipe_InstallImplantAndroid",            surgery.workerClass) ||
                IsSupertypeOf("MOARANDROIDS.Recipe_InstallArtificialBodyPartAndroid", surgery.workerClass)
            ) {
                surgeryBioTypeCache[surgery.defName] = "mech";
                return "mech";
            }

            var users = surgery.AllRecipeUsers.Where(p => IsSupertypeOf(typeof(Pawn), p.thingClass)).ToList();  // pawns only
            users.RemoveDuplicates();

            string result = "mixed";  // default
            if      (users.All(p => GetPawnBioType(p) == "mech"))      result = "mech";
            else if (users.All(p => GetPawnBioType(p) == "animal"))    result = "animal";
            else if (users.All(p => GetPawnBioType(p) == "humanlike")) result = "humanlike";
            else if (users.All(p => GetPawnBioType(p) == "other"))     result = "other";
            else if (users.All(p => Regex.IsMatch( GetPawnBioType(p), "animal|humanlike" ))) result = "fleshlike";

            surgeryBioTypeCache[surgery.defName] = result;
            return result;
        }

        internal static Dictionary<string, string> pawnBioTypeCache = new Dictionary<string, string>() {};

        public static string GetPawnBioType (ThingDef pawn) {
            if (pawnBioTypeCache.ContainsKey(pawn.defName)) return pawnBioTypeCache[pawn.defName];

            if (pawn.race == null) return "non-pawn";  // certain surgeries work against non-pawns

            string result = "other";  // default; must be a toolUser?

            // Fungi like Wildpods also don't have meat, so this takes priority
            if (pawn.race.Animal) result = "animal";

            // This catches mechanoids and droids, but not meat-containing Androids
            else if (pawn.race.IsMechanoid || pawn.GetStatValueAbstract(StatDefOf.MeatAmount) <= 0) result = "mech";

            else if (pawn.race.Humanlike) result = "humanlike";

            pawnBioTypeCache[pawn.defName] = result;
            return result;
        }

        // Sub-descriptors of body parts that might interfere with matching
        internal static string[] bodyPartAdjectives = new[] {
            "tiny", "small", "medium", "large", "huge",
            "internal", "external", 
            "tentacle", "malformed", "pupula", "duplex", "spot", "sentient", "sensor",
            "insect", "animal", "plant", "crocodile", "snake", "artificial", "skeletal", "sickle", "mech(anical)?", "xeno",
            "front", "back", "rear",
            "left", "center", "right", "upper", "middle", "lower",
            "first", "second", "third", "fourth", "fifth",
            "1st", "2nd", "3rd", "4th", "5th"
        };
        internal static Regex prefixedBodyPartAdjectives = new Regex(
            @"^(?<word>" + string.Join("|", bodyPartAdjectives) + @")\s+(?=\w)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase
        );
        internal static Regex postfixedBodyPartAdjectives = new Regex(
            @"(?<=\w)\s*\b\(?(?<word>" + string.Join("|", bodyPartAdjectives) + @")\)?\b\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase
        );

        // Regex and other replacements take a bit, and we very often hit duplicate strings
        internal static Dictionary<string, string> simplifyCache = new Dictionary<string, string>() {};

        public static string SimplifyBodyPartLabel (string label) {
            if (simplifyCache.ContainsKey(label)) return simplifyCache[label];
            var newLabel = label;

            // No spaces, like a defName?
            if (!newLabel.Contains(" ")) {
                newLabel = Regex.Replace(newLabel, @"[A-Z]+_", "");  // get rid of mod prefix letters like QQ_
                newLabel = GenText.SplitCamelCase(newLabel);         // AnimalJaw --> Animal Jaw
            }

            newLabel = newLabel.ToLower();
            newLabel = newLabel.Replace("_", " ");

            // These are tied to ^$, so repetition is warranted
            for (int i = 1; i <= 3; i++) {
                if (!newLabel.Contains(" ")) break;
                newLabel = postfixedBodyPartAdjectives.Replace(newLabel, "");
                newLabel =  prefixedBodyPartAdjectives.Replace(newLabel, "");
            }
            newLabel = newLabel.Trim();
            newLabel = Regex.Replace(newLabel, @"e?s$", "");       // no plurals
            newLabel = Regex.Replace(newLabel, @"\s*\d+\s*", "");  // no numbers

            simplifyCache[label] = newLabel;
            return newLabel;
        }

        // Zero strings
        public static bool DoesBodyPartMatch (BodyPartRecord bprFirst, BodyPartRecord bprSecond) {
            return (
                DoesBodyPartMatch(bprFirst, bprSecond.def.defName) ||
                DoesBodyPartMatch(bprFirst, bprSecond.LabelShort)  ||
                DoesBodyPartMatch(bprFirst, bprSecond.Label)
            );
        }
        public static bool DoesBodyPartMatch (BodyPartRecord bpr, BodyPartDef bpd) {
            return (
                DoesBodyPartMatch(bpr, bpd.defName)    ||
                DoesBodyPartMatch(bpr, bpd.LabelShort)
            );
        }
        public static bool DoesBodyPartMatch (BodyPartDef bpd, BodyPartRecord bpr) {
            return (
                DoesBodyPartMatch(bpr, bpd.defName)    ||
                DoesBodyPartMatch(bpr, bpd.LabelShort)
            );
        }
        public static bool DoesBodyPartMatch (BodyPartDef bpdFirst, BodyPartDef bpdSecond) {
            return (
                DoesBodyPartMatch(bpdFirst, bpdSecond.defName) ||
                DoesBodyPartMatch(bpdFirst, bpdSecond.LabelShort)
            );
        }
        // One string
        public static bool DoesBodyPartMatch (BodyPartRecord bpr, string match) {
            string cleanMatch = SimplifyBodyPartLabel(match);
        
            return (
                SimplifyBodyPartLabel(bpr.def.defName) == cleanMatch ||
                SimplifyBodyPartLabel(bpr.LabelShort)  == cleanMatch ||
                SimplifyBodyPartLabel(bpr.Label)       == cleanMatch
            );
        }
        public static bool DoesBodyPartMatch (string match, BodyPartRecord bpr) {
            return DoesBodyPartMatch(bpr, match);
        }
        public static bool DoesBodyPartMatch (BodyPartDef bpd, string match) {
            string cleanMatch = SimplifyBodyPartLabel(match);
        
            return (
                SimplifyBodyPartLabel(bpd.defName)    == cleanMatch ||
                SimplifyBodyPartLabel(bpd.LabelShort) == cleanMatch
            );
        }
        public static bool DoesBodyPartMatch (string match, BodyPartDef bpd) {
            return DoesBodyPartMatch(bpd, match);
        }
        // Two strings
        public static bool DoesBodyPartMatch (string matchFirst, string matchSecond) {
            return SimplifyBodyPartLabel(matchFirst) == SimplifyBodyPartLabel(matchSecond);
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

        internal static List<Type> surgeryTypeOrder = new List<Type> {
            SafeTypeByName("Androids.Recipe_Disassemble"),
            SafeTypeByName("Androids.Recipe_RepairKit"),
            SafeTypeByName("RRYautja.Recipe_RemoveHugger"),
            typeof(Recipe_InstallArtificialBodyPart),
            SafeTypeByName("OrenoMSE.Recipe_InstallBodyPartModule"),
            SafeTypeByName("MOARANDROIDS.Recipe_InstallArtificialBodyPartAndroid"),
            typeof(Recipe_InstallImplant),
            SafeTypeByName("OrenoMSE.Recipe_InstallImplantSystem"),
            SafeTypeByName("MOARANDROIDS.Recipe_InstallImplantAndroid"),
            typeof(Recipe_InstallNaturalBodyPart),
            typeof(Recipe_RemoveHediff),
            SafeTypeByName("ScarRemoving.Recipe_RemoveHediff_noBrain"),
            SafeTypeByName("OrenoMSE.Recipe_RemoveImplantSystem"),
            SafeTypeByName("RRYautja.Recipe_Remove_Gauntlet"),
            typeof(Recipe_AdministerUsableItem),
            typeof(Recipe_AdministerIngestible),
            AccessTools.TypeByName("RimWorld.Recipe_RemoveBodyPart"),
            typeof(Recipe_ExecuteByCut),
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
            simplifyCache      .Clear();
            typeByNameCache    .Clear();
        }
    }
}