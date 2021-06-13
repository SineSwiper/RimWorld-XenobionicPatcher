using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace XenobionicPatcher {
    public class ExtraHediffStats {
        static StatCategoryDef category;

        public static IEnumerable<StatDrawEntry> SpecialDisplayStats(HediffDef hediff, StatRequest req) {
            category = DefDatabase<StatCategoryDef>.GetNamed("Basics");

            // FIXME: Translate all the strings

            yield return HediffCategoryStat(hediff);

            if (hediff.isBad) {
                yield return new StatDrawEntry(
                    category:    category,
                    label:       "Tendable",
                    reportText:  "Whether this condition can be cured with medicine.",
                    valueString: hediff.tendable.ToStringYesNo(),
                    displayPriorityWithinCategory: 4975
                );

                yield return new StatDrawEntry(
                    category:    category,
                    label:       "Immunizable",
                    reportText:  "Whether a pawn can naturally develop an immunity to this condition.",
                    valueString: hediff.PossibleToDevelopImmunityNaturally().ToStringYesNo(),
                    displayPriorityWithinCategory: 4970
                );

                bool canBeLethal = hediff.lethalSeverity > 0 || (hediff.stages != null && hediff.stages.Any( s => s.lifeThreatening ));

                yield return new StatDrawEntry(
                    category:    category,
                    label:       "Potentially lethal",
                    reportText:  "Whether this condition has a chance to be fatal.",
                    valueString: canBeLethal.ToStringYesNo(),
                    displayPriorityWithinCategory: 4965
                );
            }

            if (hediff.addedPartProps != null) {
                StatCategoryDef implantCategory = DefDatabase<StatCategoryDef>.GetNamed("Implant");
                var props = hediff.addedPartProps;

                yield return new StatDrawEntry(
                    category:    implantCategory,
                    label:       "Related part",
                    reportText:  "The body augment installed as part of this health condition.",
                    valueString: hediff.spawnThingOnRemoved.LabelCap,
                    hyperlinks:  new[] { new Dialog_InfoCard.Hyperlink(hediff.spawnThingOnRemoved) },
                    displayPriorityWithinCategory: 5000
                );
                yield return new StatDrawEntry(
                    category:    implantCategory,
                    label:       "PartEfficiency".Translate(),
                    reportText:  "Stat_Thing_BodyPartEfficiency_Desc".Translate(),
                    valueString: props.partEfficiency.ToStringPercent(),
                    displayPriorityWithinCategory: 4950
                );
                yield return new StatDrawEntry(
                    category:    implantCategory,
                    label:       "Better than natural",
                    reportText:  "Whether this augment is better than a natural part.",
                    valueString: props.betterThanNatural.ToStringYesNo(),
                    displayPriorityWithinCategory: 4920
                );
                yield return new StatDrawEntry(
                    category:    implantCategory,
                    label:       "Solid",
                    reportText:  "Whether this augment is solid enough to resist certain types of damage.",
                    valueString: props.solid.ToStringYesNo(),
                    displayPriorityWithinCategory: 4900
                );
            }

            if (hediff.priceImpact || hediff.priceOffset != 0) {
                float priceOffset = hediff.priceOffset;
                if (priceOffset == 0 && hediff.spawnThingOnRemoved != null)
                    priceOffset = hediff.spawnThingOnRemoved.BaseMarketValue;
                
                if (priceOffset >= 1.0 || priceOffset <= -1.0) yield return new StatDrawEntry(
                    category:    category,
                    label:       "Price offset",
                    reportText:  "How much this adds to a creature's market value.",
                    valueString: priceOffset.ToStringMoneyOffset(),
                    displayPriorityWithinCategory: 5500
                );
            }
        }

        public static StatDrawEntry HediffCategoryStat (HediffDef hediff) {
            string type = 
                hediff.countsAsAddedPartOrImplant || hediff.addedPartProps != null ? "Body Augment" :
                hediff.displayWound     ? "Wound" :
                hediff.chronic          ? "Chronic Disease" :
                hediff.makesSickThought ? "Sickness" :
                hediff.isBad            ? "Affliction" :
                "Condition"
            ;

            return new StatDrawEntry(
                category:    category,
                label:       "Health condition type",
                reportText:  "The basic type of health condition.",
                valueString: type,
                displayPriorityWithinCategory: 4999
            );
        }
    }
}
