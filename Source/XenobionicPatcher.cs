using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using HugsLib;
using HugsLib.Settings;
using Verse;

namespace XenobionicPatcher {
    [StaticConstructorOnStartup]
    public class Base : ModBase {
        public override string ModIdentifier {
            get { return "XenobionicPatcher"; }
        }
        public static Base         Instance    { get; private set; }
        public static DefInjectors DefInjector { get; private set; }

        internal HugsLib.Utils.ModLogger ModLogger { get; private set; }

        public Base() {
            Instance    = this;
            DefInjector = new XenobionicPatcher.DefInjectors();
            ModLogger   = this.Logger;
        }

        internal Dictionary<string, SettingHandle> config = new Dictionary<string, SettingHandle>();

        internal List<Type> surgeryWorkerClassesFilter = new List<Type> {};

        public override void DefsLoaded() {
            ProcessSettings();

            // Curate the surgery worker class list before building allSurgeryDefs
            Dictionary<string, Type[]> searchConfigMapper = new Dictionary<string, Type[]> {
                { "Adminster",                 new[] { typeof(Recipe_AdministerIngestible), typeof(Recipe_AdministerUsableItem) } },
                { "InstallNaturalBodyPart",    new[] { typeof(Recipe_InstallNaturalBodyPart)    } },
                { "InstallArtificialBodyPart", new[] { typeof(Recipe_InstallArtificialBodyPart) } },
                { "InstallImplant",            new[] { typeof(Recipe_InstallImplant)            } }
            };
            foreach (string cName in searchConfigMapper.Keys) {
                if ( ((SettingHandle<bool>)config["Search" + cName + "Recipes"]).Value ) surgeryWorkerClassesFilter.AddRange( searchConfigMapper[cName] );
            }
            
            // Start with a few global lists
            List<ThingDef> allPawnDefs = DefDatabase<ThingDef>.AllDefs.Where(
                thing => typeof(Pawn).IsAssignableFrom(thing.thingClass)
            ).ToList();
            List<RecipeDef> allSurgeryDefs = DefDatabase<RecipeDef>.AllDefs.Where(
                recipe => recipe.IsSurgery && surgeryWorkerClassesFilter.Any( t => t.IsAssignableFrom(recipe.workerClass) )
            ).ToList();

            // Animal/Animal
            if ( ((SettingHandle<bool>)config["PatchAnimalToAnimal"]).Value ) {
                Logger.Message("Injecting animal surgical recipes into other animals");
                DefInjector.InjectSurgeryRecipes(
                    allSurgeryDefs.Where(s => GetSurgeryBioType(s) == "animal"),
                    allPawnDefs   .Where(p => GetPawnBioType   (p) == "animal")
                );
            }

            // Humanlike/Humanlike
            if ( ((SettingHandle<bool>)config["PatchHumanlikeToHumanlike"]).Value ) {
                Logger.Message("Injecting humanlike surgical recipes into other humanlikes");
                DefInjector.InjectSurgeryRecipes(
                    allSurgeryDefs.Where(s => GetSurgeryBioType(s) == "humanlike"),
                    allPawnDefs   .Where(p => GetPawnBioType   (p) == "humanlike")
                );
            }

            // */Mech (artificial only)
            if ( ((SettingHandle<bool>)config["PatchArtificialToMech"]).Value ) {
                Logger.Message("Injecting artificial part surgical recipes into mechs");
                DefInjector.InjectSurgeryRecipes(
                    allSurgeryDefs.Where(s => typeof(Recipe_InstallArtificialBodyPart).IsAssignableFrom(s.workerClass)),
                    allPawnDefs   .Where(p => GetPawnBioType   (p) == "mech")
                );
            }

            // Animal/Humanlike
            if ( ((SettingHandle<bool>)config["PatchAnimalToHumanlike"]).Value ) {
                Logger.Message("Injecting animal surgical recipes into humanlikes");
                DefInjector.InjectSurgeryRecipes(
                    allSurgeryDefs.Where(s => GetSurgeryBioType(s) == "animal"),
                    allPawnDefs   .Where(p => GetPawnBioType   (p) == "humanlike")
                );
            }

            // Humanlike/Animal
            if ( ((SettingHandle<bool>)config["PatchHumanlikeToAnimal"]).Value ) {
                Logger.Message("Injecting humanlike surgical recipes into animals");
                DefInjector.InjectSurgeryRecipes(
                    allSurgeryDefs.Where(s => GetSurgeryBioType(s) == "humanlike"),
                    allPawnDefs   .Where(p => GetPawnBioType   (p) == "animal")
                );
            }

            // Any Fleshlike to any Fleshlike (only if all other similar ones are on)
            if (
                ((SettingHandle<bool>)config["PatchAnimalToAnimal"])      .Value &&
                ((SettingHandle<bool>)config["PatchHumanlikeToHumanlike"]).Value &&
                ((SettingHandle<bool>)config["PatchAnimalToHumanlike"])   .Value &&
                ((SettingHandle<bool>)config["PatchHumanlikeToAnimal"])   .Value
            ) {
                Logger.Message("Injecting fleshlike surgical recipes into other fleshlikes");
                DefInjector.InjectSurgeryRecipes(
                    allSurgeryDefs.Where(s => Regex.IsMatch( GetSurgeryBioType(s), "animal|(?:human|flesh)like|mixed" )),
                    allPawnDefs   .Where(p => GetPawnBioType   (p) != "mech")
                );
            }

            // Clean up
            Logger.Message("Merging duplicate surgical recipes");
            DefInjector.CleanupSurgeryRecipes(allSurgeryDefs, allPawnDefs);


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

                "BlankHeader",
                "PatchHeader",
                "PatchAnimalToAnimal",
                "PatchHumanlikeToHumanlike",
                "PatchArtificialToMech",
                "PatchAnimalToHumanlike",
                "PatchHumanlikeToAnimal",
            };
            
            int order = 1;
            foreach (string sName in settingNames) {
                bool isHeader = sName.Contains("Header");

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
                        !isHeader
                    );
                }

                var setting = (SettingHandle<bool>)config[sName];
                setting.DisplayOrder = order;

                if (isHeader) {
                    // No real settings here; just for display
                    setting.Unsaved = true;
                    setting.CustomDrawer = rect => { return false; };
                }

                order++;
            }

            // Set the new config value to the current version
            configVer                             = currentVer;
            configVerStr = configVerSetting.Value = currentVerStr;
        }

    }
}
