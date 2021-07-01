using RimWorld;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Verse;

namespace XenobionicPatcher {
    public static class BodyPartMatcher {
        // Most of these are very hot (and deterministic), so they have caches built-in

        // Sub-descriptors of body parts that might interfere with matching
        internal static string[] bodyPartAdjectives = new[] {
            "a", "the",
            "tiny", "small", "little", "medium", "big", "large", "huge",
            "internal", "external", "appendix",

            // Cosmic Horror's colorful adjectives
            "malformed", "pupula", "duplex", "recessed", "bulbous", "bloodshot", "dominant", "rapid-movement",
            "over-developed", "split", "zipper-shaped", "overbite", "underbite", "toothless", "malformed",
            "scissor-shaped", "elongated", "crooked", "gumless",

            "spot", "sentient", "sensor", "set",
            "insect", "animal", "plant", "crocodile", "snake", "artificial", "skeletal", "sickle", "mech(anical)?", "xeno",
            "front", "back", "rear",
            "index", "middle", "ring",
            "segment",  // + "ring"
            "left", "center", "right", "upper", "middle", "lower",
            "first", "second", "third", "fourth", "fifth",
            "1st", "2nd", "3rd", "4th", "5th"
        };
        internal static Regex prefixedBodyPartAdjectives = new Regex(
            @"^(?<word>" + string.Join("|", bodyPartAdjectives) + @")\s+(?=\w)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase
        );
        internal static Regex postfixedBodyPartAdjectives = new Regex(
            @"(?<=\w)\s+\(?(?<word>" + string.Join("|", bodyPartAdjectives) + @")\)?\b\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase
        );

        // Regex and other replacements take a bit, and we very often hit duplicate strings
        internal static Dictionary<string, string> simplifyCache = new Dictionary<string, string>() {};

        internal static BodyPartStringEqualityComparer StringEqualityComparer = new BodyPartStringEqualityComparer {};
        internal static BodyPartRecordEqualityComparer    BPREqualityComparer = new BodyPartRecordEqualityComparer {};
        internal static    BodyPartDefEqualityComparer    BPDEqualityComparer = new    BodyPartDefEqualityComparer {};

        public static string SimplifyBodyPartLabel (string label) {
            if (label == null) return null;  // dodge exceptions

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
        public static string SimplifyBodyPartLabel (BodyPartRecord bpr) {
            string bprS = bpr.LabelShort ?? bpr.Label ?? bpr.def.defName;
            return SimplifyBodyPartLabel(bprS);
        }
        public static string SimplifyBodyPartLabel (BodyPartDef bpd) {
            string bpdS = bpd.LabelShort ?? bpd.defName;
            return SimplifyBodyPartLabel(bpdS);
        }


        public class BodyPartStringEqualityComparer : IEqualityComparer<string> {
            public bool Equals (string s1, string s2) {
                if      (s1 == null && s2 == null) return true;
                else if (s1 == null || s2 == null) return false;

                return SimplifyBodyPartLabel(s1) == SimplifyBodyPartLabel(s2);
            }

            public int GetHashCode (string s) {
                return SimplifyBodyPartLabel(s).GetHashCode();
            }
        }
        public class BodyPartRecordEqualityComparer : IEqualityComparer<BodyPartRecord> {
            public bool Equals (BodyPartRecord bpr1, BodyPartRecord bpr2) {
                if      (bpr1 == null && bpr2 == null) return true;
                else if (bpr1 == null || bpr2 == null) return false;

                return SimplifyBodyPartLabel(bpr1) == SimplifyBodyPartLabel(bpr2);
            }

            public int GetHashCode (BodyPartRecord bpr) {
                return SimplifyBodyPartLabel(bpr).GetHashCode();
            }
        }
        public class BodyPartDefEqualityComparer : IEqualityComparer<BodyPartDef> {
            public bool Equals (BodyPartDef bpd1, BodyPartDef bpd2) {
                if      (bpd1 == null && bpd2 == null) return true;
                else if (bpd1 == null || bpd2 == null) return false;

                return SimplifyBodyPartLabel(bpd1) == SimplifyBodyPartLabel(bpd2);
            }

            public int GetHashCode (BodyPartDef bpd) {
                return SimplifyBodyPartLabel(bpd).GetHashCode();
            }
        }

    }
}
