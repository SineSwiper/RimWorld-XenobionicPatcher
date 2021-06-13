using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Verse;

namespace XenobionicPatcher {
    public class ExtraSurgeryStats {
        static StatCategoryDef category;

        public static IEnumerable<StatDrawEntry> SpecialDisplayStats(RecipeDef surgery, StatRequest req) {
            category = DefDatabase<StatCategoryDef>.GetNamed("Surgery");

            // FIXME: Translate all the strings

            yield return SurgeryCategoryStat(surgery);

            yield return new StatDrawEntry(
                category:    category,
                label:       "Anesthetize",
                reportText:  "Whether this surgery requires anesthetic.",
                valueString: surgery.anesthetize.ToStringYesNo(),
                hyperlinks:  new[] { new Dialog_InfoCard.Hyperlink(HediffDefOf.Anesthetic) },
                displayPriorityWithinCategory: 4950
            );

            if (!surgery.hideBodyPartNames)                        yield return AffectedBodyPartsStat(surgery);
            if (!surgery.incompatibleWithHediffTags.NullOrEmpty()) yield return IncompatibleWithHediffTagsStat(surgery);
                
            if (surgery.addsHediff != null) yield return new StatDrawEntry(
                category:    category,
                label:       "Adds health condition",
                reportText:  "The health condition this surgery adds when complete.",
                valueString: surgery.addsHediff.LabelCap,
                hyperlinks:  new[] { new Dialog_InfoCard.Hyperlink(surgery.addsHediff) },
                displayPriorityWithinCategory: 4859
            );
            if (surgery.removesHediff != null) yield return new StatDrawEntry(
                category:    category,
                label:       "Removes health condition",
                reportText:  "The health condition this surgery removes when complete.",
                valueString: surgery.removesHediff.LabelCap,
                hyperlinks:  new[] { new Dialog_InfoCard.Hyperlink(surgery.removesHediff) },
                displayPriorityWithinCategory: 4858
            );
            if (surgery.changesHediffLevel != null) yield return new StatDrawEntry(
                category:    category,
                label:       "Augments health condition",
                reportText:  "The health condition this surgery enhances when complete.",
                valueString: surgery.changesHediffLevel.LabelCap,
                hyperlinks:  new[] { new Dialog_InfoCard.Hyperlink(surgery.changesHediffLevel) },
                displayPriorityWithinCategory: 4857
            );
        }

        public static StatDrawEntry SurgeryCategoryStat (RecipeDef surgery) {
            string workerClassName = surgery.workerClass.Name;
            string categoryLangKey = "Stat_Recipe_SurgeryCategory_" + workerClassName;

            string backupText = Regex.Replace(workerClassName, @"Recipe(Worker)?_", "");
            backupText = GenText.CapitalizeFirst( GenText.SplitCamelCase(backupText).ToLower() );

            /* If the language key doesn't exist, it will try to form an English-friendly type based on the
             * workerClass name.  In this case, the backupText.Translate() will fail and then call
             * PseudoTranslated, which will output the usual Zalgo text in dev mode.  That'll be the hint that a
             * new workerClass needs to be added into language file.  (I would have called PseudoTranslated
             * myself, but it's a private method.)
             */
            string surgeryCategory = categoryLangKey.CanTranslate() ? categoryLangKey.Translate() : backupText.Translate();

            return new StatDrawEntry(
                category:    category,
                label:       "Surgery category",
                reportText:  "The basic type of surgery.",
                valueString: surgeryCategory,
                displayPriorityWithinCategory: 4999
            );
        }

        public static StatDrawEntry AffectedBodyPartsStat (RecipeDef surgery) {
            var bodyParts = new List<BodyPartDef> {};
            string reportText = "";
            string title = "";

            if (surgery.targetsBodyPart) {
                bodyParts  = surgery.appliedOnFixedBodyParts.ListFullCopy();
                reportText = "The body parts this surgery can target.";

                // XXX: Is this slow?
                var sBPG = surgery.appliedOnFixedBodyPartGroups;
                if (!surgery.appliedOnFixedBodyPartGroups.NullOrEmpty()) {
                    bodyParts.AddRange(
                        DefDatabase<BodyDef>.AllDefs.
                        SelectMany( bd => bd.AllParts ).Distinct().
                        Where     ( bpr => sBPG.Any(bpgd => bpr.groups.Contains(bpgd)) ).
                        Select    ( bpr => bpr.def )
                    );
                }
            }

            // Remove duplicates. No, really...
            bodyParts.RemoveDuplicates();
            bodyParts = bodyParts.OrderBy( bpd => bpd.LabelShort ).ToList();

            // Use SimplifyBodyPartLabel to remove dupes, but it's not really suitable for display text, because
            // of certain spelling errors.  So, we'll use the shortest string in each group for that.
            List<string> bodyPartLabels =
                bodyParts.
                Select  ( bpd => GenText.CapitalizeFirst(bpd.LabelShort) ).
                // order first to make sure distinct picks the shortest one
                OrderBy ( s => s.Length ).
                ThenBy  ( s => s ).
                Distinct( BodyPartMatcher.StringEqualityComparer ).
                ToList()
            ;

            if      (bodyPartLabels.Count == 0) {
                if (surgery.targetsBodyPart) {
                    // FIXME: Not really an appropriate translation string
                    title      = "NoDrugUseRequirement".Translate();
                    reportText = "This surgery can target any body part.";
                }
                else {
                    title      = "None".Translate();
                    reportText = "This surgery affects the whole body.";
                }
            }
            else if (bodyPartLabels.Count <= 5) {
                title = string.Join("\n", bodyPartLabels);
            }
            else if (surgery.appliedOnFixedBodyPartGroups.Count > 0 && surgery.appliedOnFixedBodyPartGroups.Count <= 5) {
                title = string.Join("\n", surgery.appliedOnFixedBodyPartGroups.Select(bpd => bpd.LabelShortCap));
            }
            else if (surgery.appliedOnFixedBodyParts.Count > 0 && surgery.appliedOnFixedBodyParts.Count <= 5) {
                title = string.Join("\n", surgery.appliedOnFixedBodyParts.Select(bpd => bpd.LabelShortCap));
            }
            else {
                title = "VariousLabel".Translate();
            }

            var sde = new StatDrawEntry(
                category:    category,
                label:       "Affected body parts",
                reportText:  reportText,
                valueString: title,
                hyperlinks:  bodyParts.Select(bpd => new Dialog_InfoCard.Hyperlink(bpd)),
                displayPriorityWithinCategory: 4875
            );

            return sde;
        }
        public static StatDrawEntry IncompatibleWithHediffTagsStat (RecipeDef surgery) {
            List<HediffDef> incompatibleHediffs = DefDatabase<HediffDef>.AllDefs.Where( hd => !surgery.CompatibleWithHediff(hd) ).ToList();

            string title = "VariousLabel".Translate();
            if (incompatibleHediffs.Count <= 5) {
                title = string.Join("\n", incompatibleHediffs.Select( hd => hd.LabelCap ) );
            }

            return new StatDrawEntry(
                category:    category,
                label:       "Incompatible with",
                reportText:  "Health conditions that are incompatible with this surgery.",
                valueString: title,
                hyperlinks:  incompatibleHediffs.Select(hd => new Dialog_InfoCard.Hyperlink(hd)),
                displayPriorityWithinCategory: 4870
            );
        }

    }
}
