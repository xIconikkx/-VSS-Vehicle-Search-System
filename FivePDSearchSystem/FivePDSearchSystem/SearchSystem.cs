using CitizenFX.Core;
using CitizenFX.Core.Native;
using FivePD.API.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Kilo.Commons.Config;
using Newtonsoft.Json.Linq;


//## To Do ##//
// Cleanup, when player is more than 50m away from any car. Remove results for it.

namespace FivePDSearchSystem
{
    public class SearchSystem : FivePD.API.Plugin
    {
        private List<string> itemList;
        private List<VehicleDoorSearched> searchedDoorsClass = new List<VehicleDoorSearched>();
        private Config config;

        internal SearchSystem()
        {
            Debug.WriteLine("Loaded FivePD Vehicle Search System v1.0.0");
            LoadConfig();

            Tick += OnTick;
            Tick += Cleanup;
        }

        private async Task Cleanup()
        {
            //60 seconds
            await Delay(60000);
            for (int i = 0; i < searchedDoorsClass.Count; i++)
            {
                if (!searchedDoorsClass[i].vehicleObj.Exists())
                {
                    searchedDoorsClass.Remove(searchedDoorsClass[i]);
                }
                else
                {
                    Ped playerPed = Game.PlayerPed;
                    Vector3 pedPos = playerPed.Position;

                    float distance = pedPos.DistanceTo(searchedDoorsClass[i].vehicleObj.Position);

                    if(distance > 100)
                    {
                        searchedDoorsClass.Remove(searchedDoorsClass[i]);
                    }
                }
            }

            await Task.FromResult(0);
        }

