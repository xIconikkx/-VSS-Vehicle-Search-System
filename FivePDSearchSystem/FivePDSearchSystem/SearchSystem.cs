#nullable enable
using CitizenFX.Core;
using CitizenFX.Core.Native;
using FivePD.API.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;


namespace FivePDSearchSystem
{
    public class SearchSystem : FivePD.API.Plugin
    {
        private List<VehicleDoorSearched> searchedDoorsClass = new List<VehicleDoorSearched>();

        private List<Item> loadedconfigitems = new List<Item>();

        internal SearchSystem()
        {
            Debug.WriteLine("Loaded FivePD Vehicle Search System v1.1.0");
            LoadConfig();

            Tick += OnTick;
            Tick += Cleanup;
        }

        private async Task Cleanup()
        {
            //60 seconds
            await Delay(60000);
            for (int i = 0; i < searchedDoorsClass.Count - 1; i++)
            {
                if (searchedDoorsClass[i] is null)
                    continue;
                if (!searchedDoorsClass[i].vehicleObj.Exists())
                {
                    searchedDoorsClass.Remove(searchedDoorsClass[i]);
                }
                else
                {
                    Ped playerPed = Game.PlayerPed;
                    Vector3 pedPos = playerPed.Position;

                    float distance = pedPos.DistanceTo(searchedDoorsClass[i].vehicleObj.Position);

                    if (distance > 100)
                    {
                        searchedDoorsClass.Remove(searchedDoorsClass[i]);
                    }
                }

                await Delay(100);
            }
        }

        bool searching = false;

        private async Task<KeyValuePair<bool, VehicleDoor>> HandleSearchVehicle(Vehicle vehicle)
        {
            if (searching) throw new Exception("Already searching!");
            searching = true;
            Vector3 pedPos = Game.PlayerPed.Position;

            var closestDoor = Enum.GetValues(typeof(VehicleDoorIndex))
                .Cast<VehicleDoorIndex>()
                .Select(doorIndex => new
                {
                    Door = doorIndex,
                    Distance = pedPos.DistanceTo(GetDoorPosition(vehicle, doorIndex))
                })
                .Where(door => door.Distance < 2f)
                .OrderBy(door => door.Distance)
                .FirstOrDefault()?.Door ?? VehicleDoorIndex.FrontLeftDoor;

            bool isOpen = vehicle.Doors[closestDoor].IsOpen;

            if (searchedDoorsClass.Count > 0)
            {
                if (!SearchVehicleSearchedClass(closestDoor.ToString(), vehicle))
                {
                    if (API.IsControlJustPressed(0, 38))
                    {
                        if (isOpen)
                        {
                            AddSearchedVehicle(closestDoor.ToString(), vehicle);

                            _ = SearchVehicle(Game.PlayerPed, vehicle, (VehicleDoorIndex)closestDoor);
                        }
                        else
                        {
                            ToggleDoor(vehicle, closestDoor);
                            isOpen = true;
                        }
                    }
                }
            }
            else
            {
                if (API.IsControlJustPressed(0, 38))
                {
                    if (isOpen)
                    {
                        await SearchVehicle(Game.PlayerPed, vehicle, (VehicleDoorIndex)closestDoor);

                        AddSearchedVehicle(closestDoor.ToString(), vehicle);
                    }
                    else
                    {
                        ToggleDoor(vehicle, (VehicleDoorIndex)closestDoor);
                        isOpen = true;
                    }
                }
            }
            searching = false;
            return new KeyValuePair<bool, VehicleDoor>(isOpen, vehicle.Doors[closestDoor]);
        }

        private async Task OnTick()
        {
            try
            {
                if (!Game.PlayerPed.IsInVehicle() && !searching)
                {
                    Vehicle? vehicle = GetClosestVehicle(Game.PlayerPed.Position, 5f);
                    if (vehicle is null || !vehicle.Exists() ) return;

                    if (vehicle.IsStopped && vehicle.IsSeatFree(VehicleSeat.Driver))
                    {
                        var handleSearchVehicleData = await HandleSearchVehicle(vehicle);
                        var isOpen = handleSearchVehicleData.Key;
                        var door = handleSearchVehicleData.Value;
                        var closestDoorPos = GetDoorPosition(vehicle, door.Index);
                        string actionText = isOpen ? "Search" : "Open";
                        if (SearchVehicleSearchedClass(door.Index.ToString(), vehicle))
                            DrawText3D(closestDoorPos.X, closestDoorPos.Y, closestDoorPos.Z, $"SEARCHED", 17, 230, 2);
                        else
                            DrawText3D(closestDoorPos.X, closestDoorPos.Y, closestDoorPos.Z, $"Press [E] to {actionText}", 255, 243, 15);
                    }
                }
            }
            catch (Exception ex)
            {
                searching = false;
                if (ex.Message != "Already searching!")
                    Debug.Write("[VSS - ERROR] " + ex.ToString());
            }
        }

        private Vehicle? GetClosestVehicle(Vector3 position, float radius) =>
            new(API.GetClosestVehicle(position.X, position.Y, position.Z, radius, 0, 70));

