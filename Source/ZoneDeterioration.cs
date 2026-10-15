using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using Verse;

namespace TKS_ZoneDeterioration
{
    [StaticConstructorOnStartup]
    public static class InsertHarmony
    {
        static InsertHarmony()
        {
            Harmony harmony = new Harmony("TKS_ZoneDeterioration");
            //Harmony.DEBUG = true;
            ZoneDeteriorationMod.DebugMessage("harmony debugging to " + FileLog.LogPath);
            harmony.PatchAll();
            //Harmony.DEBUG = false;
            ZoneDeteriorationMod.DebugMessage($"TKS_ZoneDeterioration: Patching finished");
        }
    }

    public class ZoneDeteriorationSettings : ModSettings
    {
        public bool debugPrint = false;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref debugPrint, "debugPrint");

            base.ExposeData();
        }
    }

    public class ZoneDeteriorationMod : Mod
    {
        //static so the message prefix doesn't have to look the mod up for every message
        public static ZoneDeteriorationSettings settings;

        public ZoneDeteriorationMod(ModContentPack content) : base(content)
        {
            settings = GetSettings<ZoneDeteriorationSettings>();
        }

        public static void DebugMessage(string message)
        {
            if (settings != null && settings.debugPrint)
            {
                Log.Message(message);
            }
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listingStandard = new Listing_Standard();
            listingStandard.Begin(inRect);
            listingStandard.CheckboxLabeled("ZoneDeteriorationDebugPrint".Translate(), ref settings.debugPrint);
            listingStandard.End();
            base.DoSettingsWindowContents(inRect);
        }

        public override string SettingsCategory()
        {
            return "ZoneDeteriorationSettingsName".Translate();
        }
    }

    //created by the game for every new or loaded game (was a HugsLib ModBase; WorldLoaded is now FinalizeInit, which
    //runs at the same point, once the maps are loaded)
    public class ZoneDeteriorationBase : GameComponent
    {
        private ExtendedDataStorage _extendedDataStorage;
        public static ZoneDeteriorationBase Instance { get; private set; }

        public static List<string> interestedMessageKeys = new List<string>() { "MessageDeterioratedAway", "MessagePlantDiedOfCold", "MessagePlantDiedOfRot_LeftUnharvested",
        "MessagePlantDiedOfRot_ExposedToLight", "MessagePlantDiedOfRot",  "MessagePlantDiedOfPoison", "MessagePlantDiedOfBlight", "MessageRottedAwayInStorage" };

        public static List<string> interestedMessages = new List<string>() { };

        public ZoneDeteriorationBase(Game game)
        {
            Instance = this;
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();

            _extendedDataStorage = Find.World.GetComponent<ExtendedDataStorage>();
            _extendedDataStorage.Cleanup();

            Thing testThing = ThingMaker.MakeThing(ThingDefOf.Urn, ThingDefOf.Steel);
            Map map = Find.AnyPlayerHomeMap;
            GenSpawn.Spawn(testThing, CellFinder.RandomEdgeCell(map), map, WipeMode.FullRefund);

            //build interested strings list (static, so clear what the previous game loaded)
            interestedMessages.Clear();
            foreach(string messageKey in interestedMessageKeys)
            {
                string message = "";
                //some message types will error because we dont have a thing to pass, but it will still resolve ok for our purposes
                try
                {
                    message = messageKey.Translate("urn", testThing);
                    message = message.Replace("urn ", "");
                    message = message.Replace("steel ", "");
                } catch (Exception e) 
                { } finally
                {
                    ZoneDeteriorationMod.DebugMessage("zone deterioration looking for text '" + message + "'");
                    interestedMessages.Add(message);
                }

            }

            testThing.Destroy();

        }

        public ExtendedDataStorage GetExtendedDataStorage()
        {
            return _extendedDataStorage;
        }
    }

    public static class Utilities
    {
        public static bool ContainingStorageWantsMessage(Thing t)
        {
            if (t == null) { return true; }

            IntVec3 loc = t.Position;
            Map map = t.Map;

            return ContainingStorageWantsMessage(loc, map);
        }

        public static bool ContainingStorageWantsMessage(IntVec3 loc, Map map)
        {

            if (map.thingGrid.CellContains(loc, ThingCategory.Building))
            {
                Building building = map.thingGrid.ThingAt<Building>(loc) as Building_Storage;

                if (building != null)
                {
                    ExtendedDataStorage store = ZoneDeteriorationBase.Instance.GetExtendedDataStorage();

                    if (store != null)
                    {
                        ZoneDeteriorationMod.DebugMessage("checking zone message storage setting for building " + building.ThingID);
                        bool showMessage = store.GetExtendedDataFor(building).showWarning;
                        return showMessage;
                    }
                    else
                    {
                        Log.Warning("no zone message storage setting for building " + building.ThingID);
                    }
                }
            }

            return true;
        }

        public static bool ContainingZoneWantsMessage(Thing t)
        {
            if (t == null) { return true; }

            IntVec3 loc = t.Position;
            Map map = t.Map;

            return ContainingZoneWantsMessage(loc, map);
        }
        public static bool ContainingZoneWantsMessage(IntVec3 loc, Map map)
        {
            Zone zone = map?.zoneManager?.ZoneAt(loc);
            if (zone != null)
            {
                ExtendedDataStorage store = ZoneDeteriorationBase.Instance.GetExtendedDataStorage();

                if (store != null)
                {
                    ZoneDeteriorationMod.DebugMessage("checking show messages setting for " + zone.BaseLabel);
                    bool showMessage = store.GetExtendedDataFor(zone).showWarning;

                    return showMessage;
                }
                else
                {
                    Log.Warning("no zone message storage setting for zone " + zone.BaseLabel);
                }
            }

            return true;
        }
    }


    public class ExtendedStorageData : IExposable
    {
        public bool showWarning = true;

        public void ExposeData()
        {
            Scribe_Values.Look(ref showWarning, "showWarning", true);
        }

        public bool ShouldClean()
        {
            return false;
        }

        public void reset()
        {
            showWarning = true;
        }
    }

    public class ExtendedDataStorage : RimWorld.Planet.WorldComponent, IExposable
    {
        private Dictionary<int, ExtendedStorageData> _store =
            new Dictionary<int, ExtendedStorageData>();

        private List<int> _idWorkingList;

        private List<ExtendedStorageData> _extendedZoneDataWorkingList;

        public ExtendedDataStorage(RimWorld.Planet.World world) : base(world)
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(
                ref _store, "store",
                LookMode.Value, LookMode.Deep,
                ref _idWorkingList, ref _extendedZoneDataWorkingList);
        }

        public ExtendedStorageData GetExtendedDataFor(Zone zone)
        {

            var id = zone.ID;
            return GetExtendedDataForID(id);
        }

        public ExtendedStorageData GetExtendedDataFor(Thing t)
        {
            var id = t.thingIDNumber;
            return GetExtendedDataForID(id);
        }

        public ExtendedStorageData GetExtendedDataForID(int id)
        {
            if (_store.TryGetValue(id, out ExtendedStorageData data))
            {
                return data;
            }

            var newExtendedData = new ExtendedStorageData();

            _store[id] = newExtendedData;
            return newExtendedData;
        }

        public void DeleteExtendedDataFor(Zone zone)
        {
            _store.Remove(zone.ID);
        }

        public void DeleteExtendedDataFor(Thing t)
        {
            _store.Remove(t.thingIDNumber);
        }

        public void Cleanup()
        {
            List<int> shouldRemove = new List<int>();
            foreach (KeyValuePair<int, ExtendedStorageData> kv in _store)
            {
                if (kv.Value == null || kv.Value.ShouldClean())
                {
                    shouldRemove.Add(kv.Key);
                }
            }
            foreach (int key in shouldRemove)
            {
                _store.Remove(key);
            }
        }
    }


    [HarmonyPatch(typeof(Zone))]
    static class Zone_Patches
    {
        [HarmonyPatch(typeof(Zone), "GetGizmos")]
        [HarmonyPrefix]
        public static bool GetGizmos(ref Zone __instance, out Zone __state)
        {
            __state = __instance;
            return true;
        }

        [HarmonyPatch(typeof(Zone), "GetGizmos")]
        [HarmonyPostfix]
        public static IEnumerable<Gizmo> GetGizmos(IEnumerable<Gizmo> results, Zone __state)
        {
            //get setting for zone
            ExtendedDataStorage store = ZoneDeteriorationBase.Instance.GetExtendedDataStorage();

            foreach (Gizmo giz in results)
            {
                yield return giz;
            }

            bool showMessage = store.GetExtendedDataFor(__state).showWarning;
            yield return new Command_Toggle
            {
                icon = ContentFinder<Texture2D>.Get("UI/Commands/LaunchReport", true),
                defaultLabel = (showMessage ? "ZoneShowDeteriorationMessage".Translate() : "ZoneHideDeteriorationMessage".Translate()),
                defaultDesc = "ZoneToggleDeteriorationMessage".Translate(),
                isActive = (() => showMessage),
                toggleAction = delegate ()
                {
                    showMessage = !showMessage;
                    store.GetExtendedDataFor(__state).showWarning = showMessage;
                },
            };

            yield break;
            yield break;
        }
    }

    [HarmonyPatch(typeof(Building_Storage))]
    static class Storage_Patches
    {
        [HarmonyPatch(typeof(Building_Storage), "GetGizmos")]
        [HarmonyPrefix]
        public static bool GetGizmos(ref Building_Storage __instance, out Building_Storage __state)
        {
            __state = __instance;
            return true;
        }

        [HarmonyPatch(typeof(Building_Storage), "GetGizmos")]
        [HarmonyPostfix]
        public static IEnumerable<Gizmo> GetGizmos(IEnumerable<Gizmo> results, Building_Storage __state)
        {
            //get setting for zone
            ExtendedDataStorage store = ZoneDeteriorationBase.Instance.GetExtendedDataStorage();

            foreach (Gizmo giz in results)
            {
                yield return giz;
            }

            bool showMessage = store.GetExtendedDataFor(__state).showWarning;
            yield return new Command_Toggle
            {
                icon = ContentFinder<Texture2D>.Get("UI/Commands/LaunchReport", true),
                defaultLabel = (showMessage ? "ZoneShowDeteriorationMessage".Translate() : "ZoneHideDeteriorationMessage".Translate()),
                defaultDesc = "ZoneToggleDeteriorationMessage".Translate(),
                isActive = (() => showMessage),
                toggleAction = delegate ()
                {
                    showMessage = !showMessage;
                    store.GetExtendedDataFor(__state).showWarning = showMessage;
                },
            };

            yield break;
            yield break;
        }
    }

    [HarmonyPatch(typeof(Messages))]
    public static class Messages_Patches
    {
        [HarmonyPatch(typeof(Messages), "Message", new Type[] { typeof(string), typeof(LookTargets), typeof(MessageTypeDef), typeof(bool) })]
        [HarmonyPrefix]
        public static bool Message_Prefix(string text, LookTargets lookTargets, MessageTypeDef def, bool historical)
        {
            foreach (string searcher in ZoneDeteriorationBase.interestedMessages)
            {
                if (text.Contains(searcher))
                {
                    RimWorld.Planet.GlobalTargetInfo target = lookTargets.TryGetPrimaryTarget();
                    IntVec3 vec = target.Cell;
                    Map map = target.Map;
#if v1_4 || v1_5
                    if (vec != null) {
#else
                    if (vec.IsValid) {
#endif

                        ZoneDeteriorationMod.DebugMessage("checking deterioration message for thing at " + vec);
                        if (Utilities.ContainingStorageWantsMessage(vec, map) && Utilities.ContainingZoneWantsMessage(vec, map))
                        {
                            return true;
                        }
                        else
                        {
                            return false;
                        }
                    }
                }
            }
            return true;
        }
    }
}
