using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;
using HugsLib;

namespace TKS_ZoneDeterioration
{
    [StaticConstructorOnStartup]
    public static class InsertHarmony
    {
        static InsertHarmony()
        {
            Harmony harmony = new Harmony("TKS_ZoneDeterioration");
            //Harmony.DEBUG = true;
            harmony.PatchAll();
            //Harmony.DEBUG = false;
            Log.Message($"TKS_ZoneDeterioration: Patching finished");
        }
    }

    public class Base : ModBase
    {
        private ExtendedDataStorage _extendedDataStorage;
        public static Base Instance { get; private set; }

        public override string ModIdentifier
        {
            get { return "TKS_ZoneDeterioration"; }
        }
        public Base()
        {
            Instance = this;
        }

        public override void WorldLoaded()
        {
            base.WorldLoaded();

            _extendedDataStorage = Find.World.GetComponent<ExtendedDataStorage>();
            _extendedDataStorage.Cleanup();

        }

        public ExtendedDataStorage GetExtendedDataStorage()
        {
            return _extendedDataStorage;
        }
    }

    public static class Utilities
    {
        public static bool ContainingStorageWantsMessage(this Thing t)
        {
            IntVec3 loc = t.Position;
            Map map = t.Map;

            if (map.thingGrid.CellContains(loc, ThingCategory.Building)) {
                Building building = map.thingGrid.ThingAt<Building>(loc) as Building_Storage;

                if (building != null)
                {
                    ExtendedDataStorage store = Base.Instance.GetExtendedDataStorage();

                    bool showMessage = store.GetExtendedDataFor(building).showWarning;
                    return showMessage;
                }
            }

            return true;
        }

