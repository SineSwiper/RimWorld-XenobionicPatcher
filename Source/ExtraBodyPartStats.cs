using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Verse;

namespace XenobionicPatcher {
    public class ExtraBodyPartStats {
        static StatCategoryDef category;

        public static IEnumerable<StatDrawEntry> SpecialDisplayStats(BodyPartDef bodyPart, StatRequest req) {
            category = DefDatabase<StatCategoryDef>.GetNamed("Basics");

            yield return new StatDrawEntry(
                category:    category,
                label:       "HitPointsBasic".Translate(),
                reportText:  "Stat_HitPoints_Desc".Translate(),
                valueString: bodyPart.hitPoints.ToString(),
                displayPriorityWithinCategory: StatDisplayOrder.HitPointsBasic
            );

            if (bodyPart.spawnThingOnRemoved != null) yield return new StatDrawEntry(
                category:    category,
                label:       "Stat_BodyPart_RemovedPart_Name".Translate(),
                reportText:  "Stat_BodyPart_RemovedPart_Desc".Translate(),
                valueString: bodyPart.spawnThingOnRemoved.LabelCap,
                hyperlinks:  new[] { new Dialog_InfoCard.Hyperlink(bodyPart.spawnThingOnRemoved) },
                displayPriorityWithinCategory: 5000
            );

            string permanentInjuryChanceFactorString = bodyPart.permanentInjuryChanceFactor > 10000 ?
                "Infinite".Translate().ToString() :
                bodyPart.permanentInjuryChanceFactor.ToStringPercent()
            ;

            yield return new StatDrawEntry(
                category:    category,
                label:       "Stat_BodyPart_PermanentInjuryChanceFactor_Name".Translate(),
                reportText:  "Stat_BodyPart_PermanentInjuryChanceFactor_Desc".Translate(),
                valueString: permanentInjuryChanceFactorString,
                displayPriorityWithinCategory: 4890
            );
            yield return new StatDrawEntry(
                category:    category,
                label:       "Stat_BodyPart_BleedRate_Name".Translate(),
                reportText:  "Stat_BodyPart_BleedRate_Desc".Translate(),
                valueString: bodyPart.bleedRate.ToStringPercent(),
                displayPriorityWithinCategory: 4880
            );
            yield return new StatDrawEntry(
                category:    category,
                label:       "Stat_BodyPart_FrostbiteVulnerability_Name".Translate(),
                reportText:  "Stat_BodyPart_FrostbiteVulnerability_Desc".Translate(),
                valueString: bodyPart.frostbiteVulnerability.ToStringPercent(),
                hyperlinks:  new[] { new Dialog_InfoCard.Hyperlink( DefDatabase<HediffDef>.GetNamed("Frostbite") ) },
                displayPriorityWithinCategory: 4870
            );

            yield return new StatDrawEntry(
                category:    category,
                label:       "Stat_BodyPart_Vital_Name".Translate(),
                reportText:  "Stat_BodyPart_Vital_Desc".Translate(),
                valueString: bodyPart.tags.Any( bptd => bptd.vital ).ToStringYesNo(),
                displayPriorityWithinCategory: 4795
            );
            yield return new StatDrawEntry(
                category:    category,
                label:       "Stat_BodyPart_Alive_Name".Translate(),
                reportText:  "Stat_BodyPart_Alive_Desc".Translate(),
                valueString: bodyPart.alive.ToStringYesNo(),
                displayPriorityWithinCategory: 4790
            );
            yield return new StatDrawEntry(
                category:    category,
                label:       "Stat_BodyPart_SkinCovered_Name".Translate(),
                reportText:  "Stat_BodyPart_SkinCovered_Desc".Translate(),
                valueString: bodyPart.IsSkinCoveredInDefinition_Debug.ToStringYesNo(),  // they really don't want you to use this property...
                displayPriorityWithinCategory: 4780
            );
            yield return new StatDrawEntry(
                category:    category,
                label:       "Stat_BodyPart_Solid_Name".Translate(),
                reportText:  "Stat_BodyPart_Solid_Desc".Translate(),
                valueString: bodyPart.IsSolidInDefinition_Debug.ToStringYesNo(),  // they really don't want you to use this property...
                displayPriorityWithinCategory: 4770
            );
            yield return new StatDrawEntry(
                category:    category,
                label:       "Stat_BodyPart_Invulnerable_Name".Translate(),
                reportText:  "Stat_BodyPart_Invulnerable_Desc".Translate(),
                valueString: (!bodyPart.destroyableByDamage).ToStringYesNo(),
                displayPriorityWithinCategory: 4765
            );
            yield return new StatDrawEntry(
                category:    category,
                label:       "Stat_BodyPart_Delicate_Name".Translate(),
                reportText:  "Stat_BodyPart_Delicate_Desc".Translate(),
                valueString: bodyPart.delicate.ToStringYesNo(),
                displayPriorityWithinCategory: 4760
            );
            yield return new StatDrawEntry(
                category:    category,
                label:       "Stat_BodyPart_BeautyRelated_Name".Translate(),
                reportText:  "Stat_BodyPart_BeautyRelated_Desc".Translate(),
                valueString: bodyPart.delicate.ToStringYesNo(),
                displayPriorityWithinCategory: 4750
            );

            yield return new StatDrawEntry(
                category:    category,
                label:       "Stat_BodyPart_Amputatable_Name".Translate(),
                reportText:  "Stat_BodyPart_Amputatable_Desc".Translate(),
                valueString: bodyPart.canSuggestAmputation.ToStringYesNo(),
                displayPriorityWithinCategory: 4690
            );

            string bodyPartProperties = string.Join("\n",
                bodyPart.tags.Select( bptd => {
                    string tagLangKey = "Stat_BodyPart_BodyPartProperties_" + bptd.defName;
                    string backupText = GenText.SplitCamelCase(bptd.defName);

                    // See big comment on SurgeryCategoryStat
                    return tagLangKey.CanTranslate() ? tagLangKey.Translate() : backupText.Translate();
                } )
            );

            if (!bodyPartProperties.NullOrEmpty()) yield return new StatDrawEntry(
                category:    category,
                label:       "Stat_BodyPart_BodyPartProperties_Name".Translate(),
                reportText:  "Stat_BodyPart_BodyPartProperties_Desc".Translate(),
                valueString: bodyPartProperties,
                displayPriorityWithinCategory: 4500
            );

            List<Dialog_InfoCard.Hyperlink> bodyPartUsersHyperlinks =
                DefDatabase<PawnKindDef>.AllDefs.
                Where ( pkd  => pkd.race?.race != null ).
                Select( pkd  => pkd.race ).Distinct().
                Where ( race => race.race.body.AllParts.Any( bpr => bpr.def == bodyPart ) ).
                Select( race => new Dialog_InfoCard.Hyperlink(race) ).
                ToList()
            ;

            yield return new StatDrawEntry(
                category:    category,
                label:       "Stat_BodyPart_BodyPartUsers_Name".Translate(),
                reportText:  "Stat_BodyPart_BodyPartUsers_Desc".Translate(),
                valueString: "VariousLabel".Translate(),
                hyperlinks:  bodyPartUsersHyperlinks,
                displayPriorityWithinCategory: 4200
            );

        }
    }
}
