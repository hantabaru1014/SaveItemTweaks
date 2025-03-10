using ResoniteModLoader;
using HarmonyLib;
using FrooxEngine;
using Elements.Core;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Linq;
using System.Reflection;
using System;

namespace SaveItemTweaks
{
    public class SaveItemTweaks : ResoniteMod
    {
        public override string Name => "SaveItemTweaks";
        public override string Author => "hantabaru1014";
        public override string Version => "2.0.2";
        public override string Link => "https://github.com/hantabaru1014/SaveItemTweaks";

        private static ModConfiguration config;

        [AutoRegisterConfigKey]
        public static readonly ModConfigurationKey<bool> IgnoreUserScaleWhenSaveItemKey 
            = new ModConfigurationKey<bool>("IgnoreUserScaleWhenSaveItem", "Ignore user scale when saving items", () => true);
        [AutoRegisterConfigKey]
        public static readonly ModConfigurationKey<bool> DontScaleWhenItemSpawnKey 
            = new ModConfigurationKey<bool>("DontScaleWhenItemSpawn", "Do not change scale according to the user scale when the item spawn", () => true);
        [AutoRegisterConfigKey]
        public static readonly ModConfigurationKey<float> DontScaleWhenItemSpawnMagnificationLimitKey 
            = new ModConfigurationKey<float>("DontScaleWhenItemSpawnMagnificationLimit", "└ Magnification limit of Dont Scale When Item Spawn", () => 3f);

        public override void OnEngineInit()
        {
            config = GetConfiguration();

            var harmony = new Harmony("net.hantabaru1014.SaveItemTweaks");
            harmony.PatchAll();
            ItemHelper_SaveItemInternal_Patch.Patch(harmony);
            InventoryBrowser_SpawnItem_Patch.Patch(harmony);
        }

