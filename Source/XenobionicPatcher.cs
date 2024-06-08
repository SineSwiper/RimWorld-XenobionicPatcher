using RimWorld;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using HarmonyLib;
using HugsLib;
using HugsLib.Settings;
using Verse;
using System.Reflection;

namespace XenobionicPatcher {
    public enum XenoPatchType : ushort {
        None    = 0,
        Strict  = 1,
        Relaxed = 2,
        Loose   = 3,
    };

    internal readonly struct PatchLoopDetails {
        internal string                               ConfigName   { get; }
        internal string[]                             MsgSubs      { get; }
        internal Dictionary<XenoPatchType, XPBioType> SurgeryTypes { get; }
        internal Dictionary<XenoPatchType, XPBioType>    PawnTypes { get; }

        internal PatchLoopDetails(string configName, string[] msgSubs, Dictionary<XenoPatchType, XPBioType> surgeryTypes, Dictionary<XenoPatchType, XPBioType> pawnTypes) {
            ConfigName   = configName;
            MsgSubs      = msgSubs;
            SurgeryTypes = surgeryTypes;
            PawnTypes    = pawnTypes;
        }
        internal PatchLoopDetails(string configName, string[] msgSubs, Dictionary<XenoPatchType, XPBioType> surgeryTypes, XPBioType pawnType) {
            ConfigName   = configName;
            MsgSubs      = msgSubs;
            SurgeryTypes = surgeryTypes;
            PawnTypes    = new() {
                {XenoPatchType.Strict,  pawnType},
                {XenoPatchType.Relaxed, pawnType},
                {XenoPatchType.Loose,   pawnType},
            };
        }
        internal PatchLoopDetails(string configName, string[] msgSubs, XPBioType surgeryType, XPBioType pawnType) {
            ConfigName   = configName;
            MsgSubs      = msgSubs;
            SurgeryTypes = new() {
                {XenoPatchType.Strict,  surgeryType},
                {XenoPatchType.Relaxed, surgeryType},
                {XenoPatchType.Loose,   surgeryType},
            };
            PawnTypes    = new() {
                {XenoPatchType.Strict,  pawnType},
                {XenoPatchType.Relaxed, pawnType},
                {XenoPatchType.Loose,   pawnType},
            };
        }
    }

    [StaticConstructorOnStartup]
    public class Base : ModBase {
        public override string ModIdentifier {
            get { return "XenobionicPatcher"; }
        }
        public static Base         Instance    { get; private set; }
        public static DefInjectors DefInjector { get; private set; }
        public static bool IsDebug             { get; private set; }

        internal HugsLib.Utils.ModLogger ModLogger { get; private set; }

        public Base() {
            Instance    = this;
            DefInjector = new DefInjectors();
            ModLogger   = this.Logger;
            IsDebug     = false;
        }

        internal Dictionary<string, SettingHandle> config          = new();
        internal Dictionary<string, bool>          boolConfigCache = new();
        internal Dictionary<string, XenoPatchType> xptConfigCache  = new();

        internal List<Type> surgeryWorkerClassesFilter = new();

