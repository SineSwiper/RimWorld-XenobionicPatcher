using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Verse;
using Unity.Burst.Intrinsics;

namespace XenobionicPatcher {
    public static class BodyPartMatcher {
        private static readonly Base XP = Base.Instance;

        enum PartMatchType {
            BodyPartRecord,
            BodyPartDef,
            DefName,
            LabelShort,
            Label,
            Tags,
        };

        // Most of these are very hot (and deterministic), so they have caches built-in

        // Sub-descriptors of body parts that might interfere with matching
        internal static string[] bodyPartAdjectives = new[] {
            "a", "the",
            "tiny", "small", "little", "medium", "big", "large", "huge",
            "internal", "external", "appendix", "additional",

            // Cosmic Horror's colorful adjectives
            "malformed", "pupula", "duplex", "recessed", "bulbous", "bloodshot", "dominant", "rapid-movement",
            "over-developed", "split", "zipper-shaped", "overbite", "underbite", "toothless", "malformed",
            "scissor-shaped", "elongated", "crooked", "gumless",

            "spot", "sentient", "sensor", "set", "honey",
            "insect", "animal", "plant", "crocodile", "snake", "artificial", "skeletal", "sickle", "mech(a|anical)?", "xeno",
            "front(al)?", "back", "rear", "top",
            "index", "middle", "ring",
            "segment",  // + "ring"
            "left", "center", "right", "upper", "middle", "lower",
            "first", "second", "third", "fourth", "fifth", "sixth", "seventh", "eighth",
            "1st", "2nd", "3rd", "4th", "5th", "6th", "7th", "8th",

            // Abdominopelvic regions from Evolved Organs
            "hypoch?ondriac", "lumbar", "iliac",  // XXX: hypocondriac is a typo

            // Insect throratic regions (also from Evolved Organs)
            "prothoracic", "mesothoracic", "metathoracic",
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
        internal static Dictionary<string, string> simplifyCache = new() {};

        internal static BodyPartStringEqualityComparer StringEqualityComparer = new() {};
        internal static BodyPartRecordEqualityComparer    BPREqualityComparer = new() {};
        internal static    BodyPartDefEqualityComparer    BPDEqualityComparer = new() {};

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

        internal readonly struct PartToPartMapper {
            internal HashSet<BodyPartDef> PartDefSet      { get; }
            internal BodyPartDef          KeyedPart       { get; }
            internal string               DefName         { get; }
            internal bool                 IsVanilla       { get; }
            internal List<string>         StaticPartGroup { get; }

            internal PartToPartMapper(BodyPartDef vanillaPartDef, List<string> staticPartGroup) {
                PartDefSet      = new() { vanillaPartDef };
                KeyedPart       = vanillaPartDef;
                DefName         = vanillaPartDef.defName;
                IsVanilla       = true;
                StaticPartGroup = staticPartGroup;
            }
            internal PartToPartMapper(string vanillaPartName, List<string> staticPartGroup) {
                var vanillaPartDef = DefDatabase<BodyPartDef>.GetNamed(vanillaPartName);
                PartDefSet      = new() { vanillaPartDef };
                KeyedPart       = vanillaPartDef;
                DefName         = vanillaPartName;
                IsVanilla       = true;
                StaticPartGroup = staticPartGroup;
            }
            internal PartToPartMapper(string keyedPartName, bool isVanilla) {
                var keyedPart   = DefDatabase<BodyPartDef>.GetNamed(keyedPartName);
                PartDefSet      = new() { keyedPart };
                KeyedPart       = keyedPart;
                DefName         = keyedPartName;
                IsVanilla       = isVanilla;
                StaticPartGroup = isVanilla ? new() { keyedPartName.ToLower() } : null;
            }
            internal PartToPartMapper(BodyPartDef keyedPart, bool isVanilla) {
                PartDefSet      = new() { keyedPart };
                KeyedPart       = keyedPart;
                DefName         = keyedPart.defName;
                IsVanilla       = isVanilla;
                StaticPartGroup = isVanilla ? new() { keyedPart.defName.ToLower() } : null;
            }
            internal PartToPartMapper(BodyPartDef keyedPart, HashSet<BodyPartDef> partDefSet) {
                PartDefSet      = new(partDefSet);
                KeyedPart       = keyedPart;
                DefName         = keyedPart.defName;
                IsVanilla       = false;
                StaticPartGroup = null;
            }

            public override int GetHashCode() {
                return DefName.GetHashCode();
            }
        }

        // The part-to-part mapper
        internal static Dictionary<string, PartToPartMapper> partToPartMapper = new() {};
        internal static List<string>                         vanillaPartNames = new() {};

        internal static HashSet<BodyPartRecord> processedParts = new() {};

        // For personal debugging only
        internal const string debugVanillaPartName = null;
        internal const string debugSurgeryDefName  = null;
        internal const string debugPawnKindDefName = null;

        // Static constructor
        internal static void Initialize() {
            // Initialize the partToPartMapper

            /* Start with a hard-coded list, just in case any of these don't match.  This is especially helpful for
             * animals, since they don't always have obvious humanlike analogues.  This also works as a part group
             * separator to ensure parts don't get mixed into the wrong groups.
             */
            string staticPartSetString =
                // Basics
                "Arm Shoulder Hand Finger Foot Toe Eye Ear Nose Jaw Head Brain Torso Heart Lung Kidney Liver Stomach Neck" + ' ' +
                // Animal parts
                "Elytra Tail Horn Tusk Trunk" + ' ' +
                // Bones
                "Skull Ribcage Spine Clavicle Sternum Humerus Radius Pelvis Femur Tibia"
            ;
            partToPartMapper = staticPartSetString.Split(' ').ToDictionary(
                keySelector:     k => k,
                elementSelector: k => new PartToPartMapper(k, isVanilla: true)
            );
            
            Dictionary<string, List<string>> additionalStaticPartGroup = new() {
                { "Arm",      new() { "flipper"                                   } },
                { "Hand",     new() { "claw", "grasper", "pincer"                 } },
                { "Finger",   new() { "thumb", "pinky"                            } },
                { "Foot",     new() { "hoof", "paw"                               } },
                { "Eye",      new() { "sight", "seeing", "visual"                 } },
                { "Ear",      new() { "antenna", "hear", "hearing", "sound"       } },
                { "Nose",     new() { "nostril", "smell", "smelling"              } },
                { "Jaw",      new() { "beak", "mouth", "maw", "teeth", "mandible" } },
                { "Torso",    new() { "thorax", "body", "shell"                   } },
                { "Heart",    new() { "reactor"                                   } },
                { "Neck",     new() { "pronotum"                                  } },
                // Wing should really be the base name, but there is no vanilla Wing part (even for birds!)
                { "Elytra",   new() { "wing" } },
            };
            foreach (string vanillaPartName in additionalStaticPartGroup.Keys) {                
                partToPartMapper[vanillaPartName].StaticPartGroup.AddRange(additionalStaticPartGroup[vanillaPartName]);
            }

            /* It's futile to try to separate the hand/foot connection, as animals have "hands" which also
             * sometimes double as feet.  We can try to clean this up later in CleanupHandFootSurgeryRecipes.
             *
             * We're still going to keep the bio-boundary below to keep out leg->hand connections.  That's still a
             * bit off.  And mechs, of course.
             */
            var handGroup = partToPartMapper["Hand"].StaticPartGroup;
            var footGroup = partToPartMapper["Foot"].StaticPartGroup;
            handGroup.AddRange(footGroup);
            footGroup.Clear();
            footGroup.AddRange(handGroup);

            // Set the vanilla parts list
            vanillaPartNames = partToPartMapper.Values.Select( ptpm => ptpm.DefName ).ToList();
        }

        public static int MatchPartsToParts (List<BodyPartRecord> fullBodyPartList) {
            // Filter out already-processed body parts
            List<BodyPartRecord> raceBodyParts =
                fullBodyPartList.
                Where( bpr => !processedParts.Contains(bpr) ).
                ToList()
            ;

            // Static part loop
            foreach (BodyPartRecord raceBodyPart in raceBodyParts) {
                // this is actively being added, so we check each time
                if (partToPartMapper.ContainsKey(raceBodyPart.def.defName)) continue;

                // Try really hard to only match one vanilla part group
                foreach (PartMatchType matchType in Enum.GetValues(typeof(PartMatchType))) {
                    var partGroupMatched = new Dictionary<string, bool> {};
                    foreach (string vanillaPartName in vanillaPartNames) {
                        var partMap = partToPartMapper[vanillaPartName];
                        if (matchType != PartMatchType.Tags) {
                            // Fuzzy string match
                            partGroupMatched.Add(
                                vanillaPartName,
                                partMap.StaticPartGroup.Any( fuzzyPartName => fuzzyPartName == (
                                    matchType == PartMatchType.BodyPartRecord ? SimplifyBodyPartLabel(raceBodyPart            ) :
                                    matchType == PartMatchType.BodyPartDef    ? SimplifyBodyPartLabel(raceBodyPart.def        ) :
                                    matchType == PartMatchType.DefName        ? SimplifyBodyPartLabel(raceBodyPart.def.defName) :
                                    matchType == PartMatchType.LabelShort     ? SimplifyBodyPartLabel(raceBodyPart.LabelShort ) :
                                    matchType == PartMatchType.Label          ? SimplifyBodyPartLabel(raceBodyPart.Label      ) :
                                    ""  // ??? Forgot to add a partMatchType?
                                ) )
                            );
                        }
                        else {
                            // Match against BodyPartDef.tags (must be all of them; checked last)
                            BodyPartDef vanillaPart = partMap.KeyedPart;
                            BodyPartDef raceBPD     = raceBodyPart.def;

                            /* Some of these tag matches can get dicey with weird creature parts, eg: SnakeBody (with
                             * MovingLimbCore, like a Leg) or SnakeHead (with HearingSource, like an Ear).  InsectLeg
                             * is another problem area, as are the numerous Moving/ManipulationLimbCore tags.
                             * 
                             * So, we limit the range to just vital parts for now.
                             */

                            partGroupMatched.Add(
                                vanillaPartName,
                                raceBPD.tags.Count > 0 &&
                                raceBPD.tags.Count == vanillaPart.tags.Count &&
                                raceBPD.tags.All( vanillaPart.tags.Contains ) &&
                                // (past this point, we know raceBPD.tags == vanillaPart.tags, so we can just reference the former
                                // from now on)
                                raceBPD.tags.Any( bptd => bptd.vital )
                            );
                        }
                    }

                    // Only stop to add if there's a conclusive singular part matched
                    if ( vanillaPartNames.Count(k => partGroupMatched[k]) == 1 ) {
                        string vanillaPartName     = partGroupMatched.Keys.First(k => partGroupMatched[k]);
                        BodyPartDef vanillaPartDef = partToPartMapper[vanillaPartName].KeyedPart;
                        BodyPartDef racePartDef    = raceBodyPart.def;
                        string racePartName        = racePartDef.defName;

                        // Debugging
                        if (Base.IsDebug && debugVanillaPartName != null && debugVanillaPartName == vanillaPartName) XP.ModLogger.Message(
                            "      {0} -> {1}: PartMatchType = {2}",
                            vanillaPartName, racePartName, matchType.ToString()
                        );

                        // Add to both sides
                        partToPartMapper[vanillaPartName].PartDefSet.Add(racePartDef);
                        partToPartMapper.SetOrAddNested(
                            key:         racePartName,
                            constructor: ()   => new(vanillaPartDef, isVanilla: false),
                            appender:    ptpm => ptpm.PartDefSet.Add(vanillaPartDef)
                        );
                        break;
                    }
                }
            }

            // (Modded) Part-to-part mapping
            // (This is actually fewer combinations than all of the duplicates within
            // surgeryList -> appliedOnFixedBodyParts.)
            Dictionary<string, HashSet<BodyPartDef>> simpleLabelToBPDMapping = new() {};
            foreach (PartMatchType matchType in Enum.GetValues(typeof(PartMatchType))) {
                if (matchType == PartMatchType.Tags) continue;  // unused here

                foreach (BodyPartRecord raceBodyPart in raceBodyParts.Where(
                    bpr => !partToPartMapper.ContainsKey(bpr.def.defName) || !partToPartMapper[bpr.def.defName].IsVanilla
                ) ) {
                    simpleLabelToBPDMapping.SetOrAddNested(
                        key:   SimplifyBodyPartLabel(
                            matchType == PartMatchType.BodyPartRecord ? SimplifyBodyPartLabel(raceBodyPart            ) :
                            matchType == PartMatchType.BodyPartDef    ? SimplifyBodyPartLabel(raceBodyPart.def        ) :
                            matchType == PartMatchType.DefName        ? SimplifyBodyPartLabel(raceBodyPart.def.defName) :
                            matchType == PartMatchType.LabelShort     ? SimplifyBodyPartLabel(raceBodyPart.LabelShort ) :
                            matchType == PartMatchType.Label          ? SimplifyBodyPartLabel(raceBodyPart.Label      ) :
                            ""  // ??? Forgot to add a partMatchType?
                        ),
                        value: raceBodyPart.def
                    );
                }
            }

            foreach (HashSet<BodyPartDef> similarBPDs in simpleLabelToBPDMapping.Values.Where( hs => hs.Count() >= 2 ) ) {
                similarBPDs.
                Do( bpd => partToPartMapper.SetOrAddNested(
                    key:         bpd.defName,
                    constructor: ()   => new(bpd, similarBPDs),
                    appender:    ptpm => ptpm.PartDefSet.AddRange(similarBPDs)
                ) );
            }

            // Mark these as processed
            processedParts.AddRange(raceBodyParts);

            // Debugging
            if (Base.IsDebug && debugVanillaPartName != null) XP.ModLogger.Message(
                "      {0}: StaticParts = {1}, PartDefSet = {2}",
                debugVanillaPartName,
                string.Join(", ", partToPartMapper[debugVanillaPartName].StaticPartGroup),
                string.Join(", ", partToPartMapper[debugVanillaPartName].PartDefSet)
            );

            return raceBodyParts.Count;
        }

        public static void MatchSurgeriesToParts (List<RecipeDef> surgeryList, List<ThingDef> pawnList) {
            // There are only a few pawn bio-types, so compile all of the pawn surgery lists
            var pawnSurgeriesByBioType = new Dictionary<XPBioType, HashSet<RecipeDef>> {};
            foreach (ThingDef pawn in pawnList.Where(p => p.recipes != null)) {
                XPBioType pawnBioType = Helpers.GetPawnBioType(pawn);
                pawnSurgeriesByBioType.SetOrAddNestedRange(pawnBioType, pawn.recipes);
            }

            // Add to every usable bio-type combination
            List<XPBioType> xpBioTypes = Enum.GetValues(typeof(XPBioType)).OfType<XPBioType>().ToList();
            foreach (XPBioType comboBioType in xpBioTypes.Where( pbt => X86.Popcnt.popcnt_u32((uint)pbt) > 1 ) ) {  // combo flags only
                if (pawnSurgeriesByBioType.ContainsKey(comboBioType)) continue;
                pawnSurgeriesByBioType[comboBioType] =
                    xpBioTypes.
                    Where     ( pbt => X86.Popcnt.popcnt_u32((uint)pbt) == 1 ).  // single bits only
                    Where     ( sbt => comboBioType.HasFlag(sbt) && pawnSurgeriesByBioType.ContainsKey(sbt) ).
                    SelectMany( sbt => pawnSurgeriesByBioType[sbt] ).
                    ToHashSet()
                ;
            }

            // Surgery-to-part mapping
            foreach (RecipeDef surgery in surgeryList) {
                XPBioType surgeryBioType    = Helpers.GetSurgeryBioType(surgery);
                string    surgeryLabelLower = surgery.label.ToLower();
                bool      defnameDebug      = Base.IsDebug && debugSurgeryDefName != null && surgery.defName == debugSurgeryDefName;

                if (defnameDebug) XP.ModLogger.Message(
                    "      {0}: BioType = {1}, is in BioType cache = {2}",
                    debugSurgeryDefName, surgeryBioType, pawnSurgeriesByBioType.ContainsKey(surgeryBioType).ToStringYesNo()
                );
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

                if (defnameDebug) XP.ModLogger.Message("      {0}: pawnSurgeryBodyParts = {1}", debugSurgeryDefName, string.Join(", ", pawnSurgeryBodyParts) );
                if (pawnSurgeryBodyParts.Count == 0) continue;

                /* If this list is crossing a bunch of our static part group boundaries, we should skip it.
                 * RoM's Druid Regrowth recipe is one such example that tends to pollute the whole bunch.
                 */
                int partGroupMatches = vanillaPartNames.Count(k =>
                    partToPartMapper[k].PartDefSet.Overlaps(pawnSurgeryBodyParts) || partToPartMapper[k].PartDefSet.Overlaps(surgery.appliedOnFixedBodyParts)
                );
                if (defnameDebug) XP.ModLogger.Message("      {0}: partGroupMatches = {1}", debugSurgeryDefName, partGroupMatches );
                if (partGroupMatches >= 2) continue;

                // Look for matching surgery labels, and map them to similar body parts
                bool warnedAboutLargeSet = false;
                foreach (BodyPartDef surgeryBodyPart in surgery.appliedOnFixedBodyParts) {
                    string sbpDefName = surgeryBodyPart.defName;
                    partToPartMapper.NewIfNoKey(sbpDefName, constructor: () => new(surgeryBodyPart, isVanilla: false) );

                    // Useful to warn when it's about to add a bunch of parts into a recipe at one time
                    HashSet<BodyPartDef> diff = pawnSurgeryBodyParts.Except(partToPartMapper[sbpDefName].PartDefSet).ToHashSet();
                    if (diff.Count() > 10 && !warnedAboutLargeSet) {
                        XP.ModLogger.Warning(
                            "Mapping a large set of body parts from \"{0}\":\nSurgery parts: {1}\nCurrent mapper parts: {2} ==> {3}\nNew mapper parts: {4}",
                            surgery.LabelCap,
                            string.Join(", ", surgery.appliedOnFixedBodyParts.Select(bpd => bpd.defName)),
                            sbpDefName, string.Join(", ", partToPartMapper[sbpDefName].PartDefSet.Select(bpd => bpd.defName)),
                            string.Join(", ", diff.Select(bpd => bpd.defName))
                        );
                        warnedAboutLargeSet = true;
                    }
                    
                    bool sbpDebug = Base.IsDebug && debugVanillaPartName != null && (
                        sbpDefName == debugVanillaPartName || pawnSurgeryBodyParts.Any( bpd => bpd.defName == debugVanillaPartName )
                    );

                    // Debugging
                    if (diff.Count() >= 1 && (defnameDebug || sbpDebug)) {
                        XP.ModLogger.Message(
                            "       {0} <- {1}:\n" +
                            "         Current mapper parts: {2} ==> {3}\n" +
                            "         New mapper parts: {4}",
                            surgery.LabelCap, sbpDefName,
                            string.Join(", ", surgery.appliedOnFixedBodyParts.Select(bpd => bpd.defName)),
                            sbpDefName, string.Join(", ", partToPartMapper[sbpDefName].PartDefSet.Select(bpd => bpd.defName)),
                            string.Join(", ", diff.Select(bpd => bpd.defName))
                        );
                    };

                    partToPartMapper[sbpDefName].PartDefSet.AddRange(
                        pawnSurgeryBodyParts.Where(bp => bp != surgeryBodyPart && bp.defName != sbpDefName)
                    );
                }
            }
        }

        public static Dictionary<string, HashSet<BodyPartDef>> GetFilteredPartMapper (List<BodyPartRecord> bodyPartRecords) {
            // create a slightly deeper clone (new Dictionary, new HashSet inside)
            Dictionary<string, HashSet<BodyPartDef>> filteredMapper = partToPartMapper.ToDictionary(
                keySelector:     kvp => kvp.Key,
                elementSelector: kvp => new HashSet<BodyPartDef> (kvp.Value.PartDefSet)
            );

            // filter it down
            HashSet<BodyPartDef> bodyPartDefs = bodyPartRecords.Select( bpr => bpr.def ).ToHashSet();
            filteredMapper.Values.Do( hs => hs.IntersectWith(bodyPartDefs) );
            filteredMapper.RemoveAll( kvp => kvp.Value.Count <= 1 );

            return filteredMapper;
        }
    }
}
