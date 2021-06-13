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
            if (match == null) return false;
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
            if (match == null) return false;
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
            if (matchFirst == null || matchSecond == null) return false;
            return SimplifyBodyPartLabel(matchFirst) == SimplifyBodyPartLabel(matchSecond);
        }

        // TODO: Compare the speed of using IEqualityComparer over DoesBodyPartMatch checks
        // TODO: Create BPR/BPD versions by eliminating near-duplicate OR checks
        //    BPR: LabelShort, Label, def.defName
        //    BPD: LabelShort, defName

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
    }
}
