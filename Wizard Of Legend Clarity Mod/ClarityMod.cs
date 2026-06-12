using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;


namespace Wizard_Of_Legend_Clarity_Mod {
    public class ClarityMod: Partiality.Modloader.PartialityMod {

        //Description data is loaded from the ClarityData json files at startup.
        //See ClarityData/Relics.json, Arcana.json and UIText.json.
        public static Dictionary<string, string> CustomItemDescriptions = new Dictionary<string, string>();
        public static Dictionary<string, Tuple<string, string>> CustomSkillsDescriptions = new Dictionary<string, Tuple<string, string>>();
        public static Dictionary<string, string> CustomUIText = new Dictionary<string, string>();

        //Set to true to dump the game's own description json files next to the game data,
        //useful when updating ClarityData after a game patch.
        public static bool DumpGameText = false;

        [Serializable]
        public class DescriptionEntry {
            public string id;
            public string description;
            public string empowered;
        }

        [Serializable]
        public class DescriptionFile {
            public List<DescriptionEntry> entries;
        }

        private static Type objRefType;
        private static FieldInfo wrRefFieldInfo;
        private static FieldInfo textField;
        private static MethodInfo empDescInfo;

        public override void Init() {
            base.Init();
            this.ModID = "Clarity";
        }

        public override void OnLoad() {
            base.OnLoad();

            LoadDescriptions();

            On.GameDataManager.LoadInitial += HookLoadInitial;

            objRefType = typeof( WardrobeUI ).GetNestedType( "WardrobeObjRef", BindingFlags.NonPublic );
            wrRefFieldInfo = typeof( WardrobeUI ).GetField( "wrRef", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance );

            textField = objRefType.GetField( "infoDescText" );
            empDescInfo = typeof( TextManager ).GetMethod( "GetEmpoweredDescription", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static );
        }


        //Data loading starts here

        public static void LoadDescriptions() {
            string dataDir = FindDataDirectory();

            if( dataDir == null ) {
                Debug.LogError( "[Clarity] Could not find the ClarityData folder! Make sure it sits next to the mod dll inside the Mods folder. Descriptions will not be replaced." );
                return;
            }

            Debug.Log( "[Clarity] Loading description data from " + dataDir );

            foreach( DescriptionEntry entry in LoadEntries( Path.Combine( dataDir, "Relics.json" ) ) )
                CustomItemDescriptions[entry.id] = entry.description;

            foreach( DescriptionEntry entry in LoadEntries( Path.Combine( dataDir, "Arcana.json" ) ) )
                CustomSkillsDescriptions[entry.id] = new Tuple<string, string>( entry.description, string.IsNullOrEmpty( entry.empowered ) ? null : entry.empowered );

            foreach( DescriptionEntry entry in LoadEntries( Path.Combine( dataDir, "UIText.json" ) ) )
                CustomUIText[entry.id] = entry.description;
        }

        private static string FindDataDirectory() {
            List<string> candidates = new List<string>();

            try {
                string assemblyDir = Path.GetDirectoryName( Assembly.GetExecutingAssembly().Location );
                if( !string.IsNullOrEmpty( assemblyDir ) )
                    candidates.Add( Path.Combine( assemblyDir, "ClarityData" ) );
            } catch( Exception ) {
                //Assembly may have been loaded from memory and have no location
            }

            candidates.Add( Path.Combine( Path.Combine( Application.dataPath, ".." ), Path.Combine( "Mods", "ClarityData" ) ) );
            candidates.Add( Path.Combine( Directory.GetCurrentDirectory(), Path.Combine( "Mods", "ClarityData" ) ) );

            foreach( string candidate in candidates ) {
                if( Directory.Exists( candidate ) )
                    return candidate;
            }

            return null;
        }

