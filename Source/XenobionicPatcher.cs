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

        internal Dictionary<string, SettingHandle> config      = new();
        internal Dictionary<string, bool>          configCache = new();  // XXX: Relying on the fact that all of our config entries are boolean

        internal List<Type> surgeryWorkerClassesFilter = new List<Type> {};

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
            IsDebug = configCache["MoreDebug"];

            // Curate the surgery worker class list before building allSurgeryDefs
            Dictionary<string, Type[]> searchConfigMapper = new Dictionary<string, Type[]> {
                { "Adminster",                 new[] { typeof(Recipe_AdministerIngestible), typeof(Recipe_AdministerUsableItem) } },
                { "InstallNaturalBodyPart",    new[] { typeof(Recipe_InstallNaturalBodyPart)    } },
                { "InstallArtificialBodyPart", new[] { typeof(Recipe_InstallArtificialBodyPart) } },
                { "InstallImplant",            new[] { typeof(Recipe_InstallImplant), typeof(Recipe_ChangeImplantLevel) } },
                { "VanillaRemoval",            new[] { typeof(Recipe_RemoveHediff), typeof(Recipe_RemoveBodyPart), typeof(Recipe_RemoveImplant) } }, 
            };
            foreach (string cName in searchConfigMapper.Keys) {
                if (configCache["Search" + cName + "Recipes"]) surgeryWorkerClassesFilter.AddRange( searchConfigMapper[cName] );
            }

            // Add additional search types for modded surgery classes
            if (configCache["SearchModdedSurgeryClasses"]) {
                List<string> moddedWorkerClassNames = new List<string> {
                    // (EPOE doesn't have any custom worker classes)

                    // EPOE Forked
                    "EPIA.Recipe_RemoveImplant",
                    "EPIA.Recipe_RemoveScarHediff",
                    "EPIA.Recipe_RemoveBrainScarHediff",
                    
                    // Rah's Bionics and Surgery Expansion
                    "ScarRemoving.Recipe_RemoveHediff_noBrain",
                
                    // Medical Surgery Expansion
                    "OrenoMSE.Recipe_InstallBodyPartModule",
                    "OrenoMSE.Recipe_InstallImplantSystem",
                    "OrenoMSE.Recipe_RemoveImplantSystem",

                    // Medical Surgery Expansion 2.0
                    "MSE2.Recipe_InstallModule",
                    "MSE2.Recipe_RemoveModules",
                    "MSE2.Recipe_InstallNaturalBodyPartWithChildren",
                    "MSE2.Recipe_InstallArtificialBodyPartWithChildren",

                    // Cyber Fauna
                    "SurgeryCF_Simple",
                    "SurgeryCF_Bionic",
                    "SurgeryCF_Archo",
                    "SurgeryCF_Battle",

                    // Chj's Androids
                    "Androids.Recipe_Disassemble",
                    "Androids.Recipe_RepairKit",

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
                    
                    // Alien vs. Predator
                    "RRYautja.Recipe_Remove_Gauntlet",
                    "RRYautja.Recipe_RemoveHugger",

                    // Questionable Ethics
                    "QEthics.RecipeWorker_CreateBrainScan",
                    "QEthics.RecipeWorker_GenomeSequencing",
                    "QEthics.RecipeWorker_InstallNaturalBodyPart",
                    "QEthics.RecipeWorker_NerveStapling",

                    // Harpies
                    "SyrHarpy.Recipe_InstallPart",

                    // A RimWorld of Magic
                    "TorannMagic.Recipe_RegrowBodyPart",
                    "TorannMagic.Recipe_RegrowUniversalBodyPart",

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

                    // Vanilla Factions Expanded: Insectoids
                    "VFEI.Other.Recipe_AddMutationHediff",

                    // Vanilla Races Expanded: Androids
                    "VREAndroids.Recipe_InstallAndroidPart",
                    "VREAndroids.Recipe_InstallReactor",
                    //"VREAndroids.Recipe_RemoveArtificialBodyPart",  // Non-androids can use the standard Recipe_RemoveBodyPart

                    // Deathrattle
                    "DeathRattle.Recipe_AdministerComaDrug",

                    // Rim of Madness: Vampires has blood recipes, but who knows which blood is compatible to a vampire?
                    // Just leave them alone.
                    /*
                    "Vampire.Recipe_ExtractBloodVial",
                    "Vampire.Recipe_ExtractBloodPack",
                    "Vampire.Recipe_ExtractBloodWine",
                    "Vampire.Recipe_TransferBlood",
                    */
                };

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

            // Because we use pawn.recipes so often for surgery checks, and not the other side (surgery.recipeUsers),
            // merge the latter into the former.  Our new additions will be sure to add it in both sides to keep
            // pawn.recipes complete.
            stopwatch.Start();
            foreach (ThingDef pawn in allPawnDefs) {
                if (pawn.recipes == null) pawn.recipes = new List<RecipeDef> {};
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

            // Animal/Animal
            if (configCache["PatchAnimalToAnimal"]) {
                if (IsDebug) Logger.Message(beforeMsg, "animal", "other animals");

                var surgeryList = allSurgeryDefs.Where(s => Regex.IsMatch( Helpers.GetSurgeryBioType(s), "animal|fleshlike|mixed" )).ToList();
                var    pawnList = allPawnDefs   .Where(p => Helpers.GetPawnBioType   (p) == "animal").ToList();

                stopwatch.Start();
                DefInjector.InjectSurgeryRecipes(surgeryList, pawnList);
                stopwatch.Stop();

                Logger.Message(afterMsg, "animal", "other animals", stopwatch.ElapsedMilliseconds / 1000f, surgeryList.Count() * pawnList.Count());
            }
            stopwatch.Reset();

            // Humanlike/Humanlike
            if (configCache["PatchHumanlikeToHumanlike"]) {
                if (IsDebug) Logger.Message(beforeMsg, "humanlike", "other humanlikes");

                var surgeryList = allSurgeryDefs.Where(s => Regex.IsMatch( Helpers.GetSurgeryBioType(s), "(?:human|flesh)like|mixed" )).ToList();
                var    pawnList = allPawnDefs   .Where(p => Helpers.GetPawnBioType   (p) == "humanlike").ToList();

                stopwatch.Start();
                DefInjector.InjectSurgeryRecipes(surgeryList, pawnList);
                stopwatch.Stop();

                Logger.Message(afterMsg, "humanlike", "other humanlikes", stopwatch.ElapsedMilliseconds / 1000f, surgeryList.Count() * pawnList.Count());
            }
            stopwatch.Reset();

            // */Mech (artificial+mech only)
            if (configCache["PatchArtificialToMech"]) {
                if (IsDebug) Logger.Message(beforeMsg, "artificial part", "mechs");

                var surgeryList = allSurgeryDefs.Where(s => 
                    Helpers.IsSupertypeOf(typeof(Recipe_InstallArtificialBodyPart), s.workerClass) ||
                    Helpers.IsSupertypeOf("OrenoMSE.Recipe_InstallBodyPartModule",  s.workerClass) ||
                    Helpers.IsSupertypeOf("MSE2.Recipe_InstallArtificialBodyPartWithChildren",          s.workerClass) ||
                    Helpers.IsSupertypeOf("CONN.Recipe_InstallArtificialBodyPartAndClearPawnFromCache", s.workerClass) ||
                    Helpers.GetSurgeryBioType(s) == "mech"
                ).ToList();
                var    pawnList = allPawnDefs   .Where(p => Helpers.GetPawnBioType   (p) == "mech").ToList();

                stopwatch.Start();
                DefInjector.InjectSurgeryRecipes(surgeryList, pawnList);
                stopwatch.Stop();

                Logger.Message(afterMsg, "artificial part", "mechs", stopwatch.ElapsedMilliseconds / 1000f, surgeryList.Count() * pawnList.Count());
            }
            stopwatch.Reset();

            // Animal/Humanlike
            if (configCache["PatchAnimalToHumanlike"]) {
                if (IsDebug) Logger.Message(beforeMsg, "animal", "humanlikes");

                var surgeryList = allSurgeryDefs.Where(s => Regex.IsMatch( Helpers.GetSurgeryBioType(s), "animal|fleshlike|mixed" )).ToList();
                var    pawnList = allPawnDefs   .Where(p => Helpers.GetPawnBioType   (p) == "humanlike").ToList();

                stopwatch.Start();
                DefInjector.InjectSurgeryRecipes(surgeryList, pawnList);
                stopwatch.Stop();

                Logger.Message(afterMsg, "animal", "humanlikes", stopwatch.ElapsedMilliseconds / 1000f, surgeryList.Count() * pawnList.Count());
            }
            stopwatch.Reset();

            // Humanlike/Animal
            if (configCache["PatchHumanlikeToAnimal"]) {
                if (IsDebug) Logger.Message(beforeMsg, "humanlike", "animals");

                var surgeryList = allSurgeryDefs.Where(s => Regex.IsMatch( Helpers.GetSurgeryBioType(s), "(?:human|flesh)like|mixed" )).ToList();
                var    pawnList = allPawnDefs   .Where(p => Helpers.GetPawnBioType   (p) == "animal"   ).ToList();

                stopwatch.Start();
                DefInjector.InjectSurgeryRecipes(surgeryList, pawnList);
                stopwatch.Stop();

                Logger.Message(afterMsg, "humanlike", "animals", stopwatch.ElapsedMilliseconds / 1000f, surgeryList.Count() * pawnList.Count());
            }
            stopwatch.Reset();

            // Any Fleshlike to any Fleshlike (only if all other similar ones are on)
            if (
                configCache["PatchAnimalToAnimal"]       &&
                configCache["PatchHumanlikeToHumanlike"] &&
                configCache["PatchAnimalToHumanlike"]    &&
                configCache["PatchHumanlikeToAnimal"]
            ) {
                if (IsDebug) Logger.Message(beforeMsg, "fleshlike", "fleshlikes");

                var surgeryList = allSurgeryDefs.Where(s => Regex.IsMatch( Helpers.GetSurgeryBioType(s), "animal|(?:human|flesh)like|mixed" )).ToList();
                var    pawnList = allPawnDefs   .Where(p => Helpers.GetPawnBioType(p) != "mech").ToList();

                stopwatch.Start();
                DefInjector.InjectSurgeryRecipes(surgeryList, pawnList);
                stopwatch.Stop();

                Logger.Message(afterMsg, "fleshlike", "fleshlikes", stopwatch.ElapsedMilliseconds / 1000f, surgeryList.Count() * pawnList.Count());
            }
            stopwatch.Reset();

            // Humanlike/Mech
            if (configCache["PatchHumanlikeToMech"]) {
                if (IsDebug) Logger.Message(beforeMsg, "humanlike", "mechs");

                var surgeryList = allSurgeryDefs.Where(s => Helpers.GetSurgeryBioType(s) == "humanlike").ToList();
                var    pawnList = allPawnDefs   .Where(p => Helpers.GetPawnBioType   (p) == "mech"     ).ToList();

                stopwatch.Start();
                DefInjector.InjectSurgeryRecipes(surgeryList, pawnList);
                stopwatch.Stop();

                Logger.Message(afterMsg, "humanlike", "mechs", stopwatch.ElapsedMilliseconds / 1000f, surgeryList.Count() * pawnList.Count());
            }
            stopwatch.Reset();

            // Mech-like/Humanlike
            if (configCache["PatchMechlikeToHumanlike"]) {
                if (IsDebug) Logger.Message(beforeMsg, "mech-like", "humanlikes");

                var surgeryList = allSurgeryDefs.Where(s => Helpers.GetSurgeryBioType(s) == "mech"     ).ToList();
                var    pawnList = allPawnDefs   .Where(p => Helpers.GetPawnBioType   (p) == "humanlike").ToList();

                stopwatch.Start();
                DefInjector.InjectSurgeryRecipes(surgeryList, pawnList);
                stopwatch.Stop();

                Logger.Message(afterMsg, "mech-like", "humanlikes", stopwatch.ElapsedMilliseconds / 1000f, surgeryList.Count() * pawnList.Count());
            }
            stopwatch.Reset();

            // Hand/foot clean up
            if (configCache["CleanupHandFootSurgeries"]) {
                if (IsDebug) Logger.Message("Cleaning up hand/foot surgical recipes");

                var surgeryList = allSurgeryDefs.Where(
                    s => s.label.ToLower() is string sl && (sl.Contains("hand") || sl.Contains("foot"))
                ).ToList();

                stopwatch.Start();
                DefInjector.CleanupHandFootSurgeryRecipes(surgeryList);
                stopwatch.Stop();

                Logger.Message("Cleaning up hand/foot surgical recipes (took {0:F4}s)", stopwatch.ElapsedMilliseconds / 1000f);
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

            config["ConfigVersion"] = Settings.GetHandle<string>("ConfigVersion", "", "", currentVerStr);
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
                "SearchVanillaRemovalRecipes",
                "SearchModdedSurgeryClasses",

                "BlankHeader",
                "PatchHeader",
                "PatchAnimalToAnimal",
                "PatchHumanlikeToHumanlike",
                "PatchArtificialToMech",
                "PatchAnimalToHumanlike",
                "PatchHumanlikeToAnimal",
                "PatchHumanlikeToMech",
                "PatchMechlikeToHumanlike",

                "BlankHeader",
                "CleanupHeader",
                "CleanupHandFootSurgeries",

                "BlankHeader",
                "MoreDebug",
            };
            
            int order = 1;
            foreach (string sName in settingNames) {
                bool isHeader = sName.Contains("Header");
                bool isOffByDefault = sName == "PatchHumanlikeToMech" || sName == "PatchMechlikeToHumanlike" || sName == "MoreDebug";

                if (sName == "BlankHeader") {
                    // No translations here
                    config[sName] = Settings.GetHandle<bool>(sName + order, "", "", false);
                }
                else {
                    config[sName] = Settings.GetHandle<bool>(
                        sName,
                        string.Concat(
                            isHeader ? "<size=15><b>" : "",
                            ("XP_" + sName + "_Title").Translate(),
                            isHeader ? "</b></size>" : ""
                        ),
                        ("XP_" + sName + "_Description").Translate(),
                        !isHeader && !isOffByDefault
                    );
                }

                var setting = (SettingHandle<bool>)config[sName];
                setting.DisplayOrder = order;

                if (isHeader) {
                    // No real settings here; just for display
                    setting.Unsaved = true;
                    setting.CustomDrawer = rect => { return false; };
                }

                configCache[sName] = setting.Value;
                order++;
            }

            // Set the new config value to the current version
            configVer                             = currentVer;
            configVerStr = configVerSetting.Value = currentVerStr;
        }

    }
}
