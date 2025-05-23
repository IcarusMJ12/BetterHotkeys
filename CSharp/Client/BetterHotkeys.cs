using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

using HarmonyLib;

using Barotrauma;
using Barotrauma.Extensions;
using Barotrauma.Items;
using Barotrauma.Items.Components;
using QuickUseAction = Barotrauma.CharacterInventory.QuickUseAction;

namespace BetterHotkeys {
  class BetterHotkeys : ACsMod {
    const string PatchCategory = "aquinas.icarus.BetterHotkeys";
    private Harmony harmony;

    [HarmonyPatch(typeof(Barotrauma.CharacterInventory))]
    [HarmonyPatch(nameof(Barotrauma.CharacterInventory.GetQuickUseAction))]
    public static class BetterHotkeys_GetQuickUseAction {
      static QuickUseAction GetQuickUseAction_(CharacterInventory __instance, Item item, bool allowEquip, bool allowInventorySwap, bool allowApplyTreatment) {
        Character character = (Character)(typeof(CharacterInventory).GetField("character", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(__instance));

        if (allowApplyTreatment && CharacterHealth.OpenHealthWindow != null &&
            //if the item can be equipped in the health interface slot, don't use it as a treatment but try to equip it
            !item.AllowedSlots.Contains(InvSlotType.HealthInterface)) {
          return QuickUseAction.UseTreatment;
        }

        if (item.ParentInventory != __instance) {
          if (Screen.Selected == GameMain.GameScreen) {
            if (item.NonInteractable || item.NonPlayerTeamInteractable) {
              return QuickUseAction.None;
            }
          }
          if (item.ParentInventory == null || item.ParentInventory.Locked) {
            return QuickUseAction.None;
          }
          //in another inventory -> attempt to place in the character's inventory
          else if (allowInventorySwap) {
            if (item.Container == null || character.Inventory.FindIndex(item.Container) == -1) { // Not a subinventory in the character's inventory
              if (character.HeldItems.Any(i => i.OwnInventory != null && i.OwnInventory.CanBePut(item))) {
                return QuickUseAction.PutToEquippedItem;
              } else {
                return item.ParentInventory is CharacterInventory ? QuickUseAction.TakeFromCharacter : QuickUseAction.TakeFromContainer;
              }
            } else {
              var selectedContainer = character.SelectedItem?.GetComponent<ItemContainer>();
              if (selectedContainer != null &&
                  selectedContainer.Inventory != null &&
                  !selectedContainer.Inventory.Locked) {
                // Move the item from the subinventory to the selected container
                return QuickUseAction.PutToContainer;
              } else if (character.Inventory.AccessibleWhenAlive || character.Inventory.AccessibleByOwner) {
                // Take from the subinventory and place it in the character's main inventory if no target container is selected
                return QuickUseAction.TakeFromContainer;
              }
            }
          }
        } else {
          var selectedContainer = character.SelectedItem?.GetComponent<ItemContainer>();

          if (selectedContainer != null &&
              selectedContainer.Inventory != null &&
              !selectedContainer.Inventory.Locked &&
              allowInventorySwap) {
            //player has selected the inventory of another item -> attempt to move the item there
            return QuickUseAction.PutToContainer;
          } else if (character.SelectedCharacter?.Inventory != null &&
              !character.SelectedCharacter.Inventory.Locked &&
              allowInventorySwap) {
            //player has selected the inventory of another character -> attempt to move the item there
            return QuickUseAction.PutToCharacter;
          } else if (character.SelectedBy?.Inventory != null &&
              Character.Controlled == character.SelectedBy &&
              !character.SelectedBy.Inventory.Locked &&
              (character.SelectedBy.Inventory.AccessibleWhenAlive || character.SelectedBy.Inventory.AccessibleByOwner) &&
              allowInventorySwap) {
            return QuickUseAction.TakeFromCharacter;
          } else if (character.HeldItems.FirstOrDefault(i =>
                i.OwnInventory != null &&
                (i.OwnInventory.CanBePut(item) || ((i.OwnInventory.Capacity == 1 || i.OwnInventory.Container.HasSubContainers) && i.OwnInventory.AllowSwappingContainedItems && i.OwnInventory.Container.CanBeContained(item)))) is { } equippedContainer) {
            if (allowEquip && !character.HasEquippedItem(item, InvSlotType.RightHand | InvSlotType.LeftHand) &&
                (item.HasTag("weapon") ||
                 item.HasTag("mountableweapon"))) { // anything that can be put in a weapon holder, includes welders/cutters
              return QuickUseAction.Equip;
            }
            return QuickUseAction.PutToEquippedItem;
          } else if (allowEquip) { //doubleclicked and no other inventory is selected
                                   //not equipped -> attempt to equip
            if (!character.HasEquippedItem(item) || item.GetComponents<Pickable>().Count() > 1) {
              return QuickUseAction.Equip;
            }
            //equipped -> attempt to unequip
            else if (item.AllowedSlots.Contains(InvSlotType.Any)) {
              return QuickUseAction.Unequip;
            } else {
              return QuickUseAction.Drop;
            }
          }
        }

        return QuickUseAction.None;
      }

      static bool Prefix(ref QuickUseAction __result, CharacterInventory __instance, Item item, bool allowEquip, bool allowInventorySwap, bool allowApplyTreatment) {
        __result = (QuickUseAction)GetQuickUseAction_(__instance, item, allowEquip, allowInventorySwap, allowApplyTreatment);
        return false;
      }
    }

