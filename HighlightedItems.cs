﻿using System.Windows.Forms;
using HighlightedItems.Utils;
using ExileCore;
using ExileCore.PoEMemory.Elements.InventoryElements;
using ExileCore.PoEMemory.MemoryObjects;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using ExileCore.Shared.Enums;
using ImGuiNET;
using System;
using System.Linq.Expressions;
using System.Numerics;
using ExileCore.PoEMemory.Components;

namespace HighlightedItems
{
    public class HighlightedItems : BaseSettingsPlugin<Settings>
    {
        private IngameState ingameState;
        private SharpDX.Vector2 windowOffset = new SharpDX.Vector2();

        public HighlightedItems()
        {
        }

        public override bool Initialise()
        {
            base.Initialise();
            Name = "HighlightedItems";
            ingameState = GameController.Game.IngameState;
            windowOffset = GameController.Window.GetWindowRectangle().TopLeft;

            var pickBtn = Path.Combine(DirectoryFullName, "images\\pick.png").Replace('\\', '/');
            var pickLBtn = Path.Combine(DirectoryFullName, "images\\pickL.png").Replace('\\', '/');
            Graphics.InitImage(pickBtn, false);
            Graphics.InitImage(pickLBtn, false);

            return true;
        }

        public override void DrawSettings()
        {
            base.DrawSettings();
            this.DrawIgnoredCellsSettings();
        }

        public override void Render()
        {
            if (!Settings.Enable)
                return;

            var stashPanel = ingameState.IngameUi.StashElement;
            if (stashPanel.IsVisible)
            {
                var visibleStash = stashPanel.VisibleStash;
                if (visibleStash == null || visibleStash.InventoryUIElement == null)
                    return;

                //Determine Stash Pickup Button position and draw
                var stashRect = visibleStash.InventoryUIElement.GetClientRect();
                var pickButtonRect =
                    new SharpDX.RectangleF(stashRect.BottomRight.X - 43, stashRect.BottomRight.Y + 10, 37, 37);

                Graphics.DrawImage("pick.png", pickButtonRect);

                var highlightedItems = GetHighlightedItems();

                int? stackSizes = 0;
                foreach (var item in highlightedItems)
                {
                    stackSizes += item.Item?.GetComponent<Stack>()?.Size;
                }

                string countText;
                if (Settings.ShowStackSizes && highlightedItems.Count != stackSizes && stackSizes != null)
                    if (Settings.ShowStackCountWithSize) countText = $"{stackSizes} / {highlightedItems.Count}";
                    else countText = $"{stackSizes}";
                else
                    countText = $"{highlightedItems.Count}";

                var countPos = new Vector2(pickButtonRect.Left - 2, pickButtonRect.Center.Y);
                countPos.Y -= 11;
                Graphics.DrawText($"{countText}", new Vector2(countPos.X, countPos.Y + 2), SharpDX.Color.Black, 10, "FrizQuadrataITC:22", FontAlign.Right);
                Graphics.DrawText($"{countText}", new Vector2(countPos.X - 2, countPos.Y), SharpDX.Color.White, 10, "FrizQuadrataITC:22", FontAlign.Right);

                if (isButtonPressed(pickButtonRect) || Keyboard.IsKeyPressed(Settings.HotKey.Value))
                {
                    var prevMousePos = Mouse.GetCursorPosition();
                    foreach (var item in highlightedItems)
                    {
                        if (!ingameState.IngameUi.StashElement.IsVisible)
                        {
                            DebugWindow.LogMsg("HighlightedItems: Stash Panel closed, aborting loop");
                            break;
                        }
                        if (!ingameState.IngameUi.InventoryPanel.IsVisible)
                        {
                            DebugWindow.LogMsg("HighlightedItems: Inventory Panel closed, aborting loop");
                            break;
                        }
                        if (IsInventoryFull())
                        {
                            DebugWindow.LogMsg("HighlightedItems: Inventory full, aborting loop");
                            break;
                        }
                        moveItem(item.GetClientRect().Center);
                    }
                    Mouse.moveMouse(prevMousePos);
                }
            }

            var inventoryPanel = ingameState.IngameUi.InventoryPanel;
            var inventoryItems = inventoryPanel[InventoryIndex.PlayerInventory].VisibleInventoryItems;
            if (inventoryPanel.IsVisible && Settings.DumpButtonEnable)
            {
                //Determine Inventory Pickup Button position and draw
                var inventoryRect = inventoryPanel.Children[2].GetClientRect();
                var pickButtonRect =
                    new SharpDX.RectangleF(inventoryRect.TopLeft.X + 18, inventoryRect.TopLeft.Y - 37, 37, 37);

                Graphics.DrawImage("pickL.png", pickButtonRect);
                if (isButtonPressed(pickButtonRect))
                {
                    foreach (var item in inventoryItems)
                    {
                        if (!CheckIgnoreCells(item))
                        {
                            if (!ingameState.IngameUi.InventoryPanel.IsVisible)
                            {
                                DebugWindow.LogMsg("HighlightedItems: Inventory Panel closed, aborting loop");
                                break;
                            }
                            
                            /*
                            if (!ingameState.IngameUi.StashElement.IsVisible
                                && !ingameState.IngameUi.SellWindow.IsVisible
                                && !ingameState.IngameUi.TradeWindow.IsVisible)
                            {
                                DebugWindow.LogMsg("HighlightedItems: Stash Panel closed, aborting loop");
                                break;
                            }*/
                            moveItem(item.GetClientRect().Center);
                        }
                    }
                }
            }
        }