        private static List<DescriptionEntry> LoadEntries(string filePath) {
            List<DescriptionEntry> result = new List<DescriptionEntry>();

            try {
                if( !File.Exists( filePath ) ) {
                    Debug.LogError( "[Clarity] Missing data file: " + filePath );
                    return result;
                }

                DescriptionFile file = JsonUtility.FromJson<DescriptionFile>( File.ReadAllText( filePath ) );

                if( file == null || file.entries == null ) {
                    Debug.LogError( "[Clarity] Could not parse data file: " + filePath );
                    return result;
                }

                foreach( DescriptionEntry entry in file.entries ) {
                    if( entry != null && !string.IsNullOrEmpty( entry.id ) && entry.description != null )
                        result.Add( entry );
                }

                Debug.Log( "[Clarity] Loaded " + result.Count + " entries from " + Path.GetFileName( filePath ) );
            } catch( Exception e ) {
                Debug.LogError( "[Clarity] Failed to load " + filePath );
                Debug.LogError( e );
            }

            return result;
        }

        //Data loading ends here


        public static void HookLoadInitial(On.GameDataManager.orig_LoadInitial original, bool init) {
            original( init );
            if( init ) {
                try {
                    On.TextManager.GetItemDescription += HookItemDescription;
                    On.TextManager.GetSkillDescription += HookSpellDescription;
                    On.TextManager.GetUIText += HookUIText;
                    On.WardrobeUI.LoadInfo += HookOutfitLoad;
                } catch( Exception e ) {
                    Debug.LogError( e );
                }

                if( DumpGameText ) {
                    try {
                        File.WriteAllText( Application.dataPath + "/SpellStats.json", ChaosBundle.Get<TextAsset>( "Assets/Data/UI/SkillsInfo_eng.json" ).text );
                        File.WriteAllText( Application.dataPath + "/ItemStats.json", ChaosBundle.Get<TextAsset>( "Assets/Data/UI/ItemsInfo_eng.json" ).text );
                        File.WriteAllText( Application.dataPath + "/OutfitStats.json", ChaosBundle.Get<TextAsset>( "Assets/Data/UI/OutfitsInfo_eng.json" ).text );
                        File.WriteAllText( Application.dataPath + "/UIText.json", ChaosBundle.Get<TextAsset>( "Assets/Data/UI/UITexts_eng.json" ).text );
                    } catch( Exception e ) {
                        Debug.LogError( "[Clarity] Failed to dump game text files." );
                        Debug.LogError( e );
                    }
                }
            }
        }


        public static string HookItemDescription(On.TextManager.orig_GetItemDescription original, string itemid, int playerID) {
            string orig_itemdescription = original( itemid, playerID );

            string customDescription;
            if( CustomItemDescriptions.TryGetValue( itemid, out customDescription ) )
                orig_itemdescription = customDescription;

            return orig_itemdescription;
        }

        public static string HookSpellDescription(On.TextManager.orig_GetSkillDescription original, string givenID, bool empowered = false, bool isChaos = false) {

            string orig_skilldescription = original( givenID, empowered, isChaos );

            Tuple<string, string> text;
            if( CustomSkillsDescriptions.TryGetValue( givenID, out text ) ) {

                orig_skilldescription = text.First;

                if( empowered && text.Second != null )
                    orig_skilldescription += text.Second;
                else
                    orig_skilldescription += empDescInfo.Invoke( null, new object[] { givenID, empowered, isChaos } ) as String;

            }

            return orig_skilldescription;
        }

        public static string HookUIText(On.TextManager.orig_GetUIText original, string textID) {

            try {
                string orig_UIText = original( textID );

                string customText;
                if( CustomUIText.TryGetValue( textID, out customText ) )
                    orig_UIText = customText;

                return orig_UIText;
            } catch( Exception e ) {
                Debug.LogError( e );
                return original( textID );
            }

        }

        public static void HookOutfitLoad(On.WardrobeUI.orig_LoadInfo orig, WardrobeUI instance, Outfit outfit) {
            orig( instance, outfit );

            if( outfit.unlocked ) {
                object wrRef = wrRefFieldInfo.GetValue( instance );

                Text t = textField.GetValue( wrRef ) as Text;

                string customText;
                if( CustomUIText.TryGetValue( outfit.outfitID + "_desc", out customText ) ) {
                    t.text = customText;
                }
            }
        }

    }
}