        private Vector3 GetDoorPosition(Vehicle? vehicle, VehicleDoorIndex doorIndex)
        {
            if (doorIndex == VehicleDoorIndex.Hood || doorIndex == VehicleDoorIndex.Trunk)
            {
                // Get bone positions for hood and trunk
                int boneIndex = (doorIndex == VehicleDoorIndex.Hood)
                    ? API.GetEntityBoneIndexByName(vehicle.Handle, "bonnet")
                    : API.GetEntityBoneIndexByName(vehicle.Handle, "boot");
                return API.GetWorldPositionOfEntityBone(vehicle.Handle, boneIndex);
            }
            else
            {
                // Normal doors use entry positions
                return API.GetEntryPositionOfDoor(vehicle.Handle, (int)doorIndex);
            }
        }

        private void ToggleDoor(Vehicle? vehicle, VehicleDoorIndex doorIndex)
        {
            int door = (int)doorIndex;
            bool isOpen = API.GetVehicleDoorAngleRatio(vehicle.Handle, door) > 0;

            if (isOpen)
            {
                API.SetVehicleDoorShut(vehicle.Handle, door, false);
            }
            else
            {
                API.SetVehicleDoorOpen(vehicle.Handle, door, false, false);
            }
        }

        private async void RunAsync(Task task, Func<bool> predicate, int timeout = 100)
        {
            while (predicate())
            {
                task.Start();
                await Delay(timeout);
            }
        }

        private async Task SearchVehicle(Ped playerPed, Vehicle? vehicle, VehicleDoorIndex doorIndex)
        {
            string animDict = "amb@prop_human_bum_bin@base";
            string animName = "base";

            if (!API.HasAnimDictLoaded(animDict))
            {
                API.RequestAnimDict(animDict);
                while (!API.HasAnimDictLoaded(animDict))
                {
                    await Delay(10);
                }
            }

            API.TaskPlayAnim(playerPed.Handle, animDict, animName, 8.0f, -8.0f, 3000, 1, 0, false, false, false);
            await Delay(3000);
            API.ClearPedTasks(playerPed.Handle);

            API.SetVehicleDoorShut(vehicle.Handle, (int)doorIndex, false);

            ShowSearchResults();
        }

        private void ShowSearchResults()
        {
            //If the config items doesn't have anything in it
            if (loadedconfigitems.Count == 0) return;

            //Get a random amount of items from 1/4
            Random rand = new Random();
            int itemCount = rand.Next(1, 4);

            //Create a new list of the items we find
            List<ItemsFound> foundItems = new List<ItemsFound>();

            
            if(rand.Next(1, 10) <= 7)
            {
                //We will find something
                for (int i = 0; i < itemCount; i++)
                {
                    Item itemToAdd = loadedconfigitems[rand.Next(0, loadedconfigitems.Count)];

                    ItemsFound itemF = new ItemsFound();
                    itemF.itemName = itemToAdd.Name;
                    itemF.isIllegal = itemToAdd.IsIllegal;

                    if (foundItems.Contains(itemF))
                    {
                        i -= 1;
                        return;
                    }

                    foundItems.Add(itemF);
                }
            }

            string itemsString = "";

            if(foundItems.Count > 0)
            {
                for (int i = 0; i < foundItems.Count; i++)
                {
                    if (!foundItems[i].isIllegal)
                    {
                        itemsString = itemsString + "~w~" + foundItems[i].itemName;

                        if (i != foundItems.Count - 1)
                        {
                            itemsString = itemsString + ", ";
                        }
                    }
                    else if (foundItems[i].isIllegal)
                    {
                        itemsString = itemsString + "~r~" + foundItems[i].itemName;

                        if (i != foundItems.Count - 1)
                        {
                            itemsString = itemsString + ", ";
                        }
                    }
                }
            }
            else
            {
                itemsString = "Nothing";
            }
            

            string result = $"You found: " + itemsString;
            API.SetNotificationTextEntry("STRING");
            API.AddTextComponentString(result);
            API.DrawNotification(false, false);
        }

        private void DrawText3D(float x, float y, float z, string text, int r, int g, int b)
        {
            API.SetTextScale(0.35f, 0.35f);
            API.SetTextFont(4);
            API.SetTextProportional(true);
            API.SetTextColour(r, g, b, 255);
            API.SetTextEntry("STRING");
            API.AddTextComponentString(text);
            API.SetDrawOrigin(x, y, z, 0);
            API.DrawText(0.0f, 0.0f);
            API.ClearDrawOrigin();
        }

        private void LoadConfig()
        {
            string data = API.LoadResourceFile("fivepd", $"/config/items.json");

            loadedconfigitems = JsonConvert.DeserializeObject<List<Item>>(data);

            if(loadedconfigitems == null)
            {
                Debug.WriteLine("[VSS] There isn't a items.json in FivePD/Config or its invalid!");
            }
            else
            {
                Debug.WriteLine($"[VSS] Total items Loaded { loadedconfigitems.Count } items.");
            }
        }

        private bool SearchVehicleSearchedClass(string dName, Vehicle? veh) =>
            searchedDoorsClass.Any(item => item.DoorName == dName && item.vehicleObj == veh);

        private void AddSearchedVehicle(string dName, Vehicle? veh) => searchedDoorsClass.Add(new VehicleDoorSearched
        {
            DoorName = dName,
            vehicleObj = veh
        });

        private class VehicleDoorSearched
        {
            public string DoorName;
            public Vehicle? vehicleObj;
        }

        private class Item
        {
            public string Name { get; set; }
            public bool IsIllegal { get; set; }
            public int Multiplier { get; set; }
            public int ItemLocation { get; set; }
        }

        private class ItemsFound
        {
            public string itemName;
            public bool isIllegal;
        }

    }
}