        [HarmonyPatch(typeof(InventoryItem), nameof(InventoryItem.Unpack))]
        class InventoryItem_Unpack_Patch
        {
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var setGlobalScaleMethod = AccessTools.Method(typeof(Slot), "set_GlobalScale");
                foreach (var code in instructions)
                {
                    if (code.Calls(setGlobalScaleMethod))
                    {
                        yield return new CodeInstruction(OpCodes.Ldarg_0);
                        yield return new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(InventoryItem), nameof(InventoryItem.SavedScale)));
                        yield return new CodeInstruction(OpCodes.Call,
                            AccessTools.Method(typeof(InventoryItem_Unpack_Patch), nameof(SetGlobalScale)));
                        Msg("Patched InventoryItem.Unpack");
                    }
                    else
                    {
                        yield return code;
                    }
                }
            }

            static void SetGlobalScale(Slot holderSlot, float3 scale, Sync<float3> savedScale)
            {
                if (config.GetValue(DontScaleWhenItemSpawnKey))
                {
                    var limit = config.GetValue(DontScaleWhenItemSpawnMagnificationLimitKey);
                    limit = limit <= 0 ? float.MaxValue : limit;
                    var mag = (scale / savedScale.Value).y;
                    if ((mag < 1 && mag > 1/limit) || (mag > 1 && mag < limit))
                    {
                        var fixedScale = holderSlot.LocalUserSpace.LocalScaleToGlobal(savedScale.Value);
                        holderSlot.GlobalScale = fixedScale;
                        Msg($"[InventoryItem.Unpack] Fixed scale. Normal: {scale}, Fixed: {fixedScale}");
                        return;
                    }
                }
                holderSlot.GlobalScale = scale;
            }
        }

        class ItemHelper_SaveItemInternal_Patch
        {
            static readonly Type TargetInternalClass = typeof(ItemHelper).GetNestedType("<SaveItemInternal>d__11", BindingFlags.Instance | BindingFlags.NonPublic);

            public static void Patch(Harmony harmony)
            {
                var targetMethod = AccessTools.Method(TargetInternalClass, "MoveNext");
                var transpiler = AccessTools.Method(typeof(ItemHelper_SaveItemInternal_Patch), nameof(Transpiler));
                harmony.Patch(targetMethod, transpiler: new HarmonyMethod(transpiler));
            }

            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var codes = instructions.ToList();
                var saveObjectMethod = AccessTools.Method(typeof(Slot), nameof(Slot.SaveObject));
                for (var i = 0; i < codes.Count; i++)
                {
                    if (codes[i].Calls(saveObjectMethod))
                    {
                        // 一つ目の itemSlot.SaveObject() の戻り値を書き換える
                        codes.InsertRange(i + 1, new[]
                        {
                            new CodeInstruction(OpCodes.Ldarg_0),
                            new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(TargetInternalClass, "itemSlot")),
                            new CodeInstruction(OpCodes.Call, 
                                AccessTools.Method(typeof(ItemHelper_SaveItemInternal_Patch), nameof(FixSavedGraph)))
                        });
                        Msg("Patched ItemHelper.SaveItemInternal");
                        break;
                    }
                }
                return codes.AsEnumerable();
            }

            static SavedGraph FixSavedGraph(SavedGraph graph, Slot itemSlot)
            {
                if (!config.GetValue(IgnoreUserScaleWhenSaveItemKey)) return graph;

                var inventoryItem = (DataTreeDictionary)graph.Root.TryGetDictionary("Object")
                    ?.TryGetDictionary("Components")?.TryGetList("Data")
                    ?.FirstOrDefault(node => ((DataTreeDictionary)node).ExtractOrDefault<string>("Type") == "FrooxEngine.InventoryItem");
                if (inventoryItem != null)
                {
                    inventoryItem.TryGetDictionary("Data")?.TryGetDictionary("SavedScale")?.AddOrUpdate("Data", Coder<float3>.Save(float3.One));
                    Msg("Set InventoryItem.SavedScale to float3.One");
                }

                // Holder無しで保存された時 & Holder(InventoryItem)があってもCloud Spawn等でUnpackが呼び出されなかった時
                // When saved without Holder OR When Unpack is not called by Cloud Spawn etc. even if there is Holder(InventoryItem)
                var targetScale = itemSlot.LocalUserSpace.GlobalScaleToLocal(itemSlot.GlobalScale);
                var scaleDict = graph.Root.TryGetDictionary("Object")?.TryGetDictionary("Scale");
                Msg($"originalScale: {Coder<float3>.Load(scaleDict?.TryGetNode("Data"))}, fixedScale: {targetScale}");
                scaleDict?.AddOrUpdate("Data", Coder<float3>.Save(targetScale));

                return graph;
            }
        }

        class InventoryBrowser_SpawnItem_Patch
        {
            static readonly Type TargetInternalClass = typeof(InventoryBrowser)
                .GetNestedType("<>c__DisplayClass97_1", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetNestedType("<<SpawnItem>b__1>d", BindingFlags.Instance | BindingFlags.NonPublic);

            public static void Patch(Harmony harmony)
            {
                var targetMethod = AccessTools.Method(TargetInternalClass, "MoveNext");
                var transpiler = AccessTools.Method(typeof(InventoryBrowser_SpawnItem_Patch), nameof(Transpiler));
                harmony.Patch(targetMethod, transpiler: new HarmonyMethod(transpiler));
            }

            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var positionInFrontOfUserMethod = AccessTools.Method(typeof(SlotPositioning), nameof(SlotPositioning.PositionInFrontOfUser));
                foreach (var code in instructions)
                {
                    if (code.Calls(positionInFrontOfUserMethod))
                    {
                        yield return new CodeInstruction(OpCodes.Call, 
                            AccessTools.Method(typeof(InventoryBrowser_SpawnItem_Patch), nameof(SetPosition)));
                        Msg("Patched InventoryBrowser.SpawnItem");
                    }
                    else
                    {
                        yield return code;
                    }
                }
            }

            static void SetPosition(Slot slot, float3? faceDirection, float3? offset, float distance, User user, bool scale, bool checkOcclusion, bool preserveUp)
            {
                if (config.GetValue(DontScaleWhenItemSpawnKey))
                {
                    var limit = config.GetValue(DontScaleWhenItemSpawnMagnificationLimitKey);
                    limit = limit <= 0 ? float.MaxValue : limit;
                    var userScale = slot.LocalUserRoot.GlobalScale;
                    if ((userScale < 1 && userScale > 1 / limit) || (userScale > 1 && userScale < limit))
                    {
                        slot.PositionInFrontOfUser(faceDirection, offset, distance, user, false, checkOcclusion, preserveUp);
                        Msg("slot.PositionInFrontOfUser scale=false");
                        return;
                    }
                }
                slot.PositionInFrontOfUser(faceDirection, offset, distance, user, scale, checkOcclusion, preserveUp);
            }
        }
    }
}