    [HarmonyPatch(typeof(Barotrauma.CharacterInventory))]
    [HarmonyPatch(nameof(Barotrauma.CharacterInventory.QuickUseItem))]
    public static class BetterHotkeys_QuickUseItem {
      static bool Prefix(CharacterInventory __instance, Item item, bool allowEquip, bool allowInventorySwap, bool allowApplyTreatment, QuickUseAction? action = null, bool playSound = true) {
        Character character = (Character)(typeof(CharacterInventory).GetField("character", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(__instance));
        int capacity = (int)(typeof(CharacterInventory).GetField("capacity", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(__instance));
        InvSlotType[] SlotTypes = __instance.SlotTypes;
        List<Item> DraggingItems = CharacterInventory.DraggingItems;
        Inventory.ItemSlot[] slots = (Inventory.ItemSlot[])(typeof(CharacterInventory).GetField("slots", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(__instance));

        MethodInfo GetActiveEquippedSubInventory = typeof(CharacterInventory).GetMethod("GetActiveEquippedSubInventory", BindingFlags.NonPublic | BindingFlags.Instance);

        if (Screen.Selected is SubEditorScreen editor && !editor.WiringMode && !Submarine.Unloading) {
          // Find the slot the item was contained in and flash it
          if (item.ParentInventory?.visualSlots != null) {
            var invSlots = item.ParentInventory.visualSlots;
            for (int i = 0; i < invSlots.Length; i++) {
              if (i < 0 || invSlots.Length <= i || i < 0 || item.ParentInventory.Capacity <= i) { break; }

              var slot = invSlots[i];
              if (item.ParentInventory.GetItemAt(i) == item) {
                slot.ShowBorderHighlight(GUIStyle.Red, 0.1f, 0.4f);
                SoundPlayer.PlayUISound(GUISoundType.PickItem);
                break;
              }
            }
          }

          SubEditorScreen.StoreCommand(new AddOrDeleteCommand(new List<MapEntity> { item }, true));

          item.Remove();
          return false;
        }

        QuickUseAction quickUseAction = action ?? ((QuickUseAction)__instance.GetQuickUseAction(item, allowEquip, allowInventorySwap, allowApplyTreatment));
        bool success = false;
        switch (quickUseAction) {
          case QuickUseAction.Equip:
            if (string.IsNullOrEmpty(item.Prefab.EquipConfirmationText) || character != Character.Controlled) {
              Equip();
            } else {
              if (GUIMessageBox.MessageBoxes.Any(mb => mb.UserData as string == "equipconfirmation")) { return false; }
              var equipConfirmation = new GUIMessageBox(string.Empty, TextManager.Get(item.Prefab.EquipConfirmationText),
                  new LocalizedString[] { TextManager.Get("yes"), TextManager.Get("no") }) { UserData = "equipconfirmation" };
              equipConfirmation.Buttons[0].OnClicked = (btn, userdata) => {
                Equip();
                equipConfirmation.Close();
                return true;
              };
              equipConfirmation.Buttons[1].OnClicked = equipConfirmation.Close;
            }

            void Equip() {
              //attempt to put in a free slot first
              for (int i = capacity - 1; i >= 0; i--) {
                if (!slots[i].Empty()) { continue; }
                if (SlotTypes[i] == InvSlotType.Any || !item.AllowedSlots.Any(a => a.HasFlag(SlotTypes[i]))) { continue; }
                success = __instance.TryPutItem(item, i, true, false, Character.Controlled, true);
                if (success) { return; }
              }

              for (int i = capacity - 1; i >= 0; i--) {
                if (SlotTypes[i] == InvSlotType.Any || !item.AllowedSlots.Any(a => a.HasFlag(SlotTypes[i]))) { continue; }
                // something else already equipped in a hand slot, attempt to unequip it so items aren't unnecessarily swapped to it
                if (!slots[i].Empty() &&
                    (SlotTypes[i] == InvSlotType.LeftHand || SlotTypes[i] == InvSlotType.RightHand)) {
                  if (slots[i].First().AllowedSlots.Contains(InvSlotType.Any)) {
                    __instance.TryPutItem(slots[i].First(), Character.Controlled, new List<InvSlotType>() { InvSlotType.Any }, true);
                  } else if (slots[i].First().AllowedSlots.Contains(InvSlotType.Bag)) {
                    __instance.TryPutItem(slots[i].First(), Character.Controlled, new List<InvSlotType>() { InvSlotType.Bag }, true);
                  }
                }
                success = __instance.TryPutItem(item, i, true, false, Character.Controlled, true);
                if (success) { return; }
              }

              if (item.HasTag("weapon") ||
                  item.HasTag("mountableweapon")) {  // anything that can be put in a weapon holder, includes welders/cutters
                // swapping didn't work, attempt to drop held item and wield if weapon
                for (int i = capacity - 1; i >= 0; i--) {
                  if (SlotTypes[i] == InvSlotType.Any || !item.AllowedSlots.Any(a => a.HasFlag(SlotTypes[i]))) { continue; }
                  if (!slots[i].Empty() &&
                      (SlotTypes[i] == InvSlotType.LeftHand || SlotTypes[i] == InvSlotType.RightHand)) {
                    slots[i].First().Drop(Character.Controlled);
                  }
                  success = __instance.TryPutItem(item, i, true, false, Character.Controlled, true);
                  if (success) { return; }
                }
              }
            }
            break;
          case QuickUseAction.Unequip:
            if (item.AllowedSlots.Contains(InvSlotType.Any)) {
              success = __instance.TryPutItem(item, Character.Controlled, new List<InvSlotType>() { InvSlotType.Any }, true);
            }
            break;
          case QuickUseAction.UseTreatment:
            CharacterHealth.OpenHealthWindow?.OnItemDropped(item, ignoreMousePos: true);
            return false;
          case QuickUseAction.Drop:
            //do nothing, the item is dropped after a delay
            return false;
          case QuickUseAction.PutToCharacter:
            if (character.SelectedCharacter != null && character.SelectedCharacter.Inventory != null) {
              //player has selected the inventory of another character -> attempt to move the item there
              success = character.SelectedCharacter.Inventory.TryPutItem(item, Character.Controlled, item.AllowedSlots, true);
            }
            break;
          case QuickUseAction.PutToContainer:
            var selectedContainer = character.SelectedItem?.GetComponent<ItemContainer>();
            if (selectedContainer != null && selectedContainer.Inventory != null) {
              //player has selected the inventory of another item -> attempt to move the item there
              success = selectedContainer.Inventory.TryPutItem(item, Character.Controlled, item.AllowedSlots, true);
            }
            break;
          case QuickUseAction.TakeFromCharacter:
            if (character.SelectedBy != null && Character.Controlled == character.SelectedBy &&
                character.SelectedBy.Inventory != null) {
              //item is in the inventory of another character -> attempt to get the item from there
              success = character.SelectedBy.Inventory.TryPutItemWithAutoEquipCheck(item, Character.Controlled, item.AllowedSlots, true);
            }
            break;
          case QuickUseAction.TakeFromContainer:
            // Check open subinventories and put the item in it if equipped
            ItemInventory activeSubInventory = null;
            for (int i = 0; i < capacity; i++) {
              activeSubInventory = (ItemInventory)GetActiveEquippedSubInventory.Invoke(__instance, new object[] { i });
              if (activeSubInventory != null) {
                success = activeSubInventory.TryPutItem(item, Character.Controlled, item.AllowedSlots, true);
                break;
              }
            }

            // No subinventory found or placing unsuccessful -> attempt to put in the character's inventory
            if (!success) {
              success = __instance.TryPutItemWithAutoEquipCheck(item, Character.Controlled, item.AllowedSlots, true);
            }
            break;
          case QuickUseAction.PutToEquippedItem:
            //order by the condition of the contained item to prefer putting into the item with the emptiest ammo/battery/tank
            foreach (Item heldItem in character.HeldItems.OrderByDescending(heldItem => GetContainPriority(item, heldItem))) {
              if (heldItem.OwnInventory == null) { continue; }
              //don't allow swapping if we're moving items into an item with 1 slot holding a stack of items
              //(in that case, the quick action should just fill up the stack)
              bool disallowSwapping =
                (heldItem.OwnInventory.Capacity == 1 || heldItem.OwnInventory.Container.HasSubContainers) &&
                heldItem.OwnInventory.GetItemAt(0)?.Prefab == item.Prefab &&
                heldItem.OwnInventory.GetItemsAt(0).Count() > 1;
              if (heldItem.OwnInventory.TryPutItem(item, Character.Controlled) ||
                  ((heldItem.OwnInventory.Capacity == 1 || heldItem.OwnInventory.Container.HasSubContainers) && heldItem.OwnInventory.TryPutItem(item, 0, allowSwapping: !disallowSwapping, allowCombine: false, user: Character.Controlled))) {
                success = true;
                for (int j = 0; j < capacity; j++) {
                  if (slots[j].Contains(heldItem)) { __instance.visualSlots[j].ShowBorderHighlight(GUIStyle.Green, 0.1f, 0.4f); }
                }
                break;
              }
            }
            break;

            static float GetContainPriority(Item item, Item containerItem) {
              var container = containerItem.GetComponent<ItemContainer>();
              if (container == null) { return 0.0f; }
              for (int i = 0; i < container.Inventory.Capacity; i++) {
                var containedItems = container.Inventory.GetItemsAt(i);
                if (containedItems.Any() && container.Inventory.CanBePutInSlot(item, i)) {
                  //if there's a stack in the contained item that we can add the item to, prefer that
                  return 10.0f;
                }
              }
              return -container.GetContainedIndicatorState();
            }
        }

        if (success) {
          for (int i = 0; i < capacity; i++) {
            if (slots[i].Contains(item)) { __instance.visualSlots[i].ShowBorderHighlight(GUIStyle.Green, 0.1f, 0.4f); }
          }
        }

        DraggingItems.Clear();
        if (playSound) {
          SoundPlayer.PlayUISound(success ? GUISoundType.PickItem : GUISoundType.PickItemFail);
        }
        return false;
      }
    }

    public BetterHotkeys() {
      this.harmony = new Harmony(BetterHotkeys.PatchCategory);
      this.harmony.PatchAll();
      // load-bearing log -- mod does nothing without it :/
      LuaCsLogger.Log("[BetterHotkeys] Initialized");
    }

    public override void Stop() {
      this.harmony.UnpatchSelf();
    }
  }
}
