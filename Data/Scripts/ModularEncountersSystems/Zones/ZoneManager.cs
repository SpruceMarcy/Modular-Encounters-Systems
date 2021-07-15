﻿using ModularEncountersSystems.Core;
using ModularEncountersSystems.Entities;
using ModularEncountersSystems.Helpers;
using ModularEncountersSystems.Tasks;
using Sandbox.Game;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using VRageMath;

namespace ModularEncountersSystems.Zones {

	public enum ZoneEvaluation {
	
		None,
		ZoneName,
		Strict,
		AllowedSpawnGroup,
		FactionRestricted,
	
	}

	public static class ZoneManager {

		public static List<Zone> ActiveZones = new List<Zone>();

		public static void Setup() {

			//Get Stored Zones
			string persString = "";

			if (MyAPIGateway.Utilities.GetVariable<string>("MES-ZoneData", out persString)) {

				var persData = Convert.FromBase64String(persString);

				if (persData != null) {

					ActiveZones = MyAPIGateway.Utilities.SerializeFromBinary<List<Zone>>(persData);

					if (ActiveZones == null)
						ActiveZones = new List<Zone>();

				}
			
			}

			//Validate Existing Persistent Zones Against Profiles
			for (int i = ActiveZones.Count - 1; i >= 0; i--) {

				var zone = ActiveZones[i];

				if (string.IsNullOrWhiteSpace(zone?.ProfileSubtypeId)) {

					ActiveZones.RemoveAt(i);
					continue;

				}

				if (!ProfileManager.ZoneProfiles.ContainsKey(zone.ProfileSubtypeId)) {

					ActiveZones.RemoveAt(i);
					continue;

				}

				if (zone.PlanetaryZone) {

					bool planetExists = false;

					foreach (var planet in PlanetManager.Planets) {

						var id = planet?.Planet?.EntityId ?? -1;

						if (zone.PlanetId == id) {

							planetExists = true;
							break;
						
						}
					
					}

					if (!planetExists) {

						ActiveZones.RemoveAt(i);
						continue;

					}
				
				}
			
			}

			//Add New Zones
			bool updateZones = false;

			foreach (var zoneId in ProfileManager.ZoneProfiles.Keys) {

				var zone = ProfileManager.ZoneProfiles[zoneId];
				bool skip = false;

				if (!zone.Persistent)
					continue;

				if (!zone.PlanetaryZone) {

					foreach (var perZone in ActiveZones) {

						if (perZone.ProfileSubtypeId == zone.ProfileSubtypeId) {

							skip = true;
							break;

						}
					
					}

					if (skip)
						continue;

					var newZone = zone.Clone();
					newZone.Reset();
					ActiveZones.Add(newZone);
					updateZones = true;


				} else {

					List<PlanetEntity> planetsNeeded = null;

					foreach (var planet in PlanetManager.Planets) {

						var id = planet?.Planet?.Generator?.Id.SubtypeName ?? "";

						if (string.IsNullOrWhiteSpace(id) || id != zone.PlanetName) {

							continue;
						
						}

						bool skipPlanet = false;

						foreach (var perZone in ActiveZones) {

							if (perZone.ProfileSubtypeId == zone.ProfileSubtypeId) {

								if (id == perZone.PlanetName && planet.Planet.EntityId == perZone.PlanetId) {

									skip = true;
									break;

								}
								
							}

						}

						if (skipPlanet)
							continue;

						if (planetsNeeded == null)
							planetsNeeded = new List<PlanetEntity>();

						planetsNeeded.Add(planet);

					}

					if (planetsNeeded != null) {

						foreach (var planet in planetsNeeded) {

							var newZone = zone.Clone();
							newZone.Reset(planet.Planet.EntityId);
							newZone.PlanetSetup(planet);
							ActiveZones.Add(newZone);
							updateZones = true;

						}
					
					}
				
				}

			}

			if (updateZones) {

				UpdateZoneStorage();

			}

			TaskProcessor.Tick60.Tasks += AnnounceDepartMessages;
			MES_SessionCore.UnloadActions += Unload;
		
		}

		public static void RemoveZone(Zone zone) {

			if (ActiveZones.Contains(zone)) {

				ActiveZones.Remove(zone);
				UpdateZoneStorage();

			}

		}

		public static void UpdateZoneStorage() {

			var zoneData = MyAPIGateway.Utilities.SerializeToBinary<List<Zone>>(ActiveZones);
			var zoneString = Convert.ToBase64String(zoneData);
			MyAPIGateway.Utilities.SetVariable<string>("MES-ZoneData", zoneString);
		
		}

		public static void GetAllowedSpawns(Vector3D coords, List<string> allowedSpawns, List<string> allowedFactions) {

			foreach (var zone in ActiveZones) {

				if (!zone.Active || Vector3D.Distance(zone.Coordinates, coords) > zone.Radius)
					continue;

				if (zone.Persistent && zone.AllowedSpawnGroups.Count > 0) {

					foreach (var spawn in zone.AllowedSpawnGroups) {

						if(!string.IsNullOrWhiteSpace(spawn) && !allowedSpawns.Contains(spawn))
							allowedSpawns.Add(spawn);

					}
				
				}

				if (zone.Persistent && zone.UseLimitedFactions && zone.Factions.Count > 0) {

					foreach (var faction in zone.Factions) {

						if (!string.IsNullOrWhiteSpace(faction) && !allowedFactions.Contains(faction))
							allowedFactions.Add(faction);

					}

				}

			}

		}

		public static bool InsideNoSpawnZone(Vector3D coords) {

			foreach (var zone in ActiveZones) {

				if (!zone.Active || Vector3D.Distance(zone.Coordinates, coords) > zone.Radius)
					continue;

				if (zone.NoSpawnZone) {

					return true;

				}

			}

			return false;

		}

		public static bool PositionInsideStrictZone(Vector3D coords) {

			foreach (var zone in ActiveZones) {

				if (!zone.Active || Vector3D.Distance(zone.Coordinates, coords) > zone.Radius)
					continue;

				if (zone.Persistent && zone.Strict)
					return true;
			
			}

			return false;
		
		}

		public static void AnnounceDepartMessages() {

			bool updateZones = false;

			for (int i = 0; i < ActiveZones.Count; i++) {

				var zone = ActiveZones[i];

				if (!zone.Persistent || !zone.UseZoneAnnounce)
					continue;

				for (int j = 0; j < PlayerManager.Players.Count; j++) {

					var player = PlayerManager.Players[j];

					if (!player.ActiveEntity())
						continue;

					var distFromCenter = player.Distance(zone.Coordinates);

					if (zone.PlayersInZone.Contains(player.Player.IdentityId) && distFromCenter > zone.Radius && !string.IsNullOrWhiteSpace(zone.ZoneLeaveAnnounce)) {

						//Leave Zone
						updateZones = true;
						zone.PlayersInZone.Remove(player.Player.IdentityId);
						MyVisualScriptLogicProvider.ShowNotification(zone.ZoneLeaveAnnounce, 5000, "White", player.Player.IdentityId);

					} else if (!zone.PlayersInZone.Contains(player.Player.IdentityId) && distFromCenter < zone.Radius && !string.IsNullOrWhiteSpace(zone.ZoneEnterAnnounce)) {

						//Enter Zone
						updateZones = true;
						zone.PlayersInZone.Add(player.Player.IdentityId);
						MyVisualScriptLogicProvider.ShowNotification(zone.ZoneEnterAnnounce, 5000, "White", player.Player.IdentityId);

					}

				}
			
			}
		
		}

		public static void Unload() {
		
			
		
		}

	}

}