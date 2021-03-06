﻿using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace XenobionicPatcher {
    public class ExtraHediffStats {
        static StatCategoryDef category;

        public static IEnumerable<StatDrawEntry> SpecialDisplayStats(HediffDef hediff, StatRequest req) {
            category = DefDatabase<StatCategoryDef>.GetNamed("Basics");

            yield return HediffCategoryStat(hediff);

            if (hediff.isBad) {
                yield return new StatDrawEntry(
                    category:    category,
                    label:       "Stat_Hediff_Tendable_Name".Translate(),
                    reportText:  "Stat_Hediff_Tendable_Desc".Translate(),
                    valueString: hediff.tendable.ToStringYesNo(),
                    displayPriorityWithinCategory: 4975
                );

                yield return new StatDrawEntry(
                    category:    category,
                    label:       "Stat_Hediff_Immunizable_Name".Translate(),
                    reportText:  "Stat_Hediff_Immunizable_Desc".Translate(),
                    valueString: hediff.PossibleToDevelopImmunityNaturally().ToStringYesNo(),
                    displayPriorityWithinCategory: 4970
                );

                bool canBeLethal = hediff.lethalSeverity > 0 || (hediff.stages != null && hediff.stages.Any( s => s.lifeThreatening ));

                yield return new StatDrawEntry(
                    category:    category,
                    label:       "Stat_Hediff_Lethal_Name".Translate(),
                    reportText:  "Stat_Hediff_Lethal_Desc".Translate(),
                    valueString: canBeLethal.ToStringYesNo(),
                    displayPriorityWithinCategory: 4965
                );
            }

            if (hediff.addedPartProps != null) {
                StatCategoryDef implantCategory = DefDatabase<StatCategoryDef>.GetNamed("Implant");
                var props = hediff.addedPartProps;

                if (hediff.spawnThingOnRemoved != null) yield return new StatDrawEntry(
                    category:    implantCategory,
                    label:       "Stat_Hediff_RelatedPart_Name".Translate(),
                    reportText:  "Stat_Hediff_RelatedPart_Desc".Translate(),
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
                    label:       "Stat_Hediff_BetterThanNatural_Name".Translate(),
                    reportText:  "Stat_Hediff_BetterThanNatural_Desc".Translate(),
                    valueString: props.betterThanNatural.ToStringYesNo(),
                    displayPriorityWithinCategory: 4920
                );
                yield return new StatDrawEntry(
                    category:    implantCategory,
                    label:       "Stat_Hediff_Solid_Name".Translate(),
                    reportText:  "Stat_Hediff_Solid_Desc".Translate(),
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
                    label:       "Stat_Hediff_PriceOffset_Name".Translate(),
                    reportText:  "Stat_Hediff_PriceOffset_Desc".Translate(),
                    valueString: priceOffset.ToStringMoneyOffset(),
                    displayPriorityWithinCategory: 5500
                );
            }
        }

        public static StatDrawEntry HediffCategoryStat (HediffDef hediff) {
            string type = 
                hediff.countsAsAddedPartOrImplant || hediff.addedPartProps != null ? "BodyAugment" :
                hediff.displayWound     ? "Wound" :
                hediff.chronic          ? "ChronicDisease" :
                hediff.makesSickThought ? "Sickness" :
                hediff.isBad            ? "Affliction" :
                "Condition"
            ;
            type = ("Stat_Hediff_HediffType_" + type).Translate();

            return new StatDrawEntry(
                category:    category,
                label:       "Stat_Hediff_HediffType_Name".Translate(),
                reportText:  "Stat_Hediff_HediffType_Desc".Translate(),
                valueString: type,
                displayPriorityWithinCategory: 4999
            );
        }
    }
}