        public static bool ContainingZoneWantsMessage(this Thing t)
        {
            IntVec3 loc = t.Position;
            Map map = t.Map;

            Zone zone = map.zoneManager.ZoneAt(loc);
            if (zone != null)
            {
                ExtendedDataStorage store = Base.Instance.GetExtendedDataStorage();

                bool showMessage = store.GetExtendedDataFor(zone).showWarning;
                return showMessage;
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
            ExtendedDataStorage store = Base.Instance.GetExtendedDataStorage();

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
            ExtendedDataStorage store = Base.Instance.GetExtendedDataStorage();

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

    [HarmonyPatch(typeof(SteadyEnvironmentEffects))]
    static class SteadyEnvironmentEffects_Patches
    {
        [HarmonyPatch(typeof(SteadyEnvironmentEffects), "DoDeteriorationDamage")]
        [HarmonyPrefix]
        public static bool DoDeteriorationDamage(Thing t, IntVec3 pos, Map map, ref bool sendMessage)
        {
            if (pos == null || map == null) { return true; }

            //Log.Message("running do deterioration damage prefix");

            //check if it's in a zone
            Zone stockpile = GridsUtility.GetZone(pos, map);

            if (stockpile != null)
            {
                //Log.Message("checking for zone setting re: deterioration message");

                //get setting for zone
                ExtendedDataStorage store = Base.Instance.GetExtendedDataStorage();

                bool showMessage = store.GetExtendedDataFor(stockpile).showWarning;
                if (!showMessage)
                {
                    //Log.Message("setting deterioration message to null do to sendMessage flag = false");
                    sendMessage = false;
                }

            }

            return true;
        }
    }

    [HarmonyPatch(typeof(Plant))]
    public class Plant_Patches
    {
        /*
        [HarmonyPatch(typeof(Plant), "MakeLeafless")]
        [HarmonyPrefix]
        public static bool MakeLeafless(ref Plant __instance, Plant.LeaflessCause cause)
        {
            //Log.Message("running MakeLeafless on plant " + __instance.ToString());
            Map map = __instance.Map;

            //check if we're in a zone, otherwise do original method
            Zone gardenZone = GridsUtility.GetZone(__instance.Position, map) as Zone;

            if (gardenZone is null)
            {
                return true;
            }

            //check for the display message flag on the zone
            //get setting for zone
            ExtendedDataStorage store = Base.Instance.GetExtendedDataStorage();

            bool showMessage = store.GetExtendedDataFor(gardenZone).showWarning;

            //have to copy-paste the rest unfortunately
            bool flag = !__instance.LeaflessNow;

            if (cause == Plant.LeaflessCause.Poison && __instance.def.plant.leaflessGraphic == null)
            {
                if (__instance.IsCrop && MessagesRepeatAvoider.MessageShowAllowed("MessagePlantDiedOfPoison-" + __instance.def.defName, 240f) && showMessage)
                {
                    Messages.Message("MessagePlantDiedOfPoison".Translate(__instance.GetCustomLabelNoCount(false)), new TargetInfo(__instance.Position, map, false), MessageTypeDefOf.NegativeEvent, true);
                }
                __instance.TakeDamage(new DamageInfo(DamageDefOf.Rotting, 99999f, 0f, -1f, null, null, null, DamageInfo.SourceCategory.ThingOrUnknown, null, true, true));
            }
            else if (__instance.def.plant.dieIfLeafless)
            {
                if (__instance.IsCrop)
                {
                    if (cause == Plant.LeaflessCause.Cold)
                    {
                        if (MessagesRepeatAvoider.MessageShowAllowed("MessagePlantDiedOfCold-" + __instance.def.defName, 240f) && showMessage)
                        {
                            Messages.Message("MessagePlantDiedOfCold".Translate(__instance.GetCustomLabelNoCount(false)), new TargetInfo(__instance.Position, map, false), MessageTypeDefOf.NegativeEvent, true);
                        }
                    }
                    else if (cause == Plant.LeaflessCause.Poison)
                    {
                        if (MessagesRepeatAvoider.MessageShowAllowed("MessagePlantDiedOfPoison-" + __instance.def.defName, 240f) && showMessage)
                        {
                            Messages.Message("MessagePlantDiedOfPoison".Translate(__instance.GetCustomLabelNoCount(false)), new TargetInfo(__instance.Position, map, false), MessageTypeDefOf.NegativeEvent, true);
                        }
                    }
                    else if (cause == Plant.LeaflessCause.Pollution)
                    {
                        if (MessagesRepeatAvoider.MessageShowAllowed("MessagePlantDiedOfPollution-" + __instance.def.defName, 240f) && showMessage)
                        {
                            Messages.Message("MessagePlantDiedOfPollution".Translate(__instance.GetCustomLabelNoCount(false)), new TargetInfo(__instance.Position, map, false), MessageTypeDefOf.NegativeEvent, true);
                        }
                    }
                    else if (cause == Plant.LeaflessCause.NoPollution && MessagesRepeatAvoider.MessageShowAllowed("MessagePlantDiedOfNoPollution-" + __instance.def.defName, 240f) && showMessage)
                    {
                        Messages.Message("MessagePlantDiedOfNoPollution".Translate(__instance.GetCustomLabelNoCount(false)), new TargetInfo(__instance.Position, map, false), MessageTypeDefOf.NegativeEvent, true);
                    }
                }
                __instance.TakeDamage(new DamageInfo(DamageDefOf.Rotting, 99999f, 0f, -1f, null, null, null, DamageInfo.SourceCategory.ThingOrUnknown, null, true, true));
            }
            else
            {
                FieldInfo madeLeaflessTickField = __instance.GetType().GetField("madeLeaflessTick", BindingFlags.NonPublic | BindingFlags.Instance);
                madeLeaflessTickField.SetValue(__instance, Find.TickManager.TicksGame);
            }
            if (flag)
            {
                map.mapDrawer.MapMeshDirty(__instance.Position, MapMeshFlag.Things);
            }

            return false;
        }
    }
    */

        [HarmonyPatch(typeof(Plant), "MakeLeafless")]
        public static class PlantTrans
        {
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
            {
                Log.Message($"[ZoneDeterioration] Plant.MakeLeafless Transpiler beginning");

                int falseCommand = 0;

                var codes = new List<CodeInstruction>(instructions);
                for (int i = 0; i < codes.Count; i++)
                {
                    bool yieldIt = true;

                    if (!(codes[i].operand is null) && codes[i].operand.ToString().Contains("MessageShowAllowed"))
                    {
                        Log.Message("found if statement for trans");
                        falseCommand = i + 1;

                        yield return codes[i];
                        yieldIt = false;

                        yield return codes[falseCommand];

                        MethodInfo showMessageFunction = AccessTools.Method(typeof(TKS_ZoneDeterioration.Utilities), nameof(Utilities.ContainingZoneWantsMessage));
                        CodeInstruction function = new CodeInstruction(OpCodes.Callvirt, showMessageFunction);

                        yield return new CodeInstruction(OpCodes.Ldarg_0);
                        yield return function;

                    }

                    if (yieldIt)
                    {
                        yield return codes[i];
                    }

                }
                Log.Message($"[ZoneDeterioration] Plant.MakeLeafless Transpiler succeeded");

            }
        }

        [HarmonyPatch(typeof(Plant), "TickLong")]
        public static class PlantTrans2
        {
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
            {
                Log.Message($"[ZoneDeterioration] Plant.TickLong Transpiler beginning");

                int falseCommand = 0;

                var codes = new List<CodeInstruction>(instructions);
                for (int i = 0; i < codes.Count; i++)
                {
                    bool yieldIt = true;

                    if (!(codes[i].operand is null) && codes[i].operand.ToString().Contains("MessageShowAllowed"))
                    {
                        Log.Message("found if statement for trans");
                        falseCommand = i + 1;

                        yield return codes[i];
                        yieldIt = false;

                        yield return codes[falseCommand];

                        MethodInfo showMessageFunction = AccessTools.Method(typeof(TKS_ZoneDeterioration.Utilities), nameof(Utilities.ContainingZoneWantsMessage));
                        CodeInstruction function = new CodeInstruction(OpCodes.Callvirt, showMessageFunction);

                        yield return new CodeInstruction(OpCodes.Ldarg_0);
                        yield return function;

                    }

                    if (yieldIt)
                    {
                        yield return codes[i];
                    }

                }
                Log.Message($"[ZoneDeterioration] Plant.TickLong Transpiler succeeded");

            }
        }

    }



    [HarmonyPatch(typeof(CompRottable))]
    public static class CompRottable
    {
        static System.Reflection.MethodBase TargetMethod()
        {
            //return AccessTools.Method(typeof(CompRottable).GetMethod("Tick", BindingFlags.NonPublic | BindingFlags.Instance));
            return typeof(RimWorld.CompRottable).GetMethod("Tick", BindingFlags.NonPublic | BindingFlags.Instance) as System.Reflection.MethodBase;
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            Log.Message($"[ZoneDeterioration] CompRottable.Tick Transpiler beginning");

            bool foundIfStatement = false;
            int statementStart = 0;
            int statementEnd = 0;
            bool insertedCode = false;

            var codes = new List<CodeInstruction>(instructions);
            for (int i = 0; i < codes.Count; i++)
            {
                bool yieldIt = true;

                if (!(codes[i].operand is null) && codes[i].operand.ToString().Contains("IsInAnyStorage") && !foundIfStatement)
                {
                    Log.Message("found if statement for copy");
                    statementStart = i - 2;
                    statementEnd = i + 1;

                    foundIfStatement = true;

                }

                if (foundIfStatement && i==(statementEnd+1) && !insertedCode)
                {
                    Log.Message("Inserting check for show rot message at line " + i.ToString());

                    for (int x = statementStart; x <= statementEnd; x++)
                    {
                        CodeInstruction statement = codes[x];

                        if (!(statement.operand is null) && statement.operand.ToString().Contains("IsInAnyStorage"))
                        {
                            MethodInfo showMessageFunction = AccessTools.Method(typeof(TKS_ZoneDeterioration.Utilities), nameof(Utilities.ContainingStorageWantsMessage));
                            CodeInstruction replacer = new CodeInstruction(OpCodes.Callvirt, showMessageFunction);

                            yield return replacer;
                        }
                        else
                        {
                            yield return statement;
                        }

                        insertedCode = true;
                    }
                }

                if (yieldIt)
                {
                    yield return codes[i];
                }

            }
            Log.Message($"[ZoneDeterioration] CompRottable.Tick Transpiler succeeded");

        }
    }
}