        private async Task OnTick()
        {
            try
            {
                if (!Game.PlayerPed.IsInVehicle())
                {
                    Ped playerPed = Game.PlayerPed;

                    if (playerPed == null || !playerPed.Exists()) return;

                    Vehicle vehicle = GetClosestVehicle(playerPed.Position, 5f);

                    if (vehicle != null && vehicle.Exists())
                    {
                        Vector3 pedPos = playerPed.Position;
                        float closestDistance = float.MaxValue;
                        VehicleDoorIndex closestDoor = VehicleDoorIndex.FrontLeftDoor;
                        Vector3 closestDoorPos = Vector3.Zero;

                        // Loop through standard doors (indexes 0-3)
                        foreach (VehicleDoorIndex doorIndex in Enum.GetValues(typeof(VehicleDoorIndex)))
                        {
                            Vector3 doorPos = GetDoorPosition(vehicle, doorIndex);

                            float distance = pedPos.DistanceTo(doorPos);

                            if (distance < closestDistance && distance < 2f)
                            {
                                closestDistance = distance;
                                closestDoor = doorIndex;
                                closestDoorPos = doorPos;
                            }
                        }


                        if (closestDistance < 2f)
                        {
                            if (searchedDoorsClass.Count > 0)
                            {
                                if (!SearchVehicleSearchedClass(closestDoor.ToString(), vehicle))
                                {
                                    bool isOpen = API.GetVehicleDoorAngleRatio(vehicle.Handle, (int)closestDoor) > 0;
                                    string actionText = isOpen ? "Search" : "Open";

                                    DrawText3D(closestDoorPos.X, closestDoorPos.Y, closestDoorPos.Z, $"Press [E] to {actionText}", 255, 243, 15);

                                    if (API.IsControlJustPressed(0, 38))
                                    {
                                        if (isOpen)
                                        {
                                            AddSearchedVehicle(closestDoor.ToString(), vehicle);

                                            await SearchVehicle(playerPed, vehicle, (VehicleDoorIndex)closestDoor);
                                        }
                                        else
                                        {
                                            ToggleDoor(vehicle, (VehicleDoorIndex)closestDoor);
                                        }
                                    }
                                }
                                else
                                {
                                    DrawText3D(closestDoorPos.X, closestDoorPos.Y, closestDoorPos.Z, $"SEARCHED", 17, 230, 2);
                                }
                            }
                            else
                            {
                                bool isOpen = API.GetVehicleDoorAngleRatio(vehicle.Handle, (int)closestDoor) > 0;
                                string actionText = isOpen ? "Search" : "Open";

                                DrawText3D(closestDoorPos.X, closestDoorPos.Y, closestDoorPos.Z, $"Press [E] to {actionText}", 255, 243, 15);

                                if (API.IsControlJustPressed(0, 38))
                                {
                                    if (isOpen)
                                    {
                                        await SearchVehicle(playerPed, vehicle, (VehicleDoorIndex)closestDoor);

                                        AddSearchedVehicle(closestDoor.ToString(), vehicle);
                                    }
                                    else
                                    {
                                        ToggleDoor(vehicle, (VehicleDoorIndex)closestDoor);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.Write("[VSS - ERROR] " + ex.ToString());
            }

            await Task.FromResult(0);
        }

        private Vehicle GetClosestVehicle(Vector3 position, float radius)
        {
            int handle = API.GetClosestVehicle(position.X, position.Y, position.Z, radius, 0, 70);
            return handle != 0 ? new Vehicle(handle) : null;
        }

        private Vector3 GetDoorPosition(Vehicle vehicle, VehicleDoorIndex doorIndex)
        {
            if (doorIndex == VehicleDoorIndex.Hood || doorIndex == VehicleDoorIndex.Trunk)
            {
                // Get bone positions for hood and trunk
                int boneIndex = (doorIndex == VehicleDoorIndex.Hood) ? API.GetEntityBoneIndexByName(vehicle.Handle, "bonnet")
                                                                     : API.GetEntityBoneIndexByName(vehicle.Handle, "boot");
                return API.GetWorldPositionOfEntityBone(vehicle.Handle, boneIndex);
            }
            else
            {
                // Normal doors use entry positions
                return API.GetEntryPositionOfDoor(vehicle.Handle, (int)doorIndex);
            }
        }

        private void ToggleDoor(Vehicle vehicle, VehicleDoorIndex doorIndex)
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

        private async Task SearchVehicle(Ped playerPed, Vehicle vehicle, VehicleDoorIndex doorIndex)
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
            if (itemList.Count == 0) return;

            Random rand = new Random();
            int itemCount = rand.Next(1, 4);
            List<string> foundItems = new List<string>();

            for (int i = 0; i < itemCount; i++)
            {
                string itemToAdd = itemList[rand.Next(1, itemList.Count)].ToString();


                if (foundItems.Contains(itemToAdd))
                {
                    i -= 1;
                    return;
                }
                else
                {
                    if (i == 0 && itemToAdd == "Nothing")
                    {
                        i = 4;
                    }
                    else if(i > 0 && itemToAdd == "Nothing")
                    {
                        i -= 1;
                        return;
                    }

                    foundItems.Add(itemToAdd);
                }
            }

            string result = $"You found: {string.Join(", ", foundItems)}";
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

        //## Stuff to do with Config loading etc ##//
        private void LoadConfig()
        {
            try
            {
                config = new Config(AddonType.plugins, defaultConfig.ToString(), "VehicleSearchSystem", "items.json", "fivepd");

                if (config != null)
                {
                    if (config.ContainsKey("items"))
                    {
                        // Convert the "items" property to a List<string>
                        itemList = config["items"].ToObject<List<string>>();

                        // Print the items
                        Debug.WriteLine("[VSS] Loaded items!");
                    }
                    else
                    {
                        Debug.WriteLine("[VSS - ERROR] 'items' key not found in the JSON. JSON File might be broke!");
                    }
                }
                else
                {
                    Debug.WriteLine("[VSS] No Json File?");
                }
            }
            catch (Exception ex)
            {
                Debug.Write("[VSS - ERROR] " + ex.ToString());
            }
            
        }

        private JObject defaultConfig = new JObject()
        {
            ["items"] = "Wallet, Phone, Money, Knife"
        };
        //## End of stuff to do with items.json ##//

        private bool SearchVehicleSearchedClass(string dName, Vehicle veh)
        {
            bool u = false;

            foreach (var item in searchedDoorsClass)
            {
                if(item.DoorName == dName && item.vehicleObj == veh)
                {
                    u = true;
                }
            }

            return u;
        }
        private void AddSearchedVehicle(string dName, Vehicle veh)
        {
            VehicleDoorSearched i = new VehicleDoorSearched();
            i.DoorName = dName;
            i.vehicleObj = veh;

            searchedDoorsClass.Add(i);
        }

        private class VehicleDoorSearched
        {
            public string DoorName;
            public Vehicle vehicleObj;
        }
    }
}