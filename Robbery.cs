using CitizenFX.Core;
using CitizenFX.Core.Native;
using FivePD.API;
using FivePD.API.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ArmoredTruckRobbery
{
    [CalloutProperties("Armored Truck Robbery", "HuskyNinja", "v1")]
    internal class Robbery: Callout
    {
        private List<Vector4> _allStartCoords;
        private List<Vector4> _allEndCoords;

        private Vector4 _startCoords;
        private Vector4 _endCoords;

        private Vehicle _armoredTruck;
        private Vehicle _supportVehicle;

        private Ped _driver;
        private Ped _shooter;
        private Ped _backupAlpha;
        private Ped _backupBravo;

        private Ped _employeeDriver;
        private Ped _employeePassenger;
        //Constructor
        public Robbery()
        {
            //Get Callout Data
            Init();

            //Set Callout Data
            InitInfo((Vector3)_startCoords);
            ShortName = "Armored Truck Robbery";
            CalloutDescription = $"An Armored Truck has been hijacked in the {World.GetZoneDisplayName((Vector2)_startCoords)} area.";
            ResponseCode = 3;
            StartDistance = 200f;
        }

        //FivePD Methods
        public override async Task OnAccept()
        {
            InitBlip();
            UpdateData();

            await Task.FromResult(0);
        }
        public override async void OnStart(Ped closest)
        {
            SpawnVehicle(_startCoords);
            base.OnStart(closest);

            await Task.FromResult(0);
        }

        //CalloutData Methods
        private Vector4 SelectCoords(List<Vector4> coords)
        {
            return coords.SelectRandom();
        }
        private async Task<Ped> CreateCriminal(Vector3 coords)
        {
            Ped ped = await SpawnPed(PedHash.FreemodeMale01, coords);

            List<int> masks = new List<int>() { 1, 3, 13, 20, 43, 44, 45, 147, 165 };
            List<int> gloves = new List<int>() { 16, 17, 18, 20 };
            List<int> shoes = new List<int>() { 80, 82, 97, 12, 24, 25, 27, 51, 54, 61, 66, 71, 73 };
            List<int> hair = new List<int>() { 59, 66, 71, 72, 73, 76, 3, 5, 6, 8, 11, 13, 14, 17 };

            ValidateAndSetComponent(ped.Handle, 1, 0, 0, masks);
            ValidateAndSetComponent(ped.Handle, 3, 0, 0, gloves);
            ValidateAndSetComponent(ped.Handle, 2, 1, 0, hair);
            ValidateAndSetComponent(ped.Handle, 6, 0, 0, shoes);

            API.SetPedComponentVariation(ped.Handle, 8, 15, 0, 0);
            API.SetPedComponentVariation(ped.Handle, 4, 38, 1, 0);
            API.SetPedComponentVariation(ped.Handle, 11, 65, 1, 0);

            ped.RelationshipGroup = (RelationshipGroup)"AMBIENT_GANG_WEICHENG";

            ped.Weapons.Give(WeaponHash.AssaultRifle, 500, false, true);

            API.SetPedAccuracy(ped.Handle, 30);
            API.SetPedArmour(ped.Handle, 1200);
            API.SetPedCombatMovement(ped.Handle, 2);
            API.SetPedCombatRange(ped.Handle, 1);

            ped.BlockPermanentEvents = true;
            ped.AlwaysKeepTask = true;

            API.SetPedCombatAttributes(ped.Handle, 46, true);
            API.SetPedCombatAttributes(ped.Handle, 52, true);
            API.SetPedCombatAttributes(ped.Handle, 2, true);
            API.SetPedCombatAttributes(ped.Handle, 5, true);

            return ped;
        }
        private async Task<Ped> CreateEmployee(Vector3 coords, PedHash hash)
        {
            Ped ped = await SpawnPed(hash, coords);
            ped.BlockPermanentEvents = true;
            ped.AlwaysKeepTask = true;
            ped.AlwaysDiesOnLowHealth = true;

            return ped;
        }
        private async Task DriveToDestination()
        {
            float dist = World.GetDistance(_armoredTruck.Position, (Vector3)_endCoords);
            if (dist > 20f) { return; }
            if (!_armoredTruck.IsStopped) { return; }
            Tick -= DriveToDestination;

            _driver.Task.ClearAllImmediately();
            _shooter.Task.ClearAllImmediately();

            LoadProps();

            await BaseScript.Delay(100);
            _armoredTruck.IsEngineRunning = false;
            _armoredTruck.EngineHealth = 0;

            API.SetVehicleDoorOpen(_armoredTruck.Handle, (int)VehicleDoorIndex.BackLeftDoor, false, false);
            API.SetVehicleDoorOpen(_armoredTruck.Handle, (int)VehicleDoorIndex.BackRightDoor, false, false);

            _driver.Task.LeaveVehicle();
            _shooter.Task.LeaveVehicle();

            while(_driver.IsInVehicle() && _shooter.IsInVehicle()) { await BaseScript.Delay(10); }
            _driver.Weapons.Select(WeaponHash.AssaultRifle, true);
            _shooter.Weapons.Select(WeaponHash.AssaultRifle, true);

            Tick += DefendVehicle;
            SendBackup();

            await Task.FromResult(0);
        }
        private async Task DefendVehicle()
        {
            if (_shooter.IsDead && _driver.IsDead)
            {
                if (_shooter.AttachedBlips.Count() > 0)
                    try { _shooter.AttachedBlip.Delete(); } catch { }


                if (_driver.AttachedBlips.Count() > 0)
                    try { _driver.AttachedBlip.Delete(); } catch { }

                Tick -= DefendVehicle;
            }
            else
            {
                if (_shooter.IsAlive && !_shooter.IsInCombat)
                {
                    var players = World.GetAllPeds().Where(p => p.IsAlive && p.IsPlayer && World.GetDistance(p.Position, _shooter.Position) <= 35f);
                    if(players.Any())
                    {
                        _shooter.Task.ClearAllImmediately();
                        API.TaskCombatPed(_shooter.Handle, players.SelectRandom().Handle, 0, 16);
                    }
                    else
                        _shooter.Task.WanderAround(_shooter.Position, 25f);
                }

                if (_driver.IsAlive && !_driver.IsInCombat)
                {
                    var players = World.GetAllPeds().Where(p => p.IsAlive && p.IsPlayer && World.GetDistance(p.Position, _driver.Position) <= 35f);
                    if (players.Any())
                    {
                        _driver.Task.ClearAllImmediately();
                        API.TaskCombatPed(_driver.Handle, players.SelectRandom().Handle, 0, 16);
                    }
                    else
                        _driver.Task.WanderAround(_driver.Position, 25f);
                }
                await BaseScript.Delay(2500);
            }
        }
        private async Task BackupArrival()
        {
            float dist = World.GetDistance(_supportVehicle.Position, _armoredTruck.Position);
            if(dist > 20f) { return; }

            Tick -= BackupArrival;

            while (!_supportVehicle.IsStopped) { await BaseScript.Delay(5); }
            _supportVehicle.IsEngineRunning = false;

            _backupAlpha.Task.ClearAllImmediately();
            await BaseScript.Delay(100);

            _backupAlpha.AttachBlip();
            _backupBravo.AttachBlip();

            _backupAlpha.Task.LeaveVehicle();
            _backupBravo.Task.LeaveVehicle();
            while (_backupBravo.IsInVehicle() || _backupAlpha.IsInVehicle()) { await BaseScript.Delay(10); }

            _backupAlpha.Weapons.Select(WeaponHash.AssaultRifle, true);
            _backupBravo.Weapons.Select(WeaponHash.AssaultRifle, true);
            Tick += BackupEngage;

            await Task.FromResult(0);
        }
        private async Task BackupEngage()
        {
            if(_backupAlpha.IsDead && _backupBravo.IsDead)
            {
                if (_backupAlpha.AttachedBlips.Count() > 0)
                    try { _backupAlpha.AttachedBlip.Delete(); } catch { }
                
                if (_backupBravo.AttachedBlips.Count() > 0)
                    try { _backupBravo.AttachedBlip.Delete(); } catch { }

                Tick -= BackupEngage;
            }
            else
            {
                if (_backupAlpha.IsAlive && !_backupAlpha.IsInCombat)
                {
                    var players = World.GetAllPeds().Where(p => p.IsAlive && p.IsPlayer && World.GetDistance(p.Position, _backupAlpha.Position) <= 35f);
                    if(players.Any())
                    {
                        _backupAlpha.Task.ClearAllImmediately();
                        API.TaskCombatPed(_backupAlpha.Handle, players.SelectRandom().Handle, 0, 16);
                    }
                    else
                        _backupAlpha.Task.WanderAround(_backupAlpha.Position, 25f);
                }


                if(_backupBravo.IsAlive && !_backupAlpha.IsInCombat)
                {
                    var players = World.GetAllPeds().Where(p => p.IsAlive && p.IsPlayer && World.GetDistance(p.Position, _backupBravo.Position) <= 35f);
                    if (players.Any())
                    {
                        _backupBravo.Task.ClearAllImmediately();
                        API.TaskCombatPed(_backupBravo.Handle, players.SelectRandom().Handle, 0, 16);
                    }
                    else
                        _backupBravo.Task.WanderAround(_backupBravo.Position, 25f);
                }
                await BaseScript.Delay(2500);
            }
        }
        private async Task Driver_EnterVehicle()
        {
            if (!_driver.IsWalking)
                _driver.Task.EnterVehicle(_armoredTruck, VehicleSeat.Driver);

            if (_driver.IsInVehicle())
                Tick -= Driver_EnterVehicle;

            await BaseScript.Delay(1500);
        }
        private async Task ShootGuards()
        {
            if (_employeeDriver.IsDead && _employeePassenger.IsDead)
            {
                _shooter.Task.ClearAllImmediately();
                _shooter.Task.EnterVehicle(_armoredTruck, VehicleSeat.Passenger);
                Tick -= ShootGuards;
            }
            else
            {
                if(_employeeDriver.IsAlive)
                {
                    API.TaskShootAtEntity(_shooter.Handle, _employeeDriver.Handle, 2500, (uint)FiringPattern.FullAuto);
                    await BaseScript.Delay(2500);
                }
                else
                {
                    _shooter.Task.ClearAllImmediately();
                    API.TaskShootAtEntity(_shooter.Handle, _employeePassenger.Handle, 2500, (uint)FiringPattern.FullAuto);
                    await BaseScript.Delay(2500);
                }
            }

            await Task.FromResult(0);
        }

        private void Init()
        {
            LoadCoordinates();

            _startCoords = SelectCoords(_allStartCoords);
            _endCoords = SelectCoords(_allEndCoords);

            API.RequestModel((uint)API.GetHashKey("stockade"));
            API.RequestModel((uint)API.GetHashKey("prop_cash_crate_01"));
            API.RequestModel((uint)API.GetHashKey("s_m_m_security_01"));
            API.RequestModel((uint)API.GetHashKey("s_m_m_prisguard_01"));

            API.SetRelationshipBetweenGroups(5, 0x6A3B9F86, 0x6F0783F5);
            API.SetRelationshipBetweenGroups(5, 0x6F0783F5, 0x6A3B9F86);
        }
        private void ValidateAndSetComponent(int ped, int cid, int tid, int pid, List<int> drawables)
        {
            int drawableID = drawables.SelectRandom();
            while (!API.IsPedComponentVariationValid(ped, cid, drawableID, tid)) { drawableID = drawables.SelectRandom(); }
            API.SetPedComponentVariation(ped, cid, drawableID, tid, pid);
        }
        private async void SendBackup()
        {
            Vector3 _supportCarCoords = new Vector3(0, 0, 0);
            bool isFound = false;
            while (!isFound) { isFound = API.GetNthClosestVehicleNode(_endCoords.X, _endCoords.Y, _endCoords.Z, new Random().Next(50, 250), ref _supportCarCoords, 0, 0, 0); await BaseScript.Delay(5); }

            _supportVehicle = await SpawnVehicle(VehicleHash.Burrito3, _supportCarCoords);
            _backupAlpha = await CreateCriminal(_supportCarCoords.Around(5f));
            _backupBravo = await CreateCriminal(_supportCarCoords.Around(5f));

            while (!_backupAlpha.Exists() || !_backupBravo.Exists()) { await BaseScript.Delay(1); }
            _backupAlpha.Task.WarpIntoVehicle(_supportVehicle, VehicleSeat.Driver);
            _backupBravo.Task.WarpIntoVehicle(_supportVehicle, VehicleSeat.Passenger);

            while (!_backupAlpha.IsInVehicle(_supportVehicle) || !_backupBravo.IsInVehicle(_supportVehicle)) { await BaseScript.Delay(10); }
            _backupAlpha.Task.DriveTo(_supportVehicle, _armoredTruck.Position, 20f, 65f, 537657916);
            API.SetDriverAbility(_backupAlpha.Handle, 1.0f);
            Tick += BackupArrival;
        }
        private async void SpawnEmployees()
        {
            Vector3 spawnpoint = new Vector3(_shooter.ForwardVector.X, _shooter.ForwardVector.Y + 2f, _shooter.ForwardVector.Z) + _shooter.Position;

            _employeeDriver = await CreateEmployee(spawnpoint, PedHash.Prisguard01SMM);
            await BaseScript.Delay(100);
            _employeePassenger = await CreateEmployee(API.GetOffsetFromEntityInWorldCoords(_employeeDriver.Handle, 2f, 0, 0), PedHash.Security01SMM);

            await BaseScript.Delay(250);
            _employeeDriver.Task.HandsUp(-1);
            _employeePassenger.Task.HandsUp(-1);

            CriminalActions();
        }
        private async void SpawnCriminals(Vector3 spawn)
        {
            Vector3 location = API.GetOffsetFromEntityInWorldCoords(_armoredTruck.Handle, 0, -5.5f, 0);

            _driver = await CreateCriminal(spawn.Around(5f));
            _shooter = await CreateCriminal(location);

            while(!_driver.Exists() || !_shooter.Exists()) { await BaseScript.Delay(5); }

            API.SetDriverAbility(_driver.Handle, 1.0f);

            if (_armoredTruck.Heading >= 181)
                API.SetEntityHeading(_shooter.Handle, _armoredTruck.Heading - 180f);
            else
                API.SetEntityHeading(_shooter.Handle, _armoredTruck.Heading + 180f);

            await BaseScript.Delay(50);
            SpawnEmployees();
        }
        private async void SpawnVehicle(Vector4 spawn)
        {
            API.ClearArea(spawn.X, spawn.Y, spawn.Z, 20f, true, false, false, false);

            _armoredTruck = await SpawnVehicle(VehicleHash.Stockade, (Vector3)spawn);
            API.SetEntityHeading(_armoredTruck.Handle, _startCoords.W);

            while(!_armoredTruck.Exists()) { await BaseScript.Delay(5); }

            SpawnCriminals((Vector3)spawn);
        }
        private async void CriminalActions()
        {
            while (!_driver.Exists() || !_shooter.Exists()) { await BaseScript.Delay(1); }
            Tick += Driver_EnterVehicle;
            Tick += ShootGuards;

            //Once they are in give directions to the end point
            while (!_driver.IsInVehicle() || !_shooter.IsInVehicle()) { await BaseScript.Delay(1); }
            _driver.Task.DriveTo(_armoredTruck, (Vector3)_endCoords, 18f, 100f, 537657916);
            _armoredTruck.AttachBlip();

            //Wait for the driver to get to the end point
            Tick += DriveToDestination;
        }
        private void LoadCoordinates()
        {
            //Load coordinates file into a string
            string path = "callouts/ArmoredTruckRobbery/coords.json";
            string coordinates = API.LoadResourceFile(API.GetCurrentResourceName(), path);

            //Parse string into JSON
            JObject json = JObject.Parse(coordinates);

            //Deserialize the start coords
            _allStartCoords = new List<Vector4>();
            foreach(var item in json["Start_Coords"])
            {
                Vector4 coord = JsonConvert.DeserializeObject<Vector4>(item.ToString());
                _allStartCoords.Add(coord);
            }

            //Deserialize the end coords
            _allEndCoords = new List<Vector4>();
            foreach(var item in json["End_Coords"])
            {
                Vector4 coord = JsonConvert.DeserializeObject<Vector4>(item.ToString());
                _allEndCoords.Add(coord);
            }
        }
        private void LoadProps()
        {
            int propA = API.CreateObject(API.GetHashKey("prop_cash_crate_01"), 0, 0, 0, true, true, true);
            int propB = API.CreateObject(API.GetHashKey("prop_cash_crate_01"), 0, 0, 0, true, true, true);

            API.AttachEntityToEntity(propA, _armoredTruck.Handle, API.GetEntityBoneIndexByName(_armoredTruck.Handle, "chassis"), 0, -1.5f, 0.45f, 0, 0, 90f, true, true, false, false, 0, true);
            API.AttachEntityToEntity(propB, _armoredTruck.Handle, API.GetEntityBoneIndexByName(_armoredTruck.Handle, "chassis"), 0, -3f, 0.45f, 0, 0, 90f, true, true, false, false, 0, true);
        }
    }
}
