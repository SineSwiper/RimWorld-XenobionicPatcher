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

            // FIXME: Translate all the strings

            yield return new StatDrawEntry(
                category:    category,
                label:       "HitPointsBasic".Translate(),
                reportText:  "Stat_HitPoints_Desc".Translate(),
                valueString: bodyPart.hitPoints.ToString(),
                displayPriorityWithinCategory: StatDisplayOrder.HitPointsBasic
            );

            // XXX: Is this too far into the guts?
            if (bodyPart.spawnThingOnRemoved != null) yield return new StatDrawEntry(
                category:    category,
                label:       "Related thing",
                reportText:  "The external thing related to this body part.",
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
                label:       "Permanent injury chance factor",
                reportText:  "A multiplier on the chance this part will acquire a permanent injury when damaged. This is also impacted by the kind of injury and whether it's a delicate part or not.",
                valueString: permanentInjuryChanceFactorString,
                displayPriorityWithinCategory: 4890
            );
            yield return new StatDrawEntry(
                category:    category,
                label:       "Bleed rate",
                reportText:  "A multiplier applied to the rate of blood loss on this body part.",
                valueString: bodyPart.bleedRate.ToStringPercent(),
                displayPriorityWithinCategory: 4880
            );
            yield return new StatDrawEntry(
                category:    category,
                label:       "Frostbite vulnerability",
                reportText:  "The chance that this part succumbs to frostbite in freezing temperatures.",
                valueString: bodyPart.frostbiteVulnerability.ToStringPercent(),
                hyperlinks:  new[] { new Dialog_InfoCard.Hyperlink( DefDatabase<HediffDef>.GetNamed("Frostbite") ) },
                displayPriorityWithinCategory: 4870
            );

            yield return new StatDrawEntry(
                category:    category,
                label:       "Vital",
                reportText:  "Whether this body part is vital to the mortality of the creature.",
                valueString: bodyPart.tags.Any( bptd => bptd.vital ).ToStringYesNo(),
                displayPriorityWithinCategory: 4795
            );
            yield return new StatDrawEntry(
                category:    category,
                label:       "Alive",
                reportText:  "Whether this body part is alive. Living parts may be subject to certain health conditions.",
                valueString: bodyPart.alive.ToStringYesNo(),
                displayPriorityWithinCategory: 4790
            );
            yield return new StatDrawEntry(
                category:    category,
                label:       "Skin-covered",
                reportText:  "Whether this body part is covered in skin. Skin-covered parts provide (very) light protection against damage. These properties may change when replaced with other parts, such as bionics.",
                valueString: bodyPart.IsSkinCoveredInDefinition_Debug.ToStringYesNo(),  // they really don't want you to use this property...
                displayPriorityWithinCategory: 4780
            );
            yield return new StatDrawEntry(
                category:    category,
                label:       "Solid",
                reportText:  "Whether this body part is solid. Solid parts provide a much higher protection against damage. These properties may change when replaced with other parts, such as bionics.",
                valueString: bodyPart.IsSolidInDefinition_Debug.ToStringYesNo(),  // they really don't want you to use this property...
                displayPriorityWithinCategory: 4770
            );
            yield return new StatDrawEntry(
                category:    category,
                label:       "Invulnerable",
                reportText:  "Whether this body part is invulnerable to damage.",
                valueString: (!bodyPart.destroyableByDamage).ToStringYesNo(),
                displayPriorityWithinCategory: 4765
            );
            yield return new StatDrawEntry(
                category:    category,
                label:       "Delicate",
                reportText:  "Whether this body part is delicate. Delicate parts have a higher risk of permanent injury.",
                valueString: bodyPart.delicate.ToStringYesNo(),
                displayPriorityWithinCategory: 4760
            );
            yield return new StatDrawEntry(
                category:    category,
                label:       "Beauty-related",
                reportText:  "Whether this body part is a factor of the creature's beauty. These parts may impact relations if they are injured or destroyed.",
                valueString: bodyPart.delicate.ToStringYesNo(),
                displayPriorityWithinCategory: 4750
            );

            yield return new StatDrawEntry(
                category:    category,
                label:       "Amputatable",
                reportText:  "Whether this body part can be amputated to save a patient from an infection.",
                valueString: bodyPart.canSuggestAmputation.ToStringYesNo(),
                displayPriorityWithinCategory: 4690
            );

            // FIXME: Tie to a language key list
            string bodyPartProperties = string.Join("\n",
                bodyPart.tags.Select( bptd => GenText.CapitalizeFirst( GenText.SplitCamelCase(bptd.defName).ToLower() ) )
            );

            yield return new StatDrawEntry(
                category:    category,
                label:       "Body part properties",
                reportText:  "Essential properties that are tied to the body part.",
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
                label:       "Contained in",
                reportText:  "Creature bodies that use this body part.",
                valueString: "VariousLabel".Translate(),
                hyperlinks:  bodyPartUsersHyperlinks,
                displayPriorityWithinCategory: 4200
            );

        }
    }
}