        public IList<NormalInventoryItem> GetHighlightedItems()
        {
            List<NormalInventoryItem> highlightedItems = new List<NormalInventoryItem>();

            try
            {
                IList<NormalInventoryItem> stashItems =
                    ingameState.IngameUi.StashElement.VisibleStash.VisibleInventoryItems;

                IOrderedEnumerable<NormalInventoryItem> orderedInventoryItems = stashItems
                        .Cast<NormalInventoryItem>()
                    .OrderBy(stashItem => stashItem.InventPosX)
                    .ThenBy(stashItem => stashItem.InventPosY);

                foreach (var item in orderedInventoryItems)
                {
                    bool isHighlighted = item.isHighlighted;
                    if (isHighlighted)
                    {
                        highlightedItems.Add(item);
                    }
                }
            }
            catch (System.Exception) { }
            
            return highlightedItems;
        }

        private bool IsInventoryFull()
        {
            var inventoryPanel = ingameState.IngameUi.InventoryPanel;
            var inventoryItems = inventoryPanel[InventoryIndex.PlayerInventory].VisibleInventoryItems;

            // quick sanity check
            if (inventoryItems.Count < 12)
            {
                return false;
            }

            // track each inventory slot
            bool[,] inventorySlot = new bool[12, 5];

            // iterate through each item in the inventory and mark used slots
            foreach (var inventoryItem in inventoryItems)
            {
                int x = inventoryItem.InventPosX;
                int y = inventoryItem.InventPosY;
                int height = inventoryItem.ItemHeight;
                int width = inventoryItem.ItemWidth;
                for (int row = x; row < x + width; row++)
                {
                    for (int col = y; col < y + height; col++)
                    {
                        inventorySlot[row, col] = true;
                    }
                }
            }

            // check for any empty slots
            for (int x = 0; x < 12; x++)
            {
                for (int y = 0; y < 5; y++)
                {
                    if (inventorySlot[x, y] == false)
                    {
                        return false;
                    }
                }
            }
            // no empty slots, so inventory is full
            return true;
        }

        public void moveItem(SharpDX.Vector2 itemPosition)
        {
            itemPosition += windowOffset;
            Keyboard.KeyDown(Keys.LControlKey);
            Mouse.moveMouse(itemPosition);
            Mouse.LeftDown(Settings.ExtraDelay);
            Mouse.LeftUp(0);
            Keyboard.KeyUp(Keys.LControlKey);
        }

        public bool isButtonPressed(SharpDX.RectangleF buttonRect)
        {
            if (Control.MouseButtons == MouseButtons.Left)
            {
                var prevMousePos = Mouse.GetCursorPosition();

                if (buttonRect.Contains(Mouse.GetCursorPosition() - windowOffset))
                {
                    return true;
                }

                Mouse.moveMouse(prevMousePos);
            }

            return false;
        }


        private bool CheckIgnoreCells(NormalInventoryItem inventItem)
        {
            var inventPosX = inventItem.InventPosX;
            var inventPosY = inventItem.InventPosY;

            if (inventPosX < 0 || inventPosX >= 12)
                return true;
            if (inventPosY < 0 || inventPosY >= 5)
                return true;

            return Settings.IgnoredCells[inventPosY, inventPosX] != 0; //No need to check all item size
        }


        private void DrawIgnoredCellsSettings()
        {
            ImGui.BeginChild("##IgnoredCellsMain", new Vector2(ImGuiNative.igGetContentRegionAvail().X, 204f), true,
                (ImGuiWindowFlags) 16);
            ImGui.Text("Ignored Inventory Slots (checked = ignored)");

            Vector2 contentRegionAvail = ImGuiNative.igGetContentRegionAvail();
            ImGui.BeginChild("##IgnoredCellsCels", new Vector2(contentRegionAvail.X, contentRegionAvail.Y), true,
                (ImGuiWindowFlags) 16);

            int num = 1;
            for (int index1 = 0; index1 < 5; ++index1)
            {
                for (int index2 = 0; index2 < 12; ++index2)
                {
                    bool boolean = Convert.ToBoolean(Settings.IgnoredCells[index1, index2]);
                    if (ImGui.Checkbox(string.Format("##{0}IgnoredCells", (object) num), ref boolean))
                        Settings.IgnoredCells[index1, index2] ^= 1;
                    if ((num - 1) % 12 < 11)
                        ImGui.SameLine();
                    ++num;
                }
            }

            ImGui.EndChild();
            ImGui.EndChild();
        }
    }
}