        public override void DefsLoaded() {
            /* NOTE: Okay, I guess this is what it takes to unfuck the arms race that VREA created.  I have to _unpatch_ a method that VREA
             * patched as a Postfix because Team Oskar declared this override to always act very very last with a HarmonyPriority(-2147483648).
             * Well, sorry, but with modding, there are *always* options, except now with priorities like this, it makes VREA developers look
             * hostile towards other modders.  I'm just trying to fix bugs that my users are reporting, because they don't like the limitations
             * VREA created.
             * 
             * This all could have been avoided if the VREA developers understood that if a surgery isn't installed into pawn.def.recipes or 
             * surgery.recipeUsers, it won't end up on the list, and if it does end up on one of those list by another mod, it's there because
             * *the user wanted it!*  There's nothing "vanilla" about this sort of behavior.
             */
            if (Helpers.SafeTypeByName("VREAndroids.RecipeDef_AvailableNow_Patch") != null) {
                MethodInfo originalMethod = AccessTools.PropertyGetter(typeof(RecipeDef), "AvailableNow");
                Instance.HarmonyInst.Unpatch(originalMethod, HarmonyPatchType.Postfix, "VREAndroidsMod");
                Logger.Message("Unpatched VREAndroids Postfix on {0}.{1}", originalMethod.ReflectedType.FullName, originalMethod.Name);
            }

            ProcessSettings();

            // Set the debug flag
            IsDebug = boolConfigCache["MoreDebug"];

            // Curate the surgery worker class list before building allSurgeryDefs
            Dictionary<string, Type[]> searchConfigMapper = new() {
                { "Adminster",                 new[] { typeof(Recipe_AdministerIngestible), typeof(Recipe_AdministerUsableItem) } },
                { "InstallNaturalBodyPart",    new[] { typeof(Recipe_InstallNaturalBodyPart)    } },
                { "InstallArtificialBodyPart", new[] { typeof(Recipe_InstallArtificialBodyPart) } },
                { "InstallImplant",            new[] { typeof(Recipe_InstallImplant), typeof(Recipe_ChangeImplantLevel) } },
                { "SimpleCondition",           new[] { typeof(Recipe_AddHediff) } },
                { "VanillaRemoval",            new[] { typeof(Recipe_RemoveHediff), typeof(Recipe_RemoveBodyPart), typeof(Recipe_RemoveImplant) } },
            };
            if (ModsConfig.BiotechActive) searchConfigMapper.AddRange( new Dictionary<string, Type[]>() {
                { "BloodWork",                 new[] { typeof(Recipe_ExtractHemogen), typeof(Recipe_BloodTransfusion) } },
                // Recipe_ImplantIUD / Recipe_ExtractOvum are subclasses of Recipe_AddHediff, so either of these checks will enable them
                { "XenogermPregnancy",         new[] { typeof(Recipe_ImplantXenogerm), typeof(Recipe_TerminatePregnancy), typeof(Recipe_ImplantEmbryo), typeof(Recipe_ImplantIUD), typeof(Recipe_ExtractOvum) } },
            });
            if (ModsConfig.AnomalyActive) searchConfigMapper.AddRange( new Dictionary<string, Type[]>() {
                { "GhoulInfusion",             new[] { typeof(Recipe_GhoulInfusion) } },
                { "SurgicalInspection",        new[] { typeof(Recipe_SurgicalInspection) } }, 
            });
            foreach (string cName in searchConfigMapper.Keys) {
                if (boolConfigCache["Search" + cName + "Recipes"] && searchConfigMapper.ContainsKey(cName)) surgeryWorkerClassesFilter.AddRange( searchConfigMapper[cName] );
            }

            // Add additional search types for modded surgery classes
            if (boolConfigCache["SearchModdedSurgeryClasses"]) {
                List<string> moddedWorkerClassNames = new() {
                    // (EPOE doesn't have any custom worker classes)

                    // EPOE Forked
                    "EPIA.Recipe_RemoveImplant",
                    "EPIA.Recipe_RemoveScarHediff",
                    "EPIA.Recipe_RemoveBrainScarHediff",
                    
                    // Rah's Bionics and Surgery Expansion
                    "ScarRemoving.Recipe_RemoveHediff_noBrain",
                
                    // Medical Surgery Expansion (retired in v1.3, in favor of MSE2)
                    "OrenoMSE.Recipe_InstallBodyPartModule",
                    "OrenoMSE.Recipe_InstallImplantSystem",
                    "OrenoMSE.Recipe_RemoveImplantSystem",
                    "OrenoMSE.Recipe_RemoveBodyPartSystem",

                    // Medical Surgery Expansion 2.0
                    "MSE2.Recipe_InstallModule",
                    "MSE2.Recipe_RemoveModules",
                    "MSE2.Recipe_InstallNaturalBodyPartWithChildren",
                    "MSE2.Recipe_InstallArtificialBodyPartWithChildren",
                    "MSE2.Surgey_MakeShiftRepair",
                    "MSE2.Surgery_MakeShiftRepair",  // maybe they'll fix the typo someday...

                    // Cyber Fauna
                    "SurgeryCF_Simple",
                    "SurgeryCF_Bionic",
                    "SurgeryCF_Archo",
                    "SurgeryCF_Battle",

                    // Chj's Androids
                    "Androids.Recipe_Disassemble",
                    "Androids.Recipe_RepairKit",

                    // Alpha Animals/Mythology
                    // (There's enough "execute" type surgeries to go around...)
                    //"AlphaBehavioursAndEvents.Recipe_ShutDown",
                    //"AnimalBehaviours.Recipe_ShutDown",

                    // Android Tiers
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
                    // Not included: MOARANDROIDS.Recipe_PaintAndroidFramework[color]
                    
                    // Alien vs. Predator
                    "RRYautja.Recipe_Remove_Gauntlet",
                    "RRYautja.Recipe_RemoveHugger",

                    // Diseases+
                    "Diseases.RecipeWorker_Lobotomy",

                    // Immortals
                    "Immortals.Recipe_InstallFakeEye",

                    // Questionable Ethics
                    "QEthics.RecipeWorker_CreateBrainScan",
                    "QEthics.RecipeWorker_GenomeSequencing",
                    "QEthics.RecipeWorker_InstallNaturalBodyPart",
                    "QEthics.RecipeWorker_NerveStapling",

                    // Harpies
                    "SyrHarpy.Recipe_InstallPart",
                    "SyrHarpy.Recipe_ChangeLightningAmplifier",

                    // Scar Removal Plus
                    "SyrScarRemoval.Recipe_BodyPartRegrowth",
                    "SyrScarRemoval.Recipe_ScarRemoval",
                    "SyrScarRemoval.Recipe_ScarRemovalBrain",

                    // A RimWorld of Magic
                    "TorannMagic.Recipe_RegrowBodyPart",
                    "TorannMagic.Recipe_RegrowUniversalBodyPart",
                    "TorannMagic.Recipe_RuneCarving",

                    // PolarisBloc
                    "Polarisbloc.Recipe_MakeCartridgeSurgery",
                    "Polarisbloc.Recipe_InstallCombatChip",
                    "Polarisbloc.Recipe_RemoveHediffIsOld",
                    "Polarisbloc.Recipe_RestoreMissingPart",
                    "Polarisbloc.Recipe_RemoveImplant",
                    "Polarisbloc.Recipe_TransgenderSurgery",
                    "Polarisbloc.Recipe_SurgeryChangeBioAge",
                    "Polarisbloc.Recipe_ExtractAbility",

                    // What the Hack
                    "WhatTheHack.Recipes.Recipe_ExtractBrainData",
                    // The rest of them are really only for mechanoids

                    // CyberNet
                    "CyberNet.Recipe_InstallCyberNetBrainImplant",

                    // Cybernetic Organism and Neural Network
                    "CONN.Recipe_InstallArtificialBodyPartAndClearPawnFromCache",

                    // Vanilla Genetics Expanded (aka Genetic Rim)
                    "GeneticRim.Recipe_InstallGeneticBodyPart",

                    // Vanilla Factions Expanded: Insectoids
                    "VFEI.Other.Recipe_AddMutationHediff",

                    // Vanilla Factions Expanded: Pirates

                    // NOTE: This bugs out on VFEPirates.RecipeWorker_WarcasketRemoval.AvailableOnNow, because of the
                    // lack of pawn.apparel on animals.  Besides, we have no control over warcasket conversion, since
                    // it's not surgery, so there's little point to spreading around the removal surgery.
                    //"VFEPirates.RecipeWorker_WarcasketRemoval",

                    // Vanilla Races Expanded: Androids
                    "VREAndroids.Recipe_InstallAndroidPart",
                    "VREAndroids.Recipe_InstallReactor",
                    //"VREAndroids.Recipe_RemoveArtificialBodyPart",  // Non-androids can use the standard Recipe_RemoveBodyPart

                    // Deathrattle
                    "DeathRattle.Recipe_AdministerComaDrug",
                };

                // If they also enabled BloodWork, we'll allow this
                if (boolConfigCache["SearchBloodWorkRecipes"]) moddedWorkerClassNames.AddRange( new List<string>() {
                    // Rim of Madness: Vampires
                    "Vampire.Recipe_ExtractBloodVial",
                    "Vampire.Recipe_ExtractBloodPack",
                    "Vampire.Recipe_ExtractBloodWine",
                    "Vampire.Recipe_TransferBlood",
                });

                foreach (string workerName in moddedWorkerClassNames) {
                    Type worker = Helpers.SafeTypeByName(workerName);

                    if (worker != null) surgeryWorkerClassesFilter.Add(worker);
                }
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            string beforeMsg = "Injecting {0} surgical recipes into {1}";
            string  afterMsg = "Injected {0} surgical recipes into {1} (took {2:F4}s; {3:N0} combinations)";
            
            // Start with a few global lists
            List<ThingDef> allPawnDefs = DefDatabase<ThingDef>.AllDefs.Where(
                thing => Helpers.IsSupertypeOf(typeof(Pawn), thing.thingClass)
            ).ToList();
            List<RecipeDef> allSurgeryDefs = DefDatabase<RecipeDef>.AllDefs.Where(
                recipe => recipe.IsSurgery && surgeryWorkerClassesFilter.Any( t => Helpers.IsSupertypeOf(t, recipe.workerClass) )
            ).ToList();

            BodyPartMatcher.Initialize();

            // Because we use pawn.recipes so often for surgery checks, and not the other side (surgery.recipeUsers),
            // merge the latter into the former.  Our new additions will be sure to add it in both sides to keep
            // pawn.recipes complete.
            stopwatch.Start();
            foreach (ThingDef pawn in allPawnDefs) {
                pawn.recipes ??= new List<RecipeDef> {};
                pawn.recipes.AddRange(
                    allSurgeryDefs.Where(s => s.recipeUsers != null && s.recipeUsers.Contains(pawn))
                );
                pawn.recipes.RemoveDuplicates();
            }

            // Pre-caching
            allSurgeryDefs.ForEach(s => Helpers.GetSurgeryBioType(s));
            allPawnDefs   .ForEach(p => Helpers.GetPawnBioType   (p));
            stopwatch.Stop();

            Logger.Message("Prep work / pre-caching (took {0:F4}s; {1:N0} defs)", stopwatch.ElapsedMilliseconds / 1000f, allSurgeryDefs.Count() + allPawnDefs.Count());
            stopwatch.Reset();

            PatchLoopDetails[] patchLoopDetails = new[] {
                new PatchLoopDetails(
                    configName:   "PatchAnimalToAnimal",
                    msgSubs:      new[] { "animal", "other animals" },
                    surgeryTypes: new() {
                        {XenoPatchType.Strict,  XPBioType.Animal},
                        {XenoPatchType.Relaxed, XPBioType.Critterlike},
                        {XenoPatchType.Loose,   XPBioType.Fleshlike | XPBioType.Other},
                    },
                    pawnType:     XPBioType.Animal
                ),
                new PatchLoopDetails(
                    configName:   "PatchHumanlikeToHumanlike",
                    msgSubs:      new[] { "humanlike", "other humanlikes" },
                    surgeryTypes: new() {
                        {XenoPatchType.Strict,  XPBioType.Humanlike},
                        {XenoPatchType.Relaxed, XPBioType.SmartPawn},
                        {XenoPatchType.Loose,   XPBioType.Pawnlike | XPBioType.Other},
                    },
                    pawnType:     XPBioType.Humanlike
                ),
                new PatchLoopDetails(
                    configName:   "PatchArtificialToMech",
                    msgSubs:      new[] { "artificial part", "mechs" },
                    // PatchArtificialToMech has a different way of handling this
                    surgeryType:  XPBioType.All,
                    pawnType:     XPBioType.All
                ),
                new PatchLoopDetails(
                    configName: "PatchAnimalToHumanlike",
                    msgSubs:    new[] { "animal", "humanlikes" },
                    surgeryTypes: new() {
                        {XenoPatchType.Strict,  XPBioType.Animal},
                        {XenoPatchType.Relaxed, XPBioType.Critterlike},
                        {XenoPatchType.Loose,   XPBioType.Fleshlike | XPBioType.Other},
                    },
                    pawnType:     XPBioType.Humanlike
                ),
                new PatchLoopDetails(
                    configName:   "PatchHumanlikeToAnimal",
                    msgSubs:      new[] { "humanlike", "animals" },
                    surgeryTypes: new() {
                        {XenoPatchType.Strict,  XPBioType.Humanlike},
                        {XenoPatchType.Relaxed, XPBioType.SmartPawn},
                        {XenoPatchType.Loose,   XPBioType.Pawnlike | XPBioType.Other},
                    },
                    pawnType:     XPBioType.Animal
                ),
                new PatchLoopDetails(
                    configName:   "PatchFleshlikeToFleshlike",
                    msgSubs:      new[] { "fleshlike", "fleshlikes" },
                    surgeryTypes: new() {
                        {XenoPatchType.Strict,  XPBioType.Fleshlike},
                        {XenoPatchType.Relaxed, XPBioType.Pawnlike},
                        {XenoPatchType.Loose,   XPBioType.Pawnlike | XPBioType.Other},
                    },
                    pawnType:     XPBioType.Fleshlike
                ),
                new PatchLoopDetails(
                    configName:   "PatchHumanlikeToMech",
                    msgSubs:      new[] { "humanlike", "mechs" },
                    surgeryTypes: new() {
                        {XenoPatchType.Strict,  XPBioType.Humanlike},
                        {XenoPatchType.Relaxed, XPBioType.SmartPawn},
                        {XenoPatchType.Loose,   XPBioType.Pawnlike | XPBioType.Other},
                    },
                    pawnType:     XPBioType.Mech
                ),
                new PatchLoopDetails(
                    configName: "PatchMechlikeToHumanlike",
                    msgSubs:    new[] { "mech-like", "humanlikes" },
                    surgeryTypes: new() {
                        {XenoPatchType.Strict,  XPBioType.Mech},
                        {XenoPatchType.Relaxed, XPBioType.SmartPawn},
                        {XenoPatchType.Loose,   XPBioType.SmartPawn | XPBioType.Other},
                    },
                    pawnType:     XPBioType.Humanlike
                ),
                new PatchLoopDetails(
                    configName: "PatchAnyToAny",
                    msgSubs:    new[] { "ALL", "EVERYBODY" },
                    surgeryTypes: new() {
                        {XenoPatchType.Strict,  XPBioType.Pawnlike},
                        {XenoPatchType.Relaxed, XPBioType.Pawnlike | XPBioType.Other},
                        {XenoPatchType.Loose,   XPBioType.All},
                    },
                    pawnTypes: new() {
                        {XenoPatchType.Strict,  XPBioType.Pawnlike},
                        {XenoPatchType.Relaxed, XPBioType.Pawnlike | XPBioType.Other},
                        {XenoPatchType.Loose,   XPBioType.All},
                    }
                ),
            };

            foreach (PatchLoopDetails details in patchLoopDetails) {
                XenoPatchType xpt = xptConfigCache[details.ConfigName];
                if (xpt > 0) {
                    if (IsDebug) Logger.Message(beforeMsg, details.MsgSubs);

                    var surgeryList = allSurgeryDefs.Where(s => details.SurgeryTypes[xpt].HasFlag( Helpers.GetSurgeryBioType(s) ) ).ToList();
                    var    pawnList = allPawnDefs   .Where(p => details.   PawnTypes[xpt].HasFlag( Helpers.   GetPawnBioType(p) ) ).ToList();

                    // Use class lookups instead
                    if (details.ConfigName == "PatchArtificialToMech") {
                        var customClassSearch = new List<string> {
                            "OrenoMSE.Recipe_InstallBodyPartModule",
                            "MSE2.Recipe_InstallArtificialBodyPartWithChildren",
                            "CONN.Recipe_InstallArtificialBodyPartAndClearPawnFromCache",
                        };
                        if (xpt >= XenoPatchType.Relaxed) customClassSearch.AddRange(new[] {
                            "MSE2.Recipe_InstallModule",
                            "MSE2.Recipe_RemoveModules",
                            "MSE2.Surgey_MakeShiftRepair",
                            "MSE2.Surgery_MakeShiftRepair",
                            "RRYautja.Recipe_Remove_Gauntlet",
                            "Immortals.Recipe_InstallFakeEye",
                            "Polarisbloc.Recipe_InstallCombatChip",
                        });
                        if (xpt >= XenoPatchType.Loose) customClassSearch.AddRange(new[] {
                            "VFEPirates.RecipeWorker_WarcasketRemoval",
                        });

                        if (xpt < XenoPatchType.Loose) customClassSearch.AddRange(new[] {
                            // from Helpers.mechSurgeryClasses
                            "MOARANDROIDS.Recipe_InstallImplantAndroid",
                            "MOARANDROIDS.Recipe_InstallArtificialBodyPartAndroid",
                            "MOARANDROIDS.Recipe_InstallArtificialBrain",
                            "VREAndroids.Recipe_InstallAndroidPart",     
                            "VREAndroids.Recipe_InstallReactor",         
                            "VREAndroids.Recipe_RemoveArtificialBodyPart",
                        });
                        // Else, the Helpers.mechSurgeryClasses are covered in "mech" check below

                        surgeryList = allSurgeryDefs.Where(s => 
                            Helpers.IsSupertypeOf(typeof(Recipe_InstallArtificialBodyPart), s.workerClass) ||
                            customClassSearch.Any(t => Helpers.IsSupertypeOf(t, s.workerClass)) ||
                            // any of the extra mech-like surgeries above + anything that is exclusively tied to a mech pawn
                            (xpt == XenoPatchType.Loose && Helpers.GetSurgeryBioType(s) == XPBioType.Mech)
                        ).ToList();
                    }

                    stopwatch.Start();
                    DefInjector.InjectSurgeryRecipes(surgeryList, pawnList);
                    stopwatch.Stop();

                    Logger.Message(afterMsg, details.MsgSubs[0], details.MsgSubs[1], stopwatch.ElapsedMilliseconds / 1000f, surgeryList.Count() * pawnList.Count());
                }
                stopwatch.Reset();
            }

            // XXX: If this gets any more WET than two, this should be a 'for' loop, similar to the above

            // Hand/foot clean up
            if (boolConfigCache["CleanupHandFootSurgeries"]) {
                if (IsDebug) Logger.Message("Cleaning up hand/foot surgical recipes");

                var surgeryList = allSurgeryDefs.Where(
                    s => Regex.IsMatch( s.label.ToLower(), "hand|foot" )
                ).ToList();

                stopwatch.Start();
                DefInjector.CleanupNamedPartSurgeryRecipes(surgeryList);
                stopwatch.Stop();

                Logger.Message("Cleaning up hand/foot surgical recipes (took {0:F4}s)", stopwatch.ElapsedMilliseconds / 1000f);
            }
            stopwatch.Reset();

            // Arm/leg clean up
            if (boolConfigCache["CleanupArmLegSurgeries"]) {
                if (IsDebug) Logger.Message("Cleaning up arm/leg surgical recipes");

                var surgeryList = allSurgeryDefs.Where(
                    s => Regex.IsMatch( s.label.ToLower(), "arm|leg" )
                ).ToList();

                stopwatch.Start();
                DefInjector.CleanupNamedPartSurgeryRecipes(surgeryList);
                stopwatch.Stop();

                Logger.Message("Cleaning up arm/leg surgical recipes (took {0:F4}s)", stopwatch.ElapsedMilliseconds / 1000f);
            }
            stopwatch.Reset();

            // Clean up
            if (IsDebug) Logger.Message("Merging duplicate surgical recipes, hyperlinking, and sorting");

            stopwatch.Start();
            DefInjector.CleanupSurgeryRecipes(allSurgeryDefs, allPawnDefs);
            stopwatch.Stop();

            Logger.Message("Merged duplicate surgical recipes, hyperlinking, and sorting (took {0:F4}s)", stopwatch.ElapsedMilliseconds / 1000f);
            stopwatch.Reset();

            // No need to occupy all of this memory
            Helpers.ClearCaches();
        }

        public void ProcessSettings () {
            // Hidden config version entry
            Version currentVer    = Instance.GetVersion();
            string  currentVerStr = currentVer.ToString();

            config["ConfigVersion"] = Settings.GetHandle(
                settingName: "ConfigVersion", title: "", description: "", defaultValue: currentVerStr
            );
            var configVerSetting = (SettingHandle<string>)config["ConfigVersion"];
            configVerSetting.DisplayOrder = 0;
            configVerSetting.NeverVisible = true;

            string  configVerStr = configVerSetting.Value;
            Version configVer    = new Version(configVerStr);
            
            var settingNames = new List<string> {
                "RestartNoteHeader",

                "BlankHeader",
                "SearchHeader",
                "SearchAdminsterRecipes",
                "SearchInstallNaturalBodyPartRecipes",
                "SearchInstallArtificialBodyPartRecipes",
                "SearchInstallImplantRecipes",
                "SearchSimpleConditionRecipes",
                "SearchVanillaRemovalRecipes",
                "BlankHeader",
                "SearchBloodWorkRecipes",
                "SearchXenogermPregnancyRecipes",
                "SearchGhoulInfusionRecipes",
                "SearchSurgicalInspectionRecipes",
                "BlankHeader",
                "SearchModdedSurgeryClasses",

                "BlankHeader",
                "PatchHeader",
                "PatchAnimalToAnimal",
                "PatchHumanlikeToHumanlike",
                "PatchArtificialToMech",
                "PatchAnimalToHumanlike",
                "PatchHumanlikeToAnimal",
                "PatchFleshlikeToFleshlike",
                "PatchHumanlikeToMech",
                "PatchMechlikeToHumanlike",
                "BlankHeader",
                "PatchAnyToAny",

                "BlankHeader",
                "CleanupHeader",
                "CleanupHandFootSurgeries",
                "CleanupArmLegSurgeries",

                "BlankHeader",
                "MoreDebug",
            };

            var defaultIsOffBooleans = new HashSet<string> {
                "SearchSimpleConditionRecipes",
                "SearchXenogermPregnancyRecipes",
                "SearchGhoulInfusionRecipes",
                "SearchSurgicalInspectionRecipes",
                "MoreDebug"
            };
            var defaultPatchValues = new Dictionary<string, XenoPatchType> {
                {"PatchAnimalToAnimal",       XenoPatchType.Relaxed},
                {"PatchHumanlikeToHumanlike", XenoPatchType.Relaxed},
                {"PatchArtificialToMech",     XenoPatchType.Relaxed},
                {"PatchAnimalToHumanlike",    XenoPatchType.Relaxed},
                {"PatchHumanlikeToAnimal",    XenoPatchType.Relaxed},
                {"PatchFleshlikeToFleshlike", XenoPatchType.Strict},
                {"PatchHumanlikeToMech",      XenoPatchType.None },
                {"PatchMechlikeToHumanlike",  XenoPatchType.None },
                {"PatchAnyToAny",             XenoPatchType.None },
            };
            
            int order = 1;
            foreach (string sName in settingNames) {
                bool isHeader = sName.Contains("Header");
                bool isPatch  = sName.StartsWith("Patch");

                if (sName == "BlankHeader") {
                    // No translations here
                    config[sName] = Settings.GetHandle(sName + order, "", "", false);
                }
                else if (isPatch && !isHeader) {
                    XenoPatchType defaultValue = defaultPatchValues[sName];

                    // Convert the old bool settings to XenoPatchType
                    if (configVer <= new Version("1.2.8") && Settings.ValueExists(sName)) {
                        string oldValue = Settings.PeekValue(sName);
                        defaultValue = 
                            oldValue == "True"  ? (
                                defaultPatchValues[sName] != XenoPatchType.None ? defaultPatchValues[sName] : XenoPatchType.Strict
                            ) :
                            oldValue == "False" ? XenoPatchType.None :
                            defaultPatchValues[sName]
                        ;
                        Settings.TryRemoveUnclaimedValue(sName);
                    }

                    config[sName] = Settings.GetHandle(
                        settingName:  sName,
                        title:        ("XP_" + sName + "_Title").Translate(),
                        description:  ("XP_" + sName + "_Description").Translate(),
                        defaultValue: defaultValue,
                        enumPrefix:   "XP_XenoPatchType_"
                    );
                }
                else {
                    config[sName] = Settings.GetHandle(
                        settingName:  sName,
                        title:        string.Concat(
                            isHeader ? "<size=15><b>" : "",
                            ("XP_" + sName + "_Title").Translate(),
                            isHeader ? "</b></size>" : ""
                        ),
                        description:  ("XP_" + sName + "_Description").Translate(),
                        defaultValue: !isHeader && !defaultIsOffBooleans.Contains(sName)
                    );
                }

                config[sName].DisplayOrder = order;

                if (isHeader) {
                    // No real settings here; just for display
                    config[sName].Unsaved = true;
                    config[sName].CustomDrawer = rect => { return false; };
                }
                else if (isPatch) {
                    xptConfigCache[sName] = ((SettingHandle<XenoPatchType>)config[sName]).Value;
                }
                else {
                    boolConfigCache[sName] = ((SettingHandle<bool>)config[sName]).Value;
                }
                
                order++;
            }

            // Set the new config value to the current version
            configVer                             = currentVer;
            configVerStr = configVerSetting.Value = currentVerStr;
        }

    }